using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace WindowsWebcamReceiver
{
    class Program
    {
        const int Port = 9443;
        const string CertFileName = "iphone-webcam.pfx";

        // Simple signaling relay: one sender (iPhone), one viewer (PC browser)
        static WebSocket? senderSocket;
        static WebSocket? viewerSocket;
        static readonly SemaphoreSlim lockObj = new(1, 1);

        // Latest video frame, pushed here by receiver.html. This is what will
        // eventually feed the virtual camera.
        static readonly FrameStore frames = new();

        // Latest diagnostics reported by receiver.html (raw JSON).
        static string? sourceStats;

        static async Task Main(string[] args)
        {
            Console.WriteLine("Starting WebRTC Signaling Server...");

            var localIps = GetLocalIPv4Addresses();
            if (localIps.Count == 0)
            {
                Console.WriteLine("WARNING: No local network address found. Is Wi-Fi connected?");
            }

            var cert = GetOrCreateCertificate(localIps);

            var builder = WebApplication.CreateBuilder(args);
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(Port, listen => listen.UseHttps(cert));
            });

            var app = builder.Build();

            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.UseWebSockets();

            // iPhone connects here as the camera sender
            app.Map("/ws/sender", async context =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    using var ws = await context.WebSockets.AcceptWebSocketAsync();
                    Console.WriteLine("iPhone (sender) connected.");
                    senderSocket = ws;

                    // If a viewer is already waiting, tell the phone right away.
                    // Without this the handshake depends on connection order: a
                    // viewer that announced itself before the phone arrived had
                    // its message dropped, and nothing ever triggered an offer.
                    if (viewerSocket is { State: WebSocketState.Open })
                    {
                        Console.WriteLine("Viewer already waiting - prompting iPhone to offer.");
                        await SendJson(ws, "{\"type\":\"viewer-ready\"}");
                    }

                    await RelayMessages(ws, () => viewerSocket, "iPhone");
                    senderSocket = null;
                    Console.WriteLine("iPhone (sender) disconnected.");
                }
                else context.Response.StatusCode = 400;
            });

            // PC browser connects here as the viewer
            app.Map("/ws/viewer", async context =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    using var ws = await context.WebSockets.AcceptWebSocketAsync();
                    Console.WriteLine("PC Viewer connected.");
                    viewerSocket = ws;
                    await RelayMessages(ws, () => senderSocket, "Viewer");
                    viewerSocket = null;
                    Console.WriteLine("PC Viewer disconnected.");
                }
                else context.Response.StatusCode = 400;
            });

            // receiver.html pushes raw RGBA frames here, already letterboxed to
            // one fixed size.
            app.Map("/ws/frames", async context =>
            {
                if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }

                using var ws = await context.WebSockets.AcceptWebSocketAsync();
                Console.WriteLine("Frame receiver connected.");
                await ReceiveFrames(ws);
                Console.WriteLine("Frame receiver disconnected.");
            });

            // Diagnostics: is C# actually holding real pixels?
            app.MapGet("/frame-status", () => Results.Json(new
            {
                live = frames.IsLive,
                width = frames.Width,
                height = frames.Height,
                sequence = frames.Sequence,
                ageSeconds = double.IsInfinity(frames.AgeSeconds) ? -1 : Math.Round(frames.AgeSeconds, 2),
                source = sourceStats
            }));

            // Latest frame as an image, so it can be eyeballed in a browser.
            app.MapGet("/frame.bmp", () =>
            {
                var bmp = frames.ReadAsBmp();
                return bmp == null
                    ? Results.NotFound("no frame received yet")
                    : Results.File(bmp, "image/bmp");
            });

            PrintStartupBanner(localIps);

            // Start the invisible browser that actually decodes the video.
            // --no-webview skips it, for when you'd rather drive receiver.html
            // in a real browser window and watch what it's doing.
            if (!args.Contains("--no-webview"))
            {
                await app.StartAsync();
                WebViewHost.Start($"https://localhost:{Port}/receiver.html");
                await app.WaitForShutdownAsync();
                return;
            }

            Console.WriteLine("Running without the background browser (--no-webview).");
            Console.WriteLine($"Open https://localhost:{Port}/receiver.html yourself to feed frames.\n");
            await app.RunAsync();
        }

        static void PrintStartupBanner(List<IPAddress> localIps)
        {
            Console.WriteLine();
            Console.WriteLine("=========================================================");
            Console.WriteLine("  On your PC browser (viewer):");
            Console.WriteLine($"      https://localhost:{Port}/viewer.html");
            Console.WriteLine();
            if (localIps.Count > 0)
            {
                Console.WriteLine("  On your iPhone (Safari, same Wi-Fi network):");
                foreach (var ip in localIps)
                    Console.WriteLine($"      https://{ip}:{Port}");
                if (localIps.Count > 1)
                    Console.WriteLine("      (try each until one loads)");
            }
            else
            {
                Console.WriteLine("  No network address detected - connect to Wi-Fi and restart.");
            }
            Console.WriteLine();
            Console.WriteLine("  Safari will warn the connection is not private. This is");
            Console.WriteLine("  expected - the certificate is self-signed. Tap");
            Console.WriteLine("  'Show Details' then 'visit this website' to continue.");
            Console.WriteLine("=========================================================");
            Console.WriteLine();
        }

        /// <summary>
        /// Finds this machine's usable IPv4 addresses on real network adapters.
        /// Skips loopback, disconnected adapters, and self-assigned 169.254.x
        /// addresses (which mean "DHCP failed" and are never reachable from a phone).
        /// </summary>
        static List<IPAddress> GetLocalIPv4Addresses()
        {
            var results = new List<IPAddress>();

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var addrInfo in nic.GetIPProperties().UnicastAddresses)
                {
                    var addr = addrInfo.Address;
                    if (addr.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(addr)) continue;

                    var bytes = addr.GetAddressBytes();
                    if (bytes[0] == 169 && bytes[1] == 254) continue; // APIPA

                    results.Add(addr);
                }
            }

            // Prefer ordinary home-network ranges (192.168.x, 10.x) over anything
            // exotic, so the address most likely to work is printed first.
            return results
                .OrderByDescending(ip => IsCommonPrivateRange(ip))
                .ThenBy(ip => ip.ToString())
                .ToList();
        }

        static bool IsCommonPrivateRange(IPAddress ip)
        {
            var b = ip.GetAddressBytes();
            return (b[0] == 192 && b[1] == 168) || b[0] == 10;
        }

        /// <summary>
        /// Loads a previously generated certificate, or creates one if it is missing,
        /// expired, or no longer covers the machine's current IP addresses.
        ///
        /// The IP addresses are written into the certificate's Subject Alternative Name.
        /// Without that, Safari rejects the connection outright for the wrong reason
        /// ("this certificate is not for this address") on top of the expected
        /// self-signed warning, and there is no way to click past it.
        /// </summary>
        static X509Certificate2 GetOrCreateCertificate(List<IPAddress> localIps)
        {
            // Deliberately NOT in the build output folder. That path changes with
            // build configuration and target framework, and is wiped by a clean -
            // each of which would silently regenerate the certificate and force
            // every phone to accept the security warning all over again.
            var certDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "iPhoneWebcam");
            Directory.CreateDirectory(certDir);
            var certPath = Path.Combine(certDir, CertFileName);

            if (File.Exists(certPath))
            {
                try
                {
                    var existing = new X509Certificate2(
                        File.ReadAllBytes(certPath), (string?)null,
                        X509KeyStorageFlags.Exportable);

                    if (existing.NotAfter > DateTime.Now.AddDays(7) && CoversAll(existing, localIps))
                    {
                        Console.WriteLine($"Using existing certificate ({certPath}).");
                        return existing;
                    }

                    Console.WriteLine("Existing certificate is expired or missing this machine's IP - regenerating.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not load existing certificate ({ex.Message}) - regenerating.");
                }
            }

            var cert = CreateSelfSignedCertificate(localIps);
            File.WriteAllBytes(certPath, cert.Export(X509ContentType.Pfx));
            Console.WriteLine($"Generated new self-signed certificate ({certPath}).");
            return cert;
        }

        static bool CoversAll(X509Certificate2 cert, List<IPAddress> ips)
        {
            // SAN entries appear in the certificate's text dump; good enough for a
            // "do we need to regenerate?" check without hand-parsing ASN.1.
            var san = cert.Extensions
                .OfType<X509Extension>()
                .FirstOrDefault(e => e.Oid?.Value == "2.5.29.17");
            if (san == null) return false;

            var text = san.Format(true);
            return ips.All(ip => text.Contains(ip.ToString()));
        }

        static X509Certificate2 CreateSelfSignedCertificate(List<IPAddress> localIps)
        {
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName("localhost");
            sanBuilder.AddIpAddress(IPAddress.Loopback);
            foreach (var ip in localIps)
                sanBuilder.AddIpAddress(ip);

            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                new X500DistinguishedName("CN=iPhone Webcam (self-signed)"),
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    false));
            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false)); // server auth
            request.CertificateExtensions.Add(sanBuilder.Build());

            var cert = request.CreateSelfSigned(
                DateTimeOffset.Now.AddDays(-1),
                DateTimeOffset.Now.AddYears(1));

            // Round-tripping through PFX makes the private key usable by Kestrel on
            // Windows; the key from CreateSelfSigned alone is not directly reusable.
            return new X509Certificate2(
                cert.Export(X509ContentType.Pfx), (string?)null,
                X509KeyStorageFlags.Exportable);
        }

        static async Task SendJson(WebSocket ws, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await lockObj.WaitAsync();
            try
            {
                await ws.SendAsync(new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text, true, CancellationToken.None);
            }
            finally { lockObj.Release(); }
        }

        /// <summary>
        /// Reads raw RGBA frames off the websocket into the frame store.
        /// One frame can arrive as several websocket fragments, so we keep
        /// reading until the message ends before treating it as complete -
        /// storing a partial frame would show as a torn picture.
        /// </summary>
        static async Task ReceiveFrames(WebSocket ws)
        {
            const int OutW = 1280, OutH = 720;
            var buffer = new byte[OutW * OutH * 4];
            var reported = 0L;

            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var total = 0;
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(
                            new ArraySegment<byte>(buffer, total, buffer.Length - total),
                            CancellationToken.None);
                        if (result.CloseStatus.HasValue) return;
                        total += result.Count;
                    } while (!result.EndOfMessage && total < buffer.Length);

                    // Text messages on this socket are diagnostics, not frames.
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        sourceStats = Encoding.UTF8.GetString(buffer, 0, total);
                        continue;
                    }

                    frames.Write(buffer.AsSpan(0, total), OutW, OutH);

                    // Log the first frame and then every 300 (~10s at 30fps),
                    // so the console shows life without becoming a firehose.
                    var seq = frames.Sequence;
                    if (seq == 1 || seq - reported >= 300)
                    {
                        Console.WriteLine($"Frames received: {seq} ({OutW}x{OutH})");
                        reported = seq;
                    }
                }
            }
            catch (WebSocketException ex)
            {
                Console.WriteLine($"[frames] socket error: {ex.Message}");
            }
        }

        static async Task RelayMessages(WebSocket source, Func<WebSocket?> getTarget, string label)
        {
            var buffer = new byte[1024 * 16];
            try
            {
                while (source.State == WebSocketState.Open)
                {
                    var result = await source.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.CloseStatus.HasValue) break;

                    var target = getTarget();
                    if (target != null && target.State == WebSocketState.Open)
                    {
                        await lockObj.WaitAsync();
                        try
                        {
                            await target.SendAsync(
                                new ArraySegment<byte>(buffer, 0, result.Count),
                                WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                        finally { lockObj.Release(); }
                    }
                    else
                    {
                        Console.WriteLine($"[{label}] No peer connected yet - message dropped.");
                    }
                }
            }
            catch (WebSocketException ex)
            {
                Console.WriteLine($"[{label}] WebSocket error: {ex.Message}");
            }
        }
    }
}

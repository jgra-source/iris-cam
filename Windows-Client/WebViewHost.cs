using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WindowsWebcamReceiver
{
    /// <summary>
    /// Runs receiver.html in a browser the user never sees.
    ///
    /// The browser is doing the real work here - it is what decodes the iPhone's
    /// WebRTC video. WebView2 is the same engine as Edge and ships with Windows,
    /// so this costs no extra download and no licensing.
    ///
    /// Two things about this are less obvious than they look:
    ///
    /// 1. The window is moved far off-screen rather than hidden. A hidden or
    ///    minimised window is treated by the browser as not worth drawing, and
    ///    it slows frame delivery to a crawl - the same reason video stutters in
    ///    a background tab. Off-screen keeps it drawing at full speed.
    ///
    /// 2. Our HTTPS certificate is self-signed, which the browser would normally
    ///    refuse. We allow it only for our own address on this machine.
    /// </summary>
    public static class WebViewHost
    {
        static Form? form;

        public static void Start(string url)
        {
            var thread = new Thread(() => RunUiLoop(url))
            {
                IsBackground = true,   // never keeps the app alive on its own
                Name = "WebViewHost"
            };
            thread.SetApartmentState(ApartmentState.STA);   // required by WinForms/WebView2
            thread.Start();
        }

        public static void Stop()
        {
            try { form?.Invoke(() => Application.ExitThread()); } catch { /* already gone */ }
        }

        static void RunUiLoop(string url)
        {
            try
            {
                Application.EnableVisualStyles();

                form = new Form
                {
                    Text = "Iris Camera (background)",
                    ShowInTaskbar = false,
                    FormBorderStyle = FormBorderStyle.FixedToolWindow,
                    StartPosition = FormStartPosition.Manual,
                    // Off-screen, not hidden - see the note above.
                    Location = new System.Drawing.Point(-32000, -32000),
                    Size = new System.Drawing.Size(1280, 720)
                };

                var webView = new WebView2 { Dock = DockStyle.Fill };
                form.Controls.Add(webView);
                form.Load += async (_, _) => await InitialiseAsync(webView, url);

                Application.Run(form);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[webview] host failed: {ex.Message}");
                Console.WriteLine("[webview] falling back to manual mode - open receiver.html in a browser.");
            }
        }

        static async Task InitialiseAsync(WebView2 webView, string url)
        {
            try
            {
                // Keep browser data beside the app rather than in a temp folder,
                // so camera permissions and cache survive restarts.
                var dataFolder = Path.Combine(AppContext.BaseDirectory, "webview-data");
                Directory.CreateDirectory(dataFolder);

                var options = new CoreWebView2EnvironmentOptions
                {
                    // Stop the browser slowing timers and rendering down when it
                    // thinks nobody is looking, and let video start without a click.
                    AdditionalBrowserArguments = string.Join(' ',
                        "--disable-background-timer-throttling",
                        "--disable-renderer-backgrounding",
                        "--disable-backgrounding-occluded-windows",
                        "--autoplay-policy=no-user-gesture-required")
                };

                var env = await CoreWebView2Environment.CreateAsync(null, dataFolder, options);
                await webView.EnsureCoreWebView2Async(env);

                var core = webView.CoreWebView2;

                // Accept our own self-signed certificate, and only ours.
                core.ServerCertificateErrorDetected += (_, e) =>
                {
                    var host = new Uri(e.RequestUri).Host;
                    if (host is "localhost" or "127.0.0.1")
                        e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
                    else
                        e.Action = CoreWebView2ServerCertificateErrorAction.Default;
                };

                core.ProcessFailed += (_, e) =>
                    Console.WriteLine($"[webview] browser process failed: {e.ProcessFailedKind}");

                // Surface page errors in our console; this window has no devtools
                // anyone can reach.
                core.WebMessageReceived += (_, e) =>
                    Console.WriteLine($"[webview page] {e.TryGetWebMessageAsString()}");

                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;

                core.NavigationCompleted += (_, e) =>
                    Console.WriteLine(e.IsSuccess
                        ? "[webview] receiver page loaded - frames should start flowing."
                        : $"[webview] navigation failed: {e.WebErrorStatus}");

                core.Navigate(url);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[webview] could not start: {ex.Message}");
                Console.WriteLine("[webview] open receiver.html in a browser instead.");
            }
        }
    }
}

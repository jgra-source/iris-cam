using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsWebcamReceiver
{
    /// <summary>
    /// Keeps the shared buffer fed at a steady rate.
    ///
    /// The camera must receive frames continuously whether or not a phone is
    /// connected, so this loop always publishes something: the phone's picture
    /// when it is live, and an explanatory placeholder when it is not.
    ///
    /// It publishes on its own clock rather than reacting to arriving frames.
    /// A camera device expects an even rhythm, and this keeps the output steady
    /// even when the network delivers unevenly.
    /// </summary>
    public sealed class FramePublisher : IDisposable
    {
        readonly FrameStore store;
        readonly SharedFrameBuffer shared;
        readonly int width, height, targetFps;
        readonly CancellationTokenSource cts = new();
        Task? loop;

        int placeholderTick;
        bool lastWasLive;

        public FramePublisher(FrameStore store, int width, int height, int targetFps = 30)
        {
            this.store = store;
            this.width = width;
            this.height = height;
            this.targetFps = targetFps;
            shared = new SharedFrameBuffer(width, height);
        }

        public long PublishedFrames { get; private set; }
        public string BufferName => shared.Name;

        public void Start()
        {
            loop = Task.Run(() => RunAsync(cts.Token));
            Console.WriteLine($"Publishing {width}x{height} @ {targetFps}fps to shared memory '{shared.Name}'.");
        }

        async Task RunAsync(CancellationToken token)
        {
            var interval = TimeSpan.FromSeconds(1.0 / targetFps);
            var timer = new PeriodicTimer(interval);
            var sw = Stopwatch.StartNew();
            var lastLog = TimeSpan.Zero;

            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    var live = store.Read(out var w, out var h);
                    var isLive = live != null && store.IsLive && w == width && h == height;

                    if (isLive)
                    {
                        shared.Publish(live);
                    }
                    else
                    {
                        // Rendering text every frame would be wasteful, so the
                        // placeholder only advances a few times a second.
                        placeholderTick++;
                        var message = store.Sequence > 0 ? "Reconnecting" : "Waiting for iPhone";
                        shared.Publish(PlaceholderFrame.Render(width, height, placeholderTick, message));
                    }

                    PublishedFrames++;

                    if (isLive != lastWasLive)
                    {
                        Console.WriteLine(isLive
                            ? "Camera source: iPhone (live)."
                            : "Camera source: placeholder (no phone connected).");
                        lastWasLive = isLive;
                    }

                    if (sw.Elapsed - lastLog > TimeSpan.FromSeconds(30))
                    {
                        Console.WriteLine($"Published {PublishedFrames} frames so far ({(isLive ? "live" : "placeholder")}).");
                        lastLog = sw.Elapsed;
                    }
                }
            }
            catch (OperationCanceledException) { /* shutting down */ }
            catch (Exception ex)
            {
                Console.WriteLine($"[publisher] stopped: {ex.Message}");
            }
        }

        public void Dispose()
        {
            cts.Cancel();
            try { loop?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
            shared.Dispose();
            cts.Dispose();
        }
    }
}

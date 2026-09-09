using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsWebcamReceiver
{
    /// <summary>
    /// Publishes phone frames as soon as they arrive, without a second frame clock
    /// or a cloned image. Windows reads the latest picture at its own camera rate.
    /// A timer still supplies placeholders when the phone is disconnected, and
    /// one lock keeps those writes from colliding with newly arriving video.
    /// </summary>
    public sealed class FramePublisher : IDisposable
    {
        readonly FrameStore store;
        readonly SharedFrameBuffer shared;
        readonly int width, height, targetFps;
        readonly CancellationTokenSource cts = new();
        readonly object publishGate = new();
        Task? loop;

        int placeholderTick;
        bool lastWasLive;
        bool disposed;
        long publishedFrames;

        public FramePublisher(FrameStore store, int width, int height, int targetFps = 30, string? path = null)
        {
            this.store = store;
            this.width = width;
            this.height = height;
            this.targetFps = targetFps;
            shared = new SharedFrameBuffer(width, height, path);
        }

        public long PublishedFrames => Interlocked.Read(ref publishedFrames);
        public string BufferName => shared.Name;

        /// <summary>Hands a complete phone frame to the driver before returning.</summary>
        public void PublishFrame(ReadOnlySpan<byte> rgba, int frameWidth, int frameHeight)
        {
            // Never advertise a partial frame or a size the installed camera cannot read.
            if (frameWidth != width || frameHeight != height || rgba.Length < width * height * 4) return;
            lock (publishGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                store.Write(rgba, width, height);
                shared.Publish(rgba);
                Interlocked.Increment(ref publishedFrames);
                ReportSource(true);
            }
        }

        public void Start()
        {
            loop = Task.Run(() => RunAsync(cts.Token));
            Console.WriteLine($"Publishing {width}x{height} on arrival; placeholders @ {targetFps}fps to '{shared.Name}'.");
        }

        async Task RunAsync(CancellationToken token)
        {
            var interval = TimeSpan.FromSeconds(1.0 / targetFps);
            using var timer = new PeriodicTimer(interval);
            var sw = Stopwatch.StartNew();
            var lastLog = TimeSpan.Zero;

            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    // A healthy phone has already published its newest frame. Only
                    // absent video needs a timed write; never overwrite a reconnect.
                    lock (publishGate)
                    {
                        if (disposed) return;
                        if (!store.IsLive)
                        {
                            placeholderTick++;
                            var message = store.Sequence > 0 ? "Reconnecting" : "Waiting for iPhone";
                            shared.Publish(PlaceholderFrame.Render(width, height, placeholderTick, message));
                            Interlocked.Increment(ref publishedFrames);
                            ReportSource(false);
                        }
                    }

                    if (sw.Elapsed - lastLog > TimeSpan.FromSeconds(30))
                    {
                        Console.WriteLine($"Published {PublishedFrames} frames so far ({(store.IsLive ? "live" : "placeholder")}).");
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

        // Called under the writer lock so reconnect and placeholder messages stay in order.
        void ReportSource(bool isLive)
        {
            if (isLive == lastWasLive) return;
            Console.WriteLine(isLive
                ? "Camera source: iPhone (live)."
                : "Camera source: placeholder (no phone connected).");
            lastWasLive = isLive;
        }

        public void Dispose()
        {
            // Stop accepting frames before releasing the mapping used by both writers.
            lock (publishGate)
            {
                if (disposed) return;
                disposed = true;
                cts.Cancel();
            }
            try { loop?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
            shared.Dispose();
            cts.Dispose();
        }
    }
}

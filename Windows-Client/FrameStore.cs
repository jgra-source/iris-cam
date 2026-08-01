using System;
using System.Threading;

namespace WindowsWebcamReceiver
{
    /// <summary>
    /// Holds the most recent video frame received from the browser.
    ///
    /// Only the latest frame matters - a webcam has no use for old ones, so a
    /// new frame simply replaces the previous one. Writers never block and
    /// readers never wait.
    ///
    /// Frames are stored in two alternating buffers rather than one. The reader
    /// is handed whichever buffer is not currently being written to, so it can
    /// never observe a half-written frame. This is the tearing problem noted in
    /// plan section 2.4, solved here on the C# side; the same idea will apply
    /// again in shared memory once the native camera reads these frames.
    /// </summary>
    public sealed class FrameStore
    {
        readonly byte[][] buffers = new byte[2][];
        int writeIndex;          // buffer currently safe to write into
        long sequence;           // increments once per completed frame
        int width, height;
        DateTime lastFrameUtc = DateTime.MinValue;
        readonly object gate = new();

        public int Width { get { lock (gate) return width; } }
        public int Height { get { lock (gate) return height; } }
        public long Sequence => Interlocked.Read(ref sequence);

        /// <summary>True when a frame arrived recently enough to still be worth showing.</summary>
        public bool IsLive
        {
            get { lock (gate) return (DateTime.UtcNow - lastFrameUtc).TotalSeconds < 2; }
        }

        public double AgeSeconds
        {
            get { lock (gate) return lastFrameUtc == DateTime.MinValue
                ? double.PositiveInfinity
                : (DateTime.UtcNow - lastFrameUtc).TotalSeconds; }
        }

        /// <summary>
        /// Stores a frame. <paramref name="rgba"/> is raw RGBA pixels, row by row
        /// from the top, exactly as a browser canvas produces them.
        /// </summary>
        public void Write(ReadOnlySpan<byte> rgba, int w, int h)
        {
            var needed = w * h * 4;
            if (rgba.Length < needed) return;   // partial frame - drop it rather than show garbage

            lock (gate)
            {
                var target = buffers[writeIndex];
                if (target == null || target.Length != needed)
                {
                    target = new byte[needed];
                    buffers[writeIndex] = target;
                }

                rgba.Slice(0, needed).CopyTo(target);
                width = w;
                height = h;
                lastFrameUtc = DateTime.UtcNow;

                // Publish by flipping which buffer is "current". Readers below
                // take the other one, so they never touch what we just wrote into.
                writeIndex = 1 - writeIndex;
                Interlocked.Increment(ref sequence);
            }
        }

        /// <summary>
        /// Returns a copy of the latest complete frame, or null if none yet.
        /// </summary>
        public byte[]? Read(out int w, out int h)
        {
            lock (gate)
            {
                w = width; h = height;
                var readable = buffers[1 - writeIndex];
                if (readable == null || w == 0 || h == 0) return null;
                return (byte[])readable.Clone();
            }
        }

        /// <summary>
        /// Latest frame as a 32-bit BMP. Used to eyeball, from an ordinary browser,
        /// that C# is really holding correct pixels - not to feed the camera.
        /// </summary>
        public byte[]? ReadAsBmp()
        {
            var rgba = Read(out var w, out var h);
            if (rgba == null) return null;

            const int headerSize = 54;
            var pixelBytes = w * h * 4;
            var bmp = new byte[headerSize + pixelBytes];

            // BITMAPFILEHEADER
            bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
            WriteInt32(bmp, 2, bmp.Length);
            WriteInt32(bmp, 10, headerSize);
            // BITMAPINFOHEADER
            WriteInt32(bmp, 14, 40);
            WriteInt32(bmp, 18, w);
            WriteInt32(bmp, 22, h);          // positive height = rows stored bottom-up
            bmp[26] = 1;                      // planes
            bmp[28] = 32;                     // bits per pixel
            WriteInt32(bmp, 34, pixelBytes);

            // Canvas gives RGBA top-down; BMP wants BGRA bottom-up.
            for (var y = 0; y < h; y++)
            {
                var src = y * w * 4;
                var dst = headerSize + (h - 1 - y) * w * 4;
                for (var x = 0; x < w; x++)
                {
                    bmp[dst + x * 4 + 0] = rgba[src + x * 4 + 2]; // B
                    bmp[dst + x * 4 + 1] = rgba[src + x * 4 + 1]; // G
                    bmp[dst + x * 4 + 2] = rgba[src + x * 4 + 0]; // R
                    bmp[dst + x * 4 + 3] = 255;
                }
            }
            return bmp;
        }

        static void WriteInt32(byte[] b, int offset, int value)
        {
            b[offset + 0] = (byte)value;
            b[offset + 1] = (byte)(value >> 8);
            b[offset + 2] = (byte)(value >> 16);
            b[offset + 3] = (byte)(value >> 24);
        }
    }
}

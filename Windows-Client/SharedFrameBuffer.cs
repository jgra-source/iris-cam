using System;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace WindowsWebcamReceiver
{
    /// <summary>
    /// Publishes the current frame into shared memory, where the virtual camera
    /// can pick it up.
    ///
    /// The camera runs inside other programs - Teams, Chrome, Zoom - not inside
    /// this one. Shared memory is how a frame crosses that boundary without
    /// copying it through a network socket or a pipe.
    ///
    /// The tricky part is that the reader is a stranger running at its own pace,
    /// so it must never catch a frame mid-write and show a torn picture. Two
    /// things prevent that:
    ///
    ///   1. Two frame slots. We always write into the one nobody is reading,
    ///      then point readers at it. A reader is never looking at the slot
    ///      being written.
    ///   2. A counter that changes before and after every write. A reader checks
    ///      it on both sides of its copy; if it moved, the copy may be mixed and
    ///      the reader simply tries again. This is a standard technique - it lets
    ///      readers work without locks, so a stalled reader can never block the
    ///      camera.
    ///
    /// Pixels are stored as BGRA, which is what Media Foundation calls RGB32 -
    /// the format the camera will hand to Windows.
    /// </summary>
    public sealed class SharedFrameBuffer : IDisposable
    {
        public const string DefaultName = "Local\\iPhoneWebcamFrames";

        // "IPWC" - lets a reader confirm it is looking at our buffer and not
        // some other program's leftovers.
        const uint Magic = 0x43575049;
        const uint Version = 1;
        const int HeaderSize = 64;

        // Header layout (byte offsets):
        //   0 magic  4 version  8 width  12 height
        //  16 stride 20 slotSize  24 sequence(int64)  32 activeSlot
        const int OffMagic = 0, OffVersion = 4, OffWidth = 8, OffHeight = 12;
        const int OffStride = 16, OffSlotSize = 20, OffSequence = 24, OffActiveSlot = 32;

        readonly MemoryMappedFile mmf;
        readonly MemoryMappedViewAccessor view;
        readonly int width, height, slotSize;
        long sequence;
        int activeSlot;

        public SharedFrameBuffer(int width, int height, string name = DefaultName)
        {
            this.width = width;
            this.height = height;
            slotSize = width * height * 4;

            var capacity = HeaderSize + slotSize * 2L;
            mmf = MemoryMappedFile.CreateOrOpen(name, capacity, MemoryMappedFileAccess.ReadWrite);
            view = mmf.CreateViewAccessor(0, capacity, MemoryMappedFileAccess.ReadWrite);

            view.Write(OffMagic, Magic);
            view.Write(OffVersion, Version);
            view.Write(OffWidth, width);
            view.Write(OffHeight, height);
            view.Write(OffStride, width * 4);
            view.Write(OffSlotSize, slotSize);
            view.Write(OffSequence, 0L);
            view.Write(OffActiveSlot, 0);
        }

        public long Sequence => Interlocked.Read(ref sequence);
        public string Name { get; } = DefaultName;

        /// <summary>
        /// Publishes one frame. <paramref name="rgba"/> is raw RGBA as produced
        /// by the browser; it is converted to BGRA on the way in.
        /// </summary>
        public void Publish(ReadOnlySpan<byte> rgba)
        {
            if (rgba.Length < slotSize) return;   // never publish a partial frame

            var target = 1 - activeSlot;          // the slot nobody is reading
            var offset = HeaderSize + target * slotSize;

            // Odd sequence means "a write is in progress" - a reader seeing an
            // odd value, or a different value afterwards, knows to retry.
            var next = Interlocked.Increment(ref sequence);
            view.Write(OffSequence, next);

            var buffer = rentBuffer ??= new byte[slotSize];
            for (var i = 0; i < slotSize; i += 4)
            {
                buffer[i + 0] = rgba[i + 2]; // B
                buffer[i + 1] = rgba[i + 1]; // G
                buffer[i + 2] = rgba[i + 0]; // R
                buffer[i + 3] = 255;         // A
            }
            view.WriteArray(offset, buffer, 0, slotSize);

            // Point readers at the slot we just filled, then close the write.
            view.Write(OffActiveSlot, target);
            activeSlot = target;

            next = Interlocked.Increment(ref sequence);
            view.Write(OffSequence, next);
        }

        [ThreadStatic] static byte[]? rentBuffer;

        public void Dispose()
        {
            view.Dispose();
            mmf.Dispose();
        }
    }
}

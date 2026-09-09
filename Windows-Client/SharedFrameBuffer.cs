using System;
using System.IO;
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
    ///
    /// The buffer is backed by a FILE rather than a named shared-memory object,
    /// and that detail matters. Windows loads the camera inside a system service
    /// that runs in a different session to this app. Named objects created with
    /// the ordinary "Local\" prefix are private to one session, so the camera
    /// looked for our buffer and found nothing. The "Global\" prefix would cross
    /// sessions but needs a privilege a normal program does not have. A plain
    /// file path belongs to no session at all, so both sides simply open it.
    /// </summary>
    public sealed class SharedFrameBuffer : IDisposable
    {
        /// <summary>
        /// Shared with the camera driver, which opens this exact path.
        /// ProgramData is used because every account can read it, including the
        /// restricted one the camera service runs under.
        /// </summary>
        public static readonly string DefaultPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Iris", "frames.bin");

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
        readonly FileStream file;
        readonly int width, height, slotSize;
        long sequence;
        int activeSlot;

        public SharedFrameBuffer(int width, int height, string? path = null)
        {
            this.width = width;
            this.height = height;
            slotSize = width * height * 4;
            Path_ = path ?? DefaultPath;

            var capacity = HeaderSize + slotSize * 2L;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_)!);

            // FileShare.ReadWrite so the camera can read while we keep writing.
            file = new FileStream(Path_, FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.ReadWrite);
            if (file.Length < capacity) file.SetLength(capacity);

            mmf = MemoryMappedFile.CreateFromFile(file, null, capacity,
                MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: true);
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
        public string Path_ { get; }
        public string Name => Path_;

        /// <summary>
        /// Publishes one frame. <paramref name="rgba"/> is raw RGBA as produced
        /// by the browser; it is converted to BGRA on the way in.
        /// </summary>
        public unsafe void Publish(ReadOnlySpan<byte> rgba)
        {
            if (rgba.Length < slotSize) return;   // never publish a partial frame

            var target = 1 - activeSlot;          // the slot nobody is reading
            var offset = HeaderSize + target * slotSize;

            // Convert directly into the spare slot. WriteArray<byte> walked every
            // byte through a generic accessor and exceeded a 30fps frame budget.
            // AcquirePointer keeps the mapping alive until this write is finished.
            byte* pointer = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            try
            {
                var destination = new Span<byte>(pointer + view.PointerOffset + offset, slotSize);
                for (var pixelOffset = 0; pixelOffset < slotSize; pixelOffset += 4)
                {
                    destination[pixelOffset] = rgba[pixelOffset + 2];
                    destination[pixelOffset + 1] = rgba[pixelOffset + 1];
                    destination[pixelOffset + 2] = rgba[pixelOffset];
                    destination[pixelOffset + 3] = 255;
                }
            }
            finally { view.SafeMemoryMappedViewHandle.ReleasePointer(); }

            // Only the swap needs protecting. An odd counter means "changing
            // right now"; a reader that sees it simply waits and looks again.
            //
            // Raising the flag around the copy above instead would hold it open
            // for milliseconds every frame, and a reader could hit it several
            // times in a row, give up, and show its fallback picture - which
            // looks like flickering. Keeping the flag up for only these few
            // instructions makes that vanishingly unlikely.
            var next = Interlocked.Increment(ref sequence);
            view.Write(OffSequence, next);      // odd: swap in progress

            view.Write(OffActiveSlot, target);
            activeSlot = target;

            next = Interlocked.Increment(ref sequence);
            view.Write(OffSequence, next);      // even: readers may proceed
        }

        public void Dispose()
        {
            view.Dispose();
            mmf.Dispose();
            file.Dispose();
        }
    }
}

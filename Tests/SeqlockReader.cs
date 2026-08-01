using System.IO.MemoryMappedFiles;

namespace Iris.Tests;

/// <summary>
/// A stand-in for the camera driver, written in C# so the tests can run without
/// a C++ build.
///
/// It is a deliberate line-by-line copy of the reading half of
/// Virtual-Camera-Driver/VirtualCameraMediaSource/SharedFrameReader.h: same
/// offsets, same checks, same four attempts, same order of operations. That is
/// the whole point - if the app's writing side and the driver's reading side
/// ever stop agreeing, these tests should notice before a user does.
///
/// DriverContractTests checks that this copy has not drifted from the real one.
/// </summary>
sealed class SeqlockReader : IDisposable
{
    public const uint Magic = 0x43575049;   // 'IPWC'
    public const int HeaderSize = 64;
    public const int OffMagic = 0, OffVersion = 4, OffWidth = 8, OffHeight = 12;
    public const int OffStride = 16, OffSlotSize = 20, OffSequence = 24, OffActiveSlot = 32;

    readonly FileStream file;
    readonly MemoryMappedFile mmf;
    readonly MemoryMappedViewAccessor view;

    public SeqlockReader(string path)
    {
        // Read-only, and tolerant of the writer holding the same file open -
        // exactly the sharing flags the driver passes to CreateFileW.
        file = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        mmf = MemoryMappedFile.CreateFromFile(file, null, 0,
            MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: true);
        view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
    }

    public uint ReadMagic() => view.ReadUInt32(OffMagic);
    public uint ReadVersion() => view.ReadUInt32(OffVersion);
    public int ReadWidth() => view.ReadInt32(OffWidth);
    public int ReadHeight() => view.ReadInt32(OffHeight);
    public int ReadStride() => view.ReadInt32(OffStride);
    public int ReadSlotSize() => view.ReadInt32(OffSlotSize);
    public long ReadSequence() => view.ReadInt64(OffSequence);
    public int ReadActiveSlot() => view.ReadInt32(OffActiveSlot);

    /// <summary>Raw bytes of one slot, ignoring the sequence counter entirely.</summary>
    public byte[] ReadSlotRaw(int slot, int slotSize)
    {
        var buffer = new byte[slotSize];
        view.ReadArray(HeaderSize + slot * slotSize, buffer, 0, slotSize);
        return buffer;
    }

    /// <summary>
    /// The real thing: copies the newest frame, and returns false rather than
    /// hand back bytes that might be half of one frame and half of the next.
    /// </summary>
    public bool TryRead(byte[] dst, int width, int height)
    {
        if (width == 0 || height == 0) return false;

        var magic = view.ReadUInt32(OffMagic);
        var srcW = view.ReadInt32(OffWidth);
        var srcH = view.ReadInt32(OffHeight);
        var srcSlot = view.ReadInt32(OffSlotSize);

        if (magic != Magic) return false;
        if (srcW != width || srcH != height) return false;
        if (srcSlot != width * height * 4) return false;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var seqBefore = view.ReadInt64(OffSequence);
            if ((seqBefore & 1) != 0) continue;          // odd = write in progress

            var slot = view.ReadInt32(OffActiveSlot);
            if (slot != 0 && slot != 1) return false;

            view.ReadArray(HeaderSize + slot * srcSlot, dst, 0, srcSlot);

            var seqAfter = view.ReadInt64(OffSequence);
            if (seqAfter == seqBefore) return true;      // nothing moved: copy is whole
        }
        return false;
    }

    public void Dispose()
    {
        view.Dispose();
        mmf.Dispose();
        file.Dispose();
    }
}

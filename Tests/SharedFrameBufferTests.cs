using WindowsWebcamReceiver;
using Xunit;
using Xunit.Abstractions;

namespace Iris.Tests;

/// <summary>
/// Covers the hand-off from the app to the camera: the header the driver reads,
/// the colour conversion, and the swap that stops a reader seeing half a frame.
///
/// Every test writes to a throwaway file under the temp folder, never to the
/// real C:\ProgramData\Iris\frames.bin - running the tests must not disturb a
/// camera that happens to be live.
/// </summary>
public sealed class SharedFrameBufferTests : IDisposable
{
    readonly string path;
    readonly ITestOutputHelper output;

    public SharedFrameBufferTests(ITestOutputHelper output)
    {
        this.output = output;
        path = Path.Combine(Path.GetTempPath(), "iris-tests",
            Guid.NewGuid().ToString("N") + ".bin");
    }

    public void Dispose()
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* temp file */ }
    }

    // ---- the header the driver relies on ------------------------------------

    [Fact]
    public void Header_IsWrittenWhereTheDriverLooksForIt()
    {
        using var buffer = new SharedFrameBuffer(64, 32, path);
        using var reader = new SeqlockReader(path);

        Assert.Equal(SeqlockReader.Magic, reader.ReadMagic());
        Assert.Equal(1u, reader.ReadVersion());
        Assert.Equal(64, reader.ReadWidth());
        Assert.Equal(32, reader.ReadHeight());
        Assert.Equal(64 * 4, reader.ReadStride());
        Assert.Equal(64 * 32 * 4, reader.ReadSlotSize());
        Assert.Equal(0, reader.ReadActiveSlot());
        Assert.Equal(0L, reader.ReadSequence());
    }

    [Fact]
    public void File_IsBigEnoughForBothSlots()
    {
        using (var buffer = new SharedFrameBuffer(64, 32, path))
        {
            buffer.Publish(Frame(64, 32, 1));
        }

        var expected = SeqlockReader.HeaderSize + 64 * 32 * 4 * 2L;
        Assert.True(new FileInfo(path).Length >= expected,
            $"file is {new FileInfo(path).Length} bytes, needs at least {expected}");
    }

    [Fact]
    public void Reader_RejectsAFrameOfTheWrongSize()
    {
        // The driver asks for the resolution it advertised to Windows. If the app
        // is publishing something else, showing it would garble the picture, so
        // the answer has to be "no frame" rather than "here, near enough".
        using var buffer = new SharedFrameBuffer(64, 32, path);
        buffer.Publish(Frame(64, 32, 5));
        using var reader = new SeqlockReader(path);

        Assert.False(reader.TryRead(new byte[128 * 64 * 4], 128, 64));
        Assert.True(reader.TryRead(new byte[64 * 32 * 4], 64, 32));
    }

    // ---- colour ------------------------------------------------------------

    [Fact]
    public void Publish_TurnsRgbaFromTheBrowserIntoBgraForWindows()
    {
        // Media Foundation's RGB32 is B,G,R,A in memory; a canvas gives R,G,B,A.
        // Getting this backwards makes faces look blue, which is the kind of bug
        // that gets blamed on lighting.
        using var buffer = new SharedFrameBuffer(2, 1, path);

        var rgba = new byte[] { 10, 20, 30, 40,      // pixel 0: R=10 G=20 B=30
                                200, 100, 50, 0 };   // pixel 1: R=200 G=100 B=50
        buffer.Publish(rgba);

        using var reader = new SeqlockReader(path);
        var got = new byte[2 * 1 * 4];
        Assert.True(reader.TryRead(got, 2, 1));

        Assert.Equal(new byte[] { 30, 20, 10, 255,
                                  50, 100, 200, 255 }, got);
    }

    [Fact]
    public void Publish_ForcesAlphaOpaque()
    {
        // A transparent frame would show as a black or ghosted picture in a call.
        // Whatever the browser sends in the alpha channel is discarded.
        using var buffer = new SharedFrameBuffer(4, 4, path);
        var rgba = new byte[4 * 4 * 4];
        for (var i = 0; i < rgba.Length; i += 4) { rgba[i] = 9; rgba[i + 1] = 9; rgba[i + 2] = 9; rgba[i + 3] = 0; }
        buffer.Publish(rgba);

        using var reader = new SeqlockReader(path);
        var got = new byte[4 * 4 * 4];
        Assert.True(reader.TryRead(got, 4, 4));

        for (var i = 3; i < got.Length; i += 4)
            Assert.Equal((byte)255, got[i]);
    }

    // ---- the swap ----------------------------------------------------------

    [Fact]
    public void Publish_AlternatesSlotsSoAReaderIsNeverInTheOneBeingWritten()
    {
        using var buffer = new SharedFrameBuffer(4, 4, path);
        using var reader = new SeqlockReader(path);

        Assert.Equal(0, reader.ReadActiveSlot());
        buffer.Publish(Frame(4, 4, 1));
        Assert.Equal(1, reader.ReadActiveSlot());
        buffer.Publish(Frame(4, 4, 2));
        Assert.Equal(0, reader.ReadActiveSlot());
        buffer.Publish(Frame(4, 4, 3));
        Assert.Equal(1, reader.ReadActiveSlot());
    }

    [Fact]
    public void Publish_LeavesThePreviousFrameIntactInTheOtherSlot()
    {
        // This is what makes the double buffer worth having: while frame 2 is
        // being written, frame 1 is still whole and still readable.
        using var buffer = new SharedFrameBuffer(4, 4, path);
        buffer.Publish(Frame(4, 4, 11));   // lands in slot 1
        buffer.Publish(Frame(4, 4, 22));   // lands in slot 0

        using var reader = new SeqlockReader(path);
        var slotSize = 4 * 4 * 4;
        var older = reader.ReadSlotRaw(1, slotSize);
        var newer = reader.ReadSlotRaw(0, slotSize);

        Assert.Equal((byte)11, older[0]);
        Assert.Equal((byte)22, newer[0]);
        Assert.Equal(0, reader.ReadActiveSlot());   // readers are pointed at the new one
    }

    [Fact]
    public void Publish_LeavesTheCounterEvenSoReadersMayProceed()
    {
        // Odd means "swap in progress". If Publish ever returned with an odd
        // counter, every reader would back off forever and the camera would sit
        // on its fallback picture.
        using var buffer = new SharedFrameBuffer(4, 4, path);
        using var reader = new SeqlockReader(path);

        for (var i = 1; i <= 5; i++)
        {
            buffer.Publish(Frame(4, 4, (byte)i));
            var seq = reader.ReadSequence();
            Assert.True(seq % 2 == 0, $"counter was {seq} after {i} frames - readers would block");
            Assert.Equal((long)i * 2, seq);
        }
    }

    [Fact]
    public void Publish_IgnoresAFrameThatArrivedShort()
    {
        // A truncated frame would leave the bottom of the picture as whatever was
        // there before. Dropping it means one skipped frame instead, which nobody
        // can see at 30 a second.
        using var buffer = new SharedFrameBuffer(8, 8, path);
        buffer.Publish(Frame(8, 8, 7));

        using var reader = new SeqlockReader(path);
        var before = reader.ReadSequence();

        buffer.Publish(new byte[8 * 8 * 4 - 1]);

        Assert.Equal(before, reader.ReadSequence());
        var got = new byte[8 * 8 * 4];
        Assert.True(reader.TryRead(got, 8, 8));
        Assert.Equal((byte)7, got[0]);   // still the last good frame
    }

    [Fact]
    public void Publish_AcceptsAFrameWithBytesToSpare()
    {
        // The browser may hand over a buffer larger than one frame; only the
        // first frame's worth is used.
        using var buffer = new SharedFrameBuffer(4, 4, path);
        var oversized = new byte[4 * 4 * 4 + 64];
        for (var i = 0; i < oversized.Length; i++) oversized[i] = 3;
        buffer.Publish(oversized);

        using var reader = new SeqlockReader(path);
        Assert.Equal(2L, reader.ReadSequence());
    }

    [Fact]
    public void ASecondBufferAtABiggerSizeStillWorks()
    {
        // Guard the old per-thread scratch-buffer failure even though publishing
        // now writes directly into each instance's own mapping.
        var second = path + ".2";
        try
        {
            using (var small = new SharedFrameBuffer(4, 4, path))
                small.Publish(Frame(4, 4, 1));

            using (var large = new SharedFrameBuffer(64, 64, second))
                large.Publish(Frame(64, 64, 2));

            using var reader = new SeqlockReader(second);
            var got = new byte[64 * 64 * 4];
            Assert.True(reader.TryRead(got, 64, 64));
            Assert.Equal((byte)2, got[0]);
        }
        finally
        {
            try { if (File.Exists(second)) File.Delete(second); } catch { /* temp file */ }
        }
    }

    // ---- the part that only shows up under load -----------------------------

    [Fact]
    public void Publish_AtCameraResolution_PreservesPixelsAndReportsCost()
    {
        // Measure the real camera size without touching the running camera file.
        const int width = 1280, height = 720, frameCount = 120;
        using var buffer = new SharedFrameBuffer(width, height, path);
        var pixels = Frame(width, height, 42);
        for (var warmup = 0; warmup < 10; warmup++) buffer.Publish(pixels);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var frameNumber = 0; frameNumber < frameCount; frameNumber++)
            buffer.Publish(pixels);
        clock.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        output.WriteLine($"720p publish: {clock.Elapsed.TotalMilliseconds / frameCount:F3} ms/frame; {allocated / frameCount} allocated bytes/frame");

        // Timing is reported, not asserted: busy machines must not fail a correctness test.
        using var reader = new SeqlockReader(path);
        var received = new byte[pixels.Length];
        Assert.True(reader.TryRead(received, width, height));
        for (var pixelOffset = 0; pixelOffset < received.Length; pixelOffset += 4)
        {
            Assert.Equal((byte)42, received[pixelOffset]);
            Assert.Equal((byte)42, received[pixelOffset + 1]);
            Assert.Equal((byte)42, received[pixelOffset + 2]);
            Assert.Equal((byte)255, received[pixelOffset + 3]);
        }
    }

    [Fact]
    public void AReaderNeverSeesHalfOfOneFrameAndHalfOfAnother()
    {
        // The real test of the whole design. One thread publishes as fast as it
        // can; another reads the same way the driver does. Every frame is painted
        // a single flat colour, so any mixing of two frames shows up immediately
        // as two different colours in one picture - which is exactly what tearing
        // looks like on screen.
        const int w = 160, h = 90, frames = 3000;
        var slotSize = w * h * 4;

        using var buffer = new SharedFrameBuffer(w, h, path);
        using var reader = new SeqlockReader(path);

        // One frame before the reader starts. Until something is published, both
        // slots are still the zeroes the file was created with, and a reader is
        // perfectly entitled to return those - the header is valid and it has no
        // way to know they are not a real, very black frame. The driver does
        // exactly the same for the instant before the first frame arrives, which
        // is harmless. Without this the reader spends that startup window
        // reporting blank frames as torn ones.
        buffer.Publish(Frame(w, h, 200));

        var stop = false;
        var torn = 0;
        var good = 0;
        var refused = 0;

        var readerThread = new Thread(() =>
        {
            var got = new byte[slotSize];
            while (!Volatile.Read(ref stop))
            {
                if (!reader.TryRead(got, w, h)) { refused++; continue; }

                var expected = got[0];
                for (var i = 0; i < slotSize; i += 4)
                {
                    if (got[i] != expected || got[i + 1] != expected ||
                        got[i + 2] != expected || got[i + 3] != 255)
                    {
                        torn++;
                        break;
                    }
                }
                good++;
            }
        });
        readerThread.Start();

        try
        {
            var scratch = new byte[slotSize];
            for (var n = 0; n < frames; n++)
            {
                // 251 is prime, so consecutive frames keep a different colour for
                // a long run rather than repeating on a power-of-two cycle.
                Array.Fill(scratch, (byte)(n % 251));
                buffer.Publish(scratch);
            }
        }
        finally
        {
            // Even if publishing blew up, the reader has to be stopped before the
            // buffer it is reading gets disposed - otherwise the failure arrives
            // as a crashed test host instead of a failed test.
            Volatile.Write(ref stop, true);
            readerThread.Join(TimeSpan.FromSeconds(30));
        }

        Assert.Equal(0, torn);
        Assert.True(good > 0, "the reader never managed a single read - the test proved nothing");
    }

    [Fact]
    public void TheCounterOnlyEverMovesForwards()
    {
        // A reader decides a copy is whole by seeing the same counter twice. If
        // the counter could ever repeat a value it had before, a reader could be
        // fooled into accepting a mixed frame.
        const int w = 64, h = 64, frames = 2000;

        using var buffer = new SharedFrameBuffer(w, h, path);
        using var reader = new SeqlockReader(path);

        var stop = false;
        var wentBackwards = false;

        var watcher = new Thread(() =>
        {
            long previous = 0;
            while (!Volatile.Read(ref stop))
            {
                var now = reader.ReadSequence();
                if (now < previous) { wentBackwards = true; return; }
                previous = now;
            }
        });
        watcher.Start();

        try
        {
            var scratch = new byte[w * h * 4];
            for (var n = 0; n < frames; n++)
            {
                Array.Fill(scratch, (byte)n);
                buffer.Publish(scratch);
            }
        }
        finally
        {
            Volatile.Write(ref stop, true);
            watcher.Join(TimeSpan.FromSeconds(30));
        }

        Assert.False(wentBackwards);
        Assert.Equal((long)frames * 2, reader.ReadSequence());
    }

    /// <summary>One flat-coloured RGBA frame, so tearing is visible as two colours.</summary>
    static byte[] Frame(int w, int h, byte value)
    {
        var rgba = new byte[w * h * 4];
        Array.Fill(rgba, value);
        return rgba;
    }
}

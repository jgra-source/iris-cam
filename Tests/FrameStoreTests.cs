using WindowsWebcamReceiver;
using Xunit;

namespace Iris.Tests;

/// <summary>
/// Covers the holder that sits between the browser and everything else: it keeps
/// the newest frame, decides whether the phone is still sending, and can turn a
/// frame into a BMP for the "is C# really holding pixels?" check.
/// </summary>
public sealed class FrameStoreTests
{
    // ---- storing and fetching ----------------------------------------------

    [Fact]
    public void Read_BeforeAnyFrameArrives_ReturnsNothing()
    {
        var store = new FrameStore();
        Assert.Null(store.Read(out var w, out var h));
        Assert.Equal(0, w);
        Assert.Equal(0, h);
        Assert.Null(store.ReadAsBmp());
    }

    [Fact]
    public void Write_ThenRead_GivesBackTheSamePixels()
    {
        var store = new FrameStore();
        var frame = Frame(4, 3, 42);
        store.Write(frame, 4, 3);

        var got = store.Read(out var w, out var h);
        Assert.NotNull(got);
        Assert.Equal(4, w);
        Assert.Equal(3, h);
        Assert.Equal(frame, got);
    }

    [Fact]
    public void Read_HandsBackACopy_SoCallersCannotCorruptTheStore()
    {
        // Read is used by the BMP endpoint and by whatever else wants a look. If
        // it handed out the live buffer, one careless caller could scribble on the
        // frame the camera is about to publish.
        var store = new FrameStore();
        store.Write(Frame(4, 4, 5), 4, 4);

        var first = store.Read(out _, out _)!;
        first[0] = 200;

        var second = store.Read(out _, out _)!;
        Assert.Equal((byte)5, second[0]);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void Write_ReplacesTheOldFrameRatherThanQueueingIt()
    {
        // A webcam has no use for old frames. Only the newest one is ever shown.
        var store = new FrameStore();
        store.Write(Frame(4, 4, 1), 4, 4);
        store.Write(Frame(4, 4, 2), 4, 4);
        store.Write(Frame(4, 4, 3), 4, 4);

        var got = store.Read(out _, out _)!;
        Assert.Equal((byte)3, got[0]);
        Assert.Equal(3L, store.Sequence);
    }

    [Fact]
    public void Write_CopesWithTheResolutionChanging()
    {
        // Rotating the phone changes the shape of what the browser sends. The
        // stored size and the stored pixels must not disagree, or the BMP writer
        // would read past the end of the buffer.
        var store = new FrameStore();
        store.Write(Frame(8, 4, 1), 8, 4);
        store.Write(Frame(4, 8, 2), 4, 8);

        var got = store.Read(out var w, out var h)!;
        Assert.Equal(4, w);
        Assert.Equal(8, h);
        Assert.Equal(4 * 8 * 4, got.Length);
        Assert.Equal((byte)2, got[0]);
    }

    [Fact]
    public void Write_IgnoresAFrameThatArrivedShort()
    {
        var store = new FrameStore();
        store.Write(Frame(4, 4, 9), 4, 4);
        var before = store.Sequence;

        store.Write(new byte[4 * 4 * 4 - 1], 4, 4);

        Assert.Equal(before, store.Sequence);
        Assert.Equal((byte)9, store.Read(out _, out _)![0]);
    }

    // ---- "is the phone still there?" ----------------------------------------

    [Fact]
    public void IsLive_IsFalseUntilAFrameArrives()
    {
        // This is what decides between the real picture and "Waiting for iPhone".
        var store = new FrameStore();
        Assert.False(store.IsLive);
        Assert.Equal(double.PositiveInfinity, store.AgeSeconds);

        store.Write(Frame(4, 4, 1), 4, 4);

        Assert.True(store.IsLive);
        Assert.True(store.AgeSeconds < 1);
    }

    [Fact]
    public void IsLive_StaysFalseAfterAFrameIsRejected()
    {
        var store = new FrameStore();
        store.Write(new byte[10], 4, 4);
        Assert.False(store.IsLive);
    }

    // ---- the BMP view -------------------------------------------------------

    [Fact]
    public void ReadAsBmp_WritesAHeaderAWindowsViewerWillAccept()
    {
        var store = new FrameStore();
        store.Write(Frame(4, 3, 1), 4, 3);
        var bmp = store.ReadAsBmp()!;

        Assert.Equal((byte)'B', bmp[0]);
        Assert.Equal((byte)'M', bmp[1]);
        Assert.Equal(bmp.Length, Int32At(bmp, 2));      // total file size
        Assert.Equal(54, Int32At(bmp, 10));             // where the pixels start
        Assert.Equal(40, Int32At(bmp, 14));             // BITMAPINFOHEADER size
        Assert.Equal(4, Int32At(bmp, 18));              // width
        Assert.Equal(3, Int32At(bmp, 22));              // height, positive = bottom-up
        Assert.Equal((byte)1, bmp[26]);                 // planes
        Assert.Equal((byte)32, bmp[28]);                // bits per pixel
        Assert.Equal(4 * 3 * 4, Int32At(bmp, 34));      // pixel bytes
        Assert.Equal(54 + 4 * 3 * 4, bmp.Length);
    }

    [Fact]
    public void ReadAsBmp_FlipsTheRowsAndSwapsTheColours()
    {
        // A browser canvas gives rows from the top and R,G,B,A per pixel. A BMP
        // with a positive height stores rows from the BOTTOM, and B,G,R,A. Get
        // either wrong and the test image comes out upside down or blue-faced -
        // which is precisely what this page exists to rule out.
        var store = new FrameStore();

        // Two rows, one pixel each. Top row red, bottom row blue.
        var rgba = new byte[] { 255, 0, 0, 255,      // row 0 (top): red
                                0, 0, 255, 255 };    // row 1 (bottom): blue
        store.Write(rgba, 1, 2);

        var bmp = store.ReadAsBmp()!;
        var pixels = bmp.Skip(54).ToArray();

        // First stored row is the bottom of the picture: blue, as B,G,R,A.
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, pixels.Take(4));
        // Second stored row is the top: red, as B,G,R,A.
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, pixels.Skip(4).Take(4));
    }

    [Fact]
    public void ReadAsBmp_ForcesAlphaOpaque()
    {
        var store = new FrameStore();
        var rgba = new byte[2 * 2 * 4];   // all zeroes, alpha included
        store.Write(rgba, 2, 2);

        var bmp = store.ReadAsBmp()!;
        for (var i = 54 + 3; i < bmp.Length; i += 4)
            Assert.Equal((byte)255, bmp[i]);
    }

    // ---- under load ---------------------------------------------------------

    [Fact]
    public void AReaderNeverSeesHalfOfOneFrameAndHalfOfAnother()
    {
        // Same idea as the shared-buffer test, one layer earlier. Each frame is a
        // single flat colour; two colours in one read would mean the reader caught
        // a buffer mid-write.
        const int w = 64, h = 64, frames = 5000;
        var store = new FrameStore();
        var stop = false;
        var torn = 0;
        var good = 0;

        var readerThread = new Thread(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                var got = store.Read(out var rw, out var rh);
                if (got == null) continue;
                if (got.Length != rw * rh * 4) { torn++; continue; }

                var expected = got[0];
                foreach (var b in got)
                {
                    if (b != expected) { torn++; break; }
                }
                good++;
            }
        });
        readerThread.Start();

        try
        {
            var scratch = new byte[w * h * 4];
            for (var n = 0; n < frames; n++)
            {
                Array.Fill(scratch, (byte)(n % 251));
                store.Write(scratch, w, h);
            }
        }
        finally
        {
            Volatile.Write(ref stop, true);
            readerThread.Join(TimeSpan.FromSeconds(30));
        }

        Assert.Equal(0, torn);
        Assert.True(good > 0, "the reader never managed a single read - the test proved nothing");
    }

    /// <summary>One flat-coloured RGBA frame, so tearing shows up as two colours.</summary>
    static byte[] Frame(int w, int h, byte value)
    {
        var rgba = new byte[w * h * 4];
        Array.Fill(rgba, value);
        return rgba;
    }

    static int Int32At(byte[] b, int offset) =>
        b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24);
}

using WindowsWebcamReceiver;
using Xunit;

namespace Iris.Tests;

/// <summary>
/// Checks that fresh phone frames reach the camera immediately and cannot be
/// overwritten by a placeholder. Every publisher uses a temporary mapping so
/// the real webcam stays untouched while these tests run.
/// </summary>
public sealed class FramePublisherTests : IDisposable
{
    readonly string path = Path.Combine(Path.GetTempPath(), "iris-tests", Guid.NewGuid() + ".bin");

    [Fact]
    public void AFrameIsPublishedBeforeAnyTimerStarts()
    {
        var store = new FrameStore();
        using var publisher = new FramePublisher(store, 64, 36, path: path);
        var pixels = Pixels(64, 36, 42);
        publisher.PublishFrame(pixels, 64, 36);

        // The driver must see the image when the receive call returns, not on a later tick.
        using var reader = new SeqlockReader(path);
        var received = new byte[pixels.Length];
        Assert.True(reader.TryRead(received, 64, 36));
        Assert.Equal(pixels, received);
        Assert.Equal(1, publisher.PublishedFrames);
        Assert.Equal(1, store.Sequence);
    }

    [Fact]
    public void PartialAndWrongSizedFramesDoNotReplaceTheLastGoodFrame()
    {
        var store = new FrameStore();
        using var publisher = new FramePublisher(store, 64, 36, path: path);
        publisher.PublishFrame(Pixels(64, 36, 42), 64, 36);
        publisher.PublishFrame(new byte[64 * 36 * 4 - 1], 64, 36);
        publisher.PublishFrame(Pixels(36, 64, 99), 36, 64);
        Assert.Equal(1, publisher.PublishedFrames);
        Assert.Equal(1, store.Sequence);
        Assert.Equal((byte)42, store.Read(out _, out _)![0]);
    }

    [Fact]
    public async Task TimerKeepsFallbackAliveButDoesNotRepublishLiveFrames()
    {
        var store = new FrameStore();
        using var publisher = new FramePublisher(store, 1280, 720, path: path);
        publisher.Start();
        await WaitFor(() => publisher.PublishedFrames >= 2);

        // An arriving frame takes over immediately and stops redundant timed copies.
        var pixels = Pixels(1280, 720, 42);
        publisher.PublishFrame(pixels, 1280, 720);
        var afterArrival = publisher.PublishedFrames;
        await Task.Delay(150);
        Assert.Equal(afterArrival, publisher.PublishedFrames);

        // A disconnected phone eventually gets a placeholder; reconnect wins again.
        await WaitFor(() => publisher.PublishedFrames > afterArrival);
        publisher.PublishFrame(pixels, 1280, 720);
        using var reader = new SeqlockReader(path);
        var received = new byte[pixels.Length];
        Assert.True(reader.TryRead(received, 1280, 720));
        Assert.Equal(pixels, received);
    }

    [Fact]
    public void ConcurrentArrivalsKeepTheStoreAndSharedFrameInTheSameOrder()
    {
        var store = new FrameStore();
        using var publisher = new FramePublisher(store, 64, 36, path: path);
        Parallel.For(0, 100, frameNumber =>
            publisher.PublishFrame(Pixels(64, 36, (byte)frameNumber), 64, 36));

        using var reader = new SeqlockReader(path);
        var received = new byte[64 * 36 * 4];
        Assert.True(reader.TryRead(received, 64, 36));
        Assert.Equal(store.Read(out _, out _), received);
        Assert.Equal(100, publisher.PublishedFrames);
    }

    // Poll only test state, with a deadline so a dead publisher fails instead of hanging.
    static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(condition(), "publisher did not produce the expected frame within five seconds");
    }

    static byte[] Pixels(int width, int height, byte shade)
    {
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, shade);
        for (var pixelOffset = 3; pixelOffset < pixels.Length; pixelOffset += 4) pixels[pixelOffset] = 255;
        return pixels;
    }

    public void Dispose()
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
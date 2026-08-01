using System.Reflection;
using System.Text.RegularExpressions;
using WindowsWebcamReceiver;
using Xunit;

namespace Iris.Tests;

/// <summary>
/// The app writes frames in C#; the camera reads them in C++. Nothing forces
/// those two files to agree - no compiler checks across the boundary, and the
/// numbers are simply typed out twice. Change one and forget the other and the
/// camera shows moving colour bands, with no error anywhere to explain why.
///
/// These tests read the C++ header as text and compare it against the C# it is
/// supposed to match. They are the only thing standing between an innocent edit
/// and a camera that has quietly stopped working.
/// </summary>
public sealed class DriverContractTests
{
    static readonly string DriverHeader = File.ReadAllText(Path.Combine(
        RepoRoot(), "Virtual-Camera-Driver", "VirtualCameraMediaSource", "SharedFrameReader.h"));

    [Theory]
    [InlineData("Magic")]
    [InlineData("HeaderSize")]
    [InlineData("OffMagic")]
    [InlineData("OffWidth")]
    [InlineData("OffHeight")]
    [InlineData("OffSlotSize")]
    [InlineData("OffSequence")]
    [InlineData("OffActiveSlot")]
    public void TheDriverAndTheAppAgreeOnWhereEverythingSits(string name)
    {
        var inDriver = ConstantFromHeader(name);
        var inApp = ConstantFromApp(name);

        Assert.True(inDriver == inApp,
            $"{name} is {inApp} in SharedFrameBuffer.cs but {inDriver} in SharedFrameReader.h. " +
            "The camera would read the wrong bytes and show colour bands instead of video.");
    }

    [Fact]
    public void TheDriverOpensThePathTheAppWritesTo()
    {
        var literal = Regex.Match(DriverHeader, @"L""([^""]+frames\.bin)""");
        Assert.True(literal.Success, "could not find the frames.bin path in SharedFrameReader.h");

        var driverPath = literal.Groups[1].Value.Replace(@"\\", @"\");

        Assert.True(
            string.Equals(driverPath, SharedFrameBuffer.DefaultPath, StringComparison.OrdinalIgnoreCase),
            $"the driver opens {driverPath} but the app writes to {SharedFrameBuffer.DefaultPath}. " +
            "The camera would find no frames at all.");
    }

    [Fact]
    public void TheTestsOwnCopyOfTheReaderHasNotDrifted()
    {
        // SeqlockReader.cs is a hand copy of the C++ reader. If it drifts, every
        // other test in this project is checking against a fiction.
        Assert.Equal(SeqlockReader.Magic, ConstantFromApp("Magic"));
        Assert.Equal((uint)SeqlockReader.HeaderSize, ConstantFromApp("HeaderSize"));
        Assert.Equal((uint)SeqlockReader.OffMagic, ConstantFromApp("OffMagic"));
        Assert.Equal((uint)SeqlockReader.OffWidth, ConstantFromApp("OffWidth"));
        Assert.Equal((uint)SeqlockReader.OffHeight, ConstantFromApp("OffHeight"));
        Assert.Equal((uint)SeqlockReader.OffSlotSize, ConstantFromApp("OffSlotSize"));
        Assert.Equal((uint)SeqlockReader.OffSequence, ConstantFromApp("OffSequence"));
        Assert.Equal((uint)SeqlockReader.OffActiveSlot, ConstantFromApp("OffActiveSlot"));
    }

    [Fact]
    public void TheDriverStillGivesUpRatherThanWaitingOnTheApp()
    {
        // The reader runs inside Teams and Chrome. A loop that waits for the app
        // would freeze whichever program loaded it, so the retry count must stay
        // small and fixed.
        Assert.Matches(@"attempt\s*<\s*[1-9]\b", DriverHeader);
        Assert.Contains("seqBefore & 1", DriverHeader);
        Assert.Contains("seqAfter == seqBefore", DriverHeader);
    }

    // ---- reading the two sides ---------------------------------------------

    /// <summary>Pulls "Name = 123" or "Name = 0xABC" out of the C++ header.</summary>
    static uint ConstantFromHeader(string name)
    {
        var m = Regex.Match(DriverHeader, $@"\b{Regex.Escape(name)}\s*=\s*(0x[0-9a-fA-F]+|\d+)");
        Assert.True(m.Success, $"{name} is not defined in SharedFrameReader.h");
        var text = m.Groups[1].Value;
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToUInt32(text, 16)
            : uint.Parse(text);
    }

    /// <summary>Reads the same constant out of the compiled C#, private or not.</summary>
    static uint ConstantFromApp(string name)
    {
        var field = typeof(SharedFrameBuffer).GetField(name,
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        Assert.True(field is { IsLiteral: true }, $"{name} is not a constant on SharedFrameBuffer");
        return Convert.ToUInt32(field!.GetRawConstantValue());
    }

    /// <summary>
    /// Walks up from wherever the tests were built until it finds the checkout.
    /// </summary>
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Virtual-Camera-Driver")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Windows-Client")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"could not find the repository root above {AppContext.BaseDirectory}");
    }
}

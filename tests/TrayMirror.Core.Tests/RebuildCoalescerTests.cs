using TrayMirror.Core.Scheduling;
using Xunit;

namespace TrayMirror.Core.Tests;

public class RebuildCoalescerTests
{
    private static readonly DateTimeOffset _start = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan _window = TimeSpan.FromMilliseconds(250);

    [Fact]
    public void TryTake_ReturnsNothing_WhenNothingHasBeenRequested()
    {
        var coalescer = new RebuildCoalescer(_window);

        Assert.False(coalescer.TryTake(_start + TimeSpan.FromSeconds(10), out RebuildReasons reasons));
        Assert.Equal(RebuildReasons.None, reasons);
    }

    [Fact]
    public void TryTake_WaitsForTheQuietWindow()
    {
        var coalescer = new RebuildCoalescer(_window);
        coalescer.Request(RebuildReasons.DisplayChange, _start);

        Assert.False(coalescer.TryTake(_start + TimeSpan.FromMilliseconds(249), out _));
        Assert.True(coalescer.TryTake(_start + TimeSpan.FromMilliseconds(250), out RebuildReasons reasons));
        Assert.Equal(RebuildReasons.DisplayChange, reasons);
    }

    [Fact]
    public void Request_RestartsTheQuietWindow()
    {
        // A rapid unplug and replug arrives as several messages. Rebuilding on each one would tear
        // down and re-register thumbnails against a taskbar that is still moving.
        var coalescer = new RebuildCoalescer(_window);

        coalescer.Request(RebuildReasons.DisplayChange, _start);
        coalescer.Request(RebuildReasons.DisplayChange, _start + TimeSpan.FromMilliseconds(200));

        Assert.False(coalescer.TryTake(_start + TimeSpan.FromMilliseconds(400), out _));
        Assert.True(coalescer.TryTake(_start + TimeSpan.FromMilliseconds(450), out _));
    }

    [Fact]
    public void Request_CombinesReasons()
    {
        var coalescer = new RebuildCoalescer(_window);

        coalescer.Request(RebuildReasons.TaskbarCreated, _start);
        coalescer.Request(RebuildReasons.DisplayChange, _start);
        coalescer.Request(RebuildReasons.DpiChange, _start);

        Assert.True(coalescer.TryTake(_start + _window, out RebuildReasons reasons));
        Assert.True(reasons.HasFlag(RebuildReasons.TaskbarCreated));
        Assert.True(reasons.HasFlag(RebuildReasons.DisplayChange));
        Assert.True(reasons.HasFlag(RebuildReasons.DpiChange));
    }

    [Fact]
    public void TryTake_ClearsWhatItReturned()
    {
        var coalescer = new RebuildCoalescer(_window);
        coalescer.Request(RebuildReasons.Configuration, _start);

        Assert.True(coalescer.TryTake(_start + _window, out _));
        Assert.False(coalescer.HasPending);
        Assert.False(coalescer.TryTake(_start + TimeSpan.FromSeconds(5), out _));
    }

    [Fact]
    public void Request_WithNoReason_ChangesNothing()
    {
        var coalescer = new RebuildCoalescer(_window);

        coalescer.Request(RebuildReasons.None, _start);

        Assert.False(coalescer.HasPending);
    }

    [Fact]
    public void Clear_DiscardsWhatIsPending()
    {
        var coalescer = new RebuildCoalescer(_window);
        coalescer.Request(RebuildReasons.TrayBoundaries, _start);

        coalescer.Clear();

        Assert.False(coalescer.HasPending);
        Assert.False(coalescer.TryTake(_start + _window, out _));
    }

    [Fact]
    public void Constructor_RejectsANegativeWindow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RebuildCoalescer(TimeSpan.FromSeconds(-1)));
    }
}

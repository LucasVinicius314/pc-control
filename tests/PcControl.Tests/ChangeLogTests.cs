using PcControl.Core.Capture;

namespace PcControl.Tests;

public class RectIntersectionTests
{
    [Theory]
    [InlineData(0, 0, 10, 10, 5, 5, 10, 10, true)]
    [InlineData(0, 0, 10, 10, 10, 0, 10, 10, false)] // sharing only an edge is not overlapping
    [InlineData(0, 0, 10, 10, 0, 10, 10, 10, false)]
    [InlineData(0, 0, 10, 10, 9, 9, 1, 1, true)]
    [InlineData(0, 0, 10, 10, 20, 20, 5, 5, false)]
    [InlineData(5, 5, 2, 2, 0, 0, 100, 100, true)] // one inside the other
    [InlineData(0, 0, 0, 10, 0, 0, 10, 10, false)] // empty
    [InlineData(0, 0, 10, 0, 0, 0, 10, 10, false)]
    public void Intersects(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh, bool expected)
    {
        var a = new Rect(ax, ay, aw, ah);
        var b = new Rect(bx, by, bw, bh);

        Assert.Equal(expected, a.Intersects(b));
        Assert.Equal(expected, b.Intersects(a));
    }
}

public class ChangeLogTests
{
    private static readonly Rect Area = new(100, 100, 50, 50);

    [Fact]
    public void NothingSeenYet_MeansNothingChanged()
    {
        var log = new ChangeLog();

        Assert.Equal(0, log.Version);
        Assert.False(log.ChangedSince(0, Area));
    }

    [Fact]
    public void ARecordedPicture_RaisesTheVersion()
    {
        var log = new ChangeLog();

        log.Record([new Rect(0, 0, 5, 5)]);
        log.Record([new Rect(0, 0, 5, 5)]);

        Assert.Equal(2, log.Version);
    }

    [Fact]
    public void ChangesElsewhere_DoNotCountAsChangesHere()
    {
        var log = new ChangeLog();
        log.Record([new Rect(0, 0, 20, 20)]);
        log.Record([new Rect(1000, 800, 300, 40)]);

        Assert.False(log.ChangedSince(0, Area));
    }

    [Fact]
    public void AChangeInsideTheArea_Counts()
    {
        var log = new ChangeLog();
        log.Record([new Rect(0, 0, 20, 20)]);
        log.Record([new Rect(120, 120, 4, 4)]);

        Assert.True(log.ChangedSince(0, Area));
    }

    [Fact]
    public void AChangeThatOnlyTouchesTheEdge_DoesNotCount()
    {
        var log = new ChangeLog();
        log.Record([new Rect(150, 100, 20, 20)]); // starts exactly where the area ends

        Assert.False(log.ChangedSince(0, Area));
    }

    [Fact]
    public void OnlyChangesAfterTheVersionAsked_Count()
    {
        var log = new ChangeLog();
        log.Record([new Rect(120, 120, 4, 4)]); // version 1: inside, but before we looked
        log.Record([new Rect(0, 0, 5, 5)]); // version 2: elsewhere

        Assert.False(log.ChangedSince(1, Area));
        Assert.True(log.ChangedSince(0, Area));
    }

    [Fact]
    public void WhenWeAreUpToDate_NothingHasChanged()
    {
        var log = new ChangeLog();
        log.Record([new Rect(120, 120, 4, 4)]);

        Assert.False(log.ChangedSince(log.Version, Area));
    }

    [Fact]
    public void ChangesWithUnknownExtent_CountAsChangingEverything()
    {
        var log = new ChangeLog();
        log.Record(null);

        Assert.True(log.ChangedSince(0, Area));
        Assert.True(log.ChangedSince(0, new Rect(5000, 5000, 1, 1)));
    }

    [Fact]
    public void APictureWithNoChangedAreas_ChangedNothing()
    {
        var log = new ChangeLog();
        log.Record([]);

        Assert.False(log.ChangedSince(0, Area));
    }

    [Fact]
    public void WhenTheHistoryNoLongerReachesBack_ItAssumesTheWorst()
    {
        var log = new ChangeLog(capacity: 3);
        for (var i = 0; i < 6; i++)
        {
            log.Record([new Rect(0, 0, 1, 1)]);
        }

        // Versions 1 to 3 were forgotten; none of what was kept touches the area, but we cannot be sure about the rest.
        Assert.True(log.ChangedSince(1, Area));
        Assert.False(log.ChangedSince(3, Area)); // 4, 5 and 6 are all still known
    }

    [Fact]
    public void ANegativeVersion_IsTreatedAsNeverSeen()
    {
        var log = new ChangeLog();
        log.Record([new Rect(0, 0, 1, 1)]);

        Assert.True(log.ChangedSince(-1, Area));
    }

    [Fact]
    public void ManyChanges_AcrossManyAreas_StayCorrect()
    {
        var log = new ChangeLog();
        var rng = new Random(7);
        var seen = new List<Rect[]>();
        for (var i = 0; i < 100; i++)
        {
            var rects = Enumerable.Range(0, rng.Next(0, 4))
                .Select(_ => new Rect(rng.Next(0, 400), rng.Next(0, 300), rng.Next(1, 60), rng.Next(1, 60)))
                .ToArray();
            seen.Add(rects);
            log.Record(rects);
        }

        for (var since = 0; since < 100; since += 7)
        {
            var expected = seen.Skip(since).SelectMany(r => r).Any(r => r.Intersects(Area));

            Assert.Equal(expected, log.ChangedSince(since, Area));
        }
    }
}

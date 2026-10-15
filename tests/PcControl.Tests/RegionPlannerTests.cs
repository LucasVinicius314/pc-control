using PcControl.Core.Capture;

namespace PcControl.Tests;

public class RegionPlannerTests
{
    private static readonly Rect Screen = new(0, 0, 1920, 1080);

    private static readonly IReadOnlyList<MonitorInfo> TwoMonitors =
    [
        new MonitorInfo(0, "Left", new Rect(0, 0, 1920, 1080), IsPrimary: true),
        new MonitorInfo(1, "Right", new Rect(1920, 0, 2560, 1440), IsPrimary: false),
    ];

    [Fact]
    public void Magnifier_IsCentredOnTheCursor()
    {
        var plan = RegionPlanner.Magnifier(Screen, 960, 540, 200, 120);

        Assert.Equal(new Rect(860, 480, 200, 120), plan.Source);
        Assert.Equal((200, 120), (plan.OutputWidth, plan.OutputHeight));
        Assert.Equal((100, 60), (plan.CursorX, plan.CursorY));
    }

    [Theory]
    [InlineData(5, 5, 0, 0, 5, 5)]
    [InlineData(1915, 1075, 1720, 960, 195, 115)]
    [InlineData(5, 1075, 0, 960, 5, 115)]
    [InlineData(1915, 5, 1720, 0, 195, 5)]
    public void Magnifier_StaysInsideTheMonitor_AndMovesTheMarker(int cx, int cy, int x, int y, int markerX, int markerY)
    {
        var plan = RegionPlanner.Magnifier(Screen, cx, cy, 200, 120);

        Assert.Equal(new Rect(x, y, 200, 120), plan.Source);
        Assert.Equal((markerX, markerY), (plan.CursorX, plan.CursorY));
    }

    [Fact]
    public void Magnifier_OnASecondMonitor_UsesThatMonitorsOrigin()
    {
        var plan = RegionPlanner.Magnifier(TwoMonitors[1].Bounds, 1925, 5, 200, 120);

        Assert.Equal(new Rect(1920, 0, 200, 120), plan.Source);
        Assert.Equal((5, 5), (plan.CursorX, plan.CursorY));
    }

    [Fact]
    public void Magnifier_LargerThanTheMonitor_IsShrunkToFit()
    {
        var plan = RegionPlanner.Magnifier(new Rect(0, 0, 100, 80), 50, 40, 800, 600);

        Assert.Equal(new Rect(0, 0, 100, 80), plan.Source);
    }

    [Fact]
    public void Magnifier_WithTheCursorOnAnotherMonitor_HasNoMarker()
    {
        var plan = RegionPlanner.Magnifier(Screen, 3000, 500, 200, 120);

        Assert.Equal(RegionPlanner.NoCursor, plan.CursorX);
        Assert.Equal(RegionPlanner.NoCursor, plan.CursorY);
    }

    [Fact]
    public void Display_ScalesTheWholeMonitor_KeepingTheAspectRatio()
    {
        var plan = RegionPlanner.Display(new Rect(0, 0, 2560, 1440), 1280, 720, 640);

        Assert.Equal(new Rect(0, 0, 2560, 1440), plan.Source);
        Assert.Equal((640, 360), (plan.OutputWidth, plan.OutputHeight));
        Assert.Equal((320, 180), (plan.CursorX, plan.CursorY));
    }

    [Fact]
    public void Display_OfATallMonitor_IsLimitedByHeightToo()
    {
        var plan = RegionPlanner.Display(new Rect(0, 0, 1080, 2560), 540, 1280, 800);

        // 800 wide would be 1896 tall. The height limit wins and the shape is kept.
        Assert.Equal((380, 900), (plan.OutputWidth, plan.OutputHeight));
        Assert.Equal((190, 450), (plan.CursorX, plan.CursorY));
    }

    [Fact]
    public void Display_NeverEnlarges()
    {
        var plan = RegionPlanner.Display(new Rect(0, 0, 800, 600), 10, 10, 1920);

        Assert.Equal((800, 600), (plan.OutputWidth, plan.OutputHeight));
    }

    [Fact]
    public void Display_MapsTheCursorRelativeToTheMonitor()
    {
        var plan = RegionPlanner.Display(TwoMonitors[1].Bounds, 1920 + 1280, 720, 1280);

        Assert.Equal((1280, 720), (plan.OutputWidth, plan.OutputHeight));
        Assert.Equal((640, 360), (plan.CursorX, plan.CursorY));
    }

    [Fact]
    public void Display_CursorOnAnotherMonitor_HasNoMarker()
    {
        var plan = RegionPlanner.Display(TwoMonitors[0].Bounds, 3000, 100, 640);

        Assert.Equal(RegionPlanner.NoCursor, plan.CursorX);
    }

    [Fact]
    public void Display_CursorAtTheBottomRightCorner_StaysInsidePicture()
    {
        var plan = RegionPlanner.Display(Screen, 1919, 1079, 640);

        Assert.InRange(plan.CursorX, 0, plan.OutputWidth - 1);
        Assert.InRange(plan.CursorY, 0, plan.OutputHeight - 1);
    }

    [Fact]
    public void PickMonitor_ByIndex()
    {
        Assert.Equal(1, RegionPlanner.PickMonitor(TwoMonitors, 1, 0, 0)!.Index);
        Assert.Equal(0, RegionPlanner.PickMonitor(TwoMonitors, 0, 3000, 0)!.Index);
    }

    [Fact]
    public void PickMonitor_FollowCursor_ChoosesTheMonitorUnderIt()
    {
        Assert.Equal(0, RegionPlanner.PickMonitor(TwoMonitors, ViewOptions.FollowCursor, 100, 100)!.Index);
        Assert.Equal(1, RegionPlanner.PickMonitor(TwoMonitors, ViewOptions.FollowCursor, 2500, 100)!.Index);
    }

    [Fact]
    public void PickMonitor_CursorOutsideEverything_FallsBackToThePrimary()
    {
        Assert.Equal(0, RegionPlanner.PickMonitor(TwoMonitors, ViewOptions.FollowCursor, -500, -500)!.Index);
    }

    [Fact]
    public void PickMonitor_BadIndex_FollowsTheCursorInstead()
    {
        Assert.Equal(1, RegionPlanner.PickMonitor(TwoMonitors, 9, 2500, 100)!.Index);
    }

    [Fact]
    public void PickMonitor_NoMonitors_ReturnsNull()
    {
        Assert.Null(RegionPlanner.PickMonitor([], -1, 0, 0));
    }
}

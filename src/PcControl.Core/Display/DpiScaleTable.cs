namespace PcControl.Core.Display;

/// <summary>What Windows reports about a monitor's scale. Unknown values are null.</summary>
/// <param name="Current">The scale in use now, as a percentage (100, 125, 150, ...).</param>
/// <param name="Recommended">The scale Windows recommends for this monitor, if known.</param>
/// <param name="Maximum">The largest scale this monitor accepts, if known.</param>
public sealed record ScaleInfo(int Current, int? Recommended = null, int? Maximum = null);

/// <summary>
/// Converts between the percentages shown in Windows' display settings and the relative steps the
/// (undocumented) display-configuration calls use. Windows counts steps from the monitor's
/// recommended scale: 0 is the recommendation, -1 one step smaller, +1 one step larger.
/// </summary>
public static class DpiScaleTable
{
    /// <summary>The scale steps in the order Windows uses, smallest first.</summary>
    public static readonly IReadOnlyList<int> Percents = [100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500];

    /// <summary>
    /// Turns the three numbers Windows returns into percentages. <paramref name="minRel"/> is how
    /// many steps down from the recommendation the smallest scale (100%) is, so its absolute value is
    /// the recommendation's position in the table.
    /// </summary>
    public static bool TryRead(int minRel, int curRel, int maxRel, out ScaleInfo? info)
    {
        info = null;

        var recommended = Math.Abs(minRel);
        var current = recommended + curRel;
        var maximum = recommended + maxRel;

        if (!InTable(recommended) || !InTable(current) || !InTable(maximum) || current > maximum)
        {
            return false;
        }

        info = new ScaleInfo(Percents[current], Percents[recommended], Percents[maximum]);
        return true;
    }

    /// <summary>The relative step to ask Windows for to reach <paramref name="percent"/>.</summary>
    public static bool TryToRelative(int percent, int recommendedPercent, out int relative)
    {
        relative = 0;

        var target = IndexOf(percent);
        var recommended = IndexOf(recommendedPercent);
        if (target < 0 || recommended < 0)
        {
            return false;
        }

        relative = target - recommended;
        return true;
    }

    public static bool IsKnownScale(int percent) => IndexOf(percent) >= 0;

    /// <summary>The nearest scale in the table to a measured percentage (for example 150.0 from a DPI of 144).</summary>
    public static int Nearest(double percent) =>
        Percents.OrderBy(p => Math.Abs(p - percent)).First();

    private static int IndexOf(int percent)
    {
        for (var i = 0; i < Percents.Count; i++)
        {
            if (Percents[i] == percent)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool InTable(int index) => index >= 0 && index < Percents.Count;
}

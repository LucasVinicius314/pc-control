namespace PcControl.Core.Input;

/// <summary>
/// Turns finger movement into cursor movement. Slow movement is scaled by the base gain for
/// precision; fast movement is scaled up to cross a large screen in one swipe.
/// </summary>
public sealed class PointerCurve
{
    /// <summary>Finger speed (pixels per update) at which the acceleration stops growing.</summary>
    public const double AccelerationLimit = 30;

    /// <summary>At the limit the multiplier is <c>1 + AccelerationLimit / AccelerationScale</c> times the gain.</summary>
    public const double AccelerationScale = 10;

    private readonly double _gain;

    public PointerCurve(double gain)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(gain, 0);
        _gain = gain;
    }

    /// <summary>Multiplier for one movement of <paramref name="distance"/> pixels.</summary>
    public double Multiplier(double distance) =>
        _gain * (1 + Math.Min(Math.Max(distance, 0), AccelerationLimit) / AccelerationScale);
}

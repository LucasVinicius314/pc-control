using PcControl.Core.Input;

namespace PcControl.Tests;

public class PointerCurveTests
{
    [Fact]
    public void SlowMovement_UsesTheBaseGain()
    {
        Assert.Equal(2.0, new PointerCurve(2.0).Multiplier(0), precision: 6);
    }

    [Fact]
    public void FasterMovement_GetsLargerMultiplier()
    {
        var curve = new PointerCurve(2.0);

        Assert.True(curve.Multiplier(5) > curve.Multiplier(1));
        Assert.True(curve.Multiplier(20) > curve.Multiplier(5));
    }

    [Fact]
    public void Acceleration_StopsAtTheLimit()
    {
        var curve = new PointerCurve(2.0);

        Assert.Equal(curve.Multiplier(PointerCurve.AccelerationLimit), curve.Multiplier(500), precision: 6);
        Assert.Equal(8.0, curve.Multiplier(PointerCurve.AccelerationLimit), precision: 6);
    }

    [Fact]
    public void NegativeDistance_IsTreatedAsZero()
    {
        var curve = new PointerCurve(1.5);

        Assert.Equal(curve.Multiplier(0), curve.Multiplier(-10), precision: 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveGain_IsRejected(double gain)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PointerCurve(gain));
    }
}

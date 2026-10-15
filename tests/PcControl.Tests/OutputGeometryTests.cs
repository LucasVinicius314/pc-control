using PcControl.Core.Capture;

namespace PcControl.Tests;

public class OutputGeometryTests
{
    public static TheoryData<OutputRotation> Rotations => new()
    {
        OutputRotation.Identity,
        OutputRotation.Rotate90,
        OutputRotation.Rotate180,
        OutputRotation.Rotate270,
    };

    [Theory]
    [MemberData(nameof(Rotations))]
    public void EveryPixel_MapsToTheImage_AndBackToItself(OutputRotation rotation)
    {
        // A 5 x 3 monitor as the user sees it: small enough to check every pixel.
        const int width = 5;
        const int height = 3;
        var (imageWidth, imageHeight) = OutputGeometry.ImageSize(rotation, width, height);
        var seen = new HashSet<(int, int)>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (ix, iy) = OutputGeometry.ToImage(rotation, x, y, width, height);

                Assert.InRange(ix, 0, imageWidth - 1);
                Assert.InRange(iy, 0, imageHeight - 1);
                Assert.True(seen.Add((ix, iy)), $"two monitor pixels landed on image pixel ({ix},{iy})");
                Assert.Equal((x, y), OutputGeometry.ToDesktop(rotation, ix, iy, width, height));
            }
        }

        Assert.Equal(width * height, seen.Count);
    }

    [Fact]
    public void WithoutRotation_NothingMoves()
    {
        Assert.Equal((4, 2), OutputGeometry.ToImage(OutputRotation.Identity, 4, 2, 5, 3));
    }

    [Fact]
    public void ARotation_Of180_TurnsTheCornersOver()
    {
        Assert.Equal((4, 2), OutputGeometry.ToImage(OutputRotation.Rotate180, 0, 0, 5, 3));
        Assert.Equal((0, 0), OutputGeometry.ToImage(OutputRotation.Rotate180, 4, 2, 5, 3));
    }

    [Fact]
    public void AQuarterTurn_SwapsTheImagesWidthAndHeight()
    {
        Assert.Equal((3, 5), OutputGeometry.ImageSize(OutputRotation.Rotate90, 5, 3));
        Assert.Equal((3, 5), OutputGeometry.ImageSize(OutputRotation.Rotate270, 5, 3));
        Assert.Equal((5, 3), OutputGeometry.ImageSize(OutputRotation.Identity, 5, 3));
        Assert.Equal((5, 3), OutputGeometry.ImageSize(OutputRotation.Rotate180, 5, 3));
    }

    [Fact]
    public void The90And270Turns_AreOppositeEachOther()
    {
        // The top-left pixel of a monitor lands in different corners of the image for the two directions.
        var a = OutputGeometry.ToImage(OutputRotation.Rotate90, 0, 0, 5, 3);
        var b = OutputGeometry.ToImage(OutputRotation.Rotate270, 0, 0, 5, 3);

        Assert.NotEqual(a, b);
        Assert.Equal((0, 4), a);
        Assert.Equal((2, 0), b);
    }

    [Theory]
    [MemberData(nameof(Rotations))]
    public void TheImageBox_CoversExactlyTheRegionsPixels(OutputRotation rotation)
    {
        const int width = 40;
        const int height = 24;
        var region = new Rect(7, 5, 12, 9);
        var box = OutputGeometry.ImageBox(rotation, region, width, height);

        var wanted = new HashSet<(int, int)>();
        for (var y = region.Y; y < region.Bottom; y++)
        {
            for (var x = region.X; x < region.Right; x++)
            {
                wanted.Add(OutputGeometry.ToImage(rotation, x, y, width, height));
            }
        }

        var inBox = new HashSet<(int, int)>();
        for (var y = box.Y; y < box.Bottom; y++)
        {
            for (var x = box.X; x < box.Right; x++)
            {
                inBox.Add((x, y));
            }
        }

        Assert.Equal(wanted, inBox);
        Assert.Equal(region.Width * region.Height, box.Width * box.Height);
    }

    [Theory]
    [MemberData(nameof(Rotations))]
    public void TheImageBox_OfTheWholeMonitor_IsTheWholeImage(OutputRotation rotation)
    {
        var (imageWidth, imageHeight) = OutputGeometry.ImageSize(rotation, 1080, 2560);

        Assert.Equal(new Rect(0, 0, imageWidth, imageHeight), OutputGeometry.ImageBox(rotation, new Rect(0, 0, 1080, 2560), 1080, 2560));
    }

    [Theory]
    [InlineData(100, 100, 1)]
    [InlineData(100, 200, 1)]
    [InlineData(200, 100, 2)]
    [InlineData(3440, 800, 4)]
    [InlineData(100000, 10, 4)]
    [InlineData(101, 100, 2)]
    public void TheSampleCount_GrowsWithTheShrinkFactor_ButIsCapped(int source, int output, int expected)
    {
        Assert.Equal(expected, OutputGeometry.SamplesFor(source, output));
    }

    [Fact]
    public void SamplePositions_WithoutShrinking_AreTheIdentity()
    {
        Assert.Equal([0, 1, 2, 3, 4], OutputGeometry.SamplePositions(5, 5, 1));
    }

    [Fact]
    public void SamplePositions_StayInsideTheSource_AndSpreadAcrossIt()
    {
        var positions = OutputGeometry.SamplePositions(1000, 100, 4);

        Assert.Equal(400, positions.Length);
        Assert.All(positions, p => Assert.InRange(p, 0, 999));
        Assert.Equal(positions.OrderBy(p => p), positions); // never goes backwards
        Assert.True(positions[0] < 10);
        Assert.True(positions[^1] > 990);
    }

    [Fact]
    public void EachOutputPixel_AveragesFromItsOwnFootprint()
    {
        // 8 source pixels down to 2: output 0 covers source 0-3, output 1 covers source 4-7.
        var positions = OutputGeometry.SamplePositions(8, 2, 4);

        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7], positions);
    }

    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(10, 0, 1)]
    [InlineData(10, 10, 0)]
    public void SamplePositions_RefuseNonsense(int source, int output, int samples)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OutputGeometry.SamplePositions(source, output, samples));
    }
}

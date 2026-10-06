using Rex.Core;

namespace Rex.Tests;

/// <summary>How often the soft background and the navigator's picture run, with and without a graphics card.</summary>
public sealed class LivePicturesTests
{
    [Theory]
    [InlineData(15, false, LivePictures.Slower, 15)]
    [InlineData(15, false, LivePictures.Off, 15)]
    [InlineData(15, true, LivePictures.Slower, LivePictures.SlowerRate)]
    [InlineData(2, true, LivePictures.Slower, 2)]
    [InlineData(15, true, LivePictures.AsSet, 15)]
    [InlineData(15, true, LivePictures.Off, 0)]
    [InlineData(15, true, "anything else", LivePictures.SlowerRate)]
    [InlineData(0, false, LivePictures.AsSet, 0)]
    [InlineData(-5, true, LivePictures.AsSet, 0)]
    public void ALivePictureRunsAsOftenAsThePcCanAfford(double wanted, bool withoutGraphicsCard, string choice, double expected) =>
        Assert.Equal(expected, LivePictures.Rate(wanted, withoutGraphicsCard, choice));

    [Fact]
    public void TheChoiceIsKeptSane()
    {
        var config = new RexConfig();
        Assert.Equal(LivePictures.Slower, config.App.WithoutGraphicsCard);

        config.App.WithoutGraphicsCard = "OFF";
        config.Normalize();
        Assert.Equal(LivePictures.Off, config.App.WithoutGraphicsCard);

        config.App.WithoutGraphicsCard = "never";
        config.Normalize();
        Assert.Equal(LivePictures.Slower, config.App.WithoutGraphicsCard);

        config.App.WithoutGraphicsCard = LivePictures.AsSet;
        Assert.Equal(LivePictures.AsSet, config.Copy().App.WithoutGraphicsCard);
    }
}

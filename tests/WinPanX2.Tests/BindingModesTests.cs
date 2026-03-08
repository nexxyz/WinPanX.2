using WinPanX2.Config;

namespace WinPanX2.Tests;

public class BindingModesTests
{
    [Fact]
    public void Constants_AreBackCompatStrings()
    {
        Assert.Equal("Sticky", BindingModes.Sticky);
        Assert.Equal("FollowMostRecent", BindingModes.FollowMostRecent);
        Assert.Equal("FollowMostRecentOpened", BindingModes.FollowMostRecentOpened);
    }

    [Fact]
    public void IsFollowMostRecent_IsOrdinalExactMatch()
    {
        Assert.True(BindingModes.IsFollowMostRecent("FollowMostRecent"));
        Assert.False(BindingModes.IsFollowMostRecent("followmostrecent"));
        Assert.False(BindingModes.IsFollowMostRecent(" FollowMostRecent "));
        Assert.False(BindingModes.IsFollowMostRecent(null));
    }

    [Fact]
    public void IsFollowMostRecentOpened_IsOrdinalExactMatch()
    {
        Assert.True(BindingModes.IsFollowMostRecentOpened("FollowMostRecentOpened"));
        Assert.False(BindingModes.IsFollowMostRecentOpened("followmostrecentopened"));
        Assert.False(BindingModes.IsFollowMostRecentOpened(" FollowMostRecentOpened "));
        Assert.False(BindingModes.IsFollowMostRecentOpened(null));
    }

    [Fact]
    public void IsStickyLike_IsTrueForUnknownModes()
    {
        Assert.True(BindingModes.IsStickyLike(BindingModes.Sticky));
        Assert.True(BindingModes.IsStickyLike(""));
        Assert.True(BindingModes.IsStickyLike("Custom"));

        Assert.False(BindingModes.IsStickyLike(BindingModes.FollowMostRecent));
        Assert.False(BindingModes.IsStickyLike(BindingModes.FollowMostRecentOpened));
    }
}

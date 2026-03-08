using System;

namespace WinPanX2.Config;

public static class BindingModes
{
    // String values are persisted in config.json. Do not change.
    public const string Sticky = "Sticky";
    public const string FollowMostRecent = "FollowMostRecent";
    public const string FollowMostRecentOpened = "FollowMostRecentOpened";

    public static bool IsFollowMostRecent(string? mode)
        => string.Equals(mode, FollowMostRecent, StringComparison.Ordinal);

    public static bool IsFollowMostRecentOpened(string? mode)
        => string.Equals(mode, FollowMostRecentOpened, StringComparison.Ordinal);

    public static bool IsStickyLike(string? mode)
        => !IsFollowMostRecent(mode) && !IsFollowMostRecentOpened(mode);
}

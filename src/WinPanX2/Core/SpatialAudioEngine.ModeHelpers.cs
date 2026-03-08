using System;
using System.Collections.Generic;
using WinPanX2.Config;
using WinPanX2.Windowing;

namespace WinPanX2.Core;

internal sealed partial class SpatialAudioEngine
{
    private bool IsFollowMostRecentOpenedMode()
        => BindingModes.IsFollowMostRecentOpened(_config.BindingMode);

    private bool IsFollowMostRecentMode()
        => BindingModes.IsFollowMostRecent(_config.BindingMode);

    private Dictionary<string, WindowInfo?>? CreateOpenedModeCacheIfNeeded()
    {
        if (!IsFollowMostRecentOpenedMode())
            return null;

        return new Dictionary<string, WindowInfo?>(StringComparer.OrdinalIgnoreCase);
    }
}

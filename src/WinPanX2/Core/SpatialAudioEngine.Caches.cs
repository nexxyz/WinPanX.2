using System;
using System.Collections.Generic;
using WinPanX2.Audio;
using WinPanX2.Logging;
using WinPanX2.Windowing;

namespace WinPanX2.Core;

internal sealed partial class SpatialAudioEngine
{
    private void ClearMappingCache()
    {
        _mappingValid = false;
        _mappingDirty = true;
        _lastMappingCaptureTick = 0;
    }

    private VirtualDesktopMapper.Mapping GetCurrentMapping(long nowTick)
    {
        if (!_mappingValid || _mappingDirty || (nowTick - _lastMappingCaptureTick) >= Timing.MappingBackstopMs)
        {
            _cachedMapping = VirtualDesktopMapper.Capture();
            _mappingValid = true;
            _mappingDirty = false;
            _lastMappingCaptureTick = nowTick;
        }

        return _cachedMapping;
    }

    private static double ComputeNormalizedForRect(RECT rect, VirtualDesktopMapper.Mapping mapping)
        => PanMath.ComputeNormalizedForRect(rect, mapping);

    private (float Left, float Right) ComputeStereoForNormalized(double normalized)
        => PanMath.ComputeStereoForNormalized(normalized, _config.CenterBias, _config.MaxPan);

    private void RefreshActiveSessionCache()
    {
        ClearActiveSessionCache();

        foreach (var (deviceId, manager) in _deviceManagers)
        {
            try
            {
                _activeSessionCache[deviceId] = manager.GetActiveSessions();
            }
            catch (Exception ex)
            {
                Logger.Error($"Session enumeration failed for {deviceId}: {ex.Message}");
                _activeSessionCache[deviceId] = new List<AudioSessionWrapper>();
            }
        }
    }

    private void ClearActiveSessionCache()
    {
        foreach (var list in _activeSessionCache.Values)
        {
            foreach (var session in list)
            {
                try { session.Dispose(); } catch { }
            }
        }

        _activeSessionCache.Clear();
    }
}

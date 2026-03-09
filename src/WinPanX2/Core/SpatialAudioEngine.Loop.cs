using System;
using System.Threading;
using WinPanX2.Logging;
using WinPanX2.Windowing;

namespace WinPanX2.Core;

internal sealed partial class SpatialAudioEngine
{
    private void Loop(CancellationToken token, long generation)
    {
        var handles = new WaitHandle[] { token.WaitHandle, _workSignal };

        _lastHousekeepingTick = Environment.TickCount64;
        _lastSessionProbeTick = _lastHousekeepingTick;
        _lastNameCachePruneTick = _lastHousekeepingTick;
        _lastHealthLogTick = _lastHousekeepingTick;

        while (!token.IsCancellationRequested)
        {
            // Probe interval controls how quickly we detect new Active sessions.
            // Keep it relatively small for responsiveness; signature probing is lightweight.
            var probeIntervalMs = GetProbeIntervalMs();
            var waitResult = WaitHandle.WaitAny(handles, probeIntervalMs);
            if (ShouldExitLoop(token, generation, waitResult))
                break;

            var nowTickOuter = Environment.TickCount64;

            // Update callback filters (volatile writes).
            _openedModeEnabled = IsFollowMostRecentOpenedMode();
            var isHousekeepingDue = nowTickOuter - _lastHousekeepingTick >= Timing.HousekeepingIntervalMs;
            var isProbeDue = nowTickOuter - _lastSessionProbeTick >= probeIntervalMs;

            var recomputeRequested = waitResult == 1; // wake by signal
            var events = DrainAndCoalesceEvents(generation, nowTickOuter, ref recomputeRequested);

            if (events.TopologyChangedRequested)
            {
                try
                {
                    HandleDeviceTopologyChangedInLoop(token);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Device topology rebuild failed: {ex.Message}");
                }
            }

            try
            {
                if (events.TopologyChangedRequested)
                {
                    _activeSessionSignature = string.Empty;
                    _lastSessionProbeTick = nowTickOuter;
                    _lastHousekeepingTick = nowTickOuter;
                    recomputeRequested = true;
                }

                if (isProbeDue)
                {
                    recomputeRequested |= ProbeActiveSessions(nowTickOuter);
                }

                if (isHousekeepingDue)
                {
                    PerformHousekeeping(token, nowTickOuter);
                }

                if (!recomputeRequested && !events.TopologyChangedRequested)
                {
                    // Nothing to do besides periodic pruning.
                    HandleIdle(nowTickOuter);
                    continue;
                }

                // If we are in a LOCATIONCHANGE storm, cap recompute rate to ~60-70Hz.
                if (ShouldThrottleLocationStorm(handles, nowTickOuter, events))
                    continue;

                WindowResolver.Snapshot? windowSnapshot = null;
                WindowResolver.Snapshot CaptureSnapshot() => windowSnapshot ??= WindowResolver.CaptureSnapshot();

                var mapping = GetCurrentMapping(nowTickOuter);

                var nowTick = Environment.TickCount64;
                var shouldPrune = nowTick - _lastSmoothedPanPruneTick >= Timing.SmoothedPanPruneIntervalMs;
                var shouldPruneOpened = nowTick - _lastOpenedWindowPruneTick >= Timing.OpenedWindowPruneIntervalMs;

                if (events.ApplyNowRequested)
                {
                    ApplyCurrentPositions();
                    RefreshTrackedHwnds();
                    PruneSmoothedPanIfDue(nowTick, Timing.SmoothedPanPruneIntervalMs, Timing.SmoothedPanEntryTtlMs);
                    PruneOpenedWindowsIfDue(nowTick, Timing.OpenedWindowPruneIntervalMs, Timing.OpenedWindowEntryTtlMs);
                    continue;
                }

                var (activeSessionExes, isOpenedMode) = RecomputeActiveSessions(
                    nowTickOuter,
                    nowTick,
                    mapping,
                    events,
                    CaptureSnapshot);

                // Update callback filter set (tracked HWNDs) from the current resolution map.
                RefreshTrackedHwnds();

                if (shouldPrune)
                    PruneSmoothedPanIfDue(nowTick, Timing.SmoothedPanPruneIntervalMs, Timing.SmoothedPanEntryTtlMs);

                BackfillOpenedWindowsIfNeeded(activeSessionExes, isOpenedMode, isHousekeepingDue, CaptureSnapshot, nowTick);

                if (shouldPruneOpened)
                    PruneOpenedWindowsIfDue(nowTick, Timing.OpenedWindowPruneIntervalMs, Timing.OpenedWindowEntryTtlMs);
            }
            catch (Exception ex)
            {
                Logger.Error($"Engine loop error: {ex.Message}");
            }
        }
    }
}

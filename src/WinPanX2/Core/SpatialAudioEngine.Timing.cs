namespace WinPanX2.Core;

internal sealed partial class SpatialAudioEngine
{
    private static class Timing
    {
        public const int SmoothedPanPruneIntervalMs = 5000;
        public const int SmoothedPanEntryTtlMs = 60_000;

        public const int OpenedWindowPruneIntervalMs = 5000;
        public const int OpenedWindowEntryTtlMs = 120_000;

        public const int HousekeepingIntervalMs = 3000;

        public const int ProbeIntervalDefaultModeMs = 200;
        public const int ProbeIntervalAllDevicesModeMs = 500;

        public const int LocationStormMinIntervalMs = 15;

        public const int NameCachePruneIntervalMs = 60_000;
        public const int HealthLogIntervalMs = 300_000;

        public const int MappingBackstopMs = 60_000;
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WinPanX2.Audio;
using WinPanX2.Config;
using WinPanX2.Windowing;

namespace WinPanX2.Core;

internal sealed partial class SpatialAudioEngine : IDisposable
{
    private readonly AppConfig _config;
    private readonly IAudioDeviceProvider _deviceProvider;
    private DeviceMode _deviceMode = DeviceMode.Default;

    private readonly object _lifecycleLock = new();
    private readonly AutoResetEvent _workSignal = new(false);
    private readonly ConcurrentQueue<EngineEvent> _eventQueue = new();
    private long _generation;
    private long _currentGeneration;

    // Cross-thread callback filters (WinEvent hook callbacks can arrive on arbitrary threads).
    private volatile int _activeSessionCountApprox;
    private volatile bool _openedModeEnabled;

    private WinEventHook? _winEventHook;

    private long _winEventLocationCount;
    private long _winEventForegroundCount;
    private long _winEventCreateCount;
    private long _winEventShowCount;
    private long _winEventHideCount;
    private long _winEventDestroyCount;

    private long _winEventLocationLogged;
    private long _winEventForegroundLogged;
    private long _winEventCreateLogged;
    private long _winEventShowLogged;
    private long _winEventHideLogged;
    private long _winEventDestroyLogged;

    private readonly Dictionary<string, AudioSessionManager> _deviceManagers = new();
    private readonly ConcurrentDictionary<(string deviceId, int pid), double> _smoothedPan = new();
    private readonly ConcurrentDictionary<(string deviceId, int pid), long> _smoothedPanLastSeenTick = new();
    private readonly ConcurrentDictionary<(string deviceId, int pid), long> _smoothedPanLastUpdateTick = new();
    private readonly ConcurrentDictionary<(string deviceId, int pid), IntPtr> _boundHwnd = new();

    // For follow modes, we still want a stable "last chosen hwnd" so we can:
    // - filter location-change WinEvents
    // - recompute only sessions affected by a specific hwnd move
    private readonly Dictionary<(string deviceId, int pid), IntPtr> _lastResolvedHwnd = new();
    private readonly Dictionary<IntPtr, HashSet<(string deviceId, int pid)>> _hwndToKeys = new();
    private readonly ConcurrentDictionary<IntPtr, byte> _trackedHwnds = new();
    private int _lastForegroundPid;
    private long _lastLocationRecomputeTick;

    private readonly ConcurrentDictionary<TouchKey, StereoPair> _lastAppliedStereo = new();
    private readonly ConcurrentDictionary<TouchKey, StereoPair> _originalStereo = new();
    private readonly ConcurrentDictionary<TouchKey, string> _touchProcessName = new();

    // Cached active sessions (refreshed on housekeeping / topology changes).
    private readonly Dictionary<string, List<AudioSessionWrapper>> _activeSessionCache = new();
    private string _activeSessionSignature = string.Empty;
    private long _lastSessionProbeTick;
    private long _lastHousekeepingTick;
    private long _lastNameCachePruneTick;

    private VirtualDesktopMapper.Mapping _cachedMapping;
    private bool _mappingValid;
    private bool _mappingDirty;
    private long _lastMappingCaptureTick;

    private long _lastHealthLogTick;

    // Used only for FollowMostRecentOpened mode.
    private readonly ConcurrentDictionary<IntPtr, long> _windowFirstSeenTick = new();
    private readonly ConcurrentDictionary<IntPtr, long> _windowLastSeenTick = new();
    private long _lastOpenedWindowPruneTick;
    private long _lastOpenedWindowTrackTick;
    private long _lastSmoothedPanPruneTick;
    private HashSet<string> _excludedSet = new(StringComparer.OrdinalIgnoreCase);
    private string _excludedSignature = string.Empty;
    // Window handles are resolved fresh each loop to ensure dynamic tracking

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
}

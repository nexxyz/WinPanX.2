using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WinPanX2.Logging;

namespace WinPanX2.Windowing;

internal static class WindowResolver
{
    private enum CandidateSource
    {
        DirectPid,
        ParentPid,
        ExeMatch
    }

    private readonly struct Candidate
    {
        public WindowInfo Window { get; }
        public CandidateSource Source { get; }
        public int ParentDepth { get; }

        public Candidate(WindowInfo window, CandidateSource source, int parentDepth = 0)
        {
            Window = window;
            Source = source;
            ParentDepth = parentDepth;
        }
    }

    internal sealed class Snapshot
    {
        private readonly List<WindowInfo> _windows;
        private readonly IntPtr _foreground;
        private readonly Dictionary<int, List<WindowInfo>> _windowsByPid;
        private readonly Dictionary<int, int?> _parentCache = new();
        private readonly Dictionary<int, string?> _processNameCache = new();
        private readonly Dictionary<string, List<WindowInfo>> _windowsByExe = new(StringComparer.OrdinalIgnoreCase);

        public Snapshot(List<WindowInfo> windows, IntPtr foreground)
        {
            _windows = windows;
            _foreground = foreground;

            _windowsByPid = new Dictionary<int, List<WindowInfo>>();
            foreach (var w in _windows)
            {
                if (!_windowsByPid.TryGetValue(w.ProcessId, out var list))
                {
                    list = new List<WindowInfo>();
                    _windowsByPid[w.ProcessId] = list;
                }

                list.Add(w);
            }
        }

        public WindowInfo? ResolveForProcess(int pid, bool preferForeground)
        {
            var targetExe = GetProcessNameCached(pid);

            var candidates = new List<Candidate>();
            var seen = new HashSet<IntPtr>();

            void AddCandidateList(List<WindowInfo>? list, CandidateSource source, int parentDepth = 0)
            {
                if (list == null || list.Count == 0)
                    return;

                foreach (var w in list)
                {
                    if (seen.Add(w.Handle))
                        candidates.Add(new Candidate(w, source, parentDepth));
                }
            }

            // 1) Direct PID windows.
            _windowsByPid.TryGetValue(pid, out var direct);
            AddCandidateList(direct, CandidateSource.DirectPid);

            // 2) Walk parent chain and include those windows too.
            var current = pid;
            for (int depth = 1; depth <= 10; depth++)
            {
                var parent = GetParentProcessIdCached(current);
                if (!parent.HasValue || parent.Value <= 0)
                    break;

                if (parent.Value == current)
                    break;

                // Prevent escaping into unrelated ancestors (e.g. explorer.exe launching Chromium).
                // If we know the target exe name, only traverse parents that match it.
                if (!string.IsNullOrWhiteSpace(targetExe))
                {
                    var parentExe = GetProcessNameCached(parent.Value);
                    if (parentExe == null || !parentExe.Equals(targetExe, StringComparison.OrdinalIgnoreCase))
                        break;
                }

                _windowsByPid.TryGetValue(parent.Value, out var parentWindows);
                AddCandidateList(parentWindows, CandidateSource.ParentPid, depth);
                current = parent.Value;
            }

            // 3) Same-exe matches as a last resort.
            var exe = targetExe;
            if (!string.IsNullOrWhiteSpace(exe))
            {
                var exeWindows = GetWindowsByExeCached(exe);
                AddCandidateList(exeWindows, CandidateSource.ExeMatch);
            }

            if (candidates.Count == 0)
                return null;

            double bestScore = double.NegativeInfinity;
            Candidate bestCandidate = default;

            double secondScore = double.NegativeInfinity;
            Candidate secondCandidate = default;

            foreach (var c in candidates)
            {
                var score = ScoreCandidate(c, _foreground, preferForeground, out _);
                if (score > bestScore)
                {
                    secondScore = bestScore;
                    secondCandidate = bestCandidate;
                    bestScore = score;
                    bestCandidate = c;
                }
                else if (score > secondScore)
                {
                    secondScore = score;
                    secondCandidate = c;
                }
            }

            if (double.IsNegativeInfinity(bestScore))
                return null;

            // Log when selection is ambiguous.
            if (candidates.Count > 1 && Math.Abs(bestScore - secondScore) < 150)
            {
                _ = ScoreCandidate(bestCandidate, _foreground, preferForeground, out var bestMeta);
                _ = ScoreCandidate(secondCandidate, _foreground, preferForeground, out var secondMeta);
                Logger.Debug($"[WindowResolver] pid={pid} exe={exe ?? ""} preferFg={preferForeground} fg=0x{_foreground.ToInt64():X} chose=0x{bestCandidate.Window.Handle.ToInt64():X} score={bestScore:F1} class='{bestMeta.ClassName}' title='{bestMeta.Title}' owner=0x{bestMeta.Owner.ToInt64():X} ex=0x{bestMeta.ExStyle:X} rect={bestCandidate.Window.Rect.Left},{bestCandidate.Window.Rect.Top},{bestCandidate.Window.Rect.Right},{bestCandidate.Window.Rect.Bottom} src={bestCandidate.Source} depth={bestCandidate.ParentDepth} runnerUp=0x{secondCandidate.Window.Handle.ToInt64():X} score2={secondScore:F1} class2='{secondMeta.ClassName}' title2='{secondMeta.Title}' owner2=0x{secondMeta.Owner.ToInt64():X} ex2=0x{secondMeta.ExStyle:X} src2={secondCandidate.Source} depth2={secondCandidate.ParentDepth}");
            }

            return bestCandidate.Window;
        }

        private int? GetParentProcessIdCached(int pid)
        {
            if (_parentCache.TryGetValue(pid, out var cached))
                return cached;

            var parent = ProcessHelper.GetParentProcessId(pid);
            _parentCache[pid] = parent;
            return parent;
        }

        private string? GetProcessNameCached(int pid)
        {
            if (_processNameCache.TryGetValue(pid, out var cached))
                return cached;

            var name = ProcessHelper.GetProcessName(pid);
            _processNameCache[pid] = name;
            return name;
        }

        private List<WindowInfo> GetWindowsByExeCached(string exe)
        {
            if (_windowsByExe.TryGetValue(exe, out var cached))
                return cached;

            var list = new List<WindowInfo>();
            foreach (var w in _windows)
            {
                var name = GetProcessNameCached(w.ProcessId);
                if (name != null && name.Equals(exe, StringComparison.OrdinalIgnoreCase))
                    list.Add(w);
            }

            _windowsByExe[exe] = list;
            return list;
        }

        internal IReadOnlyList<WindowInfo> Windows => _windows;

        internal string? GetProcessName(int pid) => GetProcessNameCached(pid);
    }

    public static Snapshot CaptureSnapshot() =>
        new Snapshot(WindowEnumerator.GetVisibleWindows(), NativeMethods.GetForegroundWindow());

    public static WindowInfo? ResolveForProcess(int pid, bool preferForeground)
    {
        var snap = CaptureSnapshot();
        return snap.ResolveForProcess(pid, preferForeground);
    }

    private readonly struct CandidateMetadata
    {
        public IntPtr Owner { get; }
        public long Style { get; }
        public long ExStyle { get; }
        public bool IsCloaked { get; }
        public string Title { get; }
        public string ClassName { get; }

        public CandidateMetadata(IntPtr owner, long style, long exStyle, bool isCloaked, string title, string className)
        {
            Owner = owner;
            Style = style;
            ExStyle = exStyle;
            IsCloaked = isCloaked;
            Title = title;
            ClassName = className;
        }
    }

    private static double ScoreCandidate(Candidate candidate, IntPtr foreground, bool preferForeground, out CandidateMetadata meta)
    {
        var w = candidate.Window;
        var hwnd = w.Handle;

        // Window may have changed since enumeration.
        if (!NativeMethods.IsWindowVisible(hwnd))
        {
            meta = new CandidateMetadata(IntPtr.Zero, 0, 0, false, string.Empty, string.Empty);
            return double.NegativeInfinity;
        }

        var owner = NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER);

        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64();
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();

        var isCloaked = false;
        if (NativeMethods.TryIsCloaked(hwnd, out var cloaked))
            isCloaked = cloaked;

        // Pull a small amount of metadata (used for both scoring and debug).
        var title = GetWindowTitle(hwnd);
        var cls = GetWindowClass(hwnd);

        meta = new CandidateMetadata(owner, style, exStyle, isCloaked, title, cls);

        if (isCloaked)
            return double.NegativeInfinity;

        // Filter out typical transient Chromium hover-card / tooltip windows.
        // These are commonly owned or marked as tool/no-activate.
        if (owner != IntPtr.Zero)
            return double.NegativeInfinity;

        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
            return double.NegativeInfinity;

        if ((exStyle & NativeMethods.WS_EX_NOACTIVATE) != 0)
            return double.NegativeInfinity;

        var width = w.Rect.Right - w.Rect.Left;
        var height = w.Rect.Bottom - w.Rect.Top;
        if (width <= 0 || height <= 0)
            return double.NegativeInfinity;

        var area = (long)width * height;

        // Base weight by how we found this HWND.
        double score = candidate.Source switch
        {
            CandidateSource.DirectPid => 1000,
            CandidateSource.ParentPid => 850,
            CandidateSource.ExeMatch => 700,
            _ => 0
        };

        // Prefer nearer parents in the chain.
        if (candidate.Source == CandidateSource.ParentPid)
            score -= Math.Min(candidate.ParentDepth, 10) * 15;

        // Prefer the foreground window if requested.
        if (preferForeground && hwnd == foreground)
            score += 500;

        // Prefer large windows (main browser window beats hover cards even if not filtered).
        // Normalize with a log-ish curve: add up to ~400 points.
        score += Math.Min(400, Math.Log10(Math.Max(1, area)) * 60);

        // Title is a weak signal; many modern apps have empty titles.
        if (!string.IsNullOrWhiteSpace(title))
            score += 30;

        return score;
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        try
        {
            var len = NativeMethods.GetWindowTextLength(hWnd);
            if (len <= 0)
                return string.Empty;

            var sb = new StringBuilder(len + 1);
            NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetWindowClass(IntPtr hWnd)
    {
        try
        {
            var sb = new StringBuilder(256);
            var n = NativeMethods.GetClassName(hWnd, sb, sb.Capacity);
            if (n <= 0)
                return string.Empty;
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }
}

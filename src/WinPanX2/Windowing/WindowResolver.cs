using System;
using System.Collections.Generic;
using System.Linq;

namespace WinPanX2.Windowing;

internal static class WindowResolver
{
    public static WindowInfo? ResolveForProcess(int pid, bool preferForeground)
    {
        var windows = WindowEnumerator.GetVisibleWindows();

        var directMatches = windows.Where(w => w.ProcessId == pid).ToList();

        if (directMatches.Count == 1)
            return directMatches[0];

        if (directMatches.Count > 1)
        {
            if (preferForeground)
            {
                var fg = NativeMethods.GetForegroundWindow();
                var match = directMatches.FirstOrDefault(w => w.Handle == fg);
                if (match != null)
                    return match;
            }

            return directMatches[0];
        }

        var parent = ProcessHelper.GetParentProcessId(pid);
        if (parent.HasValue && parent.Value > 0 && parent.Value != pid)
        {
            var parentMatch = ResolveForProcess(parent.Value, preferForeground);
            if (parentMatch != null)
                return parentMatch;
        }

        var exe = ProcessHelper.GetProcessName(pid);
        if (exe != null)
        {
            var exeMatches = windows
                .Where(w => ProcessHelper.GetProcessName(w.ProcessId)?.Equals(exe, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            if (exeMatches.Count == 1)
                return exeMatches[0];

            if (exeMatches.Count > 1)
            {
                if (preferForeground)
                {
                    var fg = NativeMethods.GetForegroundWindow();
                    var match = exeMatches.FirstOrDefault(w => w.Handle == fg);
                    if (match != null)
                        return match;
                }

                return exeMatches[0];
            }
        }

        return null;
    }
}

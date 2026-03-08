using System;
using WinPanX2.Audio;

namespace WinPanX2.Core;

internal sealed partial class SpatialAudioEngine
{
    private double SmoothPan((string deviceId, int pid) key, double previous, double target, long nowTick)
    {
        var alpha = Math.Clamp(_config.SmoothingFactor, 0.0, 1.0);
        if (alpha <= 0.0)
            return previous;
        if (alpha >= 1.0)
            return target;

        if (!_smoothedPanLastUpdateTick.TryGetValue(key, out var lastTick))
        {
            _smoothedPanLastUpdateTick[key] = nowTick;
            return previous;
        }

        var dtMs = nowTick - lastTick;
        if (dtMs <= 0)
            return previous;

        // Convert the original per-tick alpha to a time constant so irregular updates
        // (event-driven) keep the same feel.
        var referenceDt = Math.Max(1, _config.PollingIntervalMs);
        var tau = -referenceDt / Math.Log(1.0 - alpha);
        if (double.IsNaN(tau) || double.IsInfinity(tau) || tau <= 0)
            return target;

        var k = 1.0 - Math.Exp(-dtMs / tau);
        var smoothed = previous + (target - previous) * k;

        _smoothedPanLastUpdateTick[key] = nowTick;
        return smoothed;
    }

    private double GetOrSeedSmoothedPan((string deviceId, int pid) key, AudioSessionWrapper session, double targetNormalized, long nowTick)
    {
        if (_smoothedPan.TryGetValue(key, out var existing))
            return existing;

        // Seed from current session stereo so new sessions transition smoothly
        // from their current balance toward the window target.
        var seed = EstimateNormalizedFromCurrentStereo(session);

        _smoothedPan[key] = seed;

        // Pretend we updated one reference step ago so SmoothPan takes an immediate step.
        var referenceDt = Math.Max(1, _config.PollingIntervalMs);
        _smoothedPanLastUpdateTick[key] = nowTick - referenceDt;

        return seed;
    }

    private double EstimateNormalizedFromCurrentStereo(AudioSessionWrapper session)
    {
        try
        {
            // Read current channel volumes (best-effort). Values are typically 0..1.
            if (session.ChannelVolume.GetChannelVolume(0, out var left) < 0)
                return 0.0;
            if (session.ChannelVolume.GetChannelVolume(1, out var right) < 0)
                return 0.0;

            if (left < 0f) left = 0f;
            if (right < 0f) right = 0f;

            if (left == 0f && right == 0f)
                return 0.0;

            // Inverse of our equal-power law mapping:
            // left=cos(angle), right=sin(angle), angle in [0, pi/2]
            return PanMath.EstimateNormalizedFromStereo(left, right, _config.CenterBias, _config.MaxPan);
        }
        catch
        {
            return 0.0;
        }
    }

    private void TryApplyStereo(AudioSessionWrapper session, (string deviceId, int pid) key, string? processName, float left, float right)
    {
        const float Epsilon = 0.002f;

        var touchKey = new TouchKey(key.deviceId, key.pid, session.SessionInstanceId);

        if (_lastAppliedStereo.TryGetValue(touchKey, out var prev))
        {
            if (Math.Abs(prev.Left - left) < Epsilon && Math.Abs(prev.Right - right) < Epsilon)
                return;
        }

        // If we don't have a session instance id, use processName as an additional guard
        // against PID reuse.
        if (touchKey.SessionInstanceId == null && !string.IsNullOrWhiteSpace(processName))
        {
            if (_touchProcessName.TryGetValue(touchKey, out var existing) && !string.Equals(existing, processName, StringComparison.OrdinalIgnoreCase))
            {
                _touchProcessName[touchKey] = processName;
                _originalStereo.TryRemove(touchKey, out _);
                _lastAppliedStereo.TryRemove(touchKey, out _);

                // Clear pid-based smoothing/bindings too.
                _smoothedPan.TryRemove(key, out _);
                _smoothedPanLastUpdateTick.TryRemove(key, out _);
                _boundHwnd.TryRemove(key, out _);
            }
            else
            {
                _touchProcessName.TryAdd(touchKey, processName);
            }
        }

        if (!_originalStereo.ContainsKey(touchKey))
        {
            try
            {
                var hrL = session.ChannelVolume.GetChannelVolume(0, out var curL);
                var hrR = session.ChannelVolume.GetChannelVolume(1, out var curR);
                if (hrL >= 0 && hrR >= 0)
                    _originalStereo.TryAdd(touchKey, new StereoPair(curL, curR));
            }
            catch
            {
                // best-effort
            }
        }

        session.SetStereo(left, right);
        _lastAppliedStereo[touchKey] = new StereoPair(left, right);
    }

    private void HandleExcludedSession(AudioSessionWrapper session, (string deviceId, int pid) key)
    {
        // "Excluded" means we should not modify the app's audio.
        // If we previously touched it (before exclusion or before the exclusion list changed),
        // restore the original values once and then stop tracking.
        try
        {
            var tk = new TouchKey(key.deviceId, key.pid, session.SessionInstanceId);
            if (!_lastAppliedStereo.ContainsKey(tk) && session.SessionInstanceId != null)
                tk = new TouchKey(key.deviceId, key.pid, null);

            if (_lastAppliedStereo.ContainsKey(tk))
            {
                if (_originalStereo.TryGetValue(tk, out var original))
                    session.SetStereo(original.Left, original.Right);
                else
                    session.SetStereo(1f, 1f);
            }
        }
        catch
        {
            // best-effort
        }

        _smoothedPan.TryRemove(key, out _);
        _smoothedPanLastSeenTick.TryRemove(key, out _);
        _smoothedPanLastUpdateTick.TryRemove(key, out _);
        _boundHwnd.TryRemove(key, out _);

        var removeKey = new TouchKey(key.deviceId, key.pid, session.SessionInstanceId);
        _lastAppliedStereo.TryRemove(removeKey, out _);
        _originalStereo.TryRemove(removeKey, out _);
        _touchProcessName.TryRemove(removeKey, out _);

        if (session.SessionInstanceId != null)
        {
            var legacyKey = new TouchKey(key.deviceId, key.pid, null);
            _lastAppliedStereo.TryRemove(legacyKey, out _);
            _originalStereo.TryRemove(legacyKey, out _);
            _touchProcessName.TryRemove(legacyKey, out _);
        }

        if (_lastResolvedHwnd.TryGetValue(key, out var prev) && prev != IntPtr.Zero)
        {
            if (_hwndToKeys.TryGetValue(prev, out var set))
            {
                set.Remove(key);
                if (set.Count == 0)
                    _hwndToKeys.Remove(prev);
            }
        }

        _lastResolvedHwnd.Remove(key);
    }

    private static double ApplyCenterBias(double normalized, double bias)
        => PanMath.ApplyCenterBias(normalized, bias);
}

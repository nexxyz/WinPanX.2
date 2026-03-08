using System;
using WinPanX2.Windowing;

namespace WinPanX2.Core;

internal static class PanMath
{
    public static double ApplyCenterBias(double normalized, double bias)
    {
        var x = Math.Clamp(normalized, -1.0, 1.0);
        var b = Math.Clamp(bias, 0.0, 1.0);
        if (b <= 0.0)
            return x;

        var ax = Math.Abs(x);

        // Combine two effects:
        // - Nonlinear curve pulls midpoints toward center
        // - Max magnitude cap prevents hard panning even at screen edges
        // Tuned so the tray presets (Medium/Strong) are clearly noticeable.
        var exp = 1.0 + (b * 6.0);          // b=1 -> exp=7 (very strong pull)
        var maxMag = 1.0 - (b * 0.8);       // b=1 -> 0.2 (hard cap)

        var y = Math.Pow(ax, exp) * maxMag;
        return x < 0 ? -y : y;
    }

    public static double ComputeNormalizedForRect(RECT rect, VirtualDesktopMapper.Mapping mapping)
    {
        var centerX = rect.Left + (rect.Right - rect.Left) / 2;
        return VirtualDesktopMapper.MapToNormalized(centerX, mapping);
    }

    public static (float Left, float Right) ComputeStereoForNormalized(double normalized, double centerBias, double maxPan)
    {
        var biased = ApplyCenterBias(normalized, centerBias);
        biased *= Math.Clamp(maxPan, 0.0, 1.0);

        var angle = (biased + 1.0) * Math.PI / 4.0;
        return ((float)Math.Cos(angle), (float)Math.Sin(angle));
    }

    public static double SmoothDt(double previous, double target, double alpha, long dtMs, int referenceDtMs)
    {
        alpha = Math.Clamp(alpha, 0.0, 1.0);
        if (alpha <= 0.0)
            return previous;
        if (alpha >= 1.0)
            return target;

        if (dtMs <= 0)
            return previous;

        var refDt = Math.Max(1, referenceDtMs);
        var tau = -refDt / Math.Log(1.0 - alpha);
        if (double.IsNaN(tau) || double.IsInfinity(tau) || tau <= 0)
            return target;

        var k = 1.0 - Math.Exp(-dtMs / tau);
        return previous + (target - previous) * k;
    }

    public static double EstimateNormalizedFromStereo(float left, float right, double centerBias, double maxPan)
    {
        if (left < 0f) left = 0f;
        if (right < 0f) right = 0f;

        if (left == 0f && right == 0f)
            return 0.0;

        // Inverse of our equal-power law mapping:
        // left=cos(angle), right=sin(angle), angle in [0, pi/2]
        var angle = Math.Atan2(right, left);
        if (angle < 0) angle = 0;
        if (angle > (Math.PI / 2.0)) angle = Math.PI / 2.0;

        // This is the "biased * MaxPan" domain.
        var biasedScaled = (angle * 4.0 / Math.PI) - 1.0;
        biasedScaled = Math.Clamp(biasedScaled, -1.0, 1.0);

        var mp = Math.Clamp(maxPan, 0.0, 1.0);
        if (mp <= 0.0)
            return 0.0;

        var biased = biasedScaled / mp;
        biased = Math.Clamp(biased, -1.0, 1.0);

        var bias = Math.Clamp(centerBias, 0.0, 1.0);
        if (bias <= 0.0)
            return biased;

        // ApplyCenterBias is monotonic; invert by binary searching magnitude.
        var sign = Math.Sign(biased);
        var targetAbs = Math.Abs(biased);
        var lo = 0.0;
        var hi = 1.0;
        for (var i = 0; i < 24; i++)
        {
            var mid = (lo + hi) / 2.0;
            var midVal = Math.Abs(ApplyCenterBias(sign * mid, bias));
            if (midVal < targetAbs)
                lo = mid;
            else
                hi = mid;
        }

        return sign * ((lo + hi) / 2.0);
    }
}

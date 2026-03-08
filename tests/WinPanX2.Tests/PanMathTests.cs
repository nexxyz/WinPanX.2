using WinPanX2.Core;

namespace WinPanX2.Tests;

public class PanMathTests
{
    [Fact]
    public void ApplyCenterBias_IsBoundedAndSignPreserving()
    {
        for (var b = 0.0; b <= 1.0; b += 0.25)
        {
            for (var x = -1.0; x <= 1.0; x += 0.1)
            {
                var y = PanMath.ApplyCenterBias(x, b);
                Assert.InRange(y, -1.0, 1.0);
                if (x < 0) Assert.True(y <= 0.0);
                if (x > 0) Assert.True(y >= 0.0);
            }
        }
    }

    [Fact]
    public void ApplyCenterBias_IsMonotonicOnPositiveSide()
    {
        var b = 0.75;
        var prev = PanMath.ApplyCenterBias(0.0, b);
        for (var x = 0.01; x <= 1.0; x += 0.01)
        {
            var y = PanMath.ApplyCenterBias(x, b);
            Assert.True(y >= prev);
            prev = y;
        }
    }

    [Fact]
    public void ComputeStereoForNormalized_MatchesExpectedExtremes()
    {
        const double bias = 0.0;
        const double maxPan = 1.0;

        var (l0, r0) = PanMath.ComputeStereoForNormalized(0.0, bias, maxPan);
        Assert.InRange(l0, 0.70f, 0.72f);
        Assert.InRange(r0, 0.70f, 0.72f);

        var (lL, rL) = PanMath.ComputeStereoForNormalized(-1.0, bias, maxPan);
        Assert.InRange(lL, 0.99f, 1.01f);
        Assert.InRange(rL, -0.01f, 0.01f);

        var (lR, rR) = PanMath.ComputeStereoForNormalized(1.0, bias, maxPan);
        Assert.InRange(lR, -0.01f, 0.01f);
        Assert.InRange(rR, 0.99f, 1.01f);
    }

    [Fact]
    public void SmoothDt_UsesReferenceAlphaAtReferenceDt()
    {
        const double prev = 0.0;
        const double target = 1.0;
        const double alpha = 0.5;
        const int refDt = 30;

        var y = PanMath.SmoothDt(prev, target, alpha, dtMs: 30, referenceDtMs: refDt);
        Assert.InRange(y, 0.49, 0.51);

        var y2 = PanMath.SmoothDt(prev, target, alpha, dtMs: 60, referenceDtMs: refDt);
        Assert.True(y2 > y);
    }

    [Fact]
    public void EstimateNormalizedFromStereo_RoundTripsBasicCases()
    {
        const double bias = 0.0;
        const double maxPan = 1.0;

        Assert.InRange(PanMath.EstimateNormalizedFromStereo(1f, 1f, bias, maxPan), -0.01, 0.01);
        Assert.InRange(PanMath.EstimateNormalizedFromStereo(1f, 0f, bias, maxPan), -1.01, -0.99);
        Assert.InRange(PanMath.EstimateNormalizedFromStereo(0f, 1f, bias, maxPan), 0.99, 1.01);
    }
}

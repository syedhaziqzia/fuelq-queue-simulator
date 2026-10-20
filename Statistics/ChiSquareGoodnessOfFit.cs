using System;
using System.Collections.Generic;
using System.Linq;
using FuelQ.Distributions;

namespace FuelQ.Statistics;

/// <summary>Chi-Square Goodness-of-Fit test for exponential inter-arrival distribution.</summary>
public static class ChiSquareGoodnessOfFit
{
    public record Result(
        int    Bins,
        int    DegreesOfFreedom,
        double ChiSquareStat,
        double CriticalValue,
        double PValue,
        bool   H0Accepted,
        double EstimatedLambda,
        double[] Observed,
        double[] Expected,
        double[] BinEdges);

    /// <param name="records">
    /// Records to test. All inter-arrival times are used, including any flagged as
    /// service-time outliers: trimming the long right tail of exponential data would
    /// bias λ̂ upwards and make the test reject true exponential data too often.
    /// </param>
    /// <param name="k">Number of equal-probability bins (default 10, df = k − 2).</param>
    /// <param name="alpha">Significance level (default 0.05).</param>
    public static Result Run(
        IList<ForecourtObservationRecord> records,
        int k = 10, double alpha = 0.05)
    {
        int n = records.Count;
        if (n == 0)
            throw new ArgumentException("At least one record is required.", nameof(records));
        if (k < 3)
            throw new ArgumentOutOfRangeException(nameof(k), "Need at least 3 bins (df = k − 2 must be ≥ 1).");

        double[] iats = [.. records.Select(r => r.InterArrivalTime).OrderBy(x => x)];
        double mean   = iats.Average();
        double lambda = mean > 0 ? 1.0 / mean : 1.0;

        // Equal-probability bins: each bin contains expected n/k observations
        // Bin edges from exponential quantiles
        var edges = new double[k + 1];
        edges[0] = 0;
        for (int i = 1; i < k; i++)
            edges[i] = -Math.Log(1.0 - (double)i / k) / lambda;
        edges[k] = double.PositiveInfinity;

        var observed = new double[k];
        foreach (double t in iats)
        {
            for (int b = 0; b < k; b++)
            {
                if (t >= edges[b] && (t < edges[b + 1] || double.IsPositiveInfinity(edges[b + 1])))
                {
                    observed[b]++;
                    break;
                }
            }
        }

        double expected    = (double)n / k;
        double chiSqStat   = 0;
        for (int b = 0; b < k; b++)
            chiSqStat += Math.Pow(observed[b] - expected, 2) / expected;

        int df            = k - 1 - 1; // p = 1 estimated parameter (λ)
        double critical   = ChiSquareCritical(df, alpha);
        double pValue     = 1.0 - ChiSquareCDF(chiSqStat, df);
        bool   h0         = chiSqStat < critical;

        var expectedArr = new double[k];
        for (int b = 0; b < k; b++) expectedArr[b] = expected;

        return new Result(k, df, chiSqStat, critical, pValue, h0, lambda, observed, expectedArr, edges);
    }

    /// <summary>Exact chi-square CDF: F(x; df) = P(df/2, x/2), the regularized lower incomplete gamma.</summary>
    public static double ChiSquareCDF(double x, int df)
    {
        if (df < 1) throw new ArgumentOutOfRangeException(nameof(df), "df must be ≥ 1.");
        if (x <= 0) return 0;
        return GammaDistribution.LowerIncompleteGammaRatio(df / 2.0, x / 2.0);
    }

    // chi-square critical value via binary search on the exact CDF
    public static double ChiSquareCritical(int df, double alpha)
    {
        double lo = 0, hi = 200;
        for (int i = 0; i < 60; i++)
        {
            double mid = (lo + hi) / 2;
            if (ChiSquareCDF(mid, df) < 1 - alpha)
                lo = mid;
            else
                hi = mid;
        }
        return Math.Round((lo + hi) / 2, 3);
    }
}

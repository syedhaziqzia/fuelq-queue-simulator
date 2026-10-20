using System;

namespace FuelQ.Models;

/// <summary>
/// Unified immutable metrics record matching the PSO / Shell Maskan simulator outputs.
/// </summary>
public record QueueMetrics(
    double Lambda,
    double Mu,
    int S,
    double Rho,
    double TrafficIntensity,
    double Lq,
    double L,
    double Wq,
    double W,
    double P0,
    double Pwait,
    double Ca = 1.0,
    double Cs = 1.0,
    string ModelName = "M/M/1",
    string FormulaUsed = "",
    bool HasExactStateProbabilities = false)
{
    public static readonly QueueMetrics Empty = new(0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0);

    /// <summary>
    /// State probability P(n customers in system). Exact only for M/M/1 and M/M/s
    /// (check <see cref="HasExactStateProbabilities"/>); returns NaN for other models,
    /// whose state distribution is not geometric.
    /// </summary>
    public double Pn(int n)
    {
        if (!HasExactStateProbabilities) return double.NaN;
        if (n < 0 || Rho >= 1.0) return 0;
        if (S == 1)
        {
            // M/M/1: P(n) = (1-ρ)·ρⁿ
            return (1.0 - Rho) * Math.Pow(Rho, n);
        }
        else
        {
            // M/M/s
            double a = TrafficIntensity;
            if (n < S)
                return P0 * Math.Pow(a, n) / Factorial(n);
            else
                // P0·aⁿ/(s!·sⁿ⁻ˢ) rewritten as P0·(aˢ/s!)·ρⁿ⁻ˢ so large n cannot overflow
                return P0 * Math.Pow(a, S) / Factorial(S) * Math.Pow(Rho, n - S);
        }
    }

    public static double Factorial(int n)
    {
        if (n <= 1) return 1.0;
        double f = 1.0;
        for (int i = 2; i <= n; i++) f *= i;
        return f;
    }
}

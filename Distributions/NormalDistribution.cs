using System;

namespace FuelQ.Distributions;

/// <summary>
/// Normal (Gaussian) distribution sampled with the Marsaglia polar method
/// (a rejection variant of Box–Muller). Stateless (no spare sample caching)
/// for thread safety and deterministic reproducibility per Random instance.
/// Samples are clipped at 0 because service and interarrival times cannot be
/// negative. This raises the mean and lowers the variance when σ is large
/// relative to the mean; keep σ ≤ mean/3 for a negligible effect.
/// </summary>
public sealed class NormalDistribution : IDistribution
{
    private readonly double _mean;
    private readonly double _std;

    public NormalDistribution(double mean, double std)
    {
        if (std < 0) throw new ArgumentException("Standard deviation σ must be ≥ 0.", nameof(std));
        _mean = mean;
        _std  = std;
    }

    public double Mean     => _mean;
    public double Variance => _std * _std;

    /// <summary>
    /// Marsaglia polar method. Returns Max(0, sample) to prevent negative times.
    /// Each call is independent — no spare caching to ensure thread safety.
    /// </summary>
    public double Sample(Random rng)
    {
        // Marsaglia polar method — rejection-samples until inside unit circle
        double u, v, s;
        do
        {
            u = rng.NextDouble() * 2.0 - 1.0;
            v = rng.NextDouble() * 2.0 - 1.0;
            s = u * u + v * v;
        } while (s >= 1.0 || s == 0.0);

        double mul = Math.Sqrt(-2.0 * Math.Log(s) / s);
        return Math.Max(0.0, u * mul * _std + _mean);
    }

    /// <summary>
    /// Inverse-transform sample for a given uniform draw u ∈ (0, 1), clipped at 0.
    /// Lets the simulation table show the exact random number behind each value.
    /// </summary>
    public double Quantile(double u) => Math.Max(0.0, _mean + _std * InverseStandardCdf(u));

    /// <summary>
    /// Inverse of the standard normal CDF (Acklam's rational approximation,
    /// relative error below 1.15×10⁻⁹).
    /// </summary>
    public static double InverseStandardCdf(double p)
    {
        if (p <= 0.0 || p >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(p), "p must be in (0, 1).");

        double[] a = [-3.969683028665376e+01,  2.209460984245205e+02, -2.759285104469687e+02,
                       1.383577518672690e+02, -3.066479806614716e+01,  2.506628277459239e+00];
        double[] b = [-5.447609879822406e+01,  1.615858368580409e+02, -1.556989798598866e+02,
                       6.680131188771972e+01, -1.328068155288572e+01];
        double[] c = [-7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00,
                      -2.549732539343734e+00,  4.374664141464968e+00,  2.938163982698783e+00];
        double[] d = [ 7.784695709041462e-03,  3.224671290700398e-01,  2.445134137142996e+00,
                       3.754408661907416e+00];

        const double pLow = 0.02425;
        if (p < pLow)
        {
            double q = Math.Sqrt(-2.0 * Math.Log(p));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                   ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1.0);
        }
        if (p > 1.0 - pLow)
        {
            double q = Math.Sqrt(-2.0 * Math.Log(1.0 - p));
            return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                    ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1.0);
        }
        double r = p - 0.5, s = r * r;
        return (((((a[0] * s + a[1]) * s + a[2]) * s + a[3]) * s + a[4]) * s + a[5]) * r /
               (((((b[0] * s + b[1]) * s + b[2]) * s + b[3]) * s + b[4]) * s + 1.0);
    }

    public double PDF(double x)
    {
        if (_std <= 0) return x == _mean ? double.PositiveInfinity : 0.0;
        double z = (x - _mean) / _std;
        return Math.Exp(-0.5 * z * z) / (_std * Math.Sqrt(2.0 * Math.PI));
    }

    public double CDF(double x)
    {
        if (_std <= 0) return x >= _mean ? 1.0 : 0.0;
        return 0.5 * (1.0 + Erf((x - _mean) / (_std * Math.Sqrt(2.0))));
    }

    // Abramowitz & Stegun approximation — max error 1.5×10⁻⁷
    private static double Erf(double z)
    {
        double t    = 1.0 / (1.0 + 0.3275911 * Math.Abs(z));
        double poly = t * (0.254829592 + t * (-0.284496736 +
                     t * (1.421413741 + t * (-1.453152027 + t * 1.061405429))));
        double result = 1.0 - poly * Math.Exp(-z * z);
        return z < 0 ? -result : result;
    }
}

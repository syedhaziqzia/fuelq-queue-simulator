using System.Globalization;
using FuelQ.Distributions;
using FuelQ.Models;
using FuelQ.Simulation;
using FuelQ.Statistics;
using FuelQ.Tests;

// FuelQ verification suite. Each check is tagged with the audit finding it covers (F1–F23).
// Run:  dotnet run --project Tests/FuelQ.Tests      (exit code 0 = all passed)

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var t = new TestRunner();

// ── Analytical solvers ────────────────────────────────────────────
t.Section("Analytical queue formulas");
{
    var mm1 = MM1Calculator.Calculate(1.8, 2.2).Metrics!;
    t.Near("F8", "M/M/1 λ=1.8, μ=2.2 /min → Wq = 2.045 min", mm1.Wq, 2.0455, 1e-3);

    var mms = MMsCalculator.Calculate(2, 1, 3).Metrics!;
    t.Near("—", "M/M/3 λ=2, μ=1 → Erlang-C Pw = 0.4444", mms.Pwait, 4.0 / 9.0, 1e-4);
    t.Near("—", "M/M/3 λ=2, μ=1 → Lq = 0.8889", mms.Lq, 8.0 / 9.0, 1e-4);

    var md1 = MD1Calculator.Calculate(1.8, 1 / 2.2).Metrics!;
    var mg1 = MG1Calculator.Calculate(1.8, 2.2, 0.45 * 0.45).Metrics!;
    t.Near("F8", "M/G/1 with σ = 0.45 min uses Var = σ² → Wq = 2.025 min", mg1.Wq, 2.0250, 1e-3);
    t.True("F8", "M/G/1 (σ = 0.45) no longer collapses to M/D/1", mg1.Wq > md1.Wq * 1.5,
        $"M/G/1 Wq = {mg1.Wq:F3}, M/D/1 Wq = {md1.Wq:F3}");

    // M/E3/1 takes the mean service TIME; the comparison page once passed μ instead (ρ = 1.8 × 2.2 → "unstable")
    var mek = MEk1Calculator.Calculate(1.8, 1 / 2.2, 3).Metrics!;
    t.Near("—", "M/E3/1 λ=1.8, μ=2.2 → Lq = (4/6)·ρ²/(1−ρ) = 2.454", mek.Lq, 2.0 / 3.0 * Math.Pow(1.8 / 2.2, 2) / (1 - 1.8 / 2.2), 1e-9);
    string cmpSrc = File.ReadAllText(Path.Combine(RepoRoot(), "Views", "ModelComparisonView.xaml.cs"));
    t.True("—", "Model Comparison passes 1/μ (mean service time) to M/Eₖ/1", cmpSrc.Contains("MEk1Calculator.Calculate(lambda, 1.0 / mu, k)"));

    var err = MMsCalculator.Calculate(1, 1, 51).Error ?? "";
    t.True("F18", "s > 50 error is not labelled 'UNSTABLE'", !err.Contains("UNSTABLE"), err);
}

// ── P(n) table (F2) ───────────────────────────────────────────────
t.Section("State probabilities P(n)");
{
    var mm1 = MM1Calculator.Calculate(1 / 3.0, 1 / 2.0).Metrics!;
    t.Near("F2", "M/M/1: Σ n·P(n) equals L", SumNPn(mm1), mm1.L, 1e-6);

    var mms = MMsCalculator.Calculate(0.9, 0.5, 2).Metrics!;
    t.Near("F2", "M/M/2: Σ n·P(n) equals L", SumNPn(mms), mms.L, 1e-6);

    var mg1 = MG1Calculator.Calculate(1 / 3.0, 1 / 2.0, 0.75).Metrics!;
    t.True("F2", "M/G/1 does not offer a (wrong) geometric P(n) table",
        !mg1.HasExactStateProbabilities && double.IsNaN(mg1.Pn(1)));
    foreach (var m in new[] {
        MGsCalculator.Calculate(0.9, 0.5, 2, 1).Metrics!,
        GG1Calculator.Calculate(0.3, 0.5, 4, 1).Metrics!,
        GGsCalculator.Calculate(0.9, 0.5, 2, 4, 1).Metrics! })
        t.True("F2", $"{m.ModelName} does not offer a P(n) table", !m.HasExactStateProbabilities);
}

// ── Uniform inputs (F3) ───────────────────────────────────────────
t.Section("Uniform service bounds");
{
    // Old default: mean box 2, bounds [1.5, 4.5] → true mean 3 → ρ = (1/3)·3 = 1 (unstable)
    var oldDefault = MG1Calculator.Calculate(1 / 3.0, 2.0 / (1.5 + 4.5), 9.0 / 12.0);
    t.True("F3", "Uniform[1.5, 4.5] with λ = 1/3 is correctly reported unstable", oldDefault.Error != null);

    var newDefault = MG1Calculator.Calculate(1 / 3.0, 2.0 / (1.0 + 3.0), 4.0 / 12.0).Metrics!;
    t.Near("F3", "New default Uniform[1, 3] gives ρ = 66.7%", newDefault.Rho, 2.0 / 3.0, 1e-9);
}

// ── Distribution samplers ─────────────────────────────────────────
t.Section("Distributions & inverse transforms");
{
    t.Near("F11", "Φ⁻¹(0.975) = 1.95996", NormalDistribution.InverseStandardCdf(0.975), 1.959964, 1e-5);
    t.Near("F11", "Φ⁻¹(0.01) = −2.32635", NormalDistribution.InverseStandardCdf(0.01), -2.326348, 1e-5);

    var g = new GammaDistribution(2, 1.5);
    t.Near("F11", "Gamma quantile inverts the CDF (u = 0.7)", g.CDF(g.Quantile(0.7)), 0.7, 1e-8);

    var rng = new Random(1);
    double[] ns = [.. Enumerable.Range(0, 400_000).Select(_ => new NormalDistribution(2, 0.5).Quantile(Uniform01(rng)))];
    t.Near("F11", "Normal(2, 0.5) via inverse transform: sample mean ≈ 2", ns.Average(), 2.0, 0.01);

    double[] gs = [.. Enumerable.Range(0, 100_000).Select(_ => g.Quantile(Uniform01(rng)))];
    t.Near("F11", "Gamma(2, 1.5) via inverse transform: sample mean ≈ 3", gs.Average(), 3.0, 0.03);

    double[] clipped = [.. Enumerable.Range(0, 400_000).Select(_ => new NormalDistribution(2, 1.5).Sample(rng))];
    t.True("F9", "Normal(2, 1.5) clipping at 0 shifts the mean up (documented limitation)",
        clipped.Average() > 2.03, $"sample mean = {clipped.Average():F3}");

    var (gm, gv) = GeneralDESimulator.Moments(new DistConfig(DistType.Gamma, 2, 1.5));
    t.True("F19", "Gamma moments use k·θ and k·θ² (not Normal's)", gm == 3.0 && gv == 4.5, $"mean {gm}, var {gv}");
}

// ── Simulation engine (F1, F10, F11, F12) ─────────────────────────
t.Section("Simulation engine");
{
    // Heavy traffic M/M/1, ρ = 0.8
    var heavy = GeneralDESimulator.Run(new DESSimConfig(1, TimeUnitType.Minutes, 200_000, 1e12, 7,
        new DistConfig(DistType.Exponential, 1.25), new DistConfig(DistType.Exponential, 1.0)));

    t.True("F1", "Max queue length is no longer stuck at 0", heavy.MaxQueueLength > 0, $"max = {heavy.MaxQueueLength}");
    t.True("F1", "Q(t) timeline never goes backwards in time",
        heavy.QueueTimeline.Zip(heavy.QueueTimeline.Skip(1)).All(p => p.Second.Time >= p.First.Time));
    t.True("F1", "Q(t) never negative", heavy.QueueTimeline.All(p => p.QLen >= 0));
    t.True("F1", "Hourly peak queue is no longer always 0", heavy.HourlyRows.Any(h => h.PeakQueueLength > 0));

    double littleLq = heavy.AvgWaitingTime / heavy.AvgInterarrivalTime; // λ̂·Wq
    t.Near("F1", "Time-average Lq from Q(t) matches λ·Wq (within 1%)", heavy.AvgQueueLength, littleLq, 0.01 * littleLq);
    t.Near("F1", "Lq matches M/M/1 theory 3.2 (within 5%)", heavy.AvgQueueLength, 3.2, 0.16);

    // Brute-force check of queue length seen on arrival
    var rows = heavy.CustomerRows.Take(3000).ToList();
    int mismatches = 0, bruteMax = 0;
    for (int i = 0; i < rows.Count; i++)
    {
        int q = 0;
        for (int j = 0; j < i; j++)
            if (rows[j].TimeServiceBegins > rows[i].ArrivalTime) q++;
        bruteMax = Math.Max(bruteMax, q);
        if (q != rows[i].QueueLengthOnArrival) mismatches++;
    }
    t.True("F1", "Queue-on-arrival matches a brute-force count (first 3,000 vehicles)", mismatches == 0,
        $"{mismatches} mismatches");

    // Default Simulator screen: 2 pumps, Exp(3) arrivals, Exp(2) service, 20,000 vehicles, seed 42
    var def = GeneralDESimulator.Run(new DESSimConfig(2, TimeUnitType.Minutes, 20_000, 10_000_000, 42,
        new DistConfig(DistType.Exponential, 3.0), new DistConfig(DistType.Exponential, 2.0)));
    var wqRow = def.ComparisonRows.First(r => r.Symbol == "Wq");
    double wqErr = double.Parse(wqRow.ErrorDelta.TrimEnd('%'));
    t.True("F10", "Default run: Wq error vs M/M/2 theory is small (< 10%)", wqErr < 10, $"Wq error = {wqRow.ErrorDelta}");
    t.True("F10", "Default run is not cut short by the duration cap",
        !def.StoppedByDurationCap && def.TotalCustomersServed == 20_000);

    var capped = GeneralDESimulator.Run(new DESSimConfig(2, TimeUnitType.Minutes, 25, 20, 42,
        new DistConfig(DistType.Exponential, 3.0), new DistConfig(DistType.Exponential, 2.0)));
    t.True("F10", "Hitting the duration cap is reported", capped.StoppedByDurationCap);

    var little = def.ComparisonRows.FirstOrDefault(r => r.Symbol == "L = λW");
    t.True("F12", "Little's Law check row compares measured L with λ·W (within 1%)",
        little != null && double.Parse(little.ErrorDelta.TrimEnd('%')) < 1, little?.ErrorDelta ?? "missing");

    // Every displayed random number must reproduce its value
    var normal = GeneralDESimulator.Run(new DESSimConfig(1, TimeUnitType.Minutes, 500, 1e9, 3,
        new DistConfig(DistType.Normal, 3.0, 0.8), new DistConfig(DistType.Gamma, 2.0, 0.5)));
    var nd = new NormalDistribution(3.0, 0.8);
    var gd = new GammaDistribution(2.0, 0.5);
    t.True("F11", "Normal arrivals: each interarrival time = F⁻¹(displayed R_A)",
        normal.CustomerRows.All(r => Math.Abs(nd.Quantile(r.ArrivalRandom) - r.InterarrivalTime) < 1e-3));
    t.True("F11", "Gamma service: each service time = F⁻¹(displayed R_S)",
        normal.CustomerRows.All(r => Math.Abs(gd.Quantile(r.ServiceRandom) - r.ServiceTime) < 1e-3));
}

// ── Statistics (F5, F7, F20) ──────────────────────────────────────
t.Section("Chi-square & outliers");
{
    t.Near("F20", "χ² critical value df = 8, α = 0.05 = 15.507", ChiSquareGoodnessOfFit.ChiSquareCritical(8, 0.05), 15.507, 0.002);
    t.Near("F20", "χ² critical value df = 1, α = 0.05 = 3.841", ChiSquareGoodnessOfFit.ChiSquareCritical(1, 0.05), 3.841, 0.002);
    t.Near("F20", "χ² CDF(2, df = 2) = 1 − e⁻¹", ChiSquareGoodnessOfFit.ChiSquareCDF(2, 2), 1 - Math.Exp(-1), 1e-9);

    var data = DatasetIngestionEngine.Generate(2026);
    t.Throws("F20", "k = 2 bins is rejected instead of dividing by zero", () => ChiSquareGoodnessOfFit.Run(data, k: 2));
    t.Throws("F20", "Empty dataset is rejected instead of returning NaN", () => ChiSquareGoodnessOfFit.Run([]));

    var before = ChiSquareGoodnessOfFit.Run(data);
    int flagged = OutlierDetector.Detect(data);
    var after = ChiSquareGoodnessOfFit.Run(data);
    t.True("F7", "χ² uses all inter-arrivals, even after outlier detection", before.ChiSquareStat == after.ChiSquareStat);
    t.True("F7", "IQR rule is applied to service durations only",
        data.Where(r => r.IsOutlier).All(r => IsServiceOutlier(data, r)), $"{flagged} flagged");

    t.Info($"Synthetic baseline (seed 2026): χ² = {before.ChiSquareStat:F2}, df = {before.DegreesOfFreedom}, " +
           $"critical = {before.CriticalValue:F3}, p = {before.PValue:F3}");

    // The Audit page must quote exactly what the code computes (F4/F5)
    string audit = File.ReadAllText(Path.Combine(RepoRoot(), "Views", "PromptAuditView.xaml"));
    t.True("F4", $"Audit page quotes the computed χ² = {before.ChiSquareStat:F2}", audit.Contains($"χ² = {before.ChiSquareStat:F2}"));
    t.True("F4", $"Audit page quotes the computed p = {before.PValue:F3}", audit.Contains($"p = {before.PValue:F3}"));
    t.True("F4", "Audit page no longer quotes χ² = 9.14 / p = 0.329", !audit.Contains("9.14") && !audit.Contains("0.329"));
    t.True("F5", "Audit page calls the dataset synthetic", audit.Contains("synthetic", StringComparison.OrdinalIgnoreCase));

    double bikeFaster = (85.0 - 35.0) / 85.0 * 100;                        // 58.8%
    double mixLower = (1 - (0.7 * 35 + 0.3 * 85) / 85.0) * 100;             // 41.2%
    t.True("F4", $"Audit page quotes bikes {bikeFaster:F1}% faster", audit.Contains($"{bikeFaster:F1}%"));
    t.True("F4", $"Audit page quotes the mix lowering utilisation by {mixLower:F0}%", audit.Contains($"{mixLower:F0}%"));

    string comparison = File.ReadAllText(Path.Combine(RepoRoot(), "Views", "ModelComparisonView.xaml.cs"))
                      + File.ReadAllText(Path.Combine(RepoRoot(), "Views", "ModelComparisonView.xaml"));
    t.True("F4", "No hard-coded 'Confidence 99.4%' / 'Calibration Delta' labels", !comparison.Contains("99.4") && !comparison.Contains("Calibration Delta"));
}

// ── Forecourt simulator (F13, F21) ────────────────────────────────
t.Section("Forecourt simulator");
{
    var sim = ForecourtSimulator.Run(ForecourtConfig.Default with { SimDurationSeconds = 36_000, NumServers = 4 });
    var arrivals = sim.Logs.Where(l => l.EventType == "Arrival").ToList();
    double posShare = arrivals.Count(l => l.PaymentPath == "4A-POS") / (double)arrivals.Count;
    t.Near("F13", "Only card/QR payers use the POS lane (≈ 15% of vehicles)", posShare, 0.15, 0.02);
    t.True("F13", "Cash-paying cars use the 4B cash lane",
        arrivals.Any(l => l.VehicleType == "Car" && l.PaymentPath == "4B-Cash"));
    t.True("F21", "Pump utilisation stays within [0, 1]", sim.ServerUtilization.All(u => u is >= 0 and <= 1));
    t.True("F21", "Queued-vehicle notes count the vehicles ahead",
        sim.Logs.Where(l => l.EventType == "Queued").All(l => l.Notes.Contains("ahead")));
}

return t.Summary();

// ── Helpers ───────────────────────────────────────────────────────
static double SumNPn(QueueMetrics m) => Enumerable.Range(0, 3000).Sum(n => n * m.Pn(n));

static double Uniform01(Random rng)
{
    double u;
    do u = rng.NextDouble(); while (u == 0.0);
    return u;
}

static bool IsServiceOutlier(List<ForecourtObservationRecord> data, ForecourtObservationRecord r)
{
    double[] svc = [.. data.Select(d => d.ServiceDuration).OrderBy(x => x)];
    var f = OutlierDetector.ComputeFences(svc);
    return r.ServiceDuration < f.Lower || r.ServiceDuration > f.Upper;
}

static string RepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FuelQ.csproj"))) dir = dir.Parent;
    return dir?.FullName ?? throw new DirectoryNotFoundException("FuelQ.csproj not found above the test binaries.");
}

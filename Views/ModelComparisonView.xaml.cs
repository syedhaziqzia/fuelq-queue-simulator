using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FuelQ.Models;
using FuelQ.Simulation;

namespace FuelQ.Views;

public partial class ModelComparisonView : UserControl
{
    // Row type for the comparison DataGrid
    private class CompRow
    {
        public string ModelName    { get; set; } = string.Empty;
        public string TypeDesc     { get; set; } = string.Empty;
        public string Rho          { get; set; } = "—";
        public string Lq           { get; set; } = "—";
        public string Wq           { get; set; } = "—";
        public string L            { get; set; } = "—";
        public string W            { get; set; } = "—";
        public string P0           { get; set; } = "—";
        public string Pwait        { get; set; } = "—";
        public string BestFitBadge { get; set; } = "—";
        public bool   IsBestFit    { get; set; }
    }

    private List<CompRow>? _lastRows;

    private bool _autoRan;

    public ModelComparisonView()
    {
        InitializeComponent();
        // Fill the page on first visit so it never opens empty
        Loaded += (_, _) =>
        {
            if (_autoRan) return;
            _autoRan = true;
            RunComparison_Click(BtnRunComparison, new RoutedEventArgs());
        };
    }

    private async void RunComparison_Click(object sender, RoutedEventArgs e)
    {
        if (!ParseParams(out double lambda, out double mu, out int c, out double sigma, out int k)) return;

        var btn = (Button)sender;
        btn.IsEnabled = false;
        btn.Content   = "⏳  Computing...";

        try
        {
            // The calculators are unit-agnostic: rates in veh/min give Wq and W in minutes.
            double variance = sigma * sigma;   // σ entered in minutes → Var(S) in min²

            List<CompRow> rows = await Task.Run(() =>
            {
                var r = new List<CompRow>();

                AddAnalytical(r, "M/M/1", "Single Exponential",
                    MM1Calculator.Calculate(lambda, mu), false, "—");

                // M/D/1 — expects meanService = 1/μ (constant service time), not the rate
                AddAnalytical(r, "M/D/1", "Deterministic Dispensing",
                    MD1Calculator.Calculate(lambda, 1.0 / mu), false, "—");

                AddAnalytical(r, "M/G/1", "General Service (P-K)",
                    MG1Calculator.Calculate(lambda, mu, variance), false, "—");

                AddAnalytical(r, $"M/E{k}/1", $"Erlang-{k} Multi-Stage",
                    MEk1Calculator.Calculate(lambda, 1.0 / mu, k), false, "—");   // expects mean service time, not μ

                AddAnalytical(r, $"M/M/c (c={c})", "Multi-Server Erlang-C",
                    MMcCalculator.Calculate(lambda, mu, c), true, $"★ Matches {c}-pump layout");

                // Forecourt DES (one run) — uses its own service model: bikes Exp(35 s), cars Exp(85 s)
                // plus POS and delay addends; only λ and c come from the inputs above.
                var cfg = new ForecourtConfig(
                    NumServers:         c,
                    ArrivalRate:        lambda / 60.0,   // veh/min → veh/s
                    BikeServiceRate:    1.0 / 35.0,
                    CarServiceRate:     1.0 / 85.0,
                    SimDurationSeconds: 3600,
                    BikeFraction:       0.70,
                    RandomSeed:         2026);
                var sim = ForecourtSimulator.Run(cfg);
                double simRho = sim.ServerUtilization.Average();
                r.Add(new CompRow
                {
                    ModelName    = "DES Simulation",
                    TypeDesc     = "Forecourt event-list DES (1 h)",
                    Rho          = $"{simRho * 100:F1}%",
                    Lq           = $"{sim.AverageQueueLength:F2} veh",
                    Wq           = $"{sim.AverageWaitingTime / 60:F2} min",
                    L            = $"{sim.AverageQueueLength + simRho * c:F2} veh",
                    W            = $"{sim.AverageSystemTime / 60:F2} min",
                    P0           = "—",   // not measured by the simulator
                    Pwait        = "—",
                    BestFitBadge = "Single run, seed 2026",
                    IsBestFit    = false
                });
                return r;
            });

            _lastRows = rows;
            CompMatrix.ItemsSource = rows;
            CalibrationLabel.Text  = "  ·  DES row: one 1-hour run (seed 2026), no confidence interval";

            UpdateEfficiencyGain(lambda, mu, c);
            await DrawSensitivityCurveAsync(mu, c);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Comparison error: {ex.Message}", "Model Comparison",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btn.IsEnabled = true;
            btn.Content   = "⚙  Run Model Comparison";
        }
    }

    private static void AddAnalytical(List<CompRow> rows, string name, string type,
        (QueueMetrics? Metrics, string? Error) result, bool best, string badge)
    {
        var m = result.Metrics;
        rows.Add(new CompRow
        {
            ModelName    = name,
            TypeDesc     = type,
            Rho          = m != null ? $"{m.Rho * 100:F1}%" : "—",
            Lq           = m != null ? $"{m.Lq:F2} veh" : "—",
            Wq           = m != null ? $"{m.Wq:F2} min" : "—",
            L            = m != null ? $"{m.L:F2} veh" : "—",
            W            = m != null ? $"{m.W:F2} min" : "—",
            P0           = m != null ? $"{m.P0 * 100:F1}%" : "—",
            Pwait        = m != null ? $"{m.Pwait * 100:F1}%" : "—",
            BestFitBadge = badge,
            IsBestFit    = best
        });
    }

    private void UpdateEfficiencyGain(double lambda, double mu, int cBest)
    {
        var single = MM1Calculator.Calculate(lambda, mu).Metrics;
        var multi  = MMcCalculator.Calculate(lambda, mu, cBest).Metrics;
        if (single == null || multi == null) return;

        double pct = single.Lq > 0 ? (single.Lq - multi.Lq) / single.Lq * 100 : 0;
        EffHeaderLabel.Text = $"1-PUMP → {cBest}-PUMPS";
        EfficiencyPct.Text  = $"{pct:F1}%";
        EfficiencyDesc.Text = $"Transitioning from single-channel (M/M/1) to {cBest}-parallel dispensers (M/M/{cBest}) mitigates systemic bottle-necking.";
        EffWq.Text  = $"{single.Wq:F2}m → {multi.Wq:F2}m ({multi.Wq * 60:F1}s)";
        EffLq.Text  = $"{single.Lq:F2} → {multi.Lq:F2} veh";
        EffP0.Text  = $"{(1 - multi.Pwait) * 100:F1}%";
    }

    private async Task DrawSensitivityCurveAsync(double mu, int c)
    {
        var rhoArr = Enumerable.Range(10, 89).Select(i => i / 100.0).ToArray();

        // Run all 3 × 89 calculator calls on a background thread
        var (lqMM1, lqMD1, lqMMC) = await Task.Run(() =>
        {
            double[] mm1 = [.. rhoArr.Select(r => { var m = MM1Calculator.Calculate(r * mu, mu).Metrics; return m?.Lq ?? 0; })];
            // M/D/1: meanService = 1/(r·μ), where r·μ is the effective service rate at utilisation r
            double[] md1 = [.. rhoArr.Select(r => { var m = MD1Calculator.Calculate(r * mu, 1.0 / mu).Metrics; return m?.Lq ?? 0; })];
            double[] mmc = [.. rhoArr.Select(r => { var m = MMcCalculator.Calculate(r * c * mu, mu, c).Metrics; return m?.Lq ?? 0; })];
            return (mm1, md1, mmc);
        });

        SensitivityChart.SetSeries(
        [
            (rhoArr, lqMM1, Color.FromRgb(0xFF, 0x6B, 0x5E), "M/M/1 (Uncontrolled)"),
            (rhoArr, lqMD1, Color.FromRgb(0x3E, 0xCF, 0xB2), "M/D/1 (P-K Lower)"),
            (rhoArr, lqMMC, Color.FromRgb(0xFF, 0xB0, 0x20), $"M/M/{c} (Maskan Layout)")
        ]);
    }

    private void ExportLatex_Click(object sender, RoutedEventArgs e)
    {
        string latex = GenerateLatex();
        var panel = new StackPanel { Margin = new Thickness(0) };

        panel.Children.Add(new TextBlock
        {
            Text = "Copy the LaTeX code below into your \\begin{table}...\\end{table} environment:",
            Foreground = (Brush)FindResource("TextSecondary"),
            FontSize = 12, Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        });

        var tb = new TextBox
        {
            Text = latex, IsReadOnly = true, TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"), FontSize = 11,
            Background = (Brush)FindResource("InputSurface"),
            Foreground = (Brush)FindResource("TextPrimary"),
            BorderBrush = (Brush)FindResource("CardBorder"), BorderThickness = new Thickness(1),
            Padding = new Thickness(10), Height = 300,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto
        };
        panel.Children.Add(tb);

        var copyBtn = new Button
        {
            Content = "📋  Copy to Clipboard", Style = (Style)FindResource("PrimaryButton"),
            Margin = new Thickness(0, 10, 0, 0)
        };
        copyBtn.Click += (_, _) => { Clipboard.SetText(latex); copyBtn.Content = "✓  Copied!"; };
        panel.Children.Add(copyBtn);

        (Window.GetWindow(this) as MainWindow)?.ShowModal("Export LaTeX Table", panel);
    }

    private string GenerateLatex()
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"\begin{table}[ht]");
        sb.AppendLine(@"\centering");
        sb.AppendLine(@"\caption{Queuing Model Comparison — Shell Maskan Chowrangi Forecourt}");
        sb.AppendLine(@"\label{tab:model_comparison}");
        sb.AppendLine(@"\begin{tabular}{lllllllll}");
        sb.AppendLine(@"\toprule");
        sb.AppendLine(@"Model & Type & $\rho$ & $L_q$ & $W_q$ & $L$ & $W$ & $P_0$ & $P_w$ \\");
        sb.AppendLine(@"\midrule");
        if (_lastRows != null)
        {
            // Escape % and & so values like "81.8%" do not comment out the rest of the LaTeX row
            static string Esc(string s) => s.Replace("&", @"\&").Replace("%", @"\%");
            foreach (var r in _lastRows)
                sb.AppendLine($"{Esc(r.ModelName)} & {Esc(r.TypeDesc)} & {Esc(r.Rho)} & {Esc(r.Lq)} & {Esc(r.Wq)} & {Esc(r.L)} & {Esc(r.W)} & {Esc(r.P0)} & {Esc(r.Pwait)} \\\\");
        }
        sb.AppendLine(@"\bottomrule");
        sb.AppendLine(@"\end{tabular}");
        sb.AppendLine(@"\end{table}");
        return sb.ToString();
    }

    private bool ParseParams(out double lambda, out double mu, out int c, out double sigma, out int k)
    {
        mu = sigma = 0; c = k = 1;

        bool parsed = double.TryParse(TbLambda.Text, out lambda)
                   && double.TryParse(TbMu.Text, out mu)
                   && int.TryParse(TbC.Text, out c)
                   && double.TryParse(TbSigma.Text, out sigma)
                   && int.TryParse(TbK.Text, out k);

        if (!parsed)
        {
            MessageBox.Show("Please enter valid numeric values in all fields.",
                "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        // Apply bounds; all values stay in minutes
        c = Math.Max(1, Math.Min(c, 20));   // clamp servers 1–20
        k = Math.Max(1, Math.Min(k, 20));   // clamp Erlang phases 1–20
        sigma = Math.Max(0, sigma);

        if (lambda <= 0 || mu <= 0)
        {
            MessageBox.Show("Arrival rate (λ) and service rate (μ) must be positive.",
                "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }
}

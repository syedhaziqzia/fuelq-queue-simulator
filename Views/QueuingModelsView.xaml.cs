using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FuelQ.Models;

namespace FuelQ.Views;

public class StateProbabilityRow
{
    public int N { get; set; }
    public string PnStr { get; set; } = string.Empty;
    public string PctStr { get; set; } = string.Empty;
    public string CumStr { get; set; } = string.Empty;
}

public partial class QueuingModelsView : UserControl
{
    private string _currentModel = "MM1";
    private bool _loaded;
    private bool _syncingInputs; // true while code writes a derived value into an input box

    private static readonly Dictionary<string, string> ModelDescriptions = new()
    {
        ["MM1"] = "M/M/1 · Single server. Poisson arrivals + Exponential service (both fixed). Exact formula: Lq = ρ²/(1−ρ).",
        ["MMS"] = "M/M/s · Multi-server (s parallel pumps). Poisson arrivals + Exponential service. Exact Erlang-C formulas for P₀ and Pw.",
        ["MG1"] = "M/G/1 · Single server. Poisson arrivals + General service distribution (Uniform / Normal). Exact Pollaczek-Khinchine formula.",
        ["MGS"] = "M/G/s · Multi-server. Poisson arrivals + General service distribution. Allen-Cunneen approximation: Wq ≈ [(1 + Cs²)/2] × Wq(M/M/s).",
        ["GG1"] = "G/G/1 · Single server. General arrivals + General service. Kingman approximation: Wq ≈ [(Ca² + Cs²)/2] × [ρ/(1−ρ)] × E[S].",
        ["GGS"] = "G/G/s · Multi-server. General arrivals + General service. Allen-Cunneen approximation: Wq ≈ [(Ca² + Cs²)/2] × Wq(M/M/s)."
    };

    public QueuingModelsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _loaded = true;
            UpdateModelUI();
            Recalculate();
        };
    }

    private void ModelTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string modelTag)
        {
            _currentModel = modelTag;

            // Update button styles
            BtnMM1.Style = _currentModel == "MM1" ? (Style)FindResource("PrimaryButton") : (Style)FindResource("SecondaryButton");
            BtnMMS.Style = _currentModel == "MMS" ? (Style)FindResource("PrimaryButton") : (Style)FindResource("SecondaryButton");
            BtnMG1.Style = _currentModel == "MG1" ? (Style)FindResource("PrimaryButton") : (Style)FindResource("SecondaryButton");
            BtnMGS.Style = _currentModel == "MGS" ? (Style)FindResource("PrimaryButton") : (Style)FindResource("SecondaryButton");
            BtnGG1.Style = _currentModel == "GG1" ? (Style)FindResource("PrimaryButton") : (Style)FindResource("SecondaryButton");
            BtnGGS.Style = _currentModel == "GGS" ? (Style)FindResource("PrimaryButton") : (Style)FindResource("SecondaryButton");

            UpdateModelUI();
            Recalculate();
        }
    }

    private void UpdateModelUI()
    {
        TxtModelDescription.Text = ModelDescriptions.GetValueOrDefault(_currentModel, "");

        bool isMulti = _currentModel is "MMS" or "MGS" or "GGS";
        PanelServers.Visibility = isMulti ? Visibility.Visible : Visibility.Collapsed;

        bool arrChoosable = _currentModel is "GG1" or "GGS";
        PanelArrivalChoice.Visibility = arrChoosable ? Visibility.Visible : Visibility.Collapsed;
        PanelArrivalFixed.Visibility = arrChoosable ? Visibility.Collapsed : Visibility.Visible;

        bool svcChoosable = _currentModel is "MG1" or "MGS" or "GG1" or "GGS";
        PanelServiceChoice.Visibility = svcChoosable ? Visibility.Visible : Visibility.Collapsed;
        PanelServiceFixed.Visibility = svcChoosable ? Visibility.Collapsed : Visibility.Visible;

        UpdateDistFields();
    }

    private void ArrivalMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        Recalculate();
    }

    private void ServiceMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        Recalculate();
    }

    private void ArrivalDist_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        UpdateDistFields();
        Recalculate();
    }

    private void ServiceDist_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        UpdateDistFields();
        Recalculate();
    }

    private void UpdateDistFields()
    {
        int arrIdx = CbArrivalDist.SelectedIndex;
        PanelArrivalUniform.Visibility = arrIdx == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelArrivalNormal.Visibility = arrIdx == 2 ? Visibility.Visible : Visibility.Collapsed;

        int svcIdx = CbServiceDist.SelectedIndex;
        PanelServiceUniform.Visibility = svcIdx == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelServiceNormal.Visibility = svcIdx == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Param_Changed(object sender, EventArgs e)
    {
        if (!_loaded) return;
        Recalculate();
    }

    private void Calculate_Click(object sender, RoutedEventArgs e)
    {
        Recalculate();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _currentModel = "MM1";
        BtnMM1.Style = (Style)FindResource("PrimaryButton");
        BtnMMS.Style = (Style)FindResource("SecondaryButton");
        BtnMG1.Style = (Style)FindResource("SecondaryButton");
        BtnMGS.Style = (Style)FindResource("SecondaryButton");
        BtnGG1.Style = (Style)FindResource("SecondaryButton");
        BtnGGS.Style = (Style)FindResource("SecondaryButton");

        CbTimeUnit.SelectedIndex = 0; // Minutes
        TbNumCustomers.Text = "1";
        RbArrivalMean.IsChecked = true;
        TbArrivalVal.Text = "3";
        RbServiceMean.IsChecked = true;
        TbServiceVal.Text = "2";
        TbNumServers.Text = "2";

        CbArrivalDist.SelectedIndex = 0;
        TbArrUniformMin.Text = "2";
        TbArrUniformMax.Text = "4";
        TbArrNormalStd.Text = "1";
        TbArrNormalVar.Text = "1";

        CbServiceDist.SelectedIndex = 0;
        TbSvcUniformMin.Text = "1";
        TbSvcUniformMax.Text = "3";
        TbSvcNormalStd.Text = "1";
        TbSvcNormalVar.Text = "1";

        UpdateModelUI();
        Recalculate();
    }

    private void GoToSimulator_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mainWin)
        {
            mainWin.NavigateTo(2); // Simulator
        }
    }

    private void Recalculate()
    {
        if (_syncingInputs) return;

        string unit = (CbTimeUnit.SelectedItem as ComboBoxItem)?.Content?.ToString() switch
        {
            "Seconds" => "sec",
            "Hours" => "hr",
            _ => "min"
        };

        // Distribution index per side: 0 = Exponential, 1 = Uniform, 2 = Normal (fixed models are always Exponential)
        int arrDist = _currentModel is "GG1" or "GGS" ? CbArrivalDist.SelectedIndex : 0;
        int svcDist = _currentModel is "MG1" or "MGS" or "GG1" or "GGS" ? CbServiceDist.SelectedIndex : 0;
        string? inputError = null;

        // Arrival mean and variance. Uniform takes its mean from the [a, b] bounds,
        // so the mean box becomes a read-only display of the derived value.
        double lambda, arrVar;
        if (arrDist == 1)
        {
            var (mean, var, err) = UniformMoments(TbArrUniformMin.Text, TbArrUniformMax.Text, 2.0, 4.0, "interarrival");
            inputError ??= err;
            lambda = 1.0 / mean;
            arrVar = var;
            ShowDerivedValue(TbArrivalVal, RbArrivalMean.IsChecked == true ? mean : lambda);
        }
        else
        {
            double arrVal = double.TryParse(TbArrivalVal.Text, out double av) ? av : 3.0;
            if (arrVal <= 0) arrVal = 1.0;
            lambda = RbArrivalMean.IsChecked == true ? 1.0 / arrVal : arrVal;
            arrVar = 1.0 / (lambda * lambda);
            if (arrDist == 2)
            {
                double std = double.TryParse(TbArrNormalStd.Text, out double ast) ? ast : 1.0;
                if (std < 0) inputError ??= "Standard deviation σ must be ≥ 0.";
                arrVar = std * std;
                ShowDerivedValue(TbArrNormalVar, arrVar);
            }
        }

        // Service mean and variance (same rules as arrivals)
        double mu, svcVar;
        if (svcDist == 1)
        {
            var (mean, var, err) = UniformMoments(TbSvcUniformMin.Text, TbSvcUniformMax.Text, 1.0, 3.0, "service");
            inputError ??= err;
            mu = 1.0 / mean;
            svcVar = var;
            ShowDerivedValue(TbServiceVal, RbServiceMean.IsChecked == true ? mean : mu);
        }
        else
        {
            double svcVal = double.TryParse(TbServiceVal.Text, out double sv) ? sv : 2.0;
            if (svcVal <= 0) svcVal = 1.0;
            mu = RbServiceMean.IsChecked == true ? 1.0 / svcVal : svcVal;
            svcVar = 1.0 / (mu * mu);
            if (svcDist == 2)
            {
                double std = double.TryParse(TbSvcNormalStd.Text, out double sst) ? sst : 1.0;
                if (std < 0) inputError ??= "Standard deviation σ must be ≥ 0.";
                svcVar = std * std;
                ShowDerivedValue(TbSvcNormalVar, svcVar);
            }
        }

        TbArrivalVal.IsReadOnly = arrDist == 1;
        TbServiceVal.IsReadOnly = svcDist == 1;
        UpdateHints(arrDist == 1, svcDist == 1);

        // Parse Servers
        int s = int.TryParse(TbNumServers.Text, out int svrs) ? Math.Max(1, svrs) : 1;
        if (_currentModel is "MM1" or "MG1" or "GG1") s = 1;

        double a = lambda / mu;
        double rho = lambda / (s * mu);

        TxtDerivedLambda.Text = $"{lambda:F4} /{unit}";
        TxtDerivedMu.Text = $"{mu:F4} /{unit}";
        TxtDerivedA.Text = $"{a:F4} Erlangs";
        TxtDerivedRho.Text = $"{rho * 100:F1}%";

        TxtRhoTitle.Text = s > 1 ? $"UTILIZATION (ρ = λ/(s·μ))" : "UTILIZATION (ρ = λ/μ)";
        TxtDerivedRho.Foreground = rho >= 1.0 ? (Brush)FindResource("StatusRedText")
                                 : rho > 0.85 ? (Brush)FindResource("AccentOrange")
                                 : (Brush)FindResource("StatusGreenText");

        QueueMetrics? metrics = null;
        string? error = null;

        switch (_currentModel)
        {
            case "MM1":
                (metrics, error) = MM1Calculator.Calculate(lambda, mu);
                break;
            case "MMS":
                (metrics, error) = MMsCalculator.Calculate(lambda, mu, s);
                break;
            case "MG1":
                (metrics, error) = MG1Calculator.Calculate(lambda, mu, svcVar);
                break;
            case "MGS":
                (metrics, error) = MGsCalculator.Calculate(lambda, mu, s, svcVar);
                break;
            case "GG1":
                (metrics, error) = GG1Calculator.Calculate(lambda, mu, arrVar, svcVar);
                break;
            case "GGS":
                (metrics, error) = GGsCalculator.Calculate(lambda, mu, s, arrVar, svcVar);
                break;
        }

        if (inputError != null) error = inputError;

        // Update stability
        if (error != null || metrics == null || rho >= 1.0)
        {
            // Only ρ ≥ 1 means "unstable"; other errors are invalid inputs or calculator limits
            bool unstable = inputError == null && rho >= 1.0;
            string none = unstable ? "∞" : "—";

            BorderStability.Background = (Brush)FindResource("StatusRedBg");
            FuelQ.Controls.Motion.SetPulsing(BorderStability, unstable);
            BorderStability.BorderBrush = (Brush)FindResource("StatusRedBorder");
            TxtStabilityTitle.Text = unstable ? "⚠ SYSTEM UNSTABLE  (ρ ≥ 1.0)" : "⚠ CANNOT COMPUTE";
            TxtStabilityTitle.Foreground = (Brush)FindResource("StatusRedText");
            TxtStabilityDetail.Text = error ?? $"Arrival rate exceeds service capacity (λ={lambda:F4} ≥ {s}×μ={s * mu:F4}). Queue grows without bound.";

            MetricLambda.Text = $"{lambda:F4}";
            MetricMu.Text = $"{mu:F4}";
            MetricA.Text = $"{a:F4}";
            MetricRho.Text = $"{rho * 100:F1}%";
            MetricP0.Text = unstable ? "0.0%" : "—";
            MetricPw.Text = unstable ? "100.0%" : "—";
            MetricLq.Text = none;
            MetricWq.Text = none;
            MetricL.Text = none;
            MetricW.Text = none;

            TxtFormulaBreakdown.Text = unstable
                ? "System is in non-stationary / saturated state. Infinite queue growth."
                : "No result: fix the input shown above.";
            TxtFormulaDetails.Text = unstable
                ? "Increase pump count (s) or service rate (μ) to restore stability."
                : string.Empty;
            GridStateProbabilities.ItemsSource = null;
            TxtPnUnavailable.Visibility = Visibility.Collapsed;
            return;
        }

        BorderStability.Background = (Brush)FindResource("StatusGreenBg");
        FuelQ.Controls.Motion.SetPulsing(BorderStability, false);
        BorderStability.BorderBrush = (Brush)FindResource("StatusGreenBorder");
        TxtStabilityTitle.Text = $"✓ SYSTEM STABLE  (ρ = {metrics.Rho * 100:F1}% < 1.0)";
        TxtStabilityTitle.Foreground = (Brush)FindResource("StatusGreenText");
        TxtStabilityDetail.Text = $"Operating with steady-state equilibrium. Off-load capacity: {(1.0 - metrics.Rho) * 100:F1}%.";

        MetricLambda.Text = $"{metrics.Lambda:F4}";
        MetricLambdaUnit.Text = $"vehicles / {unit}";
        MetricMu.Text = $"{metrics.Mu:F4}";
        MetricMuUnit.Text = $"vehicles / {unit}";
        MetricA.Text = $"{metrics.TrafficIntensity:F4}";
        MetricRho.Text = $"{metrics.Rho * 100:F1}%";
        MetricP0.Text = $"{metrics.P0 * 100:F1}%";
        MetricPw.Text = $"{metrics.Pwait * 100:F1}%";
        MetricLq.Text = $"{metrics.Lq:F4}";
        MetricWq.Text = $"{metrics.Wq:F4}";
        MetricWqUnit.Text = $"{unit} in queue";
        MetricL.Text = $"{metrics.L:F4}";
        MetricW.Text = $"{metrics.W:F4}";
        MetricWUnit.Text = $"{unit} total time";

        TxtFormulaBreakdown.Text = metrics.FormulaUsed;
        TxtFormulaDetails.Text = $"Model: {metrics.ModelName}  |  Ca = {metrics.Ca:F3}  |  Cs = {metrics.Cs:F3}  |  Servers = {metrics.S}";

        // State Probabilities: exact only for M/M/1 and M/M/s
        TxtPnUnavailable.Visibility = metrics.HasExactStateProbabilities ? Visibility.Collapsed : Visibility.Visible;
        if (!metrics.HasExactStateProbabilities)
        {
            GridStateProbabilities.ItemsSource = null;
            return;
        }

        var probRows = new List<StateProbabilityRow>();
        double cum = 0;
        for (int n = 0; n <= 10; n++)
        {
            double pn = metrics.Pn(n);
            cum += pn;
            probRows.Add(new StateProbabilityRow
            {
                N = n,
                PnStr = pn.ToString("F4"),
                PctStr = $"{(pn * 100):F2}%",
                CumStr = $"{(cum * 100):F2}%"
            });
        }
        GridStateProbabilities.ItemsSource = probRows;
    }

    /// <summary>Mean and variance of Uniform[a, b] from the two bound boxes, with an error if the bounds are invalid.</summary>
    private static (double Mean, double Variance, string? Error) UniformMoments(
        string minText, string maxText, double defaultMin, double defaultMax, string what)
    {
        double a = double.TryParse(minText, out double pa) ? pa : defaultMin;
        double b = double.TryParse(maxText, out double pb) ? pb : defaultMax;
        if (a < 0 || b <= a)
            return ((defaultMin + defaultMax) / 2.0, Math.Pow(defaultMax - defaultMin, 2) / 12.0,
                    $"Uniform {what} bounds need 0 ≤ a < b (got a = {a}, b = {b}).");
        return ((a + b) / 2.0, (b - a) * (b - a) / 12.0, null);
    }

    /// <summary>Writes a derived value into a read-only box without re-triggering Recalculate.</summary>
    private void ShowDerivedValue(TextBox box, double value)
    {
        string text = value.ToString("0.####");
        if (box.Text == text) return;
        _syncingInputs = true;
        try { box.Text = text; }
        finally { _syncingInputs = false; }
    }

    private void UpdateHints(bool arrUniform, bool svcUniform)
    {
        bool arrMean = RbArrivalMean.IsChecked == true;
        TxtArrivalHint.Text = arrUniform
            ? "Derived from Uniform bounds: mean = (a + b) / 2 (edit a and b below)"
            : arrMean ? "λ = 1 ÷ this value (Mean headway between vehicle arrivals)" : "Used directly as arrival rate λ";

        bool svcMean = RbServiceMean.IsChecked == true;
        TxtServiceHint.Text = svcUniform
            ? "Derived from Uniform bounds: mean = (a + b) / 2 (edit a and b below)"
            : svcMean ? "μ = 1 ÷ this value (Mean duration to fuel one vehicle)" : "Used directly as service rate μ";
    }
}

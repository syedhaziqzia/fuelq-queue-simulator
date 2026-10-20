using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FuelQ.Controls;

public enum ChartMode { Bar, Line }

/// <summary>
/// Native WPF chart via DrawingContext.OnRender — zero NuGet dependencies.
/// Mode A (Bar): P(n) distributions and frequency histograms.
/// Mode B (Line): Q(t) timelines and Lq-vs-ρ sensitivity curves.
/// </summary>
public class SimpleWpfChart : FrameworkElement
{
    // ── Dependency Properties ─────────────────────────────────────
    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(nameof(Mode), typeof(ChartMode), typeof(SimpleWpfChart),
        new FrameworkPropertyMetadata(ChartMode.Bar, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(nameof(Values), typeof(double[]), typeof(SimpleWpfChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
            (d, _) => ((SimpleWpfChart)d).PlayReveal()));

    /// <summary>0 → 1 while the chart draws itself in after new data arrives.</summary>
    public static readonly DependencyProperty RevealProperty =
        DependencyProperty.Register(nameof(Reveal), typeof(double), typeof(SimpleWpfChart),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelsProperty =
        DependencyProperty.Register(nameof(Labels), typeof(string[]), typeof(SimpleWpfChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighlightIndexProperty =
        DependencyProperty.Register(nameof(HighlightIndex), typeof(int), typeof(SimpleWpfChart),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty XAxisLabelProperty =
        DependencyProperty.Register(nameof(XAxisLabel), typeof(string), typeof(SimpleWpfChart),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty YAxisLabelProperty =
        DependencyProperty.Register(nameof(YAxisLabel), typeof(string), typeof(SimpleWpfChart),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    // Line chart series (list of (x,y) point arrays)
    private List<(double[] X, double[] Y, Color Color, string Label)> _series = [];

    // Line/area geometries cached per data set and size, so the reveal animation only re-clips
    private readonly Dictionary<(int Series, bool Area), StreamGeometry> _geoCache = new();
    private (object? Series, double W, double H) _geoKey;

    // ── Public Properties ─────────────────────────────────────────
    public ChartMode Mode           { get => (ChartMode)GetValue(ModeProperty);      set => SetValue(ModeProperty, value); }
    public double[]? Values         { get => (double[]?)GetValue(ValuesProperty);    set => SetValue(ValuesProperty, value); }
    public string[]? Labels         { get => (string[]?)GetValue(LabelsProperty);    set => SetValue(LabelsProperty, value); }
    public int       HighlightIndex { get => (int)GetValue(HighlightIndexProperty);  set => SetValue(HighlightIndexProperty, value); }
    public string    XAxisLabel     { get => (string)GetValue(XAxisLabelProperty);   set => SetValue(XAxisLabelProperty, value); }
    public string    YAxisLabel     { get => (string)GetValue(YAxisLabelProperty);   set => SetValue(YAxisLabelProperty, value); }
    public double    Reveal         { get => (double)GetValue(RevealProperty);       set => SetValue(RevealProperty, value); }

    public void SetSeries(List<(double[] X, double[] Y, Color Color, string Label)> series)
    {
        _series = series;
        PlayReveal();
        InvalidateVisual();
    }

    /// <summary>Draws the chart in: lines sweep left → right, bars grow up one after another.</summary>
    private void PlayReveal()
    {
        BeginAnimation(RevealProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    // ── Brushes & Pens ("forecourt at night" palette) ─────────────
    private static readonly SolidColorBrush BgBrush      = new(Color.FromRgb(0x14, 0x12, 0x10));
    private static readonly SolidColorBrush GridBrush    = new(Color.FromRgb(0x2A, 0x26, 0x20));
    private static readonly SolidColorBrush DefaultBar   = new(Color.FromRgb(0xB8, 0x7A, 0x14));
    private static readonly SolidColorBrush CyanBar      = new(Color.FromRgb(0xFF, 0xB0, 0x20));
    private static readonly SolidColorBrush TextBrush    = new(Color.FromRgb(0xA3, 0x9A, 0x8A));
    private static readonly SolidColorBrush AxisBrush    = new(Color.FromRgb(0x3A, 0x35, 0x2C));
    private static readonly Pen             GridPen      = new(GridBrush, 1) { DashStyle = DashStyles.Dot };
    private static readonly Pen             AxisPen      = new(AxisBrush, 1);
    private static readonly Pen             SteadyPen    = new(new SolidColorBrush(Color.FromArgb(160, 0xF8, 0x71, 0x71)), 1) { DashStyle = DashStyles.Dash };

    private static readonly Typeface SmallFont = new(new FontFamily("Bahnschrift, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    // Cache DPI once at first render; avoids P/Invoke on every DrawText call.
    private double _dpi = 96.0 / 96.0; // default 1.0 until updated
    private bool   _dpiCached;

    static SimpleWpfChart()
    {
        BgBrush.Freeze(); GridBrush.Freeze(); DefaultBar.Freeze();
        CyanBar.Freeze(); TextBrush.Freeze(); AxisBrush.Freeze();
        GridPen.Freeze(); AxisPen.Freeze(); SteadyPen.Freeze();
    }

    // ── Render ─────────────────────────────────────────────────────
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 10 || h < 10) return;

        // Cache DPI from the host window once per chart instance
        if (!_dpiCached)
        {
            var mainWin = Application.Current.MainWindow;
            if (mainWin != null)
            {
                _dpi = VisualTreeHelper.GetDpi(mainWin).PixelsPerDip;
                _dpiCached = true;
            }
        }

        dc.DrawRoundedRectangle(BgBrush, null, new Rect(0, 0, w, h), 8, 8);

        if (Mode == ChartMode.Bar)
            RenderBar(dc, w, h);
        else
            RenderLine(dc, w, h);
    }

    // ── Bar Chart ──────────────────────────────────────────────────
    private void RenderBar(DrawingContext dc, double w, double h)
    {
        double[]? vals   = Values;
        string[]? labels = Labels;
        if (vals == null || vals.Length == 0) { DrawEmpty(dc, w, h); return; }

        const double padL = 44, padB = 28, padT = 12, padR = 12;
        double plotW = w - padL - padR;
        double plotH = h - padT - padB;

        double maxVal = 0;
        foreach (double v in vals) if (v > maxVal) maxVal = v;
        if (maxVal == 0) maxVal = 1;

        // Grid lines
        for (int g = 0; g <= 4; g++)
        {
            double y = padT + plotH - (g / 4.0) * plotH;
            dc.DrawLine(GridPen, new Point(padL, y), new Point(padL + plotW, y));
            DrawText(dc, $"{maxVal * g / 4:F3}", padL - 4, y - 7, TextBrush, 9, TextAlignment.Right);
        }

        // Axis
        dc.DrawLine(AxisPen, new Point(padL, padT), new Point(padL, padT + plotH));
        dc.DrawLine(AxisPen, new Point(padL, padT + plotH), new Point(padL + plotW, padT + plotH));

        int n = vals.Length;
        double barW  = plotW / n;
        double gapW  = barW * 0.15;
        double rectW = barW - gapW * 2;

        for (int i = 0; i < n; i++)
        {
            // Bars grow one after another as Reveal runs 0 → 1
            double grow  = Math.Clamp(Reveal * 1.6 - (double)i / n * 0.6, 0, 1);
            double barH  = (vals[i] / maxVal) * plotH * grow;
            double x     = padL + i * barW + gapW;
            double y     = padT + plotH - barH;
            bool   hilite= i == HighlightIndex;

            var brush = hilite ? CyanBar : DefaultBar;
            if (barH > 0)
                dc.DrawRoundedRectangle(brush, null, new Rect(x, y, rectW, barH), Math.Min(3, rectW / 4), Math.Min(3, barH / 2));

            // Label
            string lbl = labels != null && i < labels.Length ? labels[i] : i.ToString();
            DrawText(dc, lbl, x + rectW / 2, padT + plotH + 4, TextBrush, 8.5, TextAlignment.Center);
        }

        // X/Y axis labels
        if (!string.IsNullOrEmpty(XAxisLabel))
            DrawText(dc, XAxisLabel, padL + plotW / 2, h - 10, TextBrush, 9, TextAlignment.Center);
        if (!string.IsNullOrEmpty(YAxisLabel))
            DrawTextVertical(dc, YAxisLabel, 8, padT + plotH / 2, TextBrush, 9);
    }

    // ── Line Chart ─────────────────────────────────────────────────
    private void RenderLine(DrawingContext dc, double w, double h)
    {
        if (_series.Count == 0 || _series[0].X.Length == 0) { DrawEmpty(dc, w, h); return; }

        const double padL = 48, padB = 28, padT = 12, padR = 16;
        double plotW = w - padL - padR;
        double plotH = h - padT - padB;

        // Find global X/Y range
        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMax = double.MinValue;
        foreach (var (xs, ys, _, _) in _series)
        {
            foreach (double x in xs) { if (x < xMin) xMin = x; if (x > xMax) xMax = x; }
            foreach (double y in ys) { if (y > yMax) yMax = y; }
        }
        if (xMax <= xMin) xMax = xMin + 1;
        if (yMax <= 0) yMax = 1;
        yMax *= 1.1;

        // Grid
        for (int g = 0; g <= 4; g++)
        {
            double gy = padT + plotH - (g / 4.0) * plotH;
            dc.DrawLine(GridPen, new Point(padL, gy), new Point(padL + plotW, gy));
            DrawText(dc, $"{yMax * g / 4:F1}", padL - 4, gy - 7, TextBrush, 9, TextAlignment.Right);
        }
        for (int g = 0; g <= 4; g++)
        {
            double gx = padL + (g / 4.0) * plotW;
            dc.DrawLine(GridPen, new Point(gx, padT), new Point(gx, padT + plotH));
            double xLabel = xMin + (xMax - xMin) * g / 4;
            DrawText(dc, FormatXLabel(xLabel, xMax - xMin), gx, padT + plotH + 4, TextBrush, 8.5, TextAlignment.Center);
        }

        // Axis lines
        dc.DrawLine(AxisPen, new Point(padL, padT), new Point(padL, padT + plotH));
        dc.DrawLine(AxisPen, new Point(padL, padT + plotH), new Point(padL + plotW, padT + plotH));

        // Series: clipped to a window that sweeps left → right while Reveal runs
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, padL + plotW * Reveal + 2, h)));
        bool single = _series.Count == 1;
        if (!ReferenceEquals(_geoKey.Series, _series) || _geoKey.W != w || _geoKey.H != h)
        {
            _geoCache.Clear();
            _geoKey = (_series, w, h);
        }
        for (int s = 0; s < _series.Count; s++)
        {
            var (xs, ys, color, _) = _series[s];
            if (xs.Length == 0) continue;
            var pen = new Pen(new SolidColorBrush(color), 2) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            var geo = Cached(s, false, () => BuildLine(xs, ys, xMin, xMax, yMax, padL, padT, plotW, plotH, closeToBase: false));
            dc.DrawGeometry(null, pen, geo);

            // A single series gets a soft area fill so its shape reads at a glance
            if (single)
            {
                var fill = new LinearGradientBrush(Color.FromArgb(70, color.R, color.G, color.B),
                                                   Color.FromArgb(0, color.R, color.G, color.B), 90);
                fill.Freeze();
                dc.DrawGeometry(fill, null, Cached(s, true, () => BuildLine(xs, ys, xMin, xMax, yMax, padL, padT, plotW, plotH, closeToBase: true)));
            }
        }
        dc.Pop();

        // Legend (only needed when several series share the chart)
        if (_series.Count > 1)
        {
            double lx = padL + 10, ly = padT + 4;
            foreach (var (_, _, color, label) in _series)
            {
                var swatch = new SolidColorBrush(color);
                swatch.Freeze();
                dc.DrawRoundedRectangle(swatch, null, new Rect(lx, ly + 4, 12, 4), 2, 2);
                DrawText(dc, label, lx + 17, ly - 1, TextBrush, 9.5, TextAlignment.Left);
                ly += 15;
            }
        }

        // Axis labels
        if (!string.IsNullOrEmpty(XAxisLabel))
            DrawText(dc, XAxisLabel, padL + plotW / 2, h - 10, TextBrush, 9, TextAlignment.Center);
        if (!string.IsNullOrEmpty(YAxisLabel))
            DrawTextVertical(dc, YAxisLabel, 8, padT + plotH / 2, TextBrush, 9);
    }

    private StreamGeometry Cached(int series, bool area, Func<StreamGeometry> build)
    {
        if (!_geoCache.TryGetValue((series, area), out var geo))
            _geoCache[(series, area)] = geo = build();
        return geo;
    }

    private static StreamGeometry BuildLine(double[] xs, double[] ys, double xMin, double xMax, double yMax,
        double padL, double padT, double plotW, double plotH, bool closeToBase)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            double baseY = padT + plotH;
            Point P(int i) => new(padL + ((xs[i] - xMin) / (xMax - xMin)) * plotW,
                                  baseY - Math.Clamp(ys[i] / yMax, 0, 1) * plotH);

            var first = P(0);
            ctx.BeginFigure(closeToBase ? new Point(first.X, baseY) : first, closeToBase, closeToBase);
            if (closeToBase) ctx.LineTo(first, false, false);
            for (int i = 1; i < xs.Length; i++) ctx.LineTo(P(i), true, false);
            if (closeToBase) ctx.LineTo(new Point(P(xs.Length - 1).X, baseY), false, false);
        }
        geo.Freeze();
        return geo;
    }

    // Plain numbers; the unit belongs in XAxisLabel. Decimals scale with the axis range
    // so small ranges such as utilisation ρ ∈ [0.1, 0.98] stay readable.
    private static string FormatXLabel(double x, double range)
    {
        string format = range < 5 ? "F2" : range < 50 ? "F1" : "F0";
        return x.ToString(format, CultureInfo.InvariantCulture);
    }

    private void DrawEmpty(DrawingContext dc, double w, double h)
    {
        DrawText(dc, "No data", w / 2, h / 2 - 8, TextBrush, 12, TextAlignment.Center);
    }

    private void DrawText(DrawingContext dc, string text, double x, double y,
        Brush brush, double size, TextAlignment align)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            SmallFont, size, brush, _dpi)
        {
            TextAlignment = align
        };
        dc.DrawText(ft, new Point(x, y));
    }

    private void DrawTextVertical(DrawingContext dc, string text, double x, double y,
        Brush brush, double size)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            SmallFont, size, brush, _dpi);
        dc.PushTransform(new RotateTransform(-90, x, y));
        dc.DrawText(ft, new Point(x - ft.Width / 2, y - ft.Height / 2));
        dc.Pop();
    }
}

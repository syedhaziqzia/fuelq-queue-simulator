using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FuelQ.Controls;

/// <summary>
/// App-wide motion helpers, usable from XAML styles as attached properties:
/// <list type="bullet">
///   <item><c>RollNumbers</c>: a TextBlock rolls from its old number to the new one, like a fuel-pump meter.</item>
///   <item><c>HoverLift</c>: the element rises a few pixels while the mouse is over it.</item>
///   <item><c>AutoExplain</c>: a metric tile gets a plain-language tooltip from <see cref="Glossary"/>.</item>
/// </list>
/// plus <see cref="PlayEntrance"/> for staggered page entrances and <see cref="SetPulsing"/> for alerts.
/// </summary>
public static class Motion
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    // ── Page entrance ─────────────────────────────────────────────

    /// <summary>
    /// Fades and slides the page's top-level sections in, one after another.
    /// Looks inside a leading ScrollViewer/Grid for the first panel of sections.
    /// </summary>
    public static void PlayEntrance(FrameworkElement view)
    {
        Panel? panel = FindSectionPanel(view);
        if (panel == null)
        {
            Reveal(view, 0);
            return;
        }

        int i = 0;
        foreach (UIElement child in panel.Children)
        {
            if (child.Visibility != Visibility.Visible) continue;
            // Cap the stagger so long pages do not take ages to settle
            Reveal(child, Math.Min(i, 9) * 55);
            i++;
        }
    }

    private static Panel? FindSectionPanel(DependencyObject root)
    {
        object? content = root switch
        {
            UserControl uc => uc.Content,
            ScrollViewer sv => sv.Content,
            Border b => b.Child,
            _ => null
        };
        return content switch
        {
            StackPanel sp => sp,
            ScrollViewer sv => FindSectionPanel(sv),
            Grid g => g,
            _ => null
        };
    }

    private static void Reveal(UIElement element, int delayMs)
    {
        var begin = TimeSpan.FromMilliseconds(delayMs);
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { BeginTime = begin, EasingFunction = EaseOut };
        element.BeginAnimation(UIElement.OpacityProperty, fade);

        var shift = GetOwnTranslate(element);
        if (shift != null)
            shift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(460)) { BeginTime = begin, EasingFunction = EaseOut });
    }

    /// <summary>Returns a TranslateTransform this helper may animate, or null if the element already uses another transform.</summary>
    private static TranslateTransform? GetOwnTranslate(UIElement element)
    {
        if (element.RenderTransform is TranslateTransform own && !own.IsFrozen) return own;
        if (element.RenderTransform == null || element.RenderTransform == Transform.Identity ||
            (element.RenderTransform is MatrixTransform m && m.Matrix.IsIdentity))
        {
            var t = new TranslateTransform();
            element.RenderTransform = t;
            return t;
        }
        return null;
    }

    // ── Pulse (alerts) ────────────────────────────────────────────

    private static readonly DependencyProperty IsPulsingProperty =
        DependencyProperty.RegisterAttached("IsPulsing", typeof(bool), typeof(Motion), new PropertyMetadata(false));

    /// <summary>Gently pulses an element's opacity to draw attention (e.g. an "unstable" banner).</summary>
    public static void SetPulsing(UIElement element, bool on)
    {
        // Called on every recalculation; only (re)start or stop when the state changes
        if ((bool)element.GetValue(IsPulsingProperty) == on) return;
        element.SetValue(IsPulsingProperty, on);

        if (!on)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            return;
        }
        var pulse = new DoubleAnimation(1, 0.62, TimeSpan.FromMilliseconds(700))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        element.BeginAnimation(UIElement.OpacityProperty, pulse);
    }

    // ── HoverLift ─────────────────────────────────────────────────

    public static readonly DependencyProperty HoverLiftProperty =
        DependencyProperty.RegisterAttached("HoverLift", typeof(bool), typeof(Motion),
            new PropertyMetadata(false, OnHoverLiftChanged));

    public static bool GetHoverLift(DependencyObject d) => (bool)d.GetValue(HoverLiftProperty);
    public static void SetHoverLift(DependencyObject d, bool value) => d.SetValue(HoverLiftProperty, value);

    private static void OnHoverLiftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        fe.MouseEnter -= LiftUp;
        fe.MouseLeave -= LiftDown;
        if ((bool)e.NewValue)
        {
            fe.MouseEnter += LiftUp;
            fe.MouseLeave += LiftDown;
        }
    }

    private static void LiftUp(object sender, System.Windows.Input.MouseEventArgs e) => Lift((UIElement)sender, -3);
    private static void LiftDown(object sender, System.Windows.Input.MouseEventArgs e) => Lift((UIElement)sender, 0);

    private static void Lift(UIElement element, double y)
    {
        var t = GetOwnTranslate(element);
        t?.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(y, TimeSpan.FromMilliseconds(180)) { EasingFunction = EaseOut });
    }

    // ── AutoExplain ───────────────────────────────────────────────

    public static readonly DependencyProperty AutoExplainProperty =
        DependencyProperty.RegisterAttached("AutoExplain", typeof(bool), typeof(Motion),
            new PropertyMetadata(false, OnAutoExplainChanged));

    public static bool GetAutoExplain(DependencyObject d) => (bool)d.GetValue(AutoExplainProperty);
    public static void SetAutoExplain(DependencyObject d, bool value) => d.SetValue(AutoExplainProperty, value);

    private static void OnAutoExplainChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement fe && (bool)e.NewValue)
            fe.Loaded += AttachExplanation;
    }

    /// <summary>Uses the tile's first TextBlock (its label) to look up a plain-language tooltip.</summary>
    private static void AttachExplanation(object sender, RoutedEventArgs e)
    {
        var fe = (FrameworkElement)sender;
        fe.Loaded -= AttachExplanation;
        if (fe.ToolTip != null) return;

        var label = FindFirst<TextBlock>(fe);
        string? help = label != null ? Glossary.Explain(label.Text) : null;
        if (help == null) return;

        fe.ToolTip = help;
        ToolTipService.SetInitialShowDelay(fe, 250);
        ToolTipService.SetShowDuration(fe, 20000);
        fe.Cursor = System.Windows.Input.Cursors.Help;
    }

    private static T? FindFirst<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var deeper = FindFirst<T>(child);
            if (deeper != null) return deeper;
        }
        return null;
    }

    // ── RollNumbers ───────────────────────────────────────────────

    public static readonly DependencyProperty RollNumbersProperty =
        DependencyProperty.RegisterAttached("RollNumbers", typeof(bool), typeof(Motion),
            new PropertyMetadata(false, OnRollNumbersChanged));

    public static bool GetRollNumbers(DependencyObject d) => (bool)d.GetValue(RollNumbersProperty);
    public static void SetRollNumbers(DependencyObject d, bool value) => d.SetValue(RollNumbersProperty, value);

    private sealed class RollState
    {
        public double? Shown;          // value currently on screen (mid-roll or settled)
        public bool Writing;           // true while the roller itself sets Text
        public EventHandler? Frame;    // active CompositionTarget.Rendering handler
    }

    private static readonly ConditionalWeakTable<TextBlock, RollState> RollStates = new();

    private static readonly DependencyPropertyDescriptor TextDescriptor =
        DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));

    private static void OnRollNumbersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        TextDescriptor.RemoveValueChanged(tb, OnRolledTextChanged);
        if (!(bool)e.NewValue) return;

        var state = RollStates.GetOrCreateValue(tb);
        state.Shown = TryParseNumber(tb.Text, out var parsed) ? parsed.Value : null;
        TextDescriptor.AddValueChanged(tb, OnRolledTextChanged);
    }

    private static void OnRolledTextChanged(object? sender, EventArgs e)
    {
        if (sender is not TextBlock tb || !RollStates.TryGetValue(tb, out var state) || state.Writing) return;

        StopRoll(state);
        string target = tb.Text;
        if (!TryParseNumber(target, out var parsed))
        {
            state.Shown = null;   // "∞", "—", etc. are shown as-is
            return;
        }

        double? from = state.Shown;
        state.Shown = parsed.Value;
        if (from is null || from.Value == parsed.Value || !tb.IsVisible) return;

        // Roll from the old value to the new one over ~0.6 s with an ease-out curve
        double start = from.Value, end = parsed.Value;
        var clock = Stopwatch.StartNew();
        const double durationMs = 620;

        void Write(double v)
        {
            state.Writing = true;
            tb.Text = parsed.Prefix + v.ToString("F" + parsed.Decimals, CultureInfo.CurrentCulture) + parsed.Suffix;
            state.Writing = false;
            state.Shown = v;
        }

        Write(start);
        state.Frame = (_, _) =>
        {
            double t = Math.Min(1.0, clock.Elapsed.TotalMilliseconds / durationMs);
            double eased = 1 - Math.Pow(1 - t, 3);
            if (t >= 1.0)
            {
                StopRoll(state);
                state.Writing = true;
                tb.Text = target;   // land exactly on the formatted target
                state.Writing = false;
                state.Shown = end;
                return;
            }
            Write(start + (end - start) * eased);
        };
        CompositionTarget.Rendering += state.Frame;
    }

    private static void StopRoll(RollState state)
    {
        if (state.Frame == null) return;
        CompositionTarget.Rendering -= state.Frame;
        state.Frame = null;
    }

    private readonly record struct ParsedNumber(string Prefix, double Value, int Decimals, string Suffix);

    private static bool TryParseNumber(string text, out ParsedNumber result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string sep = Regex.Escape(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
        var m = Regex.Match(text, $@"^(?<pre>[^\d\-]*)(?<num>-?\d+(?:{sep}(?<dec>\d+))?)(?<suf>[^\d]*)$");
        if (!m.Success) return false;
        if (!double.TryParse(m.Groups["num"].Value, NumberStyles.Float, CultureInfo.CurrentCulture, out double value))
            return false;

        result = new ParsedNumber(m.Groups["pre"].Value, value, m.Groups["dec"].Value.Length, m.Groups["suf"].Value);
        return true;
    }
}

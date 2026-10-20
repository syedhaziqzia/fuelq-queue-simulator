using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace FuelQ.Controls;

/// <summary>
/// "How this page works" strip: numbered steps joined by moving lane markings.
/// Steps are given as one string: steps separated by '|', each "Title :: detail".
/// <example><c>&lt;controls:StepGuide Steps="Pick a model :: …|Enter values :: …|Read results :: …"/&gt;</c></example>
/// </summary>
public class StepGuide : Border
{
    public static readonly DependencyProperty StepsProperty =
        DependencyProperty.Register(nameof(Steps), typeof(string), typeof(StepGuide),
            new PropertyMetadata(string.Empty, (d, _) => ((StepGuide)d).Build()));

    public string Steps
    {
        get => (string)GetValue(StepsProperty);
        set => SetValue(StepsProperty, value);
    }

    private bool _played;

    public StepGuide()
    {
        SetResourceReference(BackgroundProperty, "CardSurface");
        SetResourceReference(BorderBrushProperty, "CardBorder");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(18, 14, 18, 14);
        Margin = new Thickness(0, 0, 0, 18);
        Loaded += (_, _) => PlayIntro();
    }

    private void Build()
    {
        var parts = (Steps ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var grid = new Grid();

        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (int i = 0; i < parts.Length; i++)
        {
            var bits = parts[i].Split("::", 2, StringSplitOptions.TrimEntries);
            var step = BuildStep(i + 1, bits[0], bits.Length > 1 ? bits[1] : string.Empty);
            Grid.SetColumn(step, i * 2);
            grid.Children.Add(step);

            if (i > 0)
            {
                var lane = BuildLane();
                Grid.SetColumn(lane, i * 2 - 1);
                grid.Children.Add(lane);
            }
        }
        Child = grid;
        _played = false;
    }

    private static FrameworkElement BuildStep(int number, string title, string detail)
    {
        var badge = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15),
            Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top,
            BorderThickness = new Thickness(1.5),
            Child = new TextBlock
            {
                Text = number.ToString("00"), FontSize = 12.5, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        badge.SetResourceReference(Border.BackgroundProperty, "AmberWash");
        badge.SetResourceReference(Border.BorderBrushProperty, "AccentCyanBrush");
        ((TextBlock)badge.Child).SetResourceReference(TextBlock.ForegroundProperty, "AccentCyanBrush");
        ((TextBlock)badge.Child).SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");

        var titleBlock = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        titleBlock.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");

        var detailBlock = new TextBlock { Text = detail, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), LineHeight = 16 };
        detailBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");

        var text = new StackPanel();
        text.Children.Add(titleBlock);
        if (detail.Length > 0) text.Children.Add(detailBlock);

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(badge, Dock.Left);
        row.Children.Add(badge);
        row.Children.Add(text);
        return row;
    }

    /// <summary>A dashed "road marking" between steps that flows left → right.</summary>
    private static FrameworkElement BuildLane()
    {
        var line = new Line
        {
            X1 = 0, Y1 = 1, Y2 = 1,
            StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 2.5, 2 },
            StrokeDashCap = PenLineCap.Round, Opacity = 0.55
        };
        line.SetResourceReference(Shape.StrokeProperty, "AccentCyanBrush");
        // Dash offset runs backwards so the markings appear to travel toward the next step
        line.BeginAnimation(Shape.StrokeDashOffsetProperty,
            new DoubleAnimation(0, -9, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });

        var host = new Grid { Height = 2, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 14, 8, 0) };
        host.SizeChanged += (_, e) => line.X2 = Math.Max(0, e.NewSize.Width);
        host.Children.Add(line);
        return host;
    }

    private void PlayIntro()
    {
        if (_played || Child is not Grid grid) return;
        _played = true;
        int i = 0;
        foreach (UIElement child in grid.Children)
        {
            if (child is Grid { Height: 2 }) continue;   // lane markings stay put
            var begin = TimeSpan.FromMilliseconds(140 + i * 120);
            child.Opacity = 0;
            child.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(360)) { BeginTime = begin });
            var t = new TranslateTransform(-10, 0);
            child.RenderTransform = t;
            t.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(420))
                { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            i++;
        }
    }
}

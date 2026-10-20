using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FuelQ.Controls;
using FuelQ.Views;

namespace FuelQ;

public partial class MainWindow : Window
{
    private readonly Button[] _navButtons;
    private readonly TextBlock[] _navTexts;
    private readonly UserControl[] _views;
    private int _activeIndex = -1;
    private bool _sidebarOpen = true;
    /// <summary>Must match the Width attribute on SidebarPanel in MainWindow.xaml.</summary>
    private const double SidebarWidth = 260;

    public MainWindow()
    {
        InitializeComponent();

        _navButtons = [NavBtn0, NavBtn1, NavBtn2, NavBtn3, NavBtn4, NavBtn5];
        _navTexts   = [TbNavText0, TbNavText1, TbNavText2, TbNavText3, TbNavText4, TbNavText5];
        _views =
        [
            new ProjectInfoView(),
            new QueuingModelsView(),
            new SimulatorView(),
            new EmpiricalDataView(),
            new ModelComparisonView(),
            new PromptAuditView()
        ];

        // Keep the indicator glued to the active item when nav items change height
        NavStack.SizeChanged += (_, _) => MoveIndicator(animate: false);
        KeyDown += (_, e) => { if (e.Key == Key.Escape && ModalOverlay.IsVisible) HideModal(); };

        NavigateTo(0);
    }

    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && int.TryParse(tag, out int idx))
            NavigateTo(idx);
    }

    public void NavigateTo(int index)
    {
        if (index < 0 || index >= _views.Length || index == _activeIndex) return;
        _activeIndex = index;

        // Update nav button styles
        for (int i = 0; i < _navButtons.Length; i++)
        {
            _navButtons[i].Style = i == index
                ? (Style)FindResource("NavButtonActive")
                : (Style)FindResource("NavButton");
        }

        var view = _views[index];
        MainContent.Content = view;

        // Sections of the new page rise in one after another
        Motion.PlayEntrance(view);

        // Wait for layout so the indicator can measure the button it slides to
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => MoveIndicator(animate: true));
    }

    /// <summary>Slides the amber bar beside the active nav button.</summary>
    private void MoveIndicator(bool animate)
    {
        if (_activeIndex < 0 || !NavHost.IsLoaded) return;
        var btn = _navButtons[_activeIndex];
        if (btn.ActualHeight <= 0) return;

        double top = btn.TransformToAncestor(NavHost).Transform(new Point(0, 0)).Y;
        double y = top + (btn.ActualHeight - NavIndicator.Height) / 2;
        var shift = (TranslateTransform)NavIndicator.RenderTransform;

        if (!animate)
        {
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.Y = y;
            return;
        }
        shift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(y, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 } });

        // A quick stretch makes the bar feel like it travels
        NavIndicator.BeginAnimation(HeightProperty,
            new DoubleAnimation(40, 26, TimeSpan.FromMilliseconds(360)) { EasingFunction = EaseOut });
    }

    // ── Modal Overlay ───────────────────────────────────────────────
    public void ShowModal(string title, UIElement content)
    {
        ModalTitle.Text = title;
        ModalContent.Content = content;
        ModalOverlay.Visibility = Visibility.Visible;

        // Fade the backdrop and pop the card in
        ModalOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        var scale = (ScaleTransform)ModalCard.RenderTransform;
        var pop = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    public void HideModal()
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
        fade.Completed += (_, _) =>
        {
            ModalOverlay.Visibility = Visibility.Collapsed;
            ModalContent.Content = null;
        };
        ModalOverlay.BeginAnimation(OpacityProperty, fade);
    }

    private void CloseModal_Click(object sender, RoutedEventArgs e) => HideModal();

    private void ModalBackdrop_Click(object sender, MouseButtonEventArgs e) => HideModal();

    // ── Sidebar Toggle ───────────────────────────────────────────────
    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        _sidebarOpen = !_sidebarOpen;

        // Width animation — cubic ease-out for a snappy, premium feel
        double targetWidth = _sidebarOpen ? SidebarWidth : 64;
        var widthAnim = new DoubleAnimation(targetWidth, new Duration(TimeSpan.FromMilliseconds(240)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        SidebarPanel.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);

        // Update toggle button
        BtnToggleSidebar.Content = _sidebarOpen ? "◀" : "▶";
        BtnToggleSidebar.ToolTip  = _sidebarOpen ? "Collapse sidebar" : "Expand sidebar";
        BtnToggleSidebar.HorizontalAlignment = _sidebarOpen ? HorizontalAlignment.Right : HorizontalAlignment.Center;

        if (_sidebarOpen)
        {
            // Expanding: show elements first, then fade them in
            BrandLogoPanel.Visibility    = Visibility.Visible;
            TbSidebarSubtitle.Visibility = Visibility.Visible;
            TbSidebarMeta.Visibility     = Visibility.Visible;
            TbNavLabel.Visibility        = Visibility.Visible;
            PanelSupervisor.Visibility   = Visibility.Visible;
            foreach (var tb in _navTexts) tb.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(200)))
            {
                BeginTime = TimeSpan.FromMilliseconds(100),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            BrandLogoPanel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            TbSidebarSubtitle.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            TbSidebarMeta.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            TbNavLabel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            PanelSupervisor.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            foreach (var tb in _navTexts) tb.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }
        else
        {
            // Collapsing: fade out first, then hide
            var fadeOut = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(120)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (_, _) =>
            {
                BrandLogoPanel.Visibility    = Visibility.Collapsed;
                TbSidebarSubtitle.Visibility = Visibility.Collapsed;
                TbSidebarMeta.Visibility     = Visibility.Collapsed;
                TbNavLabel.Visibility        = Visibility.Collapsed;
                PanelSupervisor.Visibility   = Visibility.Collapsed;
                foreach (var tb in _navTexts) tb.Visibility = Visibility.Collapsed;
            };
            BrandLogoPanel.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            TbSidebarSubtitle.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            TbSidebarMeta.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            TbNavLabel.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            PanelSupervisor.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            foreach (var tb in _navTexts) tb.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
    }
}

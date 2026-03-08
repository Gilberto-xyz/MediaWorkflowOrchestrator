using MediaWorkflowOrchestrator.Messages;
using MediaWorkflowOrchestrator.Views;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using System.Windows.Input;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Numerics;
using WinRT.Interop;

namespace MediaWorkflowOrchestrator
{
    public sealed partial class MainWindow : Window, IRecipient<WorkflowSelectedMessage>
    {
        private static readonly SolidColorBrush ActiveQuickRunSelectedBackgroundBrush = CreateBrush(0xCC, 0x0F, 0x76, 0x6E);
        private static readonly SolidColorBrush ActiveQuickRunSelectedBorderBrush = CreateBrush(0xFF, 0x2A, 0xF5, 0x98);
        private static readonly SolidColorBrush ActiveQuickRunSelectedForegroundBrush = CreateBrush(0xFF, 0xFF, 0xFF, 0xFF);
        private static readonly Thickness ExpandedQuickActionsPaneMargin = new(6, 3, 6, 6);
        private static readonly Thickness CompactOverlayQuickActionsPaneMargin = new(12, 3, 12, 6);
        private static readonly Thickness CompactStripQuickActionsPaneMargin = new(0, 3, 0, 6);
        private const string MatrixGhostGlyphs = "影界電流空夜月星雨光夢幻零心火水風雪龍門森海山気道式波天狐炎雷静声鏡黒白緑青赤花鳥雲華桜刃文語字魂";
        private const double MatrixBackdropMinimumWidth = 960;
        private const double MatrixBackdropMinimumHeight = 720;
        private const float MatrixScannerMinimumDurationSeconds = 12f;
        private const double MatrixGhostGlyphMinimumDelaySeconds = 0.34;
        private const double MatrixGhostGlyphMaximumDelaySeconds = 0.78;
        private static readonly TimeSpan MatrixGhostGlyphAnimationDuration = TimeSpan.FromMilliseconds(2100);
        private readonly Random matrixBackdropRandom = new();
        private DispatcherQueueTimer? matrixGhostGlyphTimer;
        private DispatcherQueueTimer? matrixResizeDebounceTimer;
        private TextBlock[]? matrixGhostGlyphPool;
        private int matrixGhostGlyphPoolIndex;
        private DashboardPage? trackedDashboardPage;
        private bool isMatrixBackdropInitialized;

        public MainWindow()
        {
            DiagnosticsTrace.Write("MainWindow ctor start.");
            InitializeComponent();
            WeakReferenceMessenger.Default.Register(this);
            Title = "Media Workflow Orchestrator";
            TrySetWindowIcon();
            AppNavigationView.SelectedItem = DashboardItem;
            DiagnosticsTrace.Write("MainWindow ctor completed.");
        }

        public MainWindowViewModel ViewModel { get; } = new();

        private void OnRootShellLoaded(object sender, RoutedEventArgs e)
        {
            if (isMatrixBackdropInitialized)
            {
                return;
            }

            InitializeMatrixBackdrop();
            RootShell.SizeChanged += OnRootShellSizeChanged;
            StartMatrixGhostGlyphLoop();
            UpdateQuickActionsPaneState();
            isMatrixBackdropInitialized = true;
        }

        private void OnRootShellSizeChanged(object sender, SizeChangedEventArgs e)
        {
            MatrixRainHost.Width = Math.Max(e.NewSize.Width, MatrixBackdropMinimumWidth);
            MatrixRainHost.Height = Math.Max(e.NewSize.Height, MatrixBackdropMinimumHeight);
            UpdateQuickActionsPaneState();

            matrixResizeDebounceTimer ??= CreateMatrixResizeDebounceTimer();
            matrixResizeDebounceTimer.Stop();
            matrixResizeDebounceTimer.Start();
        }

        private DispatcherQueueTimer CreateMatrixResizeDebounceTimer()
        {
            var timer = DispatcherQueue.CreateTimer();
            timer.IsRepeating = false;
            timer.Interval = TimeSpan.FromMilliseconds(140);
            timer.Tick += (_, _) => LayoutMatrixBackdrop();
            return timer;
        }

        private void InitializeMatrixBackdrop()
        {
            matrixGhostGlyphPool =
            [
                MatrixGhostGlyph0,
                MatrixGhostGlyph1,
                MatrixGhostGlyph2,
                MatrixGhostGlyph3,
                MatrixGhostGlyph4
            ];

            LayoutMatrixBackdrop();
        }

        private void LayoutMatrixBackdrop()
        {
            var width = (float)Math.Max(RootShell.ActualWidth, MatrixBackdropMinimumWidth);
            var height = (float)Math.Max(RootShell.ActualHeight, MatrixBackdropMinimumHeight);

            MatrixRainHost.Width = width;
            MatrixRainHost.Height = height;
            MatrixScannerLayer.Width = width;
            MatrixScannerLayer.Height = height;
            MatrixRainImageA.Width = width;
            MatrixRainImageA.Height = height;

            ConfigureScanner(MatrixRevealScannerPrimary, width * 0.16f, height);
            ConfigureScanner(MatrixRevealScannerSecondary, width, height * 0.09f);
            ConfigureScanner(MatrixHideScanner, width * 0.22f, height);

            StartHorizontalScannerLoop(
                MatrixRevealScannerPrimary,
                startX: -0.22f * width,
                midX: 0.36f * width,
                endX: 0.12f * width,
                durationSeconds: 16f);

            StartHorizontalScannerLoop(
                MatrixHideScanner,
                startX: 1.08f * width,
                midX: 0.44f * width,
                endX: 0.82f * width,
                durationSeconds: 21f);

            StartVerticalScannerLoop(
                MatrixRevealScannerSecondary,
                startY: -0.14f * height,
                midY: 0.46f * height,
                endY: 0.18f * height,
                durationSeconds: 27f);
        }

        private static void ConfigureScanner(FrameworkElement element, float width, float height)
        {
            element.Width = width;
            element.Height = height;
        }

        private static void StartHorizontalScannerLoop(FrameworkElement element, float startX, float midX, float endX, float durationSeconds)
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;

            visual.StopAnimation("Offset.X");
            visual.StopAnimation("Offset.Y");
            visual.Offset = new Vector3(startX, 0f, 0f);

            var animationX = compositor.CreateScalarKeyFrameAnimation();
            animationX.InsertKeyFrame(0f, startX);
            animationX.InsertKeyFrame(0.42f, midX);
            animationX.InsertKeyFrame(0.74f, endX);
            animationX.InsertKeyFrame(1f, startX);
            animationX.Duration = TimeSpan.FromSeconds(Math.Max(durationSeconds, MatrixScannerMinimumDurationSeconds));
            animationX.IterationBehavior = AnimationIterationBehavior.Forever;

            visual.StartAnimation("Offset.X", animationX);
        }

        private static void StartVerticalScannerLoop(FrameworkElement element, float startY, float midY, float endY, float durationSeconds)
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;

            visual.StopAnimation("Offset.X");
            visual.StopAnimation("Offset.Y");
            visual.Offset = new Vector3(0f, startY, 0f);

            var animationY = compositor.CreateScalarKeyFrameAnimation();
            animationY.InsertKeyFrame(0f, startY);
            animationY.InsertKeyFrame(0.4f, midY);
            animationY.InsertKeyFrame(0.76f, endY);
            animationY.InsertKeyFrame(1f, startY);
            animationY.Duration = TimeSpan.FromSeconds(Math.Max(durationSeconds, MatrixScannerMinimumDurationSeconds));
            animationY.IterationBehavior = AnimationIterationBehavior.Forever;

            visual.StartAnimation("Offset.Y", animationY);
        }

        private void StartMatrixGhostGlyphLoop()
        {
            matrixGhostGlyphTimer ??= CreateMatrixGhostGlyphTimer();
            ScheduleNextMatrixGhostGlyph();
            matrixGhostGlyphTimer.Stop();
            matrixGhostGlyphTimer.Start();
        }

        private DispatcherQueueTimer CreateMatrixGhostGlyphTimer()
        {
            var timer = DispatcherQueue.CreateTimer();
            timer.IsRepeating = false;
            timer.Tick += (_, _) =>
            {
                ShowRandomMatrixGhostGlyph();
                ScheduleNextMatrixGhostGlyph();
                timer.Start();
            };

            return timer;
        }

        private void ScheduleNextMatrixGhostGlyph()
        {
            if (matrixGhostGlyphTimer is null)
            {
                return;
            }

            var seconds = MatrixGhostGlyphMinimumDelaySeconds
                + (matrixBackdropRandom.NextDouble() * (MatrixGhostGlyphMaximumDelaySeconds - MatrixGhostGlyphMinimumDelaySeconds));
            matrixGhostGlyphTimer.Interval = TimeSpan.FromSeconds(seconds);
        }

        private void ShowRandomMatrixGhostGlyph()
        {
            if (matrixGhostGlyphPool is null || matrixGhostGlyphPool.Length == 0)
            {
                return;
            }

            var width = Math.Max(MatrixScannerLayer.Width, MatrixBackdropMinimumWidth);
            var height = Math.Max(MatrixScannerLayer.Height, MatrixBackdropMinimumHeight);
            var glyph = MatrixGhostGlyphs[matrixBackdropRandom.Next(MatrixGhostGlyphs.Length)].ToString();
            var fontSize = 20 + matrixBackdropRandom.Next(0, 14);
            var glyphTextBlock = matrixGhostGlyphPool[matrixGhostGlyphPoolIndex];
            matrixGhostGlyphPoolIndex = (matrixGhostGlyphPoolIndex + 1) % matrixGhostGlyphPool.Length;

            glyphTextBlock.Text = glyph;
            glyphTextBlock.FontSize = fontSize;

            var glyphX = 28 + (matrixBackdropRandom.NextDouble() * Math.Max(1, width - 84));
            var glyphY = 22 + (matrixBackdropRandom.NextDouble() * Math.Max(1, height - 84));
            Canvas.SetLeft(glyphTextBlock, glyphX);
            Canvas.SetTop(glyphTextBlock, glyphY);

            var visual = ElementCompositionPreview.GetElementVisual(glyphTextBlock);
            visual.StopAnimation("Opacity");
            visual.StopAnimation("Scale.X");
            visual.StopAnimation("Scale.Y");
            visual.Opacity = 0f;
            visual.CenterPoint = new Vector3((float)(fontSize * 0.5), (float)(fontSize * 0.5), 0f);
            visual.Scale = new Vector3(0.94f, 0.94f, 1f);

            var compositor = visual.Compositor;
            var opacityAnimation = compositor.CreateScalarKeyFrameAnimation();
            opacityAnimation.InsertKeyFrame(0f, 0f);
            opacityAnimation.InsertKeyFrame(0.1f, 0.8f);
            opacityAnimation.InsertKeyFrame(0.32f, 0.72f);
            opacityAnimation.InsertKeyFrame(0.58f, 0.46f);
            opacityAnimation.InsertKeyFrame(0.82f, 0.2f);
            opacityAnimation.InsertKeyFrame(1f, 0f);
            opacityAnimation.Duration = MatrixGhostGlyphAnimationDuration;

            var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
            scaleAnimation.InsertKeyFrame(0f, new Vector3(0.94f, 0.94f, 1f));
            scaleAnimation.InsertKeyFrame(0.18f, new Vector3(1.02f, 1.02f, 1f));
            scaleAnimation.InsertKeyFrame(1f, new Vector3(1.06f, 1.06f, 1f));
            scaleAnimation.Duration = MatrixGhostGlyphAnimationDuration;

            visual.StartAnimation("Opacity", opacityAnimation);
            visual.StartAnimation("Scale", scaleAnimation);
        }

        private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer is not NavigationViewItem item || item.Tag is not string tag)
            {
                return;
            }

            var targetPage = tag switch
            {
                "tools" => typeof(ToolsPage),
                "history" => typeof(HistoryPage),
                _ => typeof(DashboardPage),
            };

            if (ContentFrame.CurrentSourcePageType != targetPage)
            {
                ContentFrame.Navigate(targetPage);
            }

            TryAttachDashboardPageFromFrame();
            ViewModel.SelectedNavigationTag = tag;
        }

        private void OnNavigationDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
        {
            UpdateQuickActionsPaneState();
        }

        private void OnNavigationPaneOpening(NavigationView sender, object args)
        {
            UpdateQuickActionsPaneState(paneOpenOverride: true);
        }

        private void OnNavigationPaneClosing(NavigationView sender, object args)
        {
            UpdateQuickActionsPaneState(paneOpenOverride: false);
        }

        private void OnNavigationPaneVisibilityChanged(NavigationView sender, object args)
        {
            UpdateQuickActionsPaneState();
        }

        private void UpdateQuickActionsPaneState(bool? paneOpenOverride = null)
        {
            var isPaneOpen = paneOpenOverride ?? AppNavigationView.IsPaneOpen;
            var isExpandedPane = AppNavigationView.DisplayMode == NavigationViewDisplayMode.Expanded && isPaneOpen;
            var isCompactOverlayPane = AppNavigationView.DisplayMode != NavigationViewDisplayMode.Expanded && isPaneOpen;

            UpdateQuickActionsPaneCompactMode(!isExpandedPane);
            QuickActionsPaneScrollViewer.Margin = isExpandedPane
                ? ExpandedQuickActionsPaneMargin
                : isCompactOverlayPane
                    ? CompactOverlayQuickActionsPaneMargin
                    : CompactStripQuickActionsPaneMargin;
            QuickActionsPaneScrollViewer.HorizontalAlignment =
                isExpandedPane || isCompactOverlayPane
                    ? HorizontalAlignment.Stretch
                    : HorizontalAlignment.Center;
            QuickActionsPaneScrollViewer.Width = isExpandedPane || isCompactOverlayPane
                ? double.NaN
                : AppNavigationView.CompactPaneLength;
            QuickActionsCompactRoot.HorizontalAlignment = isCompactOverlayPane
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Center;
        }

        private void UpdateQuickActionsPaneCompactMode(bool compactMode)
        {
            QuickActionsExpandedRoot.Visibility = compactMode ? Visibility.Collapsed : Visibility.Visible;
            QuickActionsCompactRoot.Visibility = compactMode ? Visibility.Visible : Visibility.Collapsed;
        }

        public void Receive(WorkflowSelectedMessage message)
        {
            AppNavigationView.SelectedItem = DashboardItem;
            if (ContentFrame.CurrentSourcePageType != typeof(DashboardPage))
            {
                ContentFrame.Navigate(typeof(DashboardPage));
            }

            TryAttachDashboardPageFromFrame();
            ViewModel.SelectedNavigationTag = "dashboard";
        }

        private DashboardPage EnsureDashboardPage()
        {
            if (ContentFrame.CurrentSourcePageType != typeof(DashboardPage))
            {
                AppNavigationView.SelectedItem = DashboardItem;
                ContentFrame.Navigate(typeof(DashboardPage));
                ViewModel.SelectedNavigationTag = "dashboard";
            }

            TryAttachDashboardPageFromFrame();
            return (DashboardPage)ContentFrame.Content;
        }

        private void TryAttachDashboardPageFromFrame()
        {
            if (ContentFrame.Content is not DashboardPage page)
            {
                AttachDashboardPage(null);
                return;
            }

            AttachDashboardPage(page);
        }

        private void AttachDashboardPage(DashboardPage? page)
        {
            if (ReferenceEquals(trackedDashboardPage, page))
            {
                UpdateQuickRunSelectedButtonVisual();
                return;
            }

            if (trackedDashboardPage is not null)
            {
                trackedDashboardPage.ViewModel.PropertyChanged -= OnDashboardViewModelPropertyChanged;
            }

            trackedDashboardPage = page;

            if (trackedDashboardPage is not null)
            {
                trackedDashboardPage.ViewModel.PropertyChanged += OnDashboardViewModelPropertyChanged;
            }

            UpdateQuickRunSelectedButtonVisual();
        }

        private void OnDashboardViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(DashboardViewModel.HasExplicitStepSelection)
                or nameof(DashboardViewModel.SelectedStep))
            {
                _ = DispatcherQueue.TryEnqueue(UpdateQuickRunSelectedButtonVisual);
            }
        }

        private void UpdateQuickRunSelectedButtonVisual()
        {
            var isActive = trackedDashboardPage?.ViewModel.HasExplicitStepSelection == true;
            if (isActive)
            {
                QuickRunSelectedButton.Background = ActiveQuickRunSelectedBackgroundBrush;
                QuickRunSelectedButton.BorderBrush = ActiveQuickRunSelectedBorderBrush;
                QuickRunSelectedButton.Foreground = ActiveQuickRunSelectedForegroundBrush;
                CompactQuickRunSelectedButton.Background = ActiveQuickRunSelectedBackgroundBrush;
                CompactQuickRunSelectedButton.BorderBrush = ActiveQuickRunSelectedBorderBrush;
                CompactQuickRunSelectedButton.Foreground = ActiveQuickRunSelectedForegroundBrush;
                return;
            }

            QuickRunSelectedButton.ClearValue(Button.BackgroundProperty);
            QuickRunSelectedButton.ClearValue(Button.BorderBrushProperty);
            QuickRunSelectedButton.ClearValue(Button.ForegroundProperty);
            CompactQuickRunSelectedButton.ClearValue(Button.BackgroundProperty);
            CompactQuickRunSelectedButton.ClearValue(Button.BorderBrushProperty);
            CompactQuickRunSelectedButton.ClearValue(Button.ForegroundProperty);
        }

        private static void ExecuteCommand(ICommand command)
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }
        }

        private void TrySetWindowIcon()
        {
            try
            {
                var hwnd = WindowNative.GetWindowHandle(this);
                if (hwnd == IntPtr.Zero)
                {
                    return;
                }

                var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icono.ico");
                if (!File.Exists(iconPath))
                {
                    DiagnosticsTrace.Write($"App icon not found: {iconPath}");
                    return;
                }

                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                AppWindow.GetFromWindowId(windowId).SetIcon(iconPath);
            }
            catch (Exception ex)
            {
                DiagnosticsTrace.Write($"Setting app icon failed: {ex}");
            }
        }

        private async void OnQuickPickFileClicked(object sender, RoutedEventArgs e)
        {
            DiagnosticsTrace.Write("Quick action clicked: pick file.");
            await EnsureDashboardPage().PickFileAsync();
        }

        private async void OnQuickPickFolderClicked(object sender, RoutedEventArgs e)
        {
            DiagnosticsTrace.Write("Quick action clicked: pick folder.");
            await EnsureDashboardPage().PickFolderAsync();
        }

        private void OnQuickDownloadWeeklyClicked(object sender, RoutedEventArgs e) =>
            ExecuteCommand(EnsureDashboardPage().ViewModel.RunDownloadCommand);

        private async void OnQuickDownloadFromLinkClicked(object sender, RoutedEventArgs e) =>
            await EnsureDashboardPage().DownloadFromLinkAsync();

        private void OnQuickRunNextClicked(object sender, RoutedEventArgs e)
        {
            DiagnosticsTrace.Write("Quick action clicked: run next.");
            ExecuteCommand(EnsureDashboardPage().ViewModel.RunNextCommand);
        }

        private void OnQuickRunSelectedClicked(object sender, RoutedEventArgs e) =>
            ExecuteCommand(EnsureDashboardPage().ViewModel.RunSelectedStepCommand);

        private void OnQuickRetryClicked(object sender, RoutedEventArgs e) =>
            ExecuteCommand(EnsureDashboardPage().ViewModel.RetrySelectedStepCommand);

        private void OnQuickSkipClicked(object sender, RoutedEventArgs e) =>
            ExecuteCommand(EnsureDashboardPage().ViewModel.SkipSelectedStepCommand);

        private void OnQuickCancelClicked(object sender, RoutedEventArgs e) =>
            ExecuteCommand(EnsureDashboardPage().ViewModel.CancelExecutionCommand);

        private void OnQuickOpenFolderClicked(object sender, RoutedEventArgs e) =>
            ExecuteCommand(EnsureDashboardPage().ViewModel.OpenRootFolderCommand);

        private void OnQuickOpenLogClicked(object sender, RoutedEventArgs e) =>
            ExecuteCommand(EnsureDashboardPage().ViewModel.OpenSelectedLogCommand);

        private static SolidColorBrush CreateBrush(byte a, byte r, byte g, byte b) =>
            new(Microsoft.UI.ColorHelper.FromArgb(a, r, g, b));
    }
}

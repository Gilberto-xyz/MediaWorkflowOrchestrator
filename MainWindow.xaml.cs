using MediaWorkflowOrchestrator.Messages;
using MediaWorkflowOrchestrator.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Windows.Input;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using WinRT.Interop;

namespace MediaWorkflowOrchestrator
{
    public sealed partial class MainWindow : Window, IRecipient<WorkflowSelectedMessage>
    {
        private const double ShellHeaderStackBreakpoint = 1180;
        private const double ShellCompactSpacingBreakpoint = 900;
        private static readonly SolidColorBrush FallbackQuickRunSelectedBackgroundBrush = CreateBrush(0xFF, 0xE5, 0xA3, 0x4F);
        private static readonly SolidColorBrush FallbackQuickRunSelectedBorderBrush = CreateBrush(0xFF, 0xF5, 0xC3, 0x72);
        private static readonly SolidColorBrush FallbackQuickRunSelectedForegroundBrush = CreateBrush(0xFF, 0x17, 0x11, 0x0A);
        private static readonly Thickness ExpandedQuickActionsPaneMargin = new(6, 3, 6, 6);
        private DashboardPage? trackedDashboardPage;
        private bool rootShellInitialized;

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
            if (rootShellInitialized)
            {
                return;
            }

            RootShell.SizeChanged += OnRootShellSizeChanged;
            UpdateQuickActionsPaneState();
            UpdateShellResponsiveLayout();
            rootShellInitialized = true;
        }

        private void OnRootShellSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateQuickActionsPaneState();
            UpdateShellResponsiveLayout();
        }

        private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer is not NavigationViewItem item || item.Tag is not string tag)
            {
                return;
            }

            if (string.Equals(tag, "reload", StringComparison.Ordinal))
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

        private async void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.InvokedItemContainer is not NavigationViewItem item
                || item.Tag is not string tag
                || !string.Equals(tag, "reload", StringComparison.Ordinal))
            {
                return;
            }

            await RestartApplicationAsync();
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
            var showQuickActions = AppNavigationView.DisplayMode == NavigationViewDisplayMode.Expanded && isPaneOpen;

            QuickActionsPaneScrollViewer.Visibility = showQuickActions ? Visibility.Visible : Visibility.Collapsed;
            QuickActionsFooterBorder.Visibility = showQuickActions ? Visibility.Visible : Visibility.Collapsed;
            UpdateQuickActionsPaneCompactMode(false);

            if (!showQuickActions)
            {
                return;
            }

            QuickActionsPaneScrollViewer.Margin = ExpandedQuickActionsPaneMargin;
            QuickActionsPaneScrollViewer.HorizontalAlignment = HorizontalAlignment.Stretch;
            QuickActionsPaneScrollViewer.Width = double.NaN;
            QuickActionsCompactRoot.HorizontalAlignment = HorizontalAlignment.Left;
        }

        private void UpdateQuickActionsPaneCompactMode(bool compactMode)
        {
            QuickActionsExpandedRoot.Visibility = compactMode ? Visibility.Collapsed : Visibility.Visible;
            QuickActionsCompactRoot.Visibility = compactMode ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateShellResponsiveLayout()
        {
            var width = RootShell.ActualWidth;
            var stackHeader = width < ShellHeaderStackBreakpoint;
            var compactShell = width < ShellCompactSpacingBreakpoint;
            var tightenSummaryCard = width < 1460;

            ShellHeaderLayout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            ShellHeaderLayout.ColumnDefinitions[1].Width = stackHeader ? new GridLength(0) : GridLength.Auto;
            Grid.SetColumn(ShellHeaderSummaryCard, stackHeader ? 0 : 1);
            Grid.SetRow(ShellHeaderSummaryCard, stackHeader ? 1 : 0);
            ShellHeaderSummaryCard.MaxWidth = stackHeader ? double.NaN : tightenSummaryCard ? 256 : 284;
            ShellHeaderSummaryCard.Padding = compactShell ? new Thickness(14) : new Thickness(16);
            ShellHeaderSummaryContent.Spacing = compactShell ? 3 : 4;
            ShellHeaderSummaryText.FontSize = compactShell ? 14 : 15;
            ShellHeaderSummaryText.LineHeight = compactShell ? 21 : 23;
            ShellHeaderLayout.ColumnSpacing = stackHeader ? 0 : 18;
            ShellHeaderLayout.RowSpacing = compactShell ? 10 : 12;

            QuickActionsFooterHintText.Visibility = compactShell ? Visibility.Collapsed : Visibility.Visible;
            ContentFrame.Margin = compactShell ? new Thickness(0, 0, 8, 8) : new Thickness(0, 0, 14, 12);
            AppNavigationView.OpenPaneLength = compactShell ? 288 : 320;
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
                var background = GetApplicationBrush("AccentPrimaryBrush", FallbackQuickRunSelectedBackgroundBrush);
                var border = GetApplicationBrush("OverlayStrokeBrush", FallbackQuickRunSelectedBorderBrush);
                var foreground = GetApplicationBrush("AccentForegroundBrush", FallbackQuickRunSelectedForegroundBrush);

                QuickRunSelectedButton.Background = background;
                QuickRunSelectedButton.BorderBrush = border;
                QuickRunSelectedButton.Foreground = foreground;
                CompactQuickRunSelectedButton.Background = background;
                CompactQuickRunSelectedButton.BorderBrush = border;
                CompactQuickRunSelectedButton.Foreground = foreground;
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

        private async Task RestartApplicationAsync()
        {
            try
            {
                await PrepareWorkflowForCleanReloadAsync();
                App.MarkNextLaunchAsCleanReload();

                var executablePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    executablePath = Process.GetCurrentProcess().MainModule?.FileName;
                }

                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                {
                    App.ClearCleanReloadMarker();
                    DiagnosticsTrace.Write("Reload requested but executable path could not be resolved.");
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = App.CleanReloadArgument,
                    WorkingDirectory = AppContext.BaseDirectory,
                    UseShellExecute = true,
                });

                App.Current.Exit();
            }
            catch (Exception ex)
            {
                App.ClearCleanReloadMarker();
                DiagnosticsTrace.Write($"Reload failed: {ex}");
            }
        }

        private async Task PrepareWorkflowForCleanReloadAsync()
        {
            if (trackedDashboardPage is not null)
            {
                await trackedDashboardPage.ViewModel.PrepareCurrentWorkflowForCleanReloadAsync();
                return;
            }

            var latestWorkflow = await App.Host.WorkflowStore.LoadLatestAsync();
            if (latestWorkflow is null
                || !TryResolveWorkflowReloadSource(latestWorkflow, out var sourcePath, out var isFile))
            {
                return;
            }

            var cleanWorkflow = App.Host.WorkflowEngine.CreateWorkflow(sourcePath, isFile);
            cleanWorkflow.Id = latestWorkflow.Id;
            cleanWorkflow.CreatedAt = latestWorkflow.CreatedAt;
            cleanWorkflow.LastExecutionSummary = "Sin ejecuciones todavía.";

            await App.Host.WorkflowStore.SaveAsync(cleanWorkflow);
        }

        private static bool TryResolveWorkflowReloadSource(Models.WorkflowInstance workflow, out string sourcePath, out bool isFile)
        {
            var sourceSelectionIsFile = workflow.SourceSelectionIsFile == true;
            var candidatePath = sourceSelectionIsFile
                ? workflow.PrimaryVideoPath
                : workflow.RootPath;

            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                candidatePath = !string.IsNullOrWhiteSpace(workflow.PrimaryVideoPath)
                    ? workflow.PrimaryVideoPath
                    : workflow.RootPath;
            }

            sourcePath = candidatePath ?? string.Empty;
            isFile = sourceSelectionIsFile;
            return !string.IsNullOrWhiteSpace(sourcePath);
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

        private static Brush GetApplicationBrush(string resourceKey, Brush fallback)
        {
            if (Application.Current?.Resources.TryGetValue(resourceKey, out var resource) == true
                && resource is Brush brush)
            {
                return brush;
            }

            return fallback;
        }
    }
}

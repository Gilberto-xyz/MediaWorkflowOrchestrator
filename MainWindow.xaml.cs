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
        private const double ShellCompactSpacingBreakpoint = 900;
        private static readonly SolidColorBrush FallbackQuickRunSelectedBackgroundBrush = CreateBrush(0xFF, 0x28, 0x5A, 0x8F);
        private static readonly SolidColorBrush FallbackQuickRunSelectedActiveBackgroundBrush = CreateBrush(0xFF, 0x34, 0x77, 0xB8);
        private static readonly SolidColorBrush FallbackQuickRunSelectedBorderBrush = CreateBrush(0xFF, 0x6B, 0xA7, 0xE6);
        private static readonly SolidColorBrush FallbackQuickRunSelectedForegroundBrush = CreateBrush(0xFF, 0xFF, 0xFF, 0xFF);
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
            UpdateShellResponsiveLayout();
            rootShellInitialized = true;
            UpdateShellWorkflowHeader();
        }

        private void OnRootShellSizeChanged(object sender, SizeChangedEventArgs e)
        {
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

        private void UpdateShellResponsiveLayout()
        {
            var width = RootShell.ActualWidth;
            var compactShell = width < ShellCompactSpacingBreakpoint;

            ShellHeaderLayout.Margin = compactShell
                ? new Thickness(0, 0, 4, 4)
                : new Thickness(0, 0, 8, 6);
            ContentFrame.Margin = compactShell
                ? new Thickness(0, 0, 4, 4)
                : new Thickness(0, 0, 8, 8);
            AppNavigationView.OpenPaneLength = compactShell ? 208 : 224;
            ShellCommandBar.DefaultLabelPosition = compactShell
                ? CommandBarDefaultLabelPosition.Collapsed
                : CommandBarDefaultLabelPosition.Right;
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
                UpdateShellProgressMonitorVisibility();
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

            UpdateShellWorkflowHeader();
            UpdateQuickRunSelectedButtonVisual();
            UpdateShellProgressMonitorVisibility();
        }

        private void OnDashboardViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(DashboardViewModel.HasExplicitStepSelection)
                or nameof(DashboardViewModel.SelectedStep))
            {
                _ = DispatcherQueue.TryEnqueue(UpdateQuickRunSelectedButtonVisual);
            }

            if (e.PropertyName == nameof(DashboardViewModel.ShowDetailProgress))
            {
                _ = DispatcherQueue.TryEnqueue(UpdateShellProgressMonitorVisibility);
            }
        }

        private void UpdateShellWorkflowHeader()
        {
            var dashboardViewModel = trackedDashboardPage?.ViewModel;
            ShellWorkflowHeader.DataContext = dashboardViewModel;
            ShellWorkflowHeader.Visibility = dashboardViewModel is null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateQuickRunSelectedButtonVisual()
        {
            var isActive = trackedDashboardPage?.ViewModel.SelectedStep is not null;
            var background = isActive
                ? GetApplicationBrush("RunSelectedActiveBackgroundBrush", FallbackQuickRunSelectedActiveBackgroundBrush)
                : GetApplicationBrush("RunSelectedBackgroundBrush", FallbackQuickRunSelectedBackgroundBrush);
            var border = GetApplicationBrush("RunSelectedBorderBrush", FallbackQuickRunSelectedBorderBrush);
            var foreground = GetApplicationBrush("RunSelectedForegroundBrush", FallbackQuickRunSelectedForegroundBrush);

            QuickRunSelectedButton.Background = background;
            QuickRunSelectedButton.BorderBrush = border;
            QuickRunSelectedButton.Foreground = foreground;
        }

        private void UpdateShellProgressMonitorVisibility()
        {
            ShellProgressMonitor.Visibility = trackedDashboardPage?.ViewModel.ShowDetailProgress == true
                ? Visibility.Visible
                : Visibility.Collapsed;
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

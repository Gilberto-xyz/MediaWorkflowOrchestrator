using MediaWorkflowOrchestrator.Messages;
using MediaWorkflowOrchestrator.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using System.Windows.Input;
using System.Diagnostics;
using System.ComponentModel;

namespace MediaWorkflowOrchestrator
{
    public sealed partial class MainWindow : Window, IRecipient<WorkflowSelectedMessage>
    {
        private static readonly SolidColorBrush ActiveQuickRunSelectedBackgroundBrush = CreateBrush(0xCC, 0x0F, 0x76, 0x6E);
        private static readonly SolidColorBrush ActiveQuickRunSelectedBorderBrush = CreateBrush(0xFF, 0x2A, 0xF5, 0x98);
        private static readonly SolidColorBrush ActiveQuickRunSelectedForegroundBrush = CreateBrush(0xFF, 0xFF, 0xFF, 0xFF);

        private readonly Stopwatch backdropClock = new();
        private DispatcherQueueTimer? backdropTimer;
        private DashboardPage? trackedDashboardPage;

        public MainWindow()
        {
            DiagnosticsTrace.Write("MainWindow ctor start.");
            InitializeComponent();
            WeakReferenceMessenger.Default.Register(this);
            Title = "Media Workflow Orchestrator";
            AppNavigationView.SelectedItem = DashboardItem;
            DiagnosticsTrace.Write("MainWindow ctor completed.");
        }

        public MainWindowViewModel ViewModel { get; } = new();

        private void OnRootShellLoaded(object sender, RoutedEventArgs e)
        {
            if (backdropTimer is not null)
            {
                return;
            }

            backdropClock.Start();
            backdropTimer = DispatcherQueue.CreateTimer();
            backdropTimer.Interval = TimeSpan.FromMilliseconds(33);
            backdropTimer.Tick += (_, _) => UpdateBackdropVisuals();
            backdropTimer.Start();
            UpdateBackdropVisuals();
        }

        private void UpdateBackdropVisuals()
        {
            var t = backdropClock.Elapsed.TotalSeconds;

            MatrixGlowTransform.TranslateX = -40 + (Math.Sin(t * 0.22) * 240);
            MatrixGlowTransform.TranslateY = Math.Cos(t * 0.18) * 120;

            AnimeGlowTransform.TranslateX = Math.Cos(t * 0.16) * 180;
            AnimeGlowTransform.TranslateY = 30 + (Math.Sin(t * 0.24) * 140);

            CinemaGlowTransform.TranslateX = -60 + (Math.Sin(t * 0.14) * 160);
            CinemaGlowTransform.TranslateY = -20 + (Math.Cos(t * 0.21) * 110);

            SweepTransform.TranslateX = -320 + (Math.Sin(t * 0.27) * 460);

            MatrixGlow.Opacity = 0.16 + ((Math.Sin(t * 0.7) + 1) * 0.08);
            AnimeGlow.Opacity = 0.18 + ((Math.Cos(t * 0.55) + 1) * 0.06);
            CinemaGlow.Opacity = 0.14 + ((Math.Sin(t * 0.62) + 1) * 0.07);
            SweepAccent.Opacity = 0.08 + ((Math.Cos(t * 0.48) + 1) * 0.04);
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
                return;
            }

            QuickRunSelectedButton.ClearValue(Button.BackgroundProperty);
            QuickRunSelectedButton.ClearValue(Button.BorderBrushProperty);
            QuickRunSelectedButton.ClearValue(Button.ForegroundProperty);
        }

        private static void ExecuteCommand(ICommand command)
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
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

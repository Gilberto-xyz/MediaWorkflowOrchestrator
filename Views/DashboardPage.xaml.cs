using Windows.Storage.Pickers;
using System.ComponentModel;
using MediaWorkflowOrchestrator.Models;

namespace MediaWorkflowOrchestrator.Views
{
    public sealed partial class DashboardPage : Page
    {
        private const double MediumLayoutBreakpoint = 900;
        private const double NarrowLayoutBreakpoint = 760;
        private WorkflowStepKey? displayedContextStep;
        private bool restoringContextScroll;
        private bool contextLayoutPending;
        private double pendingContextOffset;
        private readonly Dictionary<WorkflowStepKey, double> contextScrollOffsets = new();

        public DashboardPage()
        {
            DiagnosticsTrace.Write("DashboardPage ctor start.");
            InitializeComponent();
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            SizeChanged += OnPageSizeChanged;
            DiagnosticsTrace.Write("DashboardPage ctor completed.");
        }

        public DashboardViewModel ViewModel { get; } = new();

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateTranslationDecisionVisibility();
            UpdateQuickOptionsVisibility();
            UpdatePackageRarDetailActionsVisibility();
            UpdatePublishDetailActionsVisibility();
            UpdateResponsiveLayout(ActualWidth);
            UpdateSelectedContext();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            Unloaded -= OnUnloaded;
            SizeChanged -= OnPageSizeChanged;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateResponsiveLayout(e.NewSize.Width);
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DashboardViewModel.ShowTranslationDecisionActions))
            {
                _ = DispatcherQueue.TryEnqueue(UpdateTranslationDecisionVisibility);
            }

            if (e.PropertyName is nameof(DashboardViewModel.ShowQuickActionOptions)
                or nameof(DashboardViewModel.ShowDownloadQuickOptions)
                or nameof(DashboardViewModel.ShowTranslateQuickOptions)
                or nameof(DashboardViewModel.ShowCleanTracksQuickOptions)
                or nameof(DashboardViewModel.ShowTagAndRenameQuickOptions)
                or nameof(DashboardViewModel.ShowPackageRarQuickOptions)
                or nameof(DashboardViewModel.ShowPublishQuickOptions)
                or nameof(DashboardViewModel.ShowSkipAheadActions))
            {
                _ = DispatcherQueue.TryEnqueue(UpdateQuickOptionsVisibility);
            }

            if (e.PropertyName == nameof(DashboardViewModel.ShowPackageRarDetailActions))
            {
                _ = DispatcherQueue.TryEnqueue(UpdatePackageRarDetailActionsVisibility);
            }

            if (e.PropertyName == nameof(DashboardViewModel.ShowPublishDetailActions))
            {
                _ = DispatcherQueue.TryEnqueue(UpdatePublishDetailActionsVisibility);
            }

            if (e.PropertyName == nameof(DashboardViewModel.SelectedStep)
                && ViewModel.SelectedStep?.StepKey != displayedContextStep)
            {
                restoringContextScroll = true;
                _ = DispatcherQueue.TryEnqueue(UpdateSelectedContext);
            }
        }

        private void UpdateTranslationDecisionVisibility()
        {
            TranslationDecisionPanel.Visibility = ViewModel.ShowTranslationDecisionActions
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void UpdateQuickOptionsVisibility()
        {
            SkipAheadPanel.Visibility = ViewModel.ShowSkipAheadActions ? Visibility.Visible : Visibility.Collapsed;
            DownloadQuickOptionsPanel.Visibility = ViewModel.ShowDownloadQuickOptions ? Visibility.Visible : Visibility.Collapsed;
            TranslateQuickOptionsPanel.Visibility = ViewModel.ShowTranslateQuickOptions ? Visibility.Visible : Visibility.Collapsed;
            CleanTracksQuickOptionsPanel.Visibility = ViewModel.ShowCleanTracksQuickOptions ? Visibility.Visible : Visibility.Collapsed;
            TagAndRenameQuickOptionsPanel.Visibility = ViewModel.ShowTagAndRenameQuickOptions ? Visibility.Visible : Visibility.Collapsed;
            PackageRarQuickOptionsPanel.Visibility = ViewModel.ShowPackageRarQuickOptions ? Visibility.Visible : Visibility.Collapsed;
            PublishQuickOptionsPanel.Visibility = ViewModel.ShowPublishQuickOptions ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdatePackageRarDetailActionsVisibility()
        {
            PackageRarDetailActionsPanel.Visibility = ViewModel.ShowPackageRarDetailActions
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void UpdatePublishDetailActionsVisibility()
        {
            PublishDetailActionsPanel.Visibility = ViewModel.ShowPublishDetailActions
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void OnStepItemClicked(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: WorkflowStepState step })
            {
                if (displayedContextStep is { } key && key != step.StepKey)
                {
                    contextScrollOffsets[key] = StepOptionsScrollViewer.VerticalOffset;
                    restoringContextScroll = true;
                }
                ViewModel.SelectStepFromUser(step);
            }
        }

        private void UpdateSelectedContext()
        {
            var nextStep = ViewModel.SelectedStep?.StepKey;
            if (nextStep == displayedContextStep)
            {
                return;
            }

            displayedContextStep = nextStep;
            pendingContextOffset = nextStep is { } key && contextScrollOffsets.TryGetValue(key, out var saved) ? saved : 0;
            restoringContextScroll = true;
            contextLayoutPending = true;
            StepOptionsScrollViewer.InvalidateMeasure();
            // Diagnostics are controlled exclusively by the user, never by step changes.
        }

        private void OnContextViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (!restoringContextScroll && !e.IsIntermediate
                && displayedContextStep is { } key && key == ViewModel.SelectedStep?.StepKey)
            {
                contextScrollOffsets[key] = StepOptionsScrollViewer.VerticalOffset;
            }
        }

        private void OnContextLayoutUpdated(object? sender, object e)
        {
            if (!contextLayoutPending)
            {
                return;
            }
            contextLayoutPending = false;
            var step = displayedContextStep;
            var offset = pendingContextOffset;
            // Wait for the newly selected controls to establish their scroll extent.
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (step != displayedContextStep || step != ViewModel.SelectedStep?.StepKey)
                {
                    return;
                }
                StepOptionsScrollViewer.ChangeView(null, offset, null, disableAnimation: true);
                restoringContextScroll = false;
            });
        }

        private void OnStepWorkspaceSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateOptionsLayout(Math.Max(0, e.NewSize.Width - 24));
            DiagnosticsContentGrid.Height = Math.Clamp(e.NewSize.Height * 0.35, 100, 300);
        }

        private void OnDiagnosticsExpanding(Expander sender, ExpanderExpandingEventArgs args)
        {
            DiagnosticsContentGrid.Height = Math.Clamp(StepWorkspace.ActualHeight * 0.35, 100, 300);
        }

        private void UpdateResponsiveLayout(double width)
        {
            DashboardLayoutRoot.Padding = ResponsiveLayout.PagePadding(width);
            var stacked = width < 760;
            DashboardContentGrid.ColumnSpacing = stacked ? 0 : 16;
            DashboardContentGrid.RowSpacing = stacked ? 12 : 0;
            DashboardContentGrid.ColumnDefinitions[0].Width = stacked ? Star() : new GridLength(Math.Clamp(width * 0.18, 240, 320));
            DashboardContentGrid.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : Star();
            DashboardContentGrid.RowDefinitions[0].Height = stacked ? new GridLength(154) : Star();
            DashboardContentGrid.RowDefinitions[1].Height = stacked ? Star() : new GridLength(0);
            Grid.SetColumn(StepWorkspace, stacked ? 0 : 1);
            Grid.SetRow(StepWorkspace, stacked ? 1 : 0);
            UpdateOptionsLayout(Math.Max(0, StepWorkspace.ActualWidth - 24));
        }

        private void UpdateOptionsLayout(double width)
        {

            ApplyResponsiveGrid(
                PublicationSummaryGrid,
                width < MediumLayoutBreakpoint,
                new[] { Star(), GridLength.Auto, GridLength.Auto },
                (0, 0),
                (0, 1),
                (0, 2));

            ApplyResponsiveGrid(
                PublicationRouteGrid,
                width < MediumLayoutBreakpoint,
                new[] { Star(), Star() },
                (0, 0),
                (0, 1),
                (1, 0),
                (1, 1));

            ApplyResponsiveGrid(
                PublicationLinksGrid,
                width < MediumLayoutBreakpoint,
                new[] { Star(), Star() },
                (0, 0),
                (0, 1));

            ApplyResponsiveGrid(
                PublicationTransferGrid,
                width < MediumLayoutBreakpoint,
                new[] { Star(), new GridLength(220) },
                (0, 0),
                (0, 1));

            ApplyResponsiveGrid(
                DetailOutputToolbarGrid,
                width < 420,
                new[] { Star(), GridLength.Auto, GridLength.Auto },
                (0, 0),
                (0, 1),
                (0, 2));
        }

        private static void ApplyResponsiveGrid(Grid grid, bool stacked, GridLength[] wideColumnWidths, params (int row, int column)[] widePositions)
            => ResponsiveLayout.ApplyGrid(grid, stacked, wideColumnWidths, widePositions);

        private static GridLength Star(double value = 1) => new(value, GridUnitType.Star);

        public async Task PickFileAsync()
        {
            try
            {
                DiagnosticsTrace.Write("PickFileAsync started.");
                ViewModel.BeginWorkflowSelection("Esperando que elijas el archivo base del nuevo workflow.");
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".mkv");
                picker.FileTypeFilter.Add(".mp4");
                picker.FileTypeFilter.Add(".m4v");
                picker.FileTypeFilter.Add(".ass");
                picker.FileTypeFilter.Add(".srt");
                picker.FileTypeFilter.Add(".ssa");
                picker.FileTypeFilter.Add(".mks");
                picker.SuggestedStartLocation = PickerLocationId.Downloads;

                var windowHandle = App.MainWindowHandle;
                if (windowHandle == IntPtr.Zero)
                {
                    throw new InvalidOperationException("La ventana principal todavía no está lista para mostrar el selector de archivos.");
                }

                WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
                DiagnosticsTrace.Write("PickFileAsync initialized picker with window handle.");
                var file = await picker.PickSingleFileAsync();
                DiagnosticsTrace.Write(file is null
                    ? "PickFileAsync picker returned null."
                    : $"PickFileAsync selected file: {file.Path}");
                if (file is not null)
                {
                    await ViewModel.CreateWorkflowFromPathAsync(file.Path, true);
                    ViewModel.ShowStatus(InfoBarSeverity.Success, $"Workflow cargado desde archivo: {file.Name}");
                }
                else
                {
                    ViewModel.ShowStatus(InfoBarSeverity.Informational, "Selección de archivo cancelada.");
                }
            }
            catch (Exception ex)
            {
                DiagnosticsTrace.Write($"Pick file failed: {ex}");
                ViewModel.ShowStatus(InfoBarSeverity.Error, $"No se pudo abrir el selector de archivos: {ex.Message}");
            }
        }

        public async Task PickFolderAsync()
        {
            try
            {
                DiagnosticsTrace.Write("PickFolderAsync started.");
                ViewModel.BeginWorkflowSelection("Esperando que elijas la carpeta base del nuevo workflow.");
                var picker = new FolderPicker
                {
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary
                };
                picker.FileTypeFilter.Add("*");

                var windowHandle = App.MainWindowHandle;
                if (windowHandle == IntPtr.Zero)
                {
                    throw new InvalidOperationException("La ventana principal todavía no está lista para mostrar el selector de carpetas.");
                }

                WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
                DiagnosticsTrace.Write("PickFolderAsync initialized picker with window handle.");
                var folder = await picker.PickSingleFolderAsync();
                DiagnosticsTrace.Write(folder is null
                    ? "PickFolderAsync picker returned null."
                    : $"PickFolderAsync selected folder: {folder.Path}");
                if (folder is not null)
                {
                    await ViewModel.CreateWorkflowFromPathAsync(folder.Path, false);
                    ViewModel.ShowStatus(InfoBarSeverity.Success, $"Workflow cargado desde carpeta: {folder.Name}");
                }
                else
                {
                    ViewModel.ShowStatus(InfoBarSeverity.Informational, "Selección de carpeta cancelada.");
                }
            }
            catch (Exception ex)
            {
                DiagnosticsTrace.Write($"Pick folder failed: {ex}");
                ViewModel.ShowStatus(InfoBarSeverity.Error, $"No se pudo abrir el selector de carpetas: {ex.Message}");
            }
        }

        public async Task DownloadFromLinkAsync()
        {
            var linkTextBox = new TextBox
            {
                Header = "Link de Nyaa",
                PlaceholderText = "https://nyaa.si/?f=0&c=0_0&q=...",
                TextWrapping = TextWrapping.Wrap,
                MinWidth = ActualWidth < NarrowLayoutBreakpoint ? 280 : 420,
            };

            var modeComboBox = new ComboBox
            {
                Header = "Modo de descarga",
                SelectedIndex = 0,
                ItemsSource = new[]
                {
                    new ComboBoxItem { Content = "Solo episodios futuros (from-latest)", Tag = "from-latest" },
                    new ComboBoxItem { Content = "Descargar todo lo detectado (all)", Tag = "all" },
                }
            };

            var dialogContent = new StackPanel { Spacing = 12 };
            dialogContent.Children.Add(linkTextBox);
            dialogContent.Children.Add(modeComboBox);

            var dialog = new ContentDialog
            {
                Title = "Descargar desde link de Nyaa",
                PrimaryButtonText = "Ejecutar",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Primary,
                Content = dialogContent,
                XamlRoot = XamlRoot,
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                ViewModel.ShowStatus(InfoBarSeverity.Informational, "Descarga por link cancelada.");
                return;
            }

            var mode = (modeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "from-latest";
            await ViewModel.RunDownloadFromLinkAsync(linkTextBox.Text, mode);
        }
    }
}

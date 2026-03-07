using Windows.Storage.Pickers;
using System.ComponentModel;
using MediaWorkflowOrchestrator.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;

namespace MediaWorkflowOrchestrator.Views
{
    public sealed partial class DashboardPage : Page
    {
        private bool detailOutputResizeActive;
        private double lastDetailOutputPointerY;

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
            ViewModel.EnsureDetailOutputFitsViewport(ActualHeight);
            UpdateTranslationDecisionVisibility();
            UpdateQuickOptionsVisibility();
            UpdatePackageRarDetailActionsVisibility();
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
            ViewModel.EnsureDetailOutputFitsViewport(e.NewSize.Height);
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
                or nameof(DashboardViewModel.ShowPackageRarQuickOptions)
                or nameof(DashboardViewModel.ShowSkipAheadActions))
            {
                _ = DispatcherQueue.TryEnqueue(UpdateQuickOptionsVisibility);
            }

            if (e.PropertyName == nameof(DashboardViewModel.ShowPackageRarDetailActions))
            {
                _ = DispatcherQueue.TryEnqueue(UpdatePackageRarDetailActionsVisibility);
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
            PackageRarQuickOptionsPanel.Visibility = ViewModel.ShowPackageRarQuickOptions ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdatePackageRarDetailActionsVisibility()
        {
            PackageRarDetailActionsPanel.Visibility = ViewModel.ShowPackageRarDetailActions
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void OnStepItemClicked(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: WorkflowStepState step })
            {
                ViewModel.SelectStepFromUser(step);
            }
        }

        private void OnDetailOutputResizeHandlePointerPressed(object sender, PointerRoutedEventArgs e)
        {
            detailOutputResizeActive = true;
            lastDetailOutputPointerY = e.GetCurrentPoint(this).Position.Y;
            if (sender is UIElement element)
            {
                element.CapturePointer(e.Pointer);
            }
        }

        private void OnDetailOutputResizeHandlePointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!detailOutputResizeActive)
            {
                return;
            }

            var pointerY = e.GetCurrentPoint(this).Position.Y;
            var delta = pointerY - lastDetailOutputPointerY;
            if (Math.Abs(delta) < 1)
            {
                return;
            }

            ViewModel.ResizeDetailOutput(delta);
            lastDetailOutputPointerY = pointerY;
        }

        private void OnDetailOutputResizeHandlePointerReleased(object sender, PointerRoutedEventArgs e)
        {
            ReleaseDetailOutputResize(sender, e.Pointer);
        }

        private void OnDetailOutputResizeHandlePointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            ReleaseDetailOutputResize(sender, e.Pointer);
        }

        private void OnDetailOutputAutoSizeClicked(object sender, RoutedEventArgs e)
        {
            ViewModel.ResetDetailOutputAutoSize(ActualHeight);
        }

        private void ReleaseDetailOutputResize(object sender, Pointer pointer)
        {
            detailOutputResizeActive = false;
            if (sender is UIElement element)
            {
                element.ReleasePointerCapture(pointer);
            }
        }

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
                MinWidth = 420,
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

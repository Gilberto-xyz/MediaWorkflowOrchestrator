using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MediaWorkflowOrchestrator.Services
{
    public sealed class WorkflowExecutionService : IWorkflowExecutionService
    {
        public const string PackageRarRawDataHintKey = "package_rar_raw_data";
        public const string PackageRarWeightSummaryHintKey = "package_rar_weight_summary";
        public const string PackageRarCleanNameHintKey = "package_rar_clean_name";
        public const string PackageRarSeriesNameHintKey = "package_rar_series_name";
        private const string EmptyTrackSelectionToken = "__none__";

        private readonly IAppSettingsService appSettingsService;
        private readonly IWorkflowStore workflowStore;
        private readonly ISecretProtector secretProtector;
        private readonly IProcessRunnerService processRunnerService;
        private readonly IToolValidationService toolValidationService;
        private readonly ISubtitleInspectorService subtitleInspectorService;
        private readonly IWorkflowEngine workflowEngine;

        public WorkflowExecutionService(
            IAppSettingsService appSettingsService,
            IWorkflowStore workflowStore,
            ISecretProtector secretProtector,
            IProcessRunnerService processRunnerService,
            IToolValidationService toolValidationService,
            ISubtitleInspectorService subtitleInspectorService,
            IWorkflowEngine workflowEngine)
        {
            this.appSettingsService = appSettingsService;
            this.workflowStore = workflowStore;
            this.secretProtector = secretProtector;
            this.processRunnerService = processRunnerService;
            this.toolValidationService = toolValidationService;
            this.subtitleInspectorService = subtitleInspectorService;
            this.workflowEngine = workflowEngine;
        }

        public Task<AppSettings> GetSettingsAsync() => appSettingsService.LoadAsync();

        public async Task SaveSettingsAsync(AppSettings settings, string rarPassword)
        {
            settings.EncryptedRarPassword = secretProtector.Protect(rarPassword);
            await appSettingsService.SaveAsync(settings);
        }

        public async Task<AppSettings> RestoreDefaultSettingsAsync()
        {
            var defaults = await appSettingsService.RestoreDefaultsAsync();
            defaults.EncryptedRarPassword = secretProtector.Protect("GDRIVELatinoHD.NET");
            await appSettingsService.SaveAsync(defaults);
            return defaults;
        }

        public string GetDecryptedRarPassword(AppSettings settings) => secretProtector.Unprotect(settings.EncryptedRarPassword);

        public async Task<IReadOnlyList<ToolValidationResult>> ValidateToolsAsync()
        {
            var settings = await appSettingsService.LoadAsync();
            return await toolValidationService.ValidateAllAsync(settings);
        }

        public async Task<WorkflowInstance> CreateWorkflowAsync(string selectedPath, bool isFile, CancellationToken cancellationToken)
        {
            var workflow = workflowEngine.CreateWorkflow(selectedPath, isFile);
            var settings = await appSettingsService.LoadAsync();

            if (!string.IsNullOrWhiteSpace(workflow.PrimaryVideoPath))
            {
                var inspection = await subtitleInspectorService.InspectAsync(workflow.PrimaryVideoPath, settings, cancellationToken);
                workflowEngine.ApplyInspectionResult(workflow, inspection);
            }
            else
            {
                var translationStep = workflow.FindStep(WorkflowStepKey.TranslateSubs);
                if (translationStep is not null)
                {
                    translationStep.Status = WorkflowStepStatus.NeedsDecision;
                    translationStep.StatusReason = "No se encontró un video principal; decide manualmente si traduces subtítulos.";
                }

                workflowEngine.RefreshStatuses(workflow);
            }

            await workflowStore.SaveAsync(workflow);
            return workflow;
        }

        public Task<WorkflowInstance?> LoadLatestWorkflowAsync() => workflowStore.LoadLatestAsync();

        public Task<IReadOnlyList<WorkflowInstance>> LoadHistoryAsync() => workflowStore.LoadAllAsync();

        public Task<WorkflowInstance?> LoadWorkflowAsync(string workflowId) => workflowStore.LoadAsync(workflowId);

        public async Task<TrackCleanupAudioInspection> GetTrackCleanupAudioInspectionAsync(WorkflowInstance workflow, CancellationToken cancellationToken)
        {
            var settings = await appSettingsService.LoadAsync();
            return await BuildTrackCleanupAudioInspectionAsync(settings, workflow, cancellationToken);
        }

        public async Task<WorkflowInstance> DecideTranslationAsync(WorkflowInstance workflow, bool translateRequired)
        {
            workflowEngine.ApplyTranslationDecision(workflow, translateRequired);
            var translationStep = workflow.FindStep(WorkflowStepKey.TranslateSubs);
            if (translationStep is not null && !translateRequired)
            {
                translationStep.StatusReason = "La traducción se omitió manualmente desde la interfaz.";
            }

            workflow.LastExecutionSummary = translateRequired
                ? "La traducción de subtítulos quedó marcada como requerida."
                : "La traducción de subtítulos se omitió manualmente.";
            await workflowStore.SaveAsync(workflow);
            return workflow;
        }

        public async Task<ExecutionRecord?> ExecuteNextReadyStepAsync(WorkflowInstance workflow, Action<string>? onOutput, CancellationToken cancellationToken)
        {
            var nextStep = workflowEngine.GetNextReadyStep(workflow);
            return nextStep is null ? null : await ExecuteStepAsync(workflow, nextStep.StepKey, onOutput, cancellationToken);
        }

        public async Task<ExecutionRecord?> ExecuteStepAsync(WorkflowInstance workflow, WorkflowStepKey stepKey, Action<string>? onOutput, CancellationToken cancellationToken, bool forceExecution = false)
        {
            var settings = await appSettingsService.LoadAsync();
            var step = workflow.FindStep(stepKey);
            if (step is null)
            {
                return null;
            }

            if (!forceExecution && (step.Status is WorkflowStepStatus.Blocked or WorkflowStepStatus.NeedsDecision))
            {
                step.StatusReason = "Resuelve primero el estado del workflow antes de ejecutar este paso.";
                await workflowStore.SaveAsync(workflow);
                return null;
            }

            if (forceExecution)
            {
                PrepareStepForForcedExecution(step, stepKey, onOutput);
            }

            if (stepKey == WorkflowStepKey.InspectSubs)
            {
                var inspection = await subtitleInspectorService.InspectAsync(workflow.PrimaryVideoPath, settings, cancellationToken);
                workflowEngine.ApplyInspectionResult(workflow, inspection);
                await workflowStore.SaveAsync(workflow);
                return new ExecutionRecord
                {
                    WorkflowId = workflow.Id,
                    StepKey = stepKey,
                    StartedAt = DateTimeOffset.UtcNow,
                    FinishedAt = DateTimeOffset.UtcNow,
                    ExitCode = 0,
                    Success = inspection.Availability != SubtitleSpanishAvailability.Unknown,
                    Summary = inspection.Message,
                };
            }

            if (stepKey == WorkflowStepKey.TranslateSubs)
            {
                var (reachable, message) = await CheckOllamaReachabilityAsync(settings.OllamaHost, cancellationToken);
                if (!reachable)
                {
                    step.Status = WorkflowStepStatus.Failed;
                    step.StatusReason = message;
                    workflow.LastExecutionSummary = $"{step.DisplayName}: {message}";
                    await workflowStore.SaveAsync(workflow);
                    return new ExecutionRecord
                    {
                        WorkflowId = workflow.Id,
                        StepKey = stepKey,
                        StartedAt = DateTimeOffset.UtcNow,
                        FinishedAt = DateTimeOffset.UtcNow,
                        ExitCode = -1,
                        Success = false,
                        Summary = message,
                    };
                }
            }

            var request = stepKey == WorkflowStepKey.CleanTracks
                ? await BuildTrackCleanupRequestAsync(settings, workflow, onOutput, cancellationToken)
                : BuildRequest(settings, workflow, stepKey);
            step.Status = WorkflowStepStatus.Running;
            step.StartedAt = DateTimeOffset.UtcNow;
            step.StatusReason = "Proceso en ejecución...";
            await workflowStore.SaveAsync(workflow);

            var logPaths = CreateLogPaths(workflow.Id, stepKey);
            ProcessExecutionResult result;
            try
            {
                if (stepKey == WorkflowStepKey.TagAndRename)
                {
                    var preparation = PrepareTagAndRenameWorkspace(settings, workflow, onOutput);
                    request = BuildRequest(settings, workflow, stepKey, preparation.WorkingDirectory);
                    result = preparation.LaunchRenamerOnly
                        ? await LaunchRenamerShortcutAsync(preparation, cancellationToken, onOutput)
                        : await processRunnerService.RunAsync(request, onOutput, cancellationToken);
                }
                else if (stepKey == WorkflowStepKey.PackageRar)
                {
                    var rarInputPath = await PrepareRarPackagingInputAsync(workflow, cancellationToken, onOutput);
                    request = BuildRequest(settings, workflow, stepKey, overrideStepInputPath: rarInputPath);
                    result = await processRunnerService.RunAsync(request, onOutput, cancellationToken);
                }
                else
                {
                    result = await processRunnerService.RunAsync(request, onOutput, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                step.Status = WorkflowStepStatus.Failed;
                step.StatusReason = "Ejecución cancelada por el usuario.";
                await workflowStore.SaveAsync(workflow);
                throw;
            }
            catch (Exception ex)
            {
                step.Status = WorkflowStepStatus.Failed;
                step.StatusReason = ex.Message;
                step.FinishedAt = DateTimeOffset.UtcNow;
                workflow.LastExecutionSummary = $"{step.DisplayName}: {step.StatusReason}";
                await workflowStore.SaveAsync(workflow);
                return new ExecutionRecord
                {
                    WorkflowId = workflow.Id,
                    StepKey = stepKey,
                    StartedAt = step.StartedAt ?? DateTimeOffset.UtcNow,
                    FinishedAt = step.FinishedAt ?? DateTimeOffset.UtcNow,
                    ExitCode = -1,
                    Success = false,
                    Summary = ex.Message,
                };
            }

            result = NormalizeProcessResult(stepKey, result, onOutput);
            step.OutputHints = new Dictionary<string, string>();
            result = ExtractStructuredOutputHints(stepKey, workflow, step, result);

            await File.WriteAllTextAsync(logPaths.stdout, result.StandardOutput, cancellationToken);
            await File.WriteAllTextAsync(logPaths.stderr, result.StandardError, cancellationToken);

            step.ExitCode = result.ExitCode;
            step.StdoutLogPath = logPaths.stdout;
            step.StderrLogPath = logPaths.stderr;
            step.FinishedAt = result.FinishedAt;
            step.Status = result.Success ? WorkflowStepStatus.Succeeded : WorkflowStepStatus.Failed;
            step.StatusReason = result.Success ? BuildSuccessSummary(stepKey, result) : BuildFailureSummary(result);
            UpdateWorkflowOutputs(workflow, stepKey, result, onOutput);
            workflow.LastExecutionSummary = $"{step.DisplayName}: {step.StatusReason}";
            workflowEngine.RefreshStatuses(workflow);
            await workflowStore.SaveAsync(workflow);

            return new ExecutionRecord
            {
                WorkflowId = workflow.Id,
                StepKey = stepKey,
                StartedAt = result.StartedAt,
                FinishedAt = result.FinishedAt,
                ExitCode = result.ExitCode,
                CommandDisplay = NormalizeSecret(result.CommandDisplay, secretProtector.Unprotect(settings.EncryptedRarPassword)),
                WorkingDirectory = request.WorkingDirectory,
                StdoutLogPath = logPaths.stdout,
                StderrLogPath = logPaths.stderr,
                Success = result.Success,
                Summary = step.StatusReason,
            };
        }

        private ProcessExecutionRequest BuildRequest(
            AppSettings settings,
            WorkflowInstance workflow,
            WorkflowStepKey stepKey,
            string? overrideWorkingDirectory = null,
            string? overrideStepInputPath = null)
        {
            return stepKey switch
            {
                WorkflowStepKey.Download => new ProcessExecutionRequest
                {
                    FileName = settings.PythonPath,
                    Arguments = new[]
                    {
                        settings.DownloaderScriptPath,
                        "--config",
                        settings.DownloaderConfigPath,
                    },
                    WorkingDirectory = settings.DownloadWorkingDirectory,
                },
                WorkflowStepKey.TranslateSubs => new ProcessExecutionRequest
                {
                    FileName = settings.PythonPath,
                    Arguments = BuildSubtitleTranslatorArgs(settings, workflow).ToArray(),
                    WorkingDirectory = settings.SubtitleWorkingDirectory,
                },
                WorkflowStepKey.CleanTracks => new ProcessExecutionRequest
                {
                    FileName = settings.PythonPath,
                    Arguments = BuildTrackCleanupArgs(
                        settings,
                        ResolveTrackCleanupInputPath(workflow),
                        settings.TrackCleanupDeleteOriginals,
                        Array.Empty<string>(),
                        null,
                        null,
                        null,
                        false,
                        Array.Empty<string>(),
                        null,
                        null,
                        null).ToArray(),
                    WorkingDirectory = Path.GetDirectoryName(settings.TrackCleanupScriptPath) ?? workflow.RootPath,
                },
                WorkflowStepKey.TagAndRename => new ProcessExecutionRequest
                {
                    FileName = settings.PythonPath,
                    Arguments = new[]
                    {
                        settings.TagAndRenameScriptPath,
                        "--brand",
                        settings.BrandName,
                    },
                    WorkingDirectory = overrideWorkingDirectory ?? settings.TagAndRenameWorkingDirectory,
                },
                WorkflowStepKey.PackageRar => new ProcessExecutionRequest
                {
                    FileName = settings.PythonPath,
                    Arguments = BuildRarPackagingArgs(settings, workflow, overrideStepInputPath).ToArray(),
                    WorkingDirectory = Path.GetDirectoryName(settings.RarPackagingScriptPath) ?? workflow.RootPath,
                },
                _ => new ProcessExecutionRequest
                {
                    FileName = settings.PythonPath,
                    WorkingDirectory = workflow.RootPath,
                }
            };
        }

        private static IEnumerable<string> BuildSubtitleInputArgs(WorkflowInstance workflow)
        {
            var root = workflow.RootPath;
            if (!Directory.Exists(root))
            {
                return Array.Empty<string>();
            }

            var source = Directory.EnumerateFiles(root, "*.ass", SearchOption.AllDirectories).FirstOrDefault()
                ?? Directory.EnumerateFiles(root, "*.srt", SearchOption.AllDirectories).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(source))
            {
                return Array.Empty<string>();
            }

            var output = Path.Combine(Path.GetDirectoryName(source) ?? root, $"{Path.GetFileNameWithoutExtension(source)}.translated{Path.GetExtension(source)}");
            return new[] { "--in", source, "--out", output };
        }

        private static IEnumerable<string> BuildSubtitleTranslatorArgs(AppSettings settings, WorkflowInstance workflow)
        {
            var args = new List<string>
            {
                settings.SubtitleTranslatorScriptPath,
                "--model",
                settings.OllamaModel,
                "--host",
                settings.OllamaHost,
                "--target",
                settings.SubtitleTargetLanguage,
            };

            if (settings.SubtitleFastMode)
            {
                args.Add("--fast");
            }

            if (settings.SubtitleSkipSummary)
            {
                args.Add("--skip-summary");
            }

            args.AddRange(BuildSubtitleInputArgs(workflow));
            return args;
        }

        private async Task<ProcessExecutionRequest> BuildTrackCleanupRequestAsync(
            AppSettings settings,
            WorkflowInstance workflow,
            Action<string>? onOutput,
            CancellationToken cancellationToken)
        {
            var cleanupInputPath = ResolveTrackCleanupInputPath(workflow);
            var deleteOriginals = settings.TrackCleanupDeleteOriginals;
            var targetContext = ResolveTrackCleanupTargetContext(workflow, cleanupInputPath);
            IReadOnlyList<string> selectedAudioTrackIds = Array.Empty<string>();
            IReadOnlyList<string> selectedSubtitleTrackIds = Array.Empty<string>();
            string? selectedPrimaryAudioTrackId = null;
            string? selectedPrimarySubtitleTrackId = null;
            string? selectedAudioSignaturePayload = null;
            string? selectedPrimaryAudioSignaturePayload = null;
            string? selectedSubtitleSignaturePayload = null;
            string? selectedPrimarySubtitleSignaturePayload = null;
            var applyManualSubtitleSelection = false;

            if (targetContext.CanManuallySelectAudio)
            {
                var inspection = await BuildTrackCleanupAudioInspectionAsync(settings, workflow, cancellationToken);
                ApplyTrackCleanupAudioInspection(workflow, inspection);

                if (inspection.CanManuallySelectAudio)
                {
                    selectedAudioTrackIds = workflow.TrackCleanupAudioOptions
                        .Where(option => option.IsSelected)
                        .Select(option => option.TrackId)
                        .ToList();
                    selectedPrimaryAudioTrackId = workflow.TrackCleanupAudioOptions
                        .FirstOrDefault(option => option.IsSelected && option.IsPrimary)
                        ?.TrackId;
                    selectedAudioSignaturePayload = EncodeTrackSelectionSignatures(
                        BuildTrackSelectionSignatures(workflow.TrackCleanupAudioOptions));
                    selectedPrimaryAudioSignaturePayload = EncodeTrackSelectionSignature(
                        BuildTrackSelectionSignature(workflow.TrackCleanupAudioOptions.FirstOrDefault(option => option.IsSelected && option.IsPrimary), 0));

                    if (selectedAudioTrackIds.Count == 0)
                    {
                        throw new InvalidOperationException("Selecciona al menos un audio para conservar antes de ejecutar Limpiar tracks.");
                    }

                    selectedSubtitleTrackIds = workflow.TrackCleanupSubtitleOptions
                        .Where(option => option.IsSelected)
                        .Select(option => option.TrackId)
                        .ToList();
                    selectedPrimarySubtitleTrackId = workflow.TrackCleanupSubtitleOptions
                        .FirstOrDefault(option => option.IsSelected && option.IsPrimary)
                        ?.TrackId;
                    selectedSubtitleSignaturePayload = EncodeTrackSelectionSignatures(
                        BuildTrackSelectionSignatures(workflow.TrackCleanupSubtitleOptions));
                    selectedPrimarySubtitleSignaturePayload = EncodeTrackSelectionSignature(
                        BuildTrackSelectionSignature(workflow.TrackCleanupSubtitleOptions.FirstOrDefault(option => option.IsSelected && option.IsPrimary), 0));
                    applyManualSubtitleSelection = true;

                    if (inspection.SpecialCases.Count > 0)
                    {
                        onOutput?.Invoke(inspection.SpecialCasesMessage);
                    }
                }
                else
                {
                    onOutput?.Invoke(inspection.Message);
                    if (deleteOriginals)
                    {
                        deleteOriginals = false;
                        onOutput?.Invoke("Se omitió --delete-originals porque no se pudo cargar una selección manual segura de tracks.");
                    }
                }
            }
            else if (deleteOriginals)
            {
                deleteOriginals = false;
                onOutput?.Invoke(targetContext.Message);
                onOutput?.Invoke("Se omitió --delete-originals porque este paso procesa más de un video y la selección manual de tracks no aplica.");
            }

            if (deleteOriginals && !string.IsNullOrWhiteSpace(targetContext.TargetVideoPath))
            {
                var audioTrackCount = await GetAudioTrackCountAsync(targetContext.TargetVideoPath, settings, cancellationToken);
                if (audioTrackCount == 1)
                {
                    deleteOriginals = false;
                    onOutput?.Invoke("Se omitió --delete-originals porque el archivo objetivo solo tiene una pista de audio.");
                }
            }

            return new ProcessExecutionRequest
            {
                FileName = settings.PythonPath,
                Arguments = BuildTrackCleanupArgs(
                    settings,
                    cleanupInputPath,
                    deleteOriginals,
                    selectedAudioTrackIds,
                    selectedPrimaryAudioTrackId,
                    selectedAudioSignaturePayload,
                    selectedPrimaryAudioSignaturePayload,
                    applyManualSubtitleSelection,
                    selectedSubtitleTrackIds,
                    selectedPrimarySubtitleTrackId,
                    selectedSubtitleSignaturePayload,
                    selectedPrimarySubtitleSignaturePayload).ToArray(),
                WorkingDirectory = Path.GetDirectoryName(settings.TrackCleanupScriptPath) ?? workflow.RootPath,
            };
        }

        private static IEnumerable<string> BuildTrackCleanupArgs(
            AppSettings settings,
            string cleanupInputPath,
            bool deleteOriginals,
            IReadOnlyList<string> selectedAudioTrackIds,
            string? selectedPrimaryAudioTrackId,
            string? selectedAudioSignaturePayload,
            string? selectedPrimaryAudioSignaturePayload,
            bool applyManualSubtitleSelection,
            IReadOnlyList<string> selectedSubtitleTrackIds,
            string? selectedPrimarySubtitleTrackId,
            string? selectedSubtitleSignaturePayload,
            string? selectedPrimarySubtitleSignaturePayload)
        {
            var args = new List<string>
            {
                settings.TrackCleanupScriptPath,
                cleanupInputPath,
                "--brand",
                settings.BrandName,
            };

            if (settings.TrackCleanupCloseQbittorrent)
            {
                args.Add("--file-in-use-action");
                args.Add("close-qbittorrent");
            }

            if (deleteOriginals)
            {
                args.Add("--delete-originals");
            }

            if (selectedAudioTrackIds.Count > 0)
            {
                args.Add("--keep-audio-ids");
                args.Add(string.Join(",", selectedAudioTrackIds));
            }

            if (!string.IsNullOrWhiteSpace(selectedPrimaryAudioTrackId))
            {
                args.Add("--audio-default-id");
                args.Add(selectedPrimaryAudioTrackId);
            }

            if (!string.IsNullOrWhiteSpace(selectedAudioSignaturePayload))
            {
                args.Add("--keep-audio-signatures");
                args.Add(selectedAudioSignaturePayload);
            }

            if (!string.IsNullOrWhiteSpace(selectedPrimaryAudioSignaturePayload))
            {
                args.Add("--audio-default-signature");
                args.Add(selectedPrimaryAudioSignaturePayload);
            }

            if (applyManualSubtitleSelection)
            {
                args.Add("--keep-subtitle-ids");
                args.Add(selectedSubtitleTrackIds.Count > 0
                    ? string.Join(",", selectedSubtitleTrackIds)
                    : EmptyTrackSelectionToken);

                if (!string.IsNullOrWhiteSpace(selectedPrimarySubtitleTrackId))
                {
                    args.Add("--subtitle-default-id");
                    args.Add(selectedPrimarySubtitleTrackId);
                }

                if (!string.IsNullOrWhiteSpace(selectedSubtitleSignaturePayload))
                {
                    args.Add("--keep-subtitle-signatures");
                    args.Add(selectedSubtitleSignaturePayload);
                }

                if (!string.IsNullOrWhiteSpace(selectedPrimarySubtitleSignaturePayload))
                {
                    args.Add("--subtitle-default-signature");
                    args.Add(selectedPrimarySubtitleSignaturePayload);
                }
            }

            return args;
        }

        private async Task<int?> GetAudioTrackCountAsync(string videoPath, AppSettings settings, CancellationToken cancellationToken)
        {
            var trackOptions = await GetTrackCleanupOptionsAsync(videoPath, settings, cancellationToken);
            return trackOptions?.AudioOptions.Count;
        }

        private async Task<TrackCleanupAudioInspection> BuildTrackCleanupAudioInspectionAsync(
            AppSettings settings,
            WorkflowInstance workflow,
            CancellationToken cancellationToken)
        {
            var cleanupInputPath = ResolveTrackCleanupInputPath(workflow);
            var targetContext = ResolveTrackCleanupTargetContext(workflow, cleanupInputPath);
            if (!targetContext.CanManuallySelectAudio || string.IsNullOrWhiteSpace(targetContext.TargetVideoPath))
            {
                return new TrackCleanupAudioInspection
                {
                    CanManuallySelectAudio = false,
                    Message = targetContext.Message,
                    TargetVideoPath = targetContext.TargetVideoPath ?? string.Empty,
                };
            }

            var trackOptions = await GetTrackCleanupOptionsAsync(targetContext.TargetVideoPath, settings, cancellationToken);
            if (trackOptions is null)
            {
                return new TrackCleanupAudioInspection
                {
                    CanManuallySelectAudio = false,
                    Message = "No se pudieron inspeccionar los tracks del video objetivo. El paso seguirá en modo seguro sin borrar originales.",
                    TargetVideoPath = targetContext.TargetVideoPath,
                };
            }

            if (trackOptions.AudioOptions.Count == 0)
            {
                return new TrackCleanupAudioInspection
                {
                    CanManuallySelectAudio = false,
                    Message = "No se detectaron pistas de audio en el video objetivo.",
                    TargetVideoPath = targetContext.TargetVideoPath,
                };
            }

            var preferredLanguages = BuildTrackCleanupPreferredLanguages(trackOptions.AudioOptions, trackOptions.SubtitleOptions);
            var mergedAudioOptions = MergeTrackCleanupAudioOptions(workflow, targetContext.TargetVideoPath, trackOptions.AudioOptions, preferredLanguages);
            var mergedSubtitleOptions = MergeTrackCleanupSubtitleOptions(workflow, targetContext.TargetVideoPath, trackOptions.SubtitleOptions, preferredLanguages);
            var specialCaseReport = await BuildTrackCleanupSpecialCaseReportAsync(
                cleanupInputPath,
                targetContext.TargetVideoPath,
                trackOptions,
                settings,
                cancellationToken);
            return new TrackCleanupAudioInspection
            {
                CanManuallySelectAudio = true,
                Message = targetContext.Message,
                TargetVideoPath = targetContext.TargetVideoPath,
                SpecialCasesMessage = specialCaseReport.Message,
                AudioOptions = mergedAudioOptions,
                SubtitleOptions = mergedSubtitleOptions,
                SpecialCases = specialCaseReport.Items,
            };
        }

        private async Task<TrackCleanupInspectedTracks?> GetTrackCleanupOptionsAsync(string videoPath, AppSettings settings, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath) || !File.Exists(settings.MkvmergePath))
            {
                return null;
            }

            var result = await processRunnerService.RunAsync(
                new ProcessExecutionRequest
                {
                    FileName = settings.MkvmergePath,
                    Arguments = new[] { "-J", videoPath },
                    WorkingDirectory = Path.GetDirectoryName(videoPath) ?? Environment.CurrentDirectory,
                },
                null,
                cancellationToken);

            if (!result.Success)
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(result.StandardOutput);
                if (!doc.RootElement.TryGetProperty("tracks", out var tracks))
                {
                    return null;
                }

                var audioOptions = new List<TrackCleanupAudioOption>();
                var subtitleOptions = new List<TrackCleanupSubtitleOption>();

                foreach (var track in tracks.EnumerateArray())
                {
                    if (!track.TryGetProperty("type", out var typeElement))
                    {
                        continue;
                    }

                    var trackType = typeElement.GetString();
                    if (string.Equals(trackType, "audio", StringComparison.OrdinalIgnoreCase))
                    {
                        audioOptions.Add(BuildTrackCleanupAudioOption(track));
                    }
                    else if (string.Equals(trackType, "subtitles", StringComparison.OrdinalIgnoreCase))
                    {
                        subtitleOptions.Add(BuildTrackCleanupSubtitleOption(track));
                    }
                }

                return new TrackCleanupInspectedTracks(audioOptions, subtitleOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static TrackCleanupAudioOption BuildTrackCleanupAudioOption(JsonElement track)
        {
            var properties = track.TryGetProperty("properties", out var propertiesElement) && propertiesElement.ValueKind == JsonValueKind.Object
                ? propertiesElement
                : default;

            var languageRaw = TryGetJsonString(properties, "language");
            var languageIetf = TryGetJsonString(properties, "language_ietf") ?? TryGetJsonString(properties, "language-ietf");
            var normalizedLanguage = NormalizeTrackLanguage(languageRaw, languageIetf);
            var codec = TryGetJsonString(track, "codec");
            var name = TryGetJsonString(properties, "track_name");

            return new TrackCleanupAudioOption
            {
                TrackId = TryGetJsonString(track, "id") ?? string.Empty,
                LanguageCode = TrackLanguageCatalog.GetLookupCode(languageRaw, languageIetf),
                LanguageLabel = normalizedLanguage,
                Codec = string.IsNullOrWhiteSpace(codec)
                    ? string.Empty
                    : codec.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? codec,
                Name = name ?? string.Empty,
                IsDefault = TryGetJsonBoolean(properties, "default_track"),
                IsSelected = true,
            };
        }

        private static TrackCleanupSubtitleOption BuildTrackCleanupSubtitleOption(JsonElement track)
        {
            var properties = track.TryGetProperty("properties", out var propertiesElement) && propertiesElement.ValueKind == JsonValueKind.Object
                ? propertiesElement
                : default;

            var languageRaw = TryGetJsonString(properties, "language");
            var languageIetf = TryGetJsonString(properties, "language_ietf") ?? TryGetJsonString(properties, "language-ietf");
            var normalizedLanguage = NormalizeTrackLanguage(languageRaw, languageIetf);
            var name = TryGetJsonString(properties, "track_name");

            return new TrackCleanupSubtitleOption
            {
                TrackId = TryGetJsonString(track, "id") ?? string.Empty,
                LanguageCode = TrackLanguageCatalog.GetLookupCode(languageRaw, languageIetf),
                LanguageLabel = normalizedLanguage,
                Name = name ?? string.Empty,
                IsDefault = TryGetJsonBoolean(properties, "default_track"),
                IsForced = TryGetJsonBoolean(properties, "forced_track"),
                IsSelected = true,
            };
        }

        private static string NormalizeTrackLanguage(string? languageRaw, string? languageIetf) =>
            TrackLanguageCatalog.GetDisplayName(languageRaw, languageIetf);

        private static string NormalizeTrackLanguageCode(string? languageCode) =>
            TrackLanguageCatalog.GetCanonicalBaseCode(languageCode);

        private static string? TryGetJsonString(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!element.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            };
        }

        private static bool TryGetJsonBoolean(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!element.TryGetProperty(propertyName, out var value))
            {
                return false;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => value.TryGetInt32(out var number) && number != 0,
                _ => false,
            };
        }

        private async Task<TrackCleanupSpecialCaseReport> BuildTrackCleanupSpecialCaseReportAsync(
            string cleanupInputPath,
            string representativeVideoPath,
            TrackCleanupInspectedTracks representativeTracks,
            AppSettings settings,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(cleanupInputPath) || !Directory.Exists(cleanupInputPath))
            {
                return TrackCleanupSpecialCaseReport.Empty;
            }

            var representativeAudioFingerprint = CreateTrackLayoutFingerprint(representativeTracks.AudioOptions);
            var representativeSubtitleFingerprint = CreateTrackLayoutFingerprint(representativeTracks.SubtitleOptions);
            var representativeAudioSummary = BuildCompactTrackLayoutSummary(representativeTracks.AudioOptions);
            var representativeSubtitleSummary = BuildCompactTrackLayoutSummary(representativeTracks.SubtitleOptions);
            var items = new List<TrackCleanupSpecialCaseItem>();

            foreach (var videoPath in EnumerateTrackCleanupInspectionVideos(cleanupInputPath))
            {
                if (PathsEqual(videoPath, representativeVideoPath))
                {
                    continue;
                }

                var inspectedTracks = await GetTrackCleanupOptionsAsync(videoPath, settings, cancellationToken);
                if (inspectedTracks is null)
                {
                    items.Add(new TrackCleanupSpecialCaseItem
                    {
                        FileName = Path.GetFileName(videoPath),
                        Reason = "No se pudieron inspeccionar sus tracks para verificar compatibilidad batch.",
                    });
                    continue;
                }

                var reasons = new List<string>();
                var currentAudioFingerprint = CreateTrackLayoutFingerprint(inspectedTracks.AudioOptions);
                if (!string.Equals(representativeAudioFingerprint, currentAudioFingerprint, StringComparison.Ordinal))
                {
                    reasons.Add($"Audio base [{representativeAudioSummary}] -> [{BuildCompactTrackLayoutSummary(inspectedTracks.AudioOptions)}]");
                }

                var currentSubtitleFingerprint = CreateTrackLayoutFingerprint(inspectedTracks.SubtitleOptions);
                if (!string.Equals(representativeSubtitleFingerprint, currentSubtitleFingerprint, StringComparison.Ordinal))
                {
                    reasons.Add($"Subs base [{representativeSubtitleSummary}] -> [{BuildCompactTrackLayoutSummary(inspectedTracks.SubtitleOptions)}]");
                }

                if (reasons.Count == 0)
                {
                    continue;
                }

                items.Add(new TrackCleanupSpecialCaseItem
                {
                    FileName = Path.GetFileName(videoPath),
                    Reason = string.Join(" | ", reasons),
                });
            }

            if (items.Count == 0)
            {
                return TrackCleanupSpecialCaseReport.Empty;
            }

            var message = items.Count == 1
                ? "Caso especial detectado: 1 archivo del lote tiene un layout distinto. Limpiar tracks aplicará la selección manual por firma compatible y no solo por IDs."
                : $"Caso especial detectado: {items.Count} archivos del lote tienen layouts distintos. Limpiar tracks aplicará la selección manual por firma compatible y no solo por IDs.";
            return new TrackCleanupSpecialCaseReport(message, items);
        }

        private static IEnumerable<string> EnumerateTrackCleanupInspectionVideos(string cleanupInputPath)
        {
            if (string.IsNullOrWhiteSpace(cleanupInputPath) || !Directory.Exists(cleanupInputPath))
            {
                yield break;
            }

            foreach (var path in Directory.EnumerateFiles(cleanupInputPath, "*.*", SearchOption.AllDirectories)
                .Where(IsVideoFile)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (IsIgnoredTrackCleanupInspectionPath(path, cleanupInputPath))
                {
                    continue;
                }

                yield return path;
            }
        }

        private static bool IsIgnoredTrackCleanupInspectionPath(string candidatePath, string cleanupRootPath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(cleanupRootPath))
            {
                return false;
            }

            var relativePath = Path.GetRelativePath(cleanupRootPath, candidatePath);
            if (string.IsNullOrWhiteSpace(relativePath) || relativePath.StartsWith("..", StringComparison.Ordinal))
            {
                return false;
            }

            var fileName = Path.GetFileName(relativePath);
            if (!string.IsNullOrWhiteSpace(fileName)
                && fileName.Contains(" (filtered)", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
            return relativePath.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                .Any(part =>
                    part.Equals("filtrados", StringComparison.OrdinalIgnoreCase)
                    || part.Equals("ORIGINAL", StringComparison.OrdinalIgnoreCase)
                    || part.Equals("capturas", StringComparison.OrdinalIgnoreCase)
                    || part.Equals("RARs", StringComparison.OrdinalIgnoreCase));
        }

        private static string CreateTrackLayoutFingerprint(IReadOnlyList<TrackCleanupAudioOption> options) =>
            string.Join("||", options.Select(static option => CreateTrackLayoutToken(option)));

        private static string CreateTrackLayoutFingerprint(IReadOnlyList<TrackCleanupSubtitleOption> options) =>
            string.Join("||", options.Select(static option => CreateTrackLayoutToken(option)));

        private static string CreateTrackLayoutToken(TrackCleanupAudioOption option)
        {
            var name = NormalizeTrackSelectionName(option.Name);
            return $"{NormalizeTrackSelectionLanguageCode(option.LanguageCode)}|{(option.IsDefault ? 'd' : 'n')}|{name}";
        }

        private static string CreateTrackLayoutToken(TrackCleanupSubtitleOption option)
        {
            var name = NormalizeTrackSelectionName(option.Name);
            return $"{NormalizeTrackSelectionLanguageCode(option.LanguageCode)}|{(option.IsForced ? 'f' : 'n')}|{(option.IsDefault ? 'd' : 'n')}|{name}";
        }

        private static string BuildCompactTrackLayoutSummary(IReadOnlyList<TrackCleanupAudioOption> options) =>
            BuildCompactTrackLayoutSummaryCore(options.Select(static option =>
            {
                var token = NormalizeTrackSelectionLanguageCode(option.LanguageCode);
                return option.IsDefault ? $"{token}*" : token;
            }));

        private static string BuildCompactTrackLayoutSummary(IReadOnlyList<TrackCleanupSubtitleOption> options) =>
            BuildCompactTrackLayoutSummaryCore(options.Select(static option =>
            {
                var token = NormalizeTrackSelectionLanguageCode(option.LanguageCode);
                if (option.IsForced)
                {
                    token += " forced";
                }

                if (option.IsDefault)
                {
                    token += "*";
                }

                return token;
            }));

        private static string BuildCompactTrackLayoutSummaryCore(IEnumerable<string> values)
        {
            var items = values
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .ToList();
            if (items.Count == 0)
            {
                return "sin pistas";
            }

            const int maxItems = 7;
            var visible = items.Take(maxItems).ToList();
            var summary = string.Join(", ", visible);
            if (items.Count > maxItems)
            {
                summary += $", +{items.Count - maxItems} más";
            }

            return summary;
        }

        private static IEnumerable<TrackSelectionSignature> BuildTrackSelectionSignatures(IReadOnlyList<TrackCleanupAudioOption> options)
        {
            var order = 0;
            foreach (var option in options.Where(static option => option.IsSelected))
            {
                var signature = BuildTrackSelectionSignature(option, order++);
                if (signature is not null)
                {
                    yield return signature;
                }
            }
        }

        private static IEnumerable<TrackSelectionSignature> BuildTrackSelectionSignatures(IReadOnlyList<TrackCleanupSubtitleOption> options)
        {
            var order = 0;
            foreach (var option in options.Where(static option => option.IsSelected))
            {
                var signature = BuildTrackSelectionSignature(option, order++);
                if (signature is not null)
                {
                    yield return signature;
                }
            }
        }

        private static TrackSelectionSignature? BuildTrackSelectionSignature(TrackCleanupAudioOption? option, int order)
        {
            if (option is null)
            {
                return null;
            }

            return new TrackSelectionSignature(
                option.TrackId,
                NormalizeTrackSelectionLanguageCode(option.LanguageCode),
                NormalizeTrackLanguageCode(option.LanguageCode),
                option.Name ?? string.Empty,
                option.IsDefault,
                false,
                order);
        }

        private static TrackSelectionSignature? BuildTrackSelectionSignature(TrackCleanupSubtitleOption? option, int order)
        {
            if (option is null)
            {
                return null;
            }

            return new TrackSelectionSignature(
                option.TrackId,
                NormalizeTrackSelectionLanguageCode(option.LanguageCode),
                NormalizeTrackLanguageCode(option.LanguageCode),
                option.Name ?? string.Empty,
                option.IsDefault,
                option.IsForced,
                order);
        }

        private static string? EncodeTrackSelectionSignatures(IEnumerable<TrackSelectionSignature> signatures)
        {
            var payload = signatures.ToList();
            if (payload.Count == 0)
            {
                return null;
            }

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
        }

        private static string? EncodeTrackSelectionSignature(TrackSelectionSignature? signature)
        {
            if (signature is null)
            {
                return null;
            }

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(signature)));
        }

        private static string NormalizeTrackSelectionLanguageCode(string? languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
            {
                return "und";
            }

            return languageCode.Trim().Replace('_', '-').ToLowerInvariant();
        }

        private static string NormalizeTrackSelectionName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var normalized = name.Trim().ToLowerInvariant();
            while (normalized.EndsWith("]", StringComparison.Ordinal))
            {
                var openingBracket = normalized.LastIndexOf('[');
                if (openingBracket < 0)
                {
                    break;
                }

                normalized = normalized[..openingBracket].TrimEnd();
            }

            return Regex.Replace(normalized, "\\s+", " ").Trim();
        }

        private static HashSet<string> BuildTrackCleanupPreferredLanguages(
            IReadOnlyList<TrackCleanupAudioOption> audioOptions,
            IReadOnlyList<TrackCleanupSubtitleOption> subtitleOptions)
        {
            var preferredLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                NormalizeTrackLanguageCode("es"),
                NormalizeTrackLanguageCode("es-419"),
                NormalizeTrackLanguageCode("es-es"),
                NormalizeTrackLanguageCode("en"),
            };

            foreach (var option in audioOptions.Where(option => option.IsDefault))
            {
                var normalized = NormalizeTrackLanguageCode(option.LanguageCode);
                if (!string.Equals(normalized, "und", StringComparison.OrdinalIgnoreCase))
                {
                    preferredLanguages.Add(normalized);
                }
            }

            foreach (var option in subtitleOptions.Where(option => option.IsDefault))
            {
                var normalized = NormalizeTrackLanguageCode(option.LanguageCode);
                if (!string.Equals(normalized, "und", StringComparison.OrdinalIgnoreCase))
                {
                    preferredLanguages.Add(normalized);
                }
            }

            return preferredLanguages;
        }

        private static List<TrackCleanupAudioOption> MergeTrackCleanupAudioOptions(
            WorkflowInstance workflow,
            string targetVideoPath,
            IReadOnlyList<TrackCleanupAudioOption> inspectedOptions,
            ISet<string> preferredLanguages)
        {
            var hasMatchingSelection = string.Equals(
                workflow.TrackCleanupSelectionVideoPath,
                targetVideoPath,
                StringComparison.OrdinalIgnoreCase);

            var selectedMap = hasMatchingSelection
                ? workflow.TrackCleanupAudioOptions.ToDictionary(option => option.TrackId, option => option.IsSelected, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var primaryTrackId = hasMatchingSelection
                ? workflow.TrackCleanupAudioOptions.FirstOrDefault(option => option.IsPrimary)?.TrackId
                : null;

            foreach (var option in inspectedOptions)
            {
                if (selectedMap.TryGetValue(option.TrackId, out var isSelected))
                {
                    option.IsSelected = isSelected;
                }
                else
                {
                    option.IsSelected = option.IsDefault
                        || preferredLanguages.Contains(NormalizeTrackLanguageCode(option.LanguageCode));
                }
            }

            ApplyPrimaryAudioTrack(inspectedOptions, primaryTrackId);

            return inspectedOptions.ToList();
        }

        private static List<TrackCleanupSubtitleOption> MergeTrackCleanupSubtitleOptions(
            WorkflowInstance workflow,
            string targetVideoPath,
            IReadOnlyList<TrackCleanupSubtitleOption> inspectedOptions,
            ISet<string> preferredLanguages)
        {
            var hasMatchingSelection = string.Equals(
                workflow.TrackCleanupSelectionVideoPath,
                targetVideoPath,
                StringComparison.OrdinalIgnoreCase);

            var selectedMap = hasMatchingSelection
                ? workflow.TrackCleanupSubtitleOptions.ToDictionary(option => option.TrackId, option => option.IsSelected, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var primaryTrackId = hasMatchingSelection
                ? workflow.TrackCleanupSubtitleOptions.FirstOrDefault(option => option.IsPrimary)?.TrackId
                : null;

            foreach (var option in inspectedOptions)
            {
                if (selectedMap.TryGetValue(option.TrackId, out var isSelected))
                {
                    option.IsSelected = isSelected;
                }
                else
                {
                    option.IsSelected = option.IsDefault
                        || preferredLanguages.Contains(NormalizeTrackLanguageCode(option.LanguageCode));
                }
            }

            ApplyPrimarySubtitleTrack(inspectedOptions, primaryTrackId);

            return inspectedOptions.ToList();
        }

        private static void ApplyPrimaryAudioTrack(IReadOnlyList<TrackCleanupAudioOption> options, string? preferredTrackId)
        {
            TrackCleanupAudioOption? primaryOption = null;
            if (!string.IsNullOrWhiteSpace(preferredTrackId))
            {
                primaryOption = options.FirstOrDefault(option =>
                    option.IsSelected
                    && string.Equals(option.TrackId, preferredTrackId, StringComparison.OrdinalIgnoreCase));
            }

            primaryOption ??= options.FirstOrDefault(option => option.IsSelected && option.IsDefault);
            primaryOption ??= options.FirstOrDefault(option => option.IsSelected);

            foreach (var option in options)
            {
                option.IsPrimary = ReferenceEquals(option, primaryOption);
            }
        }

        private static void ApplyPrimarySubtitleTrack(IReadOnlyList<TrackCleanupSubtitleOption> options, string? preferredTrackId)
        {
            TrackCleanupSubtitleOption? primaryOption = null;
            if (!string.IsNullOrWhiteSpace(preferredTrackId))
            {
                primaryOption = options.FirstOrDefault(option =>
                    option.IsSelected
                    && string.Equals(option.TrackId, preferredTrackId, StringComparison.OrdinalIgnoreCase));
            }

            primaryOption ??= options.FirstOrDefault(option => option.IsSelected && option.IsDefault);
            primaryOption ??= options.FirstOrDefault(option => option.IsSelected);

            foreach (var option in options)
            {
                option.IsPrimary = ReferenceEquals(option, primaryOption);
            }
        }

        private static void ApplyTrackCleanupAudioInspection(WorkflowInstance workflow, TrackCleanupAudioInspection inspection)
        {
            workflow.TrackCleanupSelectionVideoPath = inspection.TargetVideoPath;
            workflow.TrackCleanupAudioOptions = inspection.CanManuallySelectAudio
                ? inspection.AudioOptions.ToList()
                : new List<TrackCleanupAudioOption>();
            workflow.TrackCleanupSubtitleOptions = inspection.CanManuallySelectAudio
                ? inspection.SubtitleOptions.ToList()
                : new List<TrackCleanupSubtitleOption>();
        }

        private static string ResolveTrackCleanupInputPath(WorkflowInstance workflow)
        {
            if (!string.IsNullOrWhiteSpace(workflow.RootPath) && Directory.Exists(workflow.RootPath))
            {
                var rootPath = Path.GetFullPath(workflow.RootPath);
                if (workflow.SourceSelectionIsFile is false)
                {
                    return rootPath;
                }

                if (workflow.SourceSelectionIsFile is null && CountVideoFiles(rootPath) > 1)
                {
                    return rootPath;
                }
            }

            if (!string.IsNullOrWhiteSpace(workflow.PrimaryVideoPath))
            {
                return workflow.PrimaryVideoPath;
            }

            return workflow.RootPath;
        }

        private static TrackCleanupTargetContext ResolveTrackCleanupTargetContext(WorkflowInstance workflow, string cleanupInputPath)
        {
            if (File.Exists(cleanupInputPath) && IsVideoFile(cleanupInputPath))
            {
                return new TrackCleanupTargetContext(cleanupInputPath, true, "Selecciona exactamente qué audios y subtítulos se conservarán.");
            }

            if (!string.IsNullOrWhiteSpace(workflow.PrimaryVideoPath)
                && File.Exists(workflow.PrimaryVideoPath)
                && string.Equals(cleanupInputPath, workflow.PrimaryVideoPath, StringComparison.OrdinalIgnoreCase))
            {
                return new TrackCleanupTargetContext(workflow.PrimaryVideoPath, true, "Selecciona exactamente qué audios y subtítulos se conservarán.");
            }

            if (Directory.Exists(cleanupInputPath))
            {
                var videos = EnumerateTrackCleanupInspectionVideos(cleanupInputPath)
                    .Take(2)
                    .ToList();

                if (videos.Count == 1)
                {
                    return new TrackCleanupTargetContext(videos[0], true, "Selecciona exactamente qué audios y subtítulos se conservarán.");
                }

                if (videos.Count > 1)
                {
                    var representativeVideoPath = ResolveTrackCleanupRepresentativeVideoPath(workflow, cleanupInputPath, videos[0]);
                    return new TrackCleanupTargetContext(
                        representativeVideoPath,
                        true,
                        $"Selecciona los audios y subtítulos desde '{Path.GetFileName(representativeVideoPath)}'. La selección se aplicará a todos los videos del release.");
                }
            }

            return new TrackCleanupTargetContext(
                null,
                false,
                "No se encontró un video único para inspeccionar audios y subtítulos antes de Limpiar tracks.");
        }

        private static string ResolveTrackCleanupRepresentativeVideoPath(WorkflowInstance workflow, string cleanupInputPath, string fallbackVideoPath)
        {
            if (!string.IsNullOrWhiteSpace(workflow.PrimaryVideoPath)
                && File.Exists(workflow.PrimaryVideoPath)
                && IsVideoFile(workflow.PrimaryVideoPath)
                && IsPathWithinDirectory(workflow.PrimaryVideoPath, cleanupInputPath)
                && !IsIgnoredTrackCleanupInspectionPath(workflow.PrimaryVideoPath, cleanupInputPath))
            {
                return workflow.PrimaryVideoPath;
            }

            if (!string.IsNullOrWhiteSpace(workflow.TrackCleanupSelectionVideoPath)
                && File.Exists(workflow.TrackCleanupSelectionVideoPath)
                && IsVideoFile(workflow.TrackCleanupSelectionVideoPath)
                && IsPathWithinDirectory(workflow.TrackCleanupSelectionVideoPath, cleanupInputPath)
                && !IsIgnoredTrackCleanupInspectionPath(workflow.TrackCleanupSelectionVideoPath, cleanupInputPath))
            {
                return workflow.TrackCleanupSelectionVideoPath;
            }

            return fallbackVideoPath;
        }

        private static bool IsPathWithinDirectory(string candidatePath, string directoryPath)
        {
            var fullCandidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullDirectory = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullCandidate.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullCandidate, fullDirectory, StringComparison.OrdinalIgnoreCase);
        }

        private static int CountVideoFiles(string directoryPath)
        {
            return Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly)
                .Count(IsVideoFile);
        }

        private static void PrepareStepForForcedExecution(WorkflowStepState step, WorkflowStepKey stepKey, Action<string>? onOutput)
        {
            if (step.Status == WorkflowStepStatus.Blocked)
            {
                onOutput?.Invoke($"Ejecucion manual forzada de {step.DisplayName}. Se omiten dependencias previas pendientes.");
            }
            else if (step.Status == WorkflowStepStatus.NeedsDecision)
            {
                onOutput?.Invoke($"Ejecucion manual forzada de {step.DisplayName}. Se usa la decision explicita del usuario.");
            }

            if (stepKey == WorkflowStepKey.TranslateSubs && step.Status == WorkflowStepStatus.NeedsDecision)
            {
                step.UserDecision = "translate";
            }

            step.Status = WorkflowStepStatus.Pending;
            step.StatusReason = "Paso lanzado manualmente desde la interfaz.";
            step.StartedAt = null;
            step.FinishedAt = null;
            step.ExitCode = null;
            step.StdoutLogPath = string.Empty;
            step.StderrLogPath = string.Empty;
        }

        private static string BuildFailureSummary(ProcessExecutionResult result)
        {
            var combined = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            var firstLine = combined.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return string.IsNullOrWhiteSpace(firstLine)
                ? $"El proceso terminó con código {result.ExitCode}."
                : firstLine;
        }

        private static string BuildSuccessSummary(WorkflowStepKey stepKey, ProcessExecutionResult result)
        {
            if (stepKey == WorkflowStepKey.TagAndRename)
            {
                if (ContainsRenamerLaunchMessage(result.StandardOutput) && TagAndRenameOutputHasWarnings(result.StandardOutput))
                {
                    return "Etiquetas completadas con advertencias; FileBot fue lanzado correctamente.";
                }

                if (ContainsRenamerLaunchMessage(result.StandardOutput))
                {
                    return "Etiquetas completadas y FileBot lanzado correctamente.";
                }
            }

            return "Paso completado correctamente.";
        }

        private static ProcessExecutionResult NormalizeProcessResult(WorkflowStepKey stepKey, ProcessExecutionResult result, Action<string>? onOutput)
        {
            if (stepKey != WorkflowStepKey.TagAndRename || result.Success)
            {
                return result;
            }

            if (!ContainsRenamerLaunchMessage(result.StandardOutput))
            {
                return result;
            }

            onOutput?.Invoke("El script de etiquetas reportó advertencias, pero FileBot fue lanzado correctamente. El paso se marcará como completado.");
            return new ProcessExecutionResult
            {
                ExitCode = result.ExitCode,
                StandardOutput = result.StandardOutput,
                StandardError = result.StandardError,
                StartedAt = result.StartedAt,
                FinishedAt = result.FinishedAt,
                CommandDisplay = result.CommandDisplay,
                Success = true,
            };
        }

        private static bool ContainsRenamerLaunchMessage(string output)
        {
            return output.Contains("Renombrar.lnk lanzado.", StringComparison.OrdinalIgnoreCase)
                || output.Contains("se lanzó FileBot", StringComparison.OrdinalIgnoreCase)
                || output.Contains("se lanzo FileBot", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TagAndRenameOutputHasWarnings(string output)
        {
            return output.Contains("Warning:", StringComparison.OrdinalIgnoreCase)
                || output.Contains("Advertencia:", StringComparison.OrdinalIgnoreCase)
                || output.Contains("MKV ERROR:", StringComparison.OrdinalIgnoreCase)
                || output.Contains("ERROR ", StringComparison.OrdinalIgnoreCase);
        }

        private IEnumerable<string> BuildRarPackagingArgs(AppSettings settings, WorkflowInstance workflow, string? inputBasePathOverride)
        {
            var inputBasePath = string.IsNullOrWhiteSpace(inputBasePathOverride) ? workflow.RootPath : inputBasePathOverride;
            var args = new List<string>
            {
                settings.RarPackagingScriptPath,
                inputBasePath,
                "--rar-path",
                settings.RarExePath,
            };

            if (settings.RarSkipImages)
            {
                args.Add("--skip-img");
            }

            if (settings.RarNoCompress)
            {
                args.Add("--no-compress");
            }
            else if (settings.RarUseCompressionNormal)
            {
                args.Add("--rar-compress");
            }
            else
            {
                args.Add("--rar-store-only");
            }

            if (int.TryParse(settings.RarCaptureCount, out var captures) && captures > 0)
            {
                args.Add("--num-capturas");
                args.Add(captures.ToString());
            }

            if (!string.IsNullOrWhiteSpace(settings.RarImageFormat))
            {
                args.Add("--img-format");
                args.Add(settings.RarImageFormat.Trim().ToLowerInvariant() == "png" ? "png" : "jpg");
            }

            if (settings.RarVerbose)
            {
                args.Add("--verbose");
            }

            if (!settings.RarNoCompress)
            {
                var password = secretProtector.Unprotect(settings.EncryptedRarPassword);
                if (!string.IsNullOrWhiteSpace(password))
                {
                    args.Add("--rar-password");
                    args.Add(password);
                }
            }

            return args;
        }

        private static async Task<string> PrepareRarPackagingInputAsync(
            WorkflowInstance workflow,
            CancellationToken cancellationToken,
            Action<string>? onOutput)
        {
            var targetDirectory = ResolveRarTargetDirectory(workflow);
            if (Directory.EnumerateDirectories(targetDirectory, "*", SearchOption.TopDirectoryOnly).Any())
            {
                onOutput?.Invoke($"Usando la carpeta base existente para empaquetado RAR: {targetDirectory}");
                return targetDirectory;
            }

            var directVideos = Directory.EnumerateFiles(targetDirectory, "*.*", SearchOption.TopDirectoryOnly)
                .Where(IsVideoFile)
                .ToList();
            if (directVideos.Count == 0)
            {
                onOutput?.Invoke($"La carpeta objetivo no tiene subcarpetas ni videos directos; se usará tal cual: {targetDirectory}");
                return targetDirectory;
            }

            AppDataPaths.EnsureAll();
            var wrapperRoot = Path.Combine(AppDataPaths.RootDirectory, "rar-input", workflow.Id);
            RecreateDirectory(wrapperRoot);
            var wrapperChildName = SanitizeDirectoryName(Path.GetFileName(targetDirectory));
            var junctionPath = Path.Combine(wrapperRoot, wrapperChildName);
            await CreateDirectoryJunctionAsync(junctionPath, targetDirectory, cancellationToken);
            onOutput?.Invoke($"La carpeta final contiene videos directos. Se creó un contenedor temporal para RAR: {wrapperRoot}");
            onOutput?.Invoke($"Subcarpeta enlazada para procesamiento: {junctionPath} -> {targetDirectory}");
            return wrapperRoot;
        }

        private static string ResolveRarTargetDirectory(WorkflowInstance workflow)
        {
            if (!string.IsNullOrWhiteSpace(workflow.RootPath) && Directory.Exists(workflow.RootPath))
            {
                return Path.GetFullPath(workflow.RootPath);
            }

            if (!string.IsNullOrWhiteSpace(workflow.PrimaryVideoPath) && File.Exists(workflow.PrimaryVideoPath))
            {
                return Path.GetDirectoryName(Path.GetFullPath(workflow.PrimaryVideoPath))
                    ?? throw new InvalidOperationException("No se pudo resolver la carpeta del archivo principal para empaquetar RAR.");
            }

            if (!string.IsNullOrWhiteSpace(workflow.RootPath) && File.Exists(workflow.RootPath))
            {
                return Path.GetDirectoryName(Path.GetFullPath(workflow.RootPath))
                    ?? throw new InvalidOperationException("No se pudo resolver la carpeta base para empaquetar RAR.");
            }

            throw new InvalidOperationException("No se encontró una carpeta válida para empaquetar RAR.");
        }

        private static void RecreateDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            Directory.CreateDirectory(path);
        }

        private static async Task CreateDirectoryJunctionAsync(string junctionPath, string targetPath, CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{junctionPath}\" \"{targetPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = new Process { StartInfo = psi };
            process.Start();
            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                throw new InvalidOperationException($"No se pudo crear el contenedor temporal para RAR: {detail.Trim()}");
            }
        }

        private static string SanitizeDirectoryName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(value
                .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
                .ToArray())
                .Trim();

            return string.IsNullOrWhiteSpace(sanitized) ? "release" : sanitized;
        }

        private static (string stdout, string stderr) CreateLogPaths(string workflowId, WorkflowStepKey stepKey)
        {
            AppDataPaths.EnsureAll();
            var directory = Path.Combine(AppDataPaths.LogsDirectory, workflowId);
            Directory.CreateDirectory(directory);
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
            return (
                Path.Combine(directory, $"{stamp}_{stepKey}_stdout.log"),
                Path.Combine(directory, $"{stamp}_{stepKey}_stderr.log"));
        }

        private static string NormalizeSecret(string command, string secret)
        {
            if (string.IsNullOrWhiteSpace(secret))
            {
                return command;
            }

            return command.Replace(secret, "[SECRET]", StringComparison.Ordinal);
        }

        private static ProcessExecutionResult ExtractStructuredOutputHints(
            WorkflowStepKey stepKey,
            WorkflowInstance workflow,
            WorkflowStepState step,
            ProcessExecutionResult result)
        {
            if (stepKey != WorkflowStepKey.PackageRar || string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                return result;
            }

            var visibleLines = new List<string>();
            var rawRows = new List<PackageRarStructuredRow>();
            string? weightSummary = null;

            using var reader = new StringReader(result.StandardOutput);
            while (reader.ReadLine() is { } line)
            {
                if (TryParsePackageRarRawDataLine(line, out var rawRow))
                {
                    rawRows.Add(rawRow);
                    continue;
                }

                if (TryParsePackageRarWeightSummaryLine(line, out var summary))
                {
                    weightSummary = summary;
                    continue;
                }

                visibleLines.Add(line);
            }

            if (rawRows.Count > 0)
            {
                step.OutputHints[PackageRarRawDataHintKey] = string.Join(
                    Environment.NewLine,
                    rawRows.Select(row => row.RawRow));

                var cleanNames = rawRows
                    .Select(row => row.CleanName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (cleanNames.Count > 0)
                {
                    step.OutputHints[PackageRarCleanNameHintKey] = string.Join(Environment.NewLine, cleanNames);
                }

                var shortName = BuildPackageRarShortName(rawRows, weightSummary, workflow);
                if (!string.IsNullOrWhiteSpace(shortName))
                {
                    step.OutputHints[PackageRarSeriesNameHintKey] = shortName;
                }
            }

            if (!string.IsNullOrWhiteSpace(weightSummary))
            {
                step.OutputHints[PackageRarWeightSummaryHintKey] = weightSummary;
            }

            return new ProcessExecutionResult
            {
                ExitCode = result.ExitCode,
                StandardOutput = string.Join(Environment.NewLine, visibleLines).Trim(),
                StandardError = result.StandardError,
                StartedAt = result.StartedAt,
                FinishedAt = result.FinishedAt,
                CommandDisplay = result.CommandDisplay,
                Success = result.Success,
            };
        }

        private static bool TryParsePackageRarRawDataLine(string line, out PackageRarStructuredRow rawRow)
        {
            rawRow = default;
            const string prefix = "MWO_RAW_DATA\t";
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            var parts = line.Split('\t');
            if (parts.Length < 7)
            {
                return false;
            }

            var originalName = parts[1].Trim();
            var resolution = parts[2].Trim();
            var weight = parts[3].Trim();
            var audio = parts[4].Trim();
            var subtitles = parts[5].Trim();
            rawRow = new PackageRarStructuredRow(
                originalName,
                string.Join('\t', parts.Skip(1).Take(5)),
                parts[6].Trim(),
                parts.Length >= 8 ? parts[7].Trim() : string.Empty,
                resolution,
                weight,
                audio,
                subtitles);
            return true;
        }

        private static bool TryParsePackageRarWeightSummaryLine(string line, out string summary)
        {
            summary = string.Empty;
            const string prefix = "MWO_WEIGHT_SUMMARY\t";
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            var parts = line.Split('\t');
            if (parts.Length < 3)
            {
                return false;
            }

            summary = $"{parts[1].Trim()} - Promedio {parts[2].Trim()}";
            return true;
        }

        private static string BuildPackageRarShortName(
            IReadOnlyList<PackageRarStructuredRow> rows,
            string? weightSummary,
            WorkflowInstance workflow)
        {
            if (rows.Count == 0)
            {
                return string.Empty;
            }

            if (rows.Any(row => GetEpisodeSortKey(row.OriginalName).Season != int.MaxValue))
            {
                return BuildPackageRarSeriesName(rows, weightSummary, workflow);
            }

            var movieNames = rows
                .Select(row => row.ShortName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return movieNames.Count > 0
                ? string.Join(Environment.NewLine, movieNames)
                : string.Empty;
        }

        private static string BuildPackageRarSeriesName(
            IReadOnlyList<PackageRarStructuredRow> rows,
            string? weightSummary,
            WorkflowInstance workflow)
        {
            if (rows.Count == 0)
            {
                return string.Empty;
            }

            var firstEpisode = rows
                .Select(row => new { Row = row, SortKey = GetEpisodeSortKey(row.OriginalName) })
                .OrderBy(item => item.SortKey.Season)
                .ThenBy(item => item.SortKey.Episode)
                .ThenBy(item => item.Row.OriginalName, StringComparer.OrdinalIgnoreCase)
                .Select(item => item.Row)
                .First();

            var seriesName = ResolvePackageRarSeriesName(firstEpisode, rows, workflow);
            if (string.IsNullOrWhiteSpace(seriesName))
            {
                return string.Empty;
            }

            var resolvedWeight = string.IsNullOrWhiteSpace(weightSummary)
                ? firstEpisode.Weight
                : weightSummary.Trim();

            return string.Join(
                '\t',
                new[]
                {
                    seriesName,
                    firstEpisode.Resolution,
                    resolvedWeight,
                    firstEpisode.Audio,
                    firstEpisode.Subtitles,
                });
        }

        private static string ResolvePackageRarSeriesName(
            PackageRarStructuredRow firstEpisode,
            IReadOnlyList<PackageRarStructuredRow> rows,
            WorkflowInstance workflow)
        {
            foreach (var candidate in new[]
                     {
                         BuildSeriesNameFromEpisode(firstEpisode.OriginalName),
                         BuildSeriesNameFromCleanNames(rows),
                         BuildSeriesNameFromDirectory(workflow.RootPath),
                         NormalizeSeriesCandidate(workflow.DisplayName),
                     })
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private static string BuildSeriesNameFromCleanNames(IReadOnlyList<PackageRarStructuredRow> rows)
        {
            return rows
                .Select(row => NormalizeSeriesCandidate(row.CleanName))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key.Length)
                .Select(group => group.Key)
                .FirstOrDefault() ?? string.Empty;
        }

        private static string BuildSeriesNameFromDirectory(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return string.Empty;
            }

            var directoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(rootPath));
            return NormalizeSeriesCandidate(directoryName);
        }

        private static string BuildSeriesNameFromEpisode(string originalName)
        {
            if (string.IsNullOrWhiteSpace(originalName))
            {
                return string.Empty;
            }

            var match = Regex.Match(
                originalName.Trim(),
                @"^(?<series>.+?)\s*-\s*S(?<season>\d{1,2})E\d{1,3}\s*-\s*.+?(?<metadata>\s*\([^()]+\))$",
                RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return string.Empty;
            }

            var series = match.Groups["series"].Value.Trim();
            var season = int.TryParse(match.Groups["season"].Value, out var parsedSeason) ? parsedSeason : 0;
            var metadata = match.Groups["metadata"].Value.Trim();
            if (string.IsNullOrWhiteSpace(series) || season <= 0 || string.IsNullOrWhiteSpace(metadata))
            {
                return string.Empty;
            }

            return $"{series} S{season:00} {metadata}";
        }

        private static string NormalizeSeriesCandidate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = Regex.Replace(value.Trim(), @"\s+", " ");
            return string.Equals(normalized, "Workflow nuevo", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : normalized;
        }

        private static (int Season, int Episode) GetEpisodeSortKey(string originalName)
        {
            if (string.IsNullOrWhiteSpace(originalName))
            {
                return (int.MaxValue, int.MaxValue);
            }

            var match = Regex.Match(originalName, @"\bS(?<season>\d{1,2})E(?<episode>\d{1,3})\b", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return (int.MaxValue, int.MaxValue);
            }

            return (
                int.TryParse(match.Groups["season"].Value, out var season) ? season : int.MaxValue,
                int.TryParse(match.Groups["episode"].Value, out var episode) ? episode : int.MaxValue);
        }

        private static async Task<(bool reachable, string message)> CheckOllamaReachabilityAsync(string host, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(host, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            {
                return (false, "El host de Ollama no tiene un formato válido.");
            }

            var port = uri.Port > 0 ? uri.Port : 11434;
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(uri.Host, port, cancellationToken);
                return (true, "Ollama disponible.");
            }
            catch (Exception)
            {
                return (false, $"No se pudo conectar a Ollama en {uri.Host}:{port}.");
            }
        }

        private static void UpdateWorkflowOutputs(WorkflowInstance workflow, WorkflowStepKey stepKey, ProcessExecutionResult result, Action<string>? onOutput)
        {
            if (stepKey != WorkflowStepKey.CleanTracks || !result.Success)
            {
                return;
            }

            var filteredFile = ResolveFilteredVideo(workflow);
            if (filteredFile is null)
            {
                return;
            }

            workflow.PrimaryVideoPath = filteredFile.FullName;
            workflow.RootPath = filteredFile.DirectoryName ?? workflow.RootPath;
            onOutput?.Invoke($"Archivo principal actualizado para siguientes pasos: {filteredFile.FullName}");
        }

        private static TagAndRenamePreparation PrepareTagAndRenameWorkspace(AppSettings settings, WorkflowInstance workflow, Action<string>? onOutput)
        {
            if (string.IsNullOrWhiteSpace(settings.TagAndRenameWorkingDirectory) || !Directory.Exists(settings.TagAndRenameWorkingDirectory))
            {
                throw new InvalidOperationException("La carpeta de trabajo de etiquetas y renombre no existe.");
            }

            var sourceVideo = ResolveFilteredVideo(workflow)
                ?? ResolvePrimaryVideo(workflow);
            if (sourceVideo is null)
            {
                throw new InvalidOperationException("No se encontró un archivo de video para preparar el paso de etiquetas y renombre.");
            }

            var workingDirectory = settings.TagAndRenameWorkingDirectory;
            var completadoDirectory = Path.Combine(workingDirectory, "Completado");
            Directory.CreateDirectory(completadoDirectory);
            Directory.CreateDirectory(Path.Combine(workingDirectory, "Subs"));
            Directory.CreateDirectory(Path.Combine(workingDirectory, "Audios"));
            Directory.CreateDirectory(Path.Combine(workingDirectory, "Videos"));
            Directory.CreateDirectory(Path.Combine(workingDirectory, "Originales"));

            var stagedVideoPath = StageVideoForTagAndRename(sourceVideo, completadoDirectory, workflow.Id, onOutput);
            onOutput?.Invoke($"Preparando archivo para etiquetas y renombre: {stagedVideoPath}");

            return new TagAndRenamePreparation(
                workingDirectory,
                Path.Combine(workingDirectory, "Renombrar.lnk"),
                sourceVideo.Extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) is false,
                stagedVideoPath);
        }

        private static string StageVideoForTagAndRename(FileInfo sourceVideo, string completadoDirectory, string workflowId, Action<string>? onOutput)
        {
            var preferredPath = Path.Combine(completadoDirectory, sourceVideo.Name);
            if (PathsEqual(sourceVideo.FullName, preferredPath))
            {
                onOutput?.Invoke($"El archivo ya está en la carpeta Completado y se reutilizará sin copiar: {preferredPath}");
                return preferredPath;
            }

            try
            {
                File.Copy(sourceVideo.FullName, preferredPath, overwrite: true);
                return preferredPath;
            }
            catch (IOException) when (File.Exists(preferredPath))
            {
                var alternatePath = BuildAlternateStagedPath(completadoDirectory, sourceVideo.Name, workflowId);
                onOutput?.Invoke($"El archivo ya preparado está en uso y no se puede sobrescribir: {preferredPath}");
                onOutput?.Invoke($"Se creará una copia alterna para continuar con el renombrado: {alternatePath}");
                File.Copy(sourceVideo.FullName, alternatePath, overwrite: false);
                return alternatePath;
            }
        }

        private static string BuildAlternateStagedPath(string completadoDirectory, string fileName, string workflowId)
        {
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var suffix = string.IsNullOrWhiteSpace(workflowId)
                ? DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss")
                : workflowId[..Math.Min(8, workflowId.Length)];

            var candidate = Path.Combine(completadoDirectory, $"{baseName}__{suffix}{extension}");
            var sequence = 1;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(completadoDirectory, $"{baseName}__{suffix}_{sequence}{extension}");
                sequence++;
            }

            return candidate;
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<ProcessExecutionResult> LaunchRenamerShortcutAsync(
            TagAndRenamePreparation preparation,
            CancellationToken cancellationToken,
            Action<string>? onOutput)
        {
            var startedAt = DateTimeOffset.UtcNow;
            var stdout = string.Empty;
            var stderr = string.Empty;

            if (!File.Exists(preparation.ShortcutPath))
            {
                stderr = $"No se encontró el acceso directo de renombrado: {preparation.ShortcutPath}";
                return new ProcessExecutionResult
                {
                    ExitCode = 1,
                    StandardOutput = stdout,
                    StandardError = stderr,
                    StartedAt = startedAt,
                    FinishedAt = DateTimeOffset.UtcNow,
                    CommandDisplay = preparation.ShortcutPath,
                    Success = false,
                };
            }

            cancellationToken.ThrowIfCancellationRequested();
            Process.Start(new ProcessStartInfo
            {
                FileName = preparation.ShortcutPath,
                UseShellExecute = true,
                WorkingDirectory = preparation.WorkingDirectory,
            });

            stdout = $"No hay MKV para etiquetar; se lanzó FileBot mediante {preparation.ShortcutPath}.";
            onOutput?.Invoke(stdout);

            return await Task.FromResult(new ProcessExecutionResult
            {
                ExitCode = 0,
                StandardOutput = stdout,
                StandardError = stderr,
                StartedAt = startedAt,
                FinishedAt = DateTimeOffset.UtcNow,
                CommandDisplay = preparation.ShortcutPath,
                Success = true,
            });
        }

        private static FileInfo? ResolvePrimaryVideo(WorkflowInstance workflow)
        {
            if (!string.IsNullOrWhiteSpace(workflow.PrimaryVideoPath) && File.Exists(workflow.PrimaryVideoPath))
            {
                return new FileInfo(workflow.PrimaryVideoPath);
            }

            foreach (var directory in EnumerateCandidateDirectories(workflow))
            {
                var candidate = Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(IsVideoFile)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .FirstOrDefault();
                if (candidate is not null)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static FileInfo? ResolveFilteredVideo(WorkflowInstance workflow)
        {
            foreach (var directory in EnumerateCandidateDirectories(workflow))
            {
                var candidate = Directory.EnumerateFiles(directory, "* (filtered).*", SearchOption.TopDirectoryOnly)
                    .Where(IsVideoFile)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .FirstOrDefault();
                if (candidate is not null)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool IsVideoFile(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> EnumerateCandidateDirectories(WorkflowInstance workflow)
        {
            var candidates = new[]
            {
                workflow.RootPath,
                Path.GetDirectoryName(workflow.PrimaryVideoPath),
            };

            return candidates
                .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)!;
        }

        private readonly record struct PackageRarStructuredRow(
            string OriginalName,
            string RawRow,
            string CleanName,
            string ShortName,
            string Resolution,
            string Weight,
            string Audio,
            string Subtitles);

        private readonly record struct TrackCleanupTargetContext(
            string? TargetVideoPath,
            bool CanManuallySelectAudio,
            string Message);

        private sealed record TrackCleanupSpecialCaseReport(
            string Message,
            List<TrackCleanupSpecialCaseItem> Items)
        {
            public static TrackCleanupSpecialCaseReport Empty { get; } = new(string.Empty, new List<TrackCleanupSpecialCaseItem>());
        }

        private sealed record TrackCleanupInspectedTracks(
            List<TrackCleanupAudioOption> AudioOptions,
            List<TrackCleanupSubtitleOption> SubtitleOptions);

        private sealed record TrackSelectionSignature(
            string TrackId,
            string LanguageCode,
            string CanonicalLanguageCode,
            string Name,
            bool IsDefault,
            bool IsForced,
            int Order);

        private sealed record TagAndRenamePreparation(string WorkingDirectory, string ShortcutPath, bool LaunchRenamerOnly, string StagedVideoPath);
    }
}

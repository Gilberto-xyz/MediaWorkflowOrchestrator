using System.Diagnostics;
using System.Net.Sockets;

namespace MediaWorkflowOrchestrator.Services
{
    public sealed class WorkflowExecutionService : IWorkflowExecutionService
    {
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

        public async Task<ExecutionRecord?> ExecuteStepAsync(WorkflowInstance workflow, WorkflowStepKey stepKey, Action<string>? onOutput, CancellationToken cancellationToken)
        {
            var settings = await appSettingsService.LoadAsync();
            var step = workflow.FindStep(stepKey);
            if (step is null)
            {
                return null;
            }

            if (step.Status is WorkflowStepStatus.Blocked or WorkflowStepStatus.NeedsDecision)
            {
                step.StatusReason = "Resuelve primero el estado del workflow antes de ejecutar este paso.";
                await workflowStore.SaveAsync(workflow);
                return null;
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

            var request = BuildRequest(settings, workflow, stepKey);
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
                    Arguments = BuildTrackCleanupArgs(settings, workflow).ToArray(),
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

        private static IEnumerable<string> BuildTrackCleanupArgs(AppSettings settings, WorkflowInstance workflow)
        {
            var args = new List<string>
            {
                settings.TrackCleanupScriptPath,
                string.IsNullOrWhiteSpace(workflow.PrimaryVideoPath) ? workflow.RootPath : workflow.PrimaryVideoPath,
                "--brand",
                settings.BrandName,
            };

            if (settings.TrackCleanupCloseQbittorrent)
            {
                args.Add("--file-in-use-action");
                args.Add("close-qbittorrent");
            }

            return args;
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

        private sealed record TagAndRenamePreparation(string WorkingDirectory, string ShortcutPath, bool LaunchRenamerOnly, string StagedVideoPath);
    }
}

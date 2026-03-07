using System.Text.Json;
using MediaWorkflowOrchestrator.Models;

namespace MediaWorkflowOrchestrator.Persistence
{
    public sealed class WorkflowStore : IWorkflowStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        public async Task SaveAsync(WorkflowInstance workflow)
        {
            AppDataPaths.EnsureAll();
            workflow.UpdatedAt = DateTimeOffset.UtcNow;
            var path = GetWorkflowPath(workflow.Id);
            var tempPath = $"{path}.tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, workflow, SerializerOptions);
            }

            File.Move(tempPath, path, overwrite: true);
        }

        public async Task<WorkflowInstance?> LoadAsync(string workflowId)
        {
            var path = GetWorkflowPath(workflowId);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var fileInfo = new FileInfo(path);
                if (fileInfo.Length == 0)
                {
                    DiagnosticsTrace.Write($"Workflow file is empty and will be ignored: {path}");
                    return null;
                }

                await using var stream = File.OpenRead(path);
                return await JsonSerializer.DeserializeAsync<WorkflowInstance>(stream, SerializerOptions);
            }
            catch (Exception ex)
            {
                DiagnosticsTrace.Write($"Workflow file could not be loaded and will be ignored: {path}. {ex}");
                return null;
            }
        }

        public async Task<IReadOnlyList<WorkflowInstance>> LoadAllAsync()
        {
            AppDataPaths.EnsureAll();
            var files = Directory.EnumerateFiles(AppDataPaths.WorkflowsDirectory, "*.json");
            var results = new List<WorkflowInstance>();

            foreach (var file in files)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.Length == 0)
                    {
                        DiagnosticsTrace.Write($"Skipping empty workflow file: {file}");
                        continue;
                    }

                    await using var stream = File.OpenRead(file);
                    var workflow = await JsonSerializer.DeserializeAsync<WorkflowInstance>(stream, SerializerOptions);
                    if (workflow is not null)
                    {
                        results.Add(workflow);
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticsTrace.Write($"Skipping unreadable workflow file: {file}. {ex}");
                }
            }

            return results
                .OrderByDescending(item => item.UpdatedAt)
                .ToList();
        }

        public async Task<WorkflowInstance?> LoadLatestAsync()
        {
            var all = await LoadAllAsync();
            return all.FirstOrDefault();
        }

        private static string GetWorkflowPath(string workflowId) =>
            Path.Combine(AppDataPaths.WorkflowsDirectory, $"{workflowId}.json");
    }
}

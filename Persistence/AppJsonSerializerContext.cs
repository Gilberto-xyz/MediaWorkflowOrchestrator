using System.Text.Json;
using System.Text.Json.Serialization;
using MediaWorkflowOrchestrator.Models;

namespace MediaWorkflowOrchestrator.Persistence
{
    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
    [JsonSerializable(typeof(AppSettings))]
    [JsonSerializable(typeof(WorkflowInstance))]
    [JsonSerializable(typeof(WorkflowStepState))]
    [JsonSerializable(typeof(TrackCleanupAudioOption))]
    [JsonSerializable(typeof(TrackCleanupSubtitleOption))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    [JsonSerializable(typeof(List<WorkflowStepState>))]
    [JsonSerializable(typeof(List<TrackCleanupAudioOption>))]
    [JsonSerializable(typeof(List<TrackCleanupSubtitleOption>))]
    internal sealed partial class AppJsonSerializerContext : JsonSerializerContext
    {
    }
}

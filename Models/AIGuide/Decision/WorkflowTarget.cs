namespace Netvium.Models.AIGuide.Decision
{
    /// <summary>Extracted target from user question (path, user, IP, etc.).</summary>
    public sealed class WorkflowTarget
    {
        public string Path { get; init; } = string.Empty;
        public string FolderName { get; init; } = string.Empty;
        public string UserName { get; init; } = string.Empty;
        public string PermissionLevel { get; init; } = string.Empty;
        public string Operation { get; init; } = string.Empty;
        public string? IpAddress { get; init; }
        public string MacAddress { get; init; } = string.Empty;
        public string RecordName { get; init; } = string.Empty;
        public string SiteName { get; init; } = string.Empty;
        public bool HasUserProvidedPath { get; init; }
        public bool TargetFromSelection { get; init; }
    }
}

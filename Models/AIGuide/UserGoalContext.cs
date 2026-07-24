namespace Netvium.Models.AIGuide
{
    /// <summary>
    /// Parsed user goal from a guidance question (path, operation, user, permission).
    /// User-provided path always takes priority over on-screen selection.
    /// </summary>
    public sealed class UserGoalContext
    {
        public string TargetPath { get; init; } = string.Empty;
        public string TargetFolderName { get; init; } = string.Empty;
        public string TargetUser { get; init; } = string.Empty;
        public string PermissionLevel { get; init; } = string.Empty;
        public string Operation { get; init; } = string.Empty;
        public bool HasUserProvidedPath { get; init; }
    }
}

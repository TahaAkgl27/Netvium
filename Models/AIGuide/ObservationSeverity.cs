namespace Netvium.Models.AIGuide
{
    public enum ObservationSeverity
    {
        Info = 0,
        Notice = 1,
        Warning = 2,
        Critical = 3
    }

    public enum ObservationCategory
    {
        Vision,
        Telemetry,
        Workflow,
        Role,
        Context,
        Security,
        Application
    }
}

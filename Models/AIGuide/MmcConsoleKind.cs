namespace Netvium.Models.AIGuide
{
    /// <summary>Known Microsoft MMC consoles detected from OCR/title patterns.</summary>
    public enum MmcConsoleKind
    {
        Unknown = 0,
        EventViewer,
        ActiveDirectoryUsersAndComputers,
        DnsManager,
        Dhcp,
        GroupPolicyManagement,
        ComputerManagement,
        Services,
        IisManager,
        HyperV,
        FailoverCluster,
        GenericMmc,
        TaskScheduler,
        RegistryEditor,
        ServerManager,
        FileExplorer,
        EndpointSecurity,
        NetworkSettings
    }
}

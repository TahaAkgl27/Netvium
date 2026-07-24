namespace Netvium.Rdp.Clean
{
    internal readonly record struct RdpActiveXCandidate<T>(
        RdpActiveXVersions.RdpActiveXVersion Version,
        Func<T> Create)
        where T : class;

    internal readonly record struct RdpActiveXSelection<T>(
        T Instance,
        RdpActiveXVersions.RdpActiveXVersion Version)
        where T : class;

    internal sealed class RdpActiveXUnavailableException : InvalidOperationException
    {
        internal const string SafeMessage =
            "Bu bilgisayarda desteklenen Uzak Masa\u00FCst\u00FC bile\u015Feni ba\u015Flat\u0131lamad\u0131. "
            + "Windows Uzak Masa\u00FCst\u00FC bile\u015Fenlerini denetleyip yeniden deneyin.";

        public RdpActiveXUnavailableException()
            : base(SafeMessage)
        {
        }
    }

    internal static class RdpActiveXSelector
    {
        public static RdpActiveXSelection<T> Select<T>(
            IReadOnlyList<RdpActiveXCandidate<T>> candidates,
            Action<T> initialize,
            Action<T> cleanup,
            Action<string> log)
            where T : class
        {
            ArgumentNullException.ThrowIfNull(candidates);
            ArgumentNullException.ThrowIfNull(initialize);
            ArgumentNullException.ThrowIfNull(cleanup);
            ArgumentNullException.ThrowIfNull(log);

            foreach (var candidate in candidates)
            {
                T? instance = null;
                log($"ActiveX attempt started version={candidate.Version.Label} clsid={candidate.Version.Clsid}");

                try
                {
                    instance = candidate.Create();
                    initialize(instance);
                    log($"ActiveX attempt succeeded version={candidate.Version.Label} clsid={candidate.Version.Clsid}");
                    return new RdpActiveXSelection<T>(instance, candidate.Version);
                }
                catch (Exception ex) when (IsRecoverable(ex))
                {
                    log(
                        $"ActiveX attempt failed version={candidate.Version.Label} clsid={candidate.Version.Clsid} "
                        + $"exception={ex.GetType().FullName ?? ex.GetType().Name} "
                        + $"hresult=0x{unchecked((uint)ex.HResult):X8}");

                    if (instance == null)
                        continue;

                    try
                    {
                        cleanup(instance);
                        log($"ActiveX attempt cleanup completed version={candidate.Version.Label}");
                    }
                    catch (Exception cleanupException) when (IsRecoverable(cleanupException))
                    {
                        log(
                            $"ActiveX attempt cleanup failed version={candidate.Version.Label} "
                            + $"exception={cleanupException.GetType().FullName ?? cleanupException.GetType().Name} "
                            + $"hresult=0x{unchecked((uint)cleanupException.HResult):X8}");
                    }
                }
            }

            throw new RdpActiveXUnavailableException();
        }

        private static bool IsRecoverable(Exception exception) =>
            exception is not OutOfMemoryException
            && exception is not AccessViolationException;
    }
}

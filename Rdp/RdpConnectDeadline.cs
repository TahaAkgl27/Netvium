namespace Netvium.Rdp

{

    public static class RdpConnectDeadline

    {

        /// <summary>Debug/repair sprint — extended until embedded connect is stable.</summary>

        public const int DefaultTimeoutSeconds = 120;



        public const int ConnectPhaseTimeoutSeconds = 120;



        public static DateTime CreateUtc(int timeoutSeconds = DefaultTimeoutSeconds) =>

            DateTime.UtcNow.AddSeconds(timeoutSeconds);



        public static bool IsExpired(DateTime deadlineUtc) =>

            DateTime.UtcNow >= deadlineUtc;



        public static double RemainingSeconds(DateTime deadlineUtc) =>

            Math.Max(0, (deadlineUtc - DateTime.UtcNow).TotalSeconds);



        public static int ElapsedMs(DateTime startedUtc) =>

            (int)Math.Max(0, (DateTime.UtcNow - startedUtc).TotalMilliseconds);

    }

}


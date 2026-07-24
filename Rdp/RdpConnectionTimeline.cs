using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Threading;
using Microsoft.Win32;
using Netvium.Helpers;

namespace Netvium.Rdp
{
    /// <summary>
    /// Collects a full per-attempt RDP connection timeline. Diagnostics only — no behavior changes.
    /// </summary>
    public sealed class RdpConnectionTimeline
    {
        public static readonly string[] TrackedOcxEvents =
        {
            "OnConnecting",
            "OnConnected",
            "OnLoginComplete",
            "OnDisconnected",
            "OnFatalError",
            "OnLogonError",
            "OnWarning",
            "OnAuthenticationWarningDisplayed",
            "OnConfirmClose",
            "OnRemoteDesktopSizeChange",
        };

        private readonly object _lock = new();
        private readonly List<string> _lines = new();
        private readonly HashSet<string> _firedOcxEvents = new(StringComparer.Ordinal);
        private readonly string _host;
        private readonly int _port;
        private readonly DateTime _startedUtc;
        private readonly DateTime _timeoutUtc;
        private bool _finished;

        public RdpConnectionTimeline(string host, int port, DateTime timeoutUtc)
        {
            _host = host;
            _port = port;
            _startedUtc = DateTime.UtcNow;
            _timeoutUtc = timeoutUtc;
        }

        public void RecordStartConnect(string? serverName = null, string? profileLabel = null)
        {
            RecordThreadContext("START CONNECT");
            Record(
                "START CONNECT",
                $"server={serverName ?? "—"} host={_host} port={_port} profile={profileLabel ?? "—"}");
            Record("Timeout timestamp (UTC)", _timeoutUtc.ToString("O"));
        }

        public void Record(string milestone, string? detail = null, Exception? ex = null)
        {
            var line = FormatLine(milestone, detail, ex);
            lock (_lock)
            {
                _lines.Add(line);
            }

            Debug.WriteLine($"[RDP-TIMELINE] {line}");
        }

        public void RecordOcxEvent(string eventName, string? detail = null)
        {
            lock (_lock)
            {
                _firedOcxEvents.Add(eventName);
            }

            Record(eventName, detail);
        }

        public void RecordOcxComCallback(string eventName, string? detail = null)
        {
            RecordThreadContext($"COM callback {eventName}");
            RecordOcxEvent($"{eventName} (COM callback)", detail);
        }

        public void RecordThreadContext(string label)
        {
            var apartment = Thread.CurrentThread.GetApartmentState();
            var threadId = Environment.CurrentManagedThreadId;
            var syncCtx = SynchronizationContext.Current?.GetType().FullName ?? "null";
            var dispatcher = DescribeDispatcherAccess();
            Record(
                label,
                $"managedThreadId={threadId} apartment={apartment} synchronizationContext={syncCtx} {dispatcher}");
        }

        public void RecordControlVerification(EmbeddedRdpClient client, Control? parent = null)
        {
            RecordThreadContext("Control verification");

            nint controlHwnd = nint.Zero;
            nint parentHwnd = nint.Zero;
            try
            {
                if (client.IsHandleCreated)
                    controlHwnd = client.Handle;
            }
            catch (Exception ex)
            {
                Record("Control handle read failed", null, ex);
            }

            try
            {
                if (parent != null && parent.IsHandleCreated)
                    parentHwnd = parent.Handle;
                else if (client.Parent != null && client.Parent.IsHandleCreated)
                    parentHwnd = client.Parent.Handle;
            }
            catch (Exception ex)
            {
                Record("Parent handle read failed", null, ex);
            }

            Record(
                "ActiveX handle exists",
                $"IsHandleCreated={client.IsHandleCreated} hwnd=0x{controlHwnd:X} IsDisposed={client.IsDisposed}");
            Record(
                "ActiveX Parent assigned",
                $"parent={(client.Parent?.GetType().Name ?? "null")} parentHwnd=0x{parentHwnd:X} parentAssigned={client.Parent != null}");
            Record(
                "Control Loaded",
                $"Visible={client.Visible} Created={client.IsHandleCreated} ClientSize={client.ClientSize.Width}x{client.ClientSize.Height}");
            Record("HWND created", $"controlHwnd=0x{controlHwnd:X} parentHwnd=0x{parentHwnd:X}");
            Record("Dispatcher access", DescribeDispatcherAccess());
            Record("STA thread", $"apartment={Thread.CurrentThread.GetApartmentState()} managedThreadId={Environment.CurrentManagedThreadId}");
        }

        public void RecordComRegistration(string clsid)
        {
            var registered = false;
            string? progid = null;
            try
            {
                using var key = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}");
                registered = key != null;
                progid = key?.GetValue(string.Empty) as string;
            }
            catch (Exception ex)
            {
                Record("COM registration check failed", $"clsid={clsid}", ex);
                return;
            }

            Record("COM registration", $"clsid={clsid} registered={registered} progid={progid ?? "—"}");
        }

        public void Finish(string outcome)
        {
            if (_finished)
                return;

            _finished = true;
            Flush(outcome);
        }

        private void Flush(string outcome)
        {
            var sb = new StringBuilder();
            sb.AppendLine("======= CONNECTION TIMELINE =======");
            sb.AppendLine($"START | host={_host} port={_port} startedUtc={_startedUtc:O}");
            sb.AppendLine($"Timeout timestamp (UTC): {_timeoutUtc:O}");

            lock (_lock)
            {
                foreach (var line in _lines)
                    sb.AppendLine(line);
            }

            lock (_lock)
            {
                foreach (var ev in TrackedOcxEvents)
                {
                    if (!_firedOcxEvents.Contains(ev))
                        sb.AppendLine($"Expected event missing:{Environment.NewLine}{ev}");
                }
            }

            sb.AppendLine($"END | outcome={outcome} finishedUtc={DateTime.UtcNow:O} elapsedMs={(DateTime.UtcNow - _startedUtc).TotalMilliseconds:F0}");
            sb.AppendLine("===================================");

            var report = sb.ToString();
            Debug.WriteLine(report);
            AppLogger.Debug(report, category: "RDP", action: "ConnectionTimeline");

            var captured = report;
            _ = Task.Run(() =>
            {
                try
                {
                    var dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Netvium");
                    Directory.CreateDirectory(dir);
                    var path = Path.Combine(dir, "rdp-timeline.log");
                    File.AppendAllText(path, captured + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Failed to write RDP connection timeline.", ex);
                }
            });
        }

        private string FormatLine(string milestone, string? detail, Exception? ex)
        {
            var ts = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var sb = new StringBuilder();
            sb.Append('[').Append(ts).Append(" UTC] ");
            sb.Append(milestone);
            if (!string.IsNullOrWhiteSpace(detail))
                sb.Append(" | ").Append(detail);

            if (ex != null)
                sb.Append(" | ").Append(DescribeException(ex));

            return sb.ToString();
        }

        internal static string DescribeException(Exception ex)
        {
            if (ex is TargetInvocationException tie && tie.InnerException != null)
                ex = tie.InnerException;

            var sb = new StringBuilder();
            sb.Append("exception=").Append(ex.GetType().FullName);
            sb.Append(" message=").Append(ex.Message);

            if (ex is COMException com)
                sb.Append($" HRESULT=0x{com.ErrorCode:X8} ({com.ErrorCode})");

            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
            {
                sb.Append(" stackTrace=");
                sb.Append(ex.StackTrace.Replace('\r', ' ').Replace('\n', ' '));
            }

            if (ex.InnerException != null)
                sb.Append(" inner=").Append(DescribeException(ex.InnerException));

            return sb.ToString();
        }

        private static string DescribeDispatcherAccess()
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                return dispatcher != null
                    ? $"dispatcherThreadId={dispatcher.Thread.ManagedThreadId} checkAccess={dispatcher.CheckAccess()}"
                    : "dispatcher=null";
            }
            catch (Exception ex)
            {
                return $"dispatcher=unavailable ({ex.GetType().Name}: {ex.Message})";
            }
        }
    }
}

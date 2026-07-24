using System.IO;

using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Netvium.Rdp.Clean
{
    /// <summary>
    /// Ordered, bounded RDP diagnostic logger. Enqueue is non-blocking; a single
    /// background worker owns all file I/O so UI/RDP threads never write the file.
    /// </summary>
    public static class RdpCleanHostLogger
    {
        private const int DefaultMaxFileBytes = 5 * 1024 * 1024;
        private const int DefaultMaxRetainedFiles = 3;
        private const int DefaultMaxQueueLength = 4096;

        private static readonly object Gate = new();
        private static readonly AsyncLocal<string?> SessionContext = new();
        private static Queue<LogEntry> _queue = new();
        private static SemaphoreSlim _signal = new(0);
        private static Task? _worker;
        private static bool _stopping;
        private static bool _disposed;
        private static int _disposeStarted;
        private static string _logPath = GetDefaultLogPath();
        private static int _maxFileBytes = DefaultMaxFileBytes;
        private static int _maxRetainedFiles = DefaultMaxRetainedFiles;
        private static int _maxQueueLength = DefaultMaxQueueLength;
        private static long _droppedEntries;
        private static StreamWriter? _writer;
        private static string? _writerPath;
        private static long _writerLength;

        public static string LogFilePath
        {
            get
            {
                lock (Gate)
                    return _logPath;
            }
        }

        public static long DroppedEntryCount => Interlocked.Read(ref _droppedEntries);

        public static void Log(string message, string? sessionId = null)
        {
            var safeMessage = RdpLogRedactor.Redact(message ?? string.Empty);
            var entry = LogEntry.Create(
                DateTimeOffset.UtcNow,
                NormalizeSessionId(sessionId ?? SessionContext.Value),
                safeMessage);

            lock (Gate)
            {
                if (_stopping || _disposed)
                    return;

                if (_queue.Count >= _maxQueueLength)
                {
                    Interlocked.Increment(ref _droppedEntries);
                    return;
                }

                _queue.Enqueue(entry);
                EnsureWorkerLocked();
            }

            ReleaseWorker();
            Debug.WriteLine($"[RDP-CLEAN] [{entry.SessionId}] {safeMessage}");
        }

        public static void LogBlock(string title, IEnumerable<string> lines, string? sessionId = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"--- {title} ---");
            foreach (var line in lines)
                sb.AppendLine(line);

            Log(sb.ToString().TrimEnd(), sessionId);
        }

        /// <summary>
        /// Captures a session id for logs emitted by a host callback/action.
        /// The id is restored when the scope is disposed.
        /// </summary>
        internal static IDisposable PushSessionId(string? sessionId)
        {
            var previous = SessionContext.Value;
            SessionContext.Value = NormalizeSessionId(sessionId);
            return new SessionScope(previous);
        }

        public static async Task<bool> FlushAsync(TimeSpan timeout)
        {
            if (timeout < TimeSpan.Zero)
                timeout = TimeSpan.Zero;

            TaskCompletionSource<bool> completion;
            lock (Gate)
            {
                if (_disposed)
                    return true;

                if (_queue.Count == 0 && (_worker == null || _worker.IsCompleted))
                    return true;

                completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _queue.Enqueue(LogEntry.FlushMarker(completion));
                EnsureWorkerLocked();
            }

            ReleaseWorker();

            try
            {
                await completion.Task.WaitAsync(timeout).ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool Flush(TimeSpan timeout) =>
            FlushAsync(timeout).GetAwaiter().GetResult();

        /// <summary>
        /// Stops the worker after a bounded flush. Repeated calls are harmless.
        /// </summary>
        public static void Dispose(TimeSpan? timeout = null)
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
                return;

            var budget = timeout ?? TimeSpan.FromSeconds(2);
            if (budget < TimeSpan.Zero)
                budget = TimeSpan.Zero;
            var deadline = Stopwatch.GetTimestamp() +
                (long)(budget.TotalSeconds * Stopwatch.Frequency);

            Task? worker = null;
            try
            {
                TaskCompletionSource<bool>? completion = null;
                lock (Gate)
                {
                    if (_disposed)
                        return;

                    if (_queue.Count > 0 || (_worker != null && !_worker.IsCompleted))
                    {
                        completion = new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        _queue.Enqueue(LogEntry.FlushMarker(completion));
                        EnsureWorkerLocked();
                    }
                    worker = _worker;
                }

                ReleaseWorker();
                var remaining = Remaining(deadline);
                if (completion != null && remaining > TimeSpan.Zero)
                {
                    try { completion.Task.Wait(remaining); } catch { }
                }

                lock (Gate)
                    _stopping = true;
                ReleaseWorker();

                remaining = Remaining(deadline);
                if (worker != null && remaining > TimeSpan.Zero)
                {
                    try { worker.Wait(remaining); } catch { }
                }
            }
            catch
            {
                // Logger shutdown must never affect application shutdown.
            }
            finally
            {
                lock (Gate)
                {
                    CloseWriter();
                    _disposed = true;
                }
            }
        }

        private static TimeSpan Remaining(long deadline)
        {
            var ticks = deadline - Stopwatch.GetTimestamp();
            return ticks <= 0
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);
        }
        // Test-only configuration keeps production behavior unchanged while
        // allowing deterministic rotation/error/shutdown tests.
        internal static void ConfigureForTests(
            string logPath,
            int maxFileBytes = DefaultMaxFileBytes,
            int maxRetainedFiles = DefaultMaxRetainedFiles,
            int maxQueueLength = DefaultMaxQueueLength)
        {
            Dispose(TimeSpan.FromSeconds(2));

            lock (Gate)
            {
                _signal.Dispose();
                _signal = new SemaphoreSlim(0);
                _queue = new Queue<LogEntry>();
                _worker = null;
                _writer = null;
                _writerPath = null;
                _writerLength = 0;
                _stopping = false;
                _disposed = false;
                _disposeStarted = 0;
                _logPath = logPath;
                _maxFileBytes = Math.Max(1, maxFileBytes);
                _maxRetainedFiles = Math.Max(1, maxRetainedFiles);
                _maxQueueLength = Math.Max(1, maxQueueLength);
                Interlocked.Exchange(ref _droppedEntries, 0);
            }
        }

        internal static void ResetForTests() =>
            ConfigureForTests(GetDefaultLogPath());

        private static void EnsureWorkerLocked()
        {
            if (_worker == null || _worker.IsCompleted)
                _worker = Task.Run(WorkerAsync);
        }

        private static void ReleaseWorker()
        {
            try
            {
                _signal.Release();
            }
            catch
            {
                // Dispose/configuration races are intentionally benign.
            }
        }

        private static async Task WorkerAsync()
        {
            while (true)
            {
                try
                {
                    await _signal.WaitAsync().ConfigureAwait(false);
                }
                catch
                {
                    return;
                }

                while (true)
                {
                    LogEntry? entry;
                    lock (Gate)
                    {
                        if (_queue.Count == 0)
                        {
                            if (_stopping)
                                return;
                            break;
                        }

                        entry = _queue.Dequeue();
                    }

                    if (entry.IsFlushMarker)
                    {
                        CloseWriter();
                        entry.FlushCompletion!.TrySetResult(true);
                        continue;
                    }

                    try
                    {
                        WriteEntry(entry);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[RDP-CLEAN] log write failed: {ex.GetType().Name}");
                    }
                }
            }
        }

        private static void WriteEntry(LogEntry entry)
        {
            var line = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [session={entry.SessionId}] {entry.Message}";
            var payload = line + Environment.NewLine;
            var bytes = Encoding.UTF8.GetByteCount(payload);
            var path = LogFilePath;

            if (_writer == null || !string.Equals(_writerPath, path, StringComparison.OrdinalIgnoreCase))
            {
                CloseWriter();
                OpenWriter(path);
            }

            if (_writerLength > 0 && _writerLength + bytes > _maxFileBytes)
            {
                CloseWriter();
                RotateIfNeeded(path, bytes);
                OpenWriter(path);
            }

            _writer!.Write(payload);
            _writer.Flush();
            _writerLength += bytes;
        }

        private static void OpenWriter(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: 4096,
                options: FileOptions.SequentialScan);
            _writer = new StreamWriter(stream, new UTF8Encoding(false));
            _writerPath = path;
            _writerLength = stream.Length;
        }

        private static void CloseWriter()
        {
            try { _writer?.Dispose(); } catch { }
            _writer = null;
            _writerPath = null;
            _writerLength = 0;
        }
        private static void RotateIfNeeded(string path, int incomingBytes)
        {
            if (!File.Exists(path))
                return;

            long currentLength;
            try
            {
                currentLength = new FileInfo(path).Length;
            }
            catch
            {
                return;
            }

            if (currentLength == 0 || currentLength + incomingBytes <= _maxFileBytes)
                return;

            for (var index = _maxRetainedFiles - 1; index >= 1; index--)
            {
                var source = path + "." + index;
                var destination = path + "." + (index + 1);
                try
                {
                    if (File.Exists(source))
                        File.Move(source, destination, true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[RDP-CLEAN] log rotation failed: {ex.GetType().Name}");
                }
            }

            try
            {
                File.Move(path, path + ".1", true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RDP-CLEAN] log rotation failed: {ex.GetType().Name}");
            }
        }

        private static string NormalizeSessionId(string? sessionId) =>
            string.IsNullOrWhiteSpace(sessionId) ? "n/a" : sessionId.Trim();

        private static string GetDefaultLogPath() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Netvium",
                "rdp-cleanhost.log");

        private sealed class SessionScope : IDisposable
        {
            private readonly string? _previous;
            private bool _disposed;

            public SessionScope(string? previous) => _previous = previous;

            public void Dispose()
            {
                if (_disposed)
                    return;

                SessionContext.Value = _previous;
                _disposed = true;
            }
        }

        private sealed record LogEntry(
            DateTimeOffset Timestamp,
            string SessionId,
            string Message,
            TaskCompletionSource<bool>? FlushCompletion)
        {
            public bool IsFlushMarker => FlushCompletion != null;

            public static LogEntry Create(DateTimeOffset timestamp, string sessionId, string message) =>
                new(timestamp, sessionId, message, null);

            public static LogEntry FlushMarker(TaskCompletionSource<bool> completion) =>
                new(DateTimeOffset.UtcNow, "n/a", string.Empty, completion);
        }
    }


    public static class RdpLogRedactor
    {
        private const string SensitiveKeys =
            "password|passwd|pwd|pass|token|api[_-]?key|apikey|secret|access[_-]?token|refresh[_-]?token|username|user[ ]?name|user[ ]?id|uid|server|hostname|host|ip|address|connection[ ]?string|connstr|connection[ ]?info";

        private static readonly Regex SensitiveAssignment = new(
            $@"(?<key>\b(?:{SensitiveKeys})\b)\s*[:=]\s*(?<value>""[^""\r\n]*""|'[^'\r\n]*'|(?:(?!\s+\b(?:{SensitiveKeys})\b\s*[:=])[^,;|}}\r\n])*)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex DomainUser = new(
            @"(?<![\w.])[^\s\\,;|]+\\[^\s,;|]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex IpEndpoint = new(
            @"(?<![\w.])(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?(?![\w.])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string Redact(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return value ?? string.Empty;

            try
            {
                var result = SensitiveAssignment.Replace(value, match =>
                    $"{match.Groups["key"].Value}=[REDACTED]");
                result = DomainUser.Replace(result, "[REDACTED]");
                return IpEndpoint.Replace(result, "[REDACTED]");
            }
            catch
            {
                // A redaction failure must never write the original value.
                return "[REDACTED]";
            }
        }
    }
}

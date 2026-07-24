using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Markup;

namespace Netvium.Helpers
{
    internal enum GlobalExceptionKind
    {
        RecoverableUi,
        BackgroundTask,
        Critical
    }

    internal enum GlobalExceptionOrigin
    {
        Dispatcher,
        AppDomain,
        UnobservedTask,
        AsyncCommand,
        FireAndForget
    }

    internal sealed class ExceptionNotificationThrottle
    {
        private readonly TimeSpan _window;
        private readonly Dictionary<string, DateTime> _lastShown = new(StringComparer.Ordinal);
        private readonly object _gate = new();

        public ExceptionNotificationThrottle(TimeSpan window)
        {
            _window = window > TimeSpan.Zero
                ? window
                : throw new ArgumentOutOfRangeException(nameof(window));
        }

        public bool ShouldNotify(string fingerprint, DateTime utcNow)
        {
            lock (_gate)
            {
                if (_lastShown.TryGetValue(fingerprint, out var previous)
                    && utcNow - previous < _window)
                {
                    return false;
                }

                _lastShown[fingerprint] = utcNow;
                if (_lastShown.Count > 128)
                {
                    var cutoff = utcNow - _window;
                    foreach (var key in _lastShown.Where(pair => pair.Value < cutoff).Select(pair => pair.Key).ToArray())
                        _lastShown.Remove(key);
                }

                return true;
            }
        }
    }

    internal static class GlobalExceptionHandler
    {
        private static readonly ExceptionNotificationThrottle NotificationThrottle =
            new(TimeSpan.FromSeconds(30));

        public static bool Handle(
            Exception exception,
            GlobalExceptionOrigin origin,
            string context,
            bool notifyUser = false,
            string? userMessage = null,
            bool appDomainIsTerminating = false)
        {
            ArgumentNullException.ThrowIfNull(exception);
            context = string.IsNullOrWhiteSpace(context) ? "Unknown" : context;

            try
            {
                var kind = Classify(exception, origin, appDomainIsTerminating);
                var logMessage = $"Yakalanmam\u0131\u015f hata i\u015flendi. Kaynak={origin}; Ba\u011flam={context}; S\u0131n\u0131f={kind}.";

                if (kind == GlobalExceptionKind.Critical)
                    AppLogger.Critical(logMessage, exception, category: "Exception", action: context);
                else if (kind == GlobalExceptionKind.BackgroundTask)
                    AppLogger.Error(logMessage, exception, category: "Exception", action: context);
                else
                    AppLogger.Error(logMessage, exception, category: "Exception", action: context);

                if (notifyUser || origin is GlobalExceptionOrigin.Dispatcher or GlobalExceptionOrigin.AsyncCommand)
                    NotifyOnce(exception, kind, context, userMessage);

                return kind != GlobalExceptionKind.Critical;
            }
            catch (Exception handlerFailure)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Global exception handler failure: {handlerFailure.GetType().Name}");
                return false;
            }
        }

        public static void Observe(
            Task task,
            string context,
            bool notifyUser = false,
            string? userMessage = null)
        {
            ArgumentNullException.ThrowIfNull(task);
            _ = ObserveAsync(task, context, notifyUser, userMessage);
        }

        internal static async Task ObserveAsync(
            Task task,
            string context,
            bool notifyUser = false,
            string? userMessage = null,
            Action<Exception>? testReporter = null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is expected lifecycle control, not an application error.
            }
            catch (Exception ex)
            {
                if (testReporter != null)
                {
                    testReporter(ex);
                    return;
                }

                _ = Handle(
                    ex,
                    GlobalExceptionOrigin.FireAndForget,
                    context,
                    notifyUser,
                    userMessage);
            }
        }

        internal static GlobalExceptionKind Classify(
            Exception exception,
            GlobalExceptionOrigin origin,
            bool appDomainIsTerminating = false)
        {
            IEnumerable<Exception> exceptions = exception is AggregateException aggregate
                ? aggregate.Flatten().InnerExceptions
                : new[] { exception };

            if (appDomainIsTerminating || exceptions.Any(IsCritical))
                return GlobalExceptionKind.Critical;

            if (origin is GlobalExceptionOrigin.UnobservedTask
                or GlobalExceptionOrigin.FireAndForget
                or GlobalExceptionOrigin.AppDomain)
            {
                return GlobalExceptionKind.BackgroundTask;
            }

            return exceptions.All(IsKnownRecoverableUi)
                ? GlobalExceptionKind.RecoverableUi
                : GlobalExceptionKind.Critical;
        }

        private static bool IsCritical(Exception exception) =>
            exception is OutOfMemoryException
                or StackOverflowException
                or AccessViolationException
                or BadImageFormatException
                or InvalidProgramException
                or TypeInitializationException
                or ReflectionTypeLoadException
                or XamlParseException
                or SEHException;

        private static bool IsKnownRecoverableUi(Exception exception) =>
            exception is ArgumentException
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException
                or TimeoutException
                or HttpRequestException
                or JsonException
                or COMException
                or Win32Exception
                or NotSupportedException;

        private static void NotifyOnce(
            Exception exception,
            GlobalExceptionKind kind,
            string context,
            string? userMessage)
        {
            var safeExceptionMessage =
                Logging.SensitiveDataRedactor.Redact(exception.Message);
            var fingerprint = $"{exception.GetType().FullName}|{safeExceptionMessage}|{kind}";
            if (!NotificationThrottle.ShouldNotify(fingerprint, DateTime.UtcNow))
                return;

            var message = !string.IsNullOrWhiteSpace(userMessage)
                ? userMessage
                : kind == GlobalExceptionKind.Critical
                    ? "Uygulamada kritik bir hata olu\u015ftu. Ayr\u0131nt\u0131lar g\u00fcvenli bi\u00e7imde logland\u0131; uygulaman\u0131n kapat\u0131lmas\u0131 gerekebilir."
                    : "\u0130\u015flem tamamlanamad\u0131. Ayr\u0131nt\u0131lar g\u00fcvenli bi\u00e7imde logland\u0131; l\u00fctfen yeniden deneyin.";
            var title = kind == GlobalExceptionKind.Critical ? "Kritik Uygulama Hatas\u0131" : "\u0130\u015flem Hatas\u0131";
            var image = kind == GlobalExceptionKind.Critical ? MessageBoxImage.Error : MessageBoxImage.Warning;

            void Show() => System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, image);

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;

            if (dispatcher.CheckAccess())
                Show();
            else
                _ = dispatcher.BeginInvoke((Action)Show);
        }
    }
}

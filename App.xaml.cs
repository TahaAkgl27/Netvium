using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Netvium.Helpers;
using Netvium.Helpers.Logging;
using Netvium.Services;
using Netvium.Rdp.Clean;

namespace Netvium
{
    public partial class App : System.Windows.Application
    {
        private int _globalHandlersRegistered;
        private int _applicationShutdownRequested;

        protected override async void OnStartup(StartupEventArgs e)
        {
            RegisterGlobalExceptionHandlers();
            base.OnStartup(e);
            LoggingContext.NewCorrelationId();
            SecurePowerShellLauncher.CleanupPendingScriptsOnStartup();
            AuditLogService.PurgeExpiredLogs();
            CacheManager.PurgeExpired();

            await SettingsService.Instance.LoadAsync();
            SettingsRuntime.SyncFromService();
            UiLocalizationService.ApplyCulture(SettingsService.Instance.Current);
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded));
            AppLogger.Configure(SettingsService.Instance.Current);
            StartupHelper.SetEnabled(SettingsService.Instance.Current.StartWithWindows);
            await StartAccountFlowAsync();
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window && ReferenceEquals(e.OriginalSource, window))
                UiLocalizationService.Apply(window, SettingsService.Instance.Current);
        }

        public void RequestApplicationShutdown(int exitCode = 0)
        {
            if (Interlocked.Exchange(ref _applicationShutdownRequested, 1) != 0)
                return;

            try
            {
                if (Dispatcher.CheckAccess())
                    Shutdown(exitCode);
                else
                    _ = Dispatcher.BeginInvoke(new Action(() => Shutdown(exitCode)));
            }
            catch
            {
                // The dispatcher may already be closing; OnExit remains the final cleanup boundary.
            }
        }
        private void RegisterGlobalExceptionHandlers()
        {
            if (Interlocked.Exchange(ref _globalHandlersRegistered, 1) != 0)
                return;

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private static void OnDispatcherUnhandledException(
            object sender,
            DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = GlobalExceptionHandler.Handle(
                e.Exception,
                GlobalExceptionOrigin.Dispatcher,
                "WPF.Dispatcher",
                notifyUser: true);
        }

        private static void OnCurrentDomainUnhandledException(
            object? sender,
            UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception
                ?? new Exception(
                    $"Exception olmayan yakalanmam\u0131\u015f nesne: {e.ExceptionObject?.GetType().FullName ?? "null"}");

            _ = GlobalExceptionHandler.Handle(
                exception,
                GlobalExceptionOrigin.AppDomain,
                "AppDomain",
                notifyUser: false,
                appDomainIsTerminating: e.IsTerminating);
        }

        private static void OnUnobservedTaskException(
            object? sender,
            UnobservedTaskExceptionEventArgs e)
        {
            var canContinue = GlobalExceptionHandler.Handle(
                e.Exception,
                GlobalExceptionOrigin.UnobservedTask,
                "TaskScheduler",
                notifyUser: false);

            if (canContinue)
                e.SetObserved();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                RdpCleanHostLogger.Dispose(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                _ = GlobalExceptionHandler.Handle(
                    ex,
                    GlobalExceptionOrigin.FireAndForget,
                    "App.ExitLoggerDispose",
                    notifyUser: false);
            }

            base.OnExit(e);
        }
    }
}

using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

using FileAuditor.Core.Services;
using FileAuditor.WPF.ViewModels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Serilog;

namespace FileAuditor.WPF
{
    public partial class App : System.Windows.Application
    {
        private ServiceProvider? _serviceProvider;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ── Global exception safety nets ─────────────────────────────────────
            // These three handlers ensure that no unhandled exception ever produces
            // a silent crash. Together they cover every execution context:
            //
            //   DispatcherUnhandledException   → UI thread (button clicks, bindings)
            //   AppDomain.UnhandledException   → background threads / ThreadPool
            //   TaskScheduler.Unobserved...    → fire-and-forget Tasks not awaited
            //
            // Each handler logs the exception via Serilog (which writes to the
            // rolling log file) before showing a message box or letting the runtime
            // terminate the process.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Debug()
                .WriteTo.File(
                    path: System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "FileAuditor", "Logs", "log-.txt"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7)
                .CreateLogger();

            // Configure services
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            // Show main window
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Logging
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(dispose: true);
            });

            // Core services
            services.AddSingleton<IBoxDriveHandler, BoxDriveHandler>();
            services.AddSingleton<IFileScanner, FileScanner>();
            services.AddSingleton<IExportService, ExportService>();
            services.AddSingleton<IScanHistoryService, ScanHistoryService>();
            services.AddSingleton<ICleanupService, CleanupService>();

            // ViewModels
            services.AddSingleton<MainViewModel>();
            // CleanupViewModel is NOT registered here — MainWindow constructs two instances
            // manually (Delete tab + Move tab) with different initialMode parameters.

            // Views
            services.AddSingleton<MainWindow>();
        }

        /// <summary>
        /// Handles unhandled exceptions on the WPF UI / Dispatcher thread.
        /// Setting <see cref="DispatcherUnhandledExceptionEventArgs.Handled"/> to
        /// <c>true</c> prevents the default WPF behaviour of terminating the process,
        /// allowing the application to continue running after showing an error message.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Fatal(e.Exception,
                "Unhandled exception on WPF Dispatcher thread: {Message}", e.Exception.Message);

            System.Windows.MessageBox.Show(
                $"An unexpected error occurred:\n\n{e.Exception.Message}\n\n" +
                $"The error has been logged. The application will continue running.\n\n" +
                $"If the problem persists, please check the logs in:\n" +
                $"%LocalAppData%\\FileAuditor\\Logs\\",
                "Unexpected Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);

            e.Handled = true; // Prevents process termination.
        }

        /// <summary>
        /// Handles unhandled exceptions thrown on non-UI threads (background threads,
        /// <see cref="System.Threading.ThreadPool"/> workers, etc.).
        /// When <see cref="UnhandledExceptionEventArgs.IsTerminating"/> is <c>true</c>
        /// the runtime will kill the process regardless — we at least guarantee the
        /// error is flushed to the log file before that happens.
        /// </summary>
        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            Log.Fatal(ex,
                "Unhandled exception on background thread (IsTerminating={IsTerminating}): {Message}",
                e.IsTerminating,
                ex?.Message ?? e.ExceptionObject?.ToString());

            // Flush synchronously so the log entry is written before the process exits.
            Log.CloseAndFlush();
        }

        /// <summary>
        /// Handles <see cref="Task"/> exceptions that were never observed (i.e. a Task
        /// faulted but was never awaited and no <c>ContinueWith</c> checked its state).
        /// Calling <see cref="UnobservedTaskExceptionEventArgs.SetObserved"/> prevents
        /// the default behaviour of escalating the exception to
        /// <see cref="AppDomain.UnhandledException"/> and terminating the process.
        /// </summary>
        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Log.Warning(e.Exception,
                "Unobserved Task exception (marked as observed to prevent process termination): {Message}",
                e.Exception.Message);

            e.SetObserved(); // Prevents escalation and process termination.
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _serviceProvider?.Dispose();
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}

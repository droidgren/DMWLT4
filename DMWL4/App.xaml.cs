using FellowOakDicom;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using ILogger = Serilog.ILogger;

namespace MWL4
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            var serilogLogger = ConfigureLogging();
            RegisterGlobalExceptionHandlers();

            var services = new ServiceCollection();
            services.AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddSerilog(serilogLogger);
            });

            new DicomSetupBuilder()
                .RegisterServices(s =>
                {
                    foreach (var service in services)
                    {
                        s.Add(service);
                    }
                })
                .Build();

            base.OnStartup(e);
        }

        public ILogger ConfigureLogging()
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"DMWLT4\Logs\Trace.log");

            var loggerConfig = new LoggerConfiguration()
                .Enrich.WithProperty("MachineName", Environment.MachineName)
                .MinimumLevel.Verbose()
                .WriteTo.File(
                    logPath,
                    global::Serilog.Events.LogEventLevel.Verbose,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}");

            var logger = loggerConfig.CreateLogger();

            Log.Logger = logger;
            return logger;
        }

        /// <summary>
        /// Makes sure unexpected exceptions end up in the log instead of silently crashing the app.
        /// </summary>
        private void RegisterGlobalExceptionHandlers()
        {
            DispatcherUnhandledException += (s, args) =>
            {
                Log.Error(args.Exception, "Unhandled UI exception");
                MessageBox.Show(
                    "An unexpected error occurred:\n" + args.Exception.Message + "\n\nSee the log for details.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                Log.Fatal(args.ExceptionObject as Exception, "Unhandled exception (terminating: {terminating})", args.IsTerminating);
                if (args.IsTerminating) Log.CloseAndFlush();
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                Log.Error(args.Exception, "Unobserved task exception");
                args.SetObserved();
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}

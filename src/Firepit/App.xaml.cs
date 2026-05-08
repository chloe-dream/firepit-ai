using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Firepit.Singleton;
using Serilog;

namespace Firepit;

public partial class App : Application
{
    private SingletonGuard? _guard;

    protected override async void OnStartup(StartupEventArgs e)
    {
        ConfigureLogging();
        HookUnhandledExceptions();

        Log.Information("Firepit starting (pid {Pid})", Environment.ProcessId);

        _guard = new SingletonGuard();

        if (!_guard.TryAcquire())
        {
            Log.Information("Existing instance detected — sending focus and exiting");
            await _guard.TrySendAsync(SingletonCommand.Focus(), TimeSpan.FromSeconds(2));
            _guard.Dispose();
            Shutdown(0);
            return;
        }

        _guard.StartListening(HandleSingletonCommand);
        base.OnStartup(e);
    }

    private Task HandleSingletonCommand(SingletonCommand command)
    {
        Log.Information("Singleton command received: {Command} {Project}", command.Command, command.Project);
        return Dispatcher.InvokeAsync(() =>
        {
            if (MainWindow is null) return;
            if (MainWindow.WindowState == WindowState.Minimized)
            {
                MainWindow.WindowState = WindowState.Normal;
            }
            MainWindow.Activate();
            MainWindow.Topmost = true;
            MainWindow.Topmost = false;

            if (command.Command == "summon"
                && !string.IsNullOrEmpty(command.Project)
                && MainWindow is MainWindow mw)
            {
                mw.SummonByName(command.Project);
            }
        }).Task;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Firepit shutting down (exit code {Code})", e.ApplicationExitCode);
        _guard?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void ConfigureLogging()
    {
        var logsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Firepit", "logs");
        Directory.CreateDirectory(logsDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                path: Path.Combine(logsDir, "firepit-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10_000_000,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private void HookUnhandledExceptions()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled dispatcher exception");
            if (MainWindow is MainWindow mw)
            {
                mw.ShowToast($"Unhandled error: {args.Exception.Message}", isError: true);
            }
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                Log.Fatal(ex, "AppDomain unhandled exception (terminating={IsTerminating})", args.IsTerminating);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }
}

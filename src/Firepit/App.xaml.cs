using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Firepit.Core.Settings;
using Firepit.Mcp;
using Firepit.Singleton;
using Serilog;

namespace Firepit;

public partial class App : Application
{
    private SingletonGuard? _guard;
    private McpHost? _mcpHost;

    protected override async void OnStartup(StartupEventArgs e)
    {
        ConfigureLogging();
        HookUnhandledExceptions();

        Log.Information("Firepit starting (pid {Pid})", Environment.ProcessId);

        // Load settings once at startup so font-scaling tokens are written into
        // Application.Resources BEFORE any Window XAML resolves StaticResource lookups.
        // MainWindow re-loads settings (cheap — same JSON file) for its own state;
        // we only need the font knob here.
        try
        {
            var initial = new JsonSettingsStore().Load();
            ApplyFontResources(initial.Ui?.ResolvedFontSize ?? UiSettings.DefaultFontSize);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not pre-load settings for font tokens — defaults stay in effect");
        }

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

        // NOTE: do NOT attach to MainWindow.Loaded here. WPF defers the
        // StartupUri's window construction to a follow-up dispatcher op, so
        // Application.MainWindow is still null at this point and the
        // 'is MainWindow mw' check silently no-ops — that's how the MCP host
        // failed to start at all in v0.5.13–v0.5.15 (see issue #12). MainWindow
        // calls EnsureMcpHostStarted from its own OnLoaded instead.
    }

    /// <summary>
    /// Idempotent. Called by MainWindow once it has loaded, because App.OnStartup
    /// can't reliably reach MainWindow at the time it runs (StartupUri is
    /// processed after OnStartup returns).
    /// </summary>
    public void EnsureMcpHostStarted(IMcpBackend backend)
    {
        if (_mcpHost is not null) return;
        try
        {
            var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.5.0";
            _mcpHost = new McpHost(backend, version);
            _mcpHost.Start();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "MCP host failed to start");
        }
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
        _mcpHost?.Dispose();
        _guard?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    public static void ApplyFontResources(int fontSize)
    {
        fontSize = Math.Clamp(fontSize, UiSettings.MinFontSize, UiSettings.MaxFontSize);
        var scale = fontSize / (double)UiSettings.DefaultFontSize;
        var r = Current.Resources;
        r["BaseFontSize"]              = (double)fontSize;
        r["SmallFontSize"]             = (double)Math.Max(UiSettings.MinFontSize - 1, fontSize - 1);
        r["MediumFontSize"]            = (double)(fontSize + 1);
        r["TitleFontSize"]             = (double)(fontSize + 2);
        r["CaptionPixelHeight"]        = 36.0 * scale;
        r["DialogCaptionPixelHeight"]  = 32.0 * scale;
        r["TabItemPixelHeight"]        = 36.0 * scale;
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

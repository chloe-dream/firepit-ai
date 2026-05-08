using System;
using System.Threading.Tasks;
using System.Windows;
using Firepit.Singleton;

namespace Firepit;

public partial class App : Application
{
    private SingletonGuard? _guard;

    protected override async void OnStartup(StartupEventArgs e)
    {
        _guard = new SingletonGuard();

        if (!_guard.TryAcquire())
        {
            // Another instance is alive — focus it and exit ourselves.
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
        _guard?.Dispose();
        base.OnExit(e);
    }
}

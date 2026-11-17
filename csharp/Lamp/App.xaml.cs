using System.Windows;
using Lamp.Core;
using Lamp.Services;

namespace Lamp;

public partial class App : Application
{
    public static new App Current => (App)Application.Current;

    public LampStore Store { get; private set; } = null!;
    public MainWindow Desk { get; private set; } = null!;
    public IslandWindow Island { get; private set; } = null!;

    TrayService? _tray;
    PresenceService? _presence;
    bool _quit;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Store = LampStore.Load();
        ThemeService.Apply(Store.State.Settings.Theme);
        _tray = new TrayService(Restore, Quit);
        Desk = new MainWindow(Store);
        Island = new IslandWindow(Store);
        Desk.Closing += (_, args) =>
        {
            if (_quit) return;
            args.Cancel = true;
            Tuck();
        };
        Desk.StateChanged += (_, _) =>
        {
            if (_quit) return;
            if (Desk.WindowState == WindowState.Minimized) Tuck();
        };
        Desk.Show();
        _presence = new PresenceService(count => Dispatcher.Invoke(() => Store.SetOnline(count)));
    }

    public void Tuck()
    {
        if (_quit) return;
        if (Desk.WindowState == WindowState.Minimized) Desk.WindowState = WindowState.Normal;
        Desk.Hide();
        Island.ShowIsland();
    }

    public void Restore()
    {
        if (_quit) return;
        Island.HideIsland();
        if (Desk.WindowState == WindowState.Minimized) Desk.WindowState = WindowState.Normal;
        Desk.Show();
        Desk.Activate();
        Desk.Focus();
    }

    public void Quit()
    {
        _quit = true;
        _presence?.Dispose();
        _tray?.Dispose();
        Island.HideIsland();
        Island.Close();
        Desk.Close();
        Shutdown();
    }
}

using System.ComponentModel;
using System.Windows;
using Lamp.Services;

namespace Lamp;

public partial class SetupWindow : Window
{
    bool _allowClose;

    public SetupWindow()
    {
        InitializeComponent();
    }

    async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var progress = new Progress<string>(text => StatusLabel.Text = text);
        try
        {
            await Task.Run(() => SetupService.Install(progress));
            Bar.IsIndeterminate = false;
            Bar.Value = 100;
            StatusLabel.Text = "Готово. Открываем Лампу…";
            await Task.Delay(700);
            _allowClose = true;
            SetupService.LaunchInstalled();
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _allowClose = true;
            Bar.IsIndeterminate = false;
            StatusLabel.Text = "Не получилось поставить: " + ex.Message;
            CloseBtn.Visibility = Visibility.Visible;
        }
    }

    void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose) e.Cancel = true;
        base.OnClosing(e);
    }
}

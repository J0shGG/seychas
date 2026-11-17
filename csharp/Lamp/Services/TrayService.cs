using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Lamp.Services;

public sealed class TrayService : IDisposable
{
    readonly Forms.NotifyIcon _icon;

    public TrayService(Action restore, Action quit)
    {
        _icon = new Forms.NotifyIcon
        {
            Text = "Лампа",
            Visible = true,
            Icon = LoadIcon(),
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => restore());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выйти", null, (_, _) => quit());
        _icon.ContextMenuStrip = menu;
        _icon.Click += (_, e) =>
        {
            if (e is Forms.MouseEventArgs m && m.Button == Forms.MouseButtons.Left) restore();
        };
        NotifyService.Balloon = (title, body) =>
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = body;
            _icon.ShowBalloonTip(4000);
        };
    }

    static Drawing.Icon LoadIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                var fromExe = Drawing.Icon.ExtractAssociatedIcon(path);
                if (fromExe is not null) return fromExe;
            }
        }
        catch
        {
            /* fall through */
        }
        var ico = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (File.Exists(ico)) return new Drawing.Icon(ico);
        return Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}

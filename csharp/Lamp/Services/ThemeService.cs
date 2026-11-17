using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lamp.Core;

namespace Lamp.Services;

public static class ThemeService
{
    static readonly Uri DeskUri = new("pack://application:,,,/Assets/desk.jpg");

    public static void Apply(ThemeId theme)
    {
        var app = Application.Current;
        if (app is null) return;
        var r = app.Resources;
        switch (theme)
        {
            case ThemeId.light:
                Set(r, "#1c1612", "#3d342c", "#c4784a", "#a05a32", "#5c5046", "#f3eadc", "#fff8ee", 0.08);
                r["DeskBackground"] = new SolidColorBrush(Color.FromRgb(0xF3, 0xEA, 0xDC));
                r["WindowBg"] = Color.FromRgb(0xF3, 0xEA, 0xDC);
                r["IslandFill"] = Color.FromRgb(0xF7, 0xF1, 0xE6);
                r["IslandOpen"] = Color.FromRgb(0xFF, 0xFA, 0xF3);
                r["IslandLine"] = Color.FromArgb(0x24, 0x1C, 0x16, 0x12);
                break;
            case ThemeId.dark:
                Set(r, "#f4f0e8", "#a39e96", "#d4896a", "#f0b090", "#ece7df", "#121214", "#1a1a1d", 0.72);
                r["DeskBackground"] = Solid("#121214");
                r["WindowBg"] = Color.FromRgb(0x10, 0x10, 0x12);
                r["IslandFill"] = Color.FromRgb(0x16, 0x16, 0x18);
                r["IslandOpen"] = Color.FromRgb(0x1C, 0x1C, 0x1F);
                r["IslandLine"] = Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF);
                break;
            case ThemeId.ink:
                Set(r, "#e7eef8", "#8ea0b8", "#d4a574", "#e8c49a", "#d5deea", "#101624", "#151c2e", 0.74);
                r["DeskBackground"] = Solid("#101624");
                r["WindowBg"] = Color.FromRgb(0x10, 0x16, 0x24);
                r["IslandFill"] = Color.FromRgb(0x14, 0x1B, 0x2B);
                r["IslandOpen"] = Color.FromRgb(0x1A, 0x24, 0x38);
                r["IslandLine"] = Color.FromArgb(0x33, 0xB4, 0xCE, 0xE8);
                break;
            default:
                Set(r, "#f3ead8", "#b7a894", "#c4784a", "#e8a06a", "#e8c9a0", "#120e0b", "#1b1512", 0.58);
                r["DeskBackground"] = DeskBrush();
                r["WindowBg"] = Color.FromRgb(0x12, 0x0E, 0x0B);
                r["IslandFill"] = Color.FromRgb(0x14, 0x11, 0x0F);
                r["IslandOpen"] = Color.FromRgb(0x1B, 0x15, 0x12);
                r["IslandLine"] = Color.FromArgb(0x38, 0xE8, 0xC9, 0xA0);
                break;
        }
        r["InkBrush"] = new SolidColorBrush((Color)r["Ink"]);
        r["PaperBrush"] = new SolidColorBrush((Color)r["Paper"]);
        r["CopperBrush"] = new SolidColorBrush((Color)r["Copper"]);
        r["CopperBrightBrush"] = new SolidColorBrush((Color)r["CopperBright"]);
        r["GoldBrush"] = new SolidColorBrush((Color)r["Gold"]);
        r["MutedBrush"] = new SolidColorBrush((Color)r["Muted"]);
        r["PanelBrush"] = new SolidColorBrush((Color)r["Panel"]);
        r["IslandFillBrush"] = new SolidColorBrush((Color)r["IslandFill"]);
        r["IslandOpenBrush"] = new SolidColorBrush((Color)r["IslandOpen"]);
        r["IslandLineBrush"] = new SolidColorBrush((Color)r["IslandLine"]);
        r["WindowBgBrush"] = new SolidColorBrush((Color)r["WindowBg"]);
        Native.TintOpenWindows();
    }

    static void Set(ResourceDictionary r, string paper, string muted, string copper, string bright, string gold, string window, string panel, double panelA)
    {
        r["Paper"] = Parse(paper);
        r["Muted"] = Parse(muted);
        r["Copper"] = Parse(copper);
        r["CopperBright"] = Parse(bright);
        r["Gold"] = Parse(gold);
        r["Ink"] = Parse("#1c1612");
        var p = Parse(panel);
        p.A = (byte)Math.Round(panelA * 255);
        r["Panel"] = p;
        r["WindowBg"] = Parse(window);
    }

    static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    static SolidColorBrush Solid(string hex) => new(Parse(hex));

    static Brush DeskBrush()
    {
        try
        {
            return new ImageBrush(new BitmapImage(DeskUri))
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
            };
        }
        catch
        {
            return Solid("#120e0b");
        }
    }
}

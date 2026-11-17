using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Lamp.Services;

public static class SetupService
{
    public const string ProductName = "Лампа";
    public const string ExeName = "Seychas.exe";
    public const string AppId = "app.seychas.desktop";

    public static string Version =>
        typeof(SetupService).Assembly.GetName().Version?.ToString(3) ?? "2.0.0";

    public static string InstallDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "Seychas");

    public static string InstalledExe => Path.Combine(InstallDir, ExeName);

    public static string DesktopShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        $"{ProductName}.lnk");

    public static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        $"{ProductName}.lnk");

    static string UninstallKey => $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{AppId}";

    public static bool IsSetupMode(IEnumerable<string> args)
    {
        if (args.Any(a => a is "--install" or "/install")) return true;
        if (args.Any(a => a is "--uninstall" or "/uninstall" or "--portable")) return false;
        var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "");
        return name.StartsWith("Seychas-Setup", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsUninstall(IEnumerable<string> args) =>
        args.Any(a => a is "--uninstall" or "/uninstall");

    public static bool IsQuiet(IEnumerable<string> args) =>
        args.Any(a => a is "--quiet" or "/S" or "/quiet" or "-q");

    public static bool IsFromTemp(IEnumerable<string> args) =>
        args.Any(a => a is "--from-temp");

    public static int ImportTaskCount()
    {
        try { return ElectronImport.ReadBest()?.Tasks.Count ?? 0; }
        catch { return 0; }
    }

    public static void Install(IProgress<string>? progress = null)
    {
        void Say(string text) => progress?.Report(text);

        Say("Закрываем старую Лампу…");
        StopOldApps();
        Thread.Sleep(400);

        Say("Переносим задачи, мысли и журнал…");
        var tasks = ImportTaskCount();
        ElectronImport.ImportNow();
        Say(tasks > 0
            ? $"Перенесли задач: {tasks}."
            : "Старых задач не нашлось — стол будет как есть.");

        Say("Убираем Electron…");
        RunNsisUninstall();
        StopOldApps();

        Say("Копируем новую Лампу…");
        Directory.CreateDirectory(InstallDir);
        CopySelf(InstalledExe);

        Say("Обновляем ярлыки…");
        WriteShortcut(DesktopShortcut);
        WriteShortcut(StartMenuShortcut);
        RemoveStaleShortcuts();
        WriteUninstallRegistry();

        Say("Готово.");
    }

    public static void LaunchInstalled()
    {
        if (!File.Exists(InstalledExe)) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = InstalledExe,
            WorkingDirectory = InstallDir,
            UseShellExecute = true,
        });
    }

    public static void Uninstall(bool quiet, bool fromTemp = false)
    {
        var self = Environment.ProcessPath ?? "";
        var inInstall = PathsEqual(Path.GetDirectoryName(self), InstallDir);
        if (inInstall && !fromTemp)
        {
            var temp = Path.Combine(Path.GetTempPath(), "Seychas-Uninstall.exe");
            File.Copy(self, temp, true);
            Process.Start(new ProcessStartInfo
            {
                FileName = temp,
                Arguments = quiet ? "--uninstall --quiet --from-temp" : "--uninstall --from-temp",
                UseShellExecute = true,
            });
            return;
        }

        StopOldApps();
        DeleteFile(DesktopShortcut);
        DeleteFile(StartMenuShortcut);
        RemoveStaleShortcuts();
        try
        {
            if (Directory.Exists(InstallDir))
                Directory.Delete(InstallDir, true);
        }
        catch
        {
            foreach (var file in Directory.Exists(InstallDir) ? Directory.GetFiles(InstallDir, "*", SearchOption.AllDirectories) : [])
            {
                try { File.Delete(file); } catch { /* in use */ }
            }
        }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { /* already gone */ }
    }

    static void CopySelf(string dest)
    {
        var src = Environment.ProcessPath ?? throw new InvalidOperationException("нет пути к установщику");
        for (var i = 0; i < 10; i++)
        {
            try
            {
                File.Copy(src, dest, true);
                try { File.SetAttributes(dest, FileAttributes.Normal); } catch { }
                return;
            }
            catch (IOException) when (i < 9)
            {
                StopOldApps();
                Thread.Sleep(500);
            }
        }
        File.Copy(src, dest, true);
    }

    static void StopOldApps()
    {
        var self = Environment.ProcessId;
        var selfPath = Environment.ProcessPath;
        foreach (var name in new[] { "Seychas", "Lamp" })
        {
            Process[] list;
            try { list = Process.GetProcessesByName(name); }
            catch { continue; }
            foreach (var p in list)
            {
                using (p)
                {
                    if (p.Id == self) continue;
                    try
                    {
                        var path = p.MainModule?.FileName;
                        if (path is not null && PathsEqual(path, selfPath)) continue;
                    }
                    catch { /* access denied is fine */ }
                    try
                    {
                        p.CloseMainWindow();
                        if (!p.WaitForExit(4000)) p.Kill(true);
                        p.WaitForExit(4000);
                    }
                    catch
                    {
                        try { p.Kill(true); } catch { }
                    }
                }
            }
        }
    }

    static void RunNsisUninstall()
    {
        var uninstaller = FindNsisUninstaller();
        if (uninstaller is null) return;
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = uninstaller,
                Arguments = "/S",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            p?.WaitForExit(45_000);
        }
        catch
        {
            /* copy over the leftover tree */
        }
        Thread.Sleep(400);
        try
        {
            if (Directory.Exists(InstallDir))
            {
                foreach (var file in Directory.GetFiles(InstallDir, "*", SearchOption.AllDirectories))
                {
                    if (PathsEqual(file, InstalledExe)) continue;
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }

    static string? FindNsisUninstaller()
    {
        foreach (var name in new[] { "Uninstall Seychas.exe", "Uninstall Лампа.exe" })
        {
            var path = Path.Combine(InstallDir, name);
            if (File.Exists(path)) return path;
        }
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var root = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (root is null) continue;
            foreach (var sub in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(sub);
                if (key is null) continue;
                var display = key.GetValue("DisplayName") as string ?? "";
                var cmd = key.GetValue("UninstallString") as string ?? "";
                var match = sub.Equals(AppId, StringComparison.OrdinalIgnoreCase)
                    || display.Contains("Лампа", StringComparison.Ordinal)
                    || display.Contains("Seychas", StringComparison.OrdinalIgnoreCase);
                if (!match || string.IsNullOrWhiteSpace(cmd)) continue;
                var path = FirstPath(cmd);
                if (path is null) continue;
                if (path.EndsWith(ExeName, StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(path)) return path;
            }
        }
        return null;
    }

    static string? FirstPath(string command)
    {
        var quoted = Regex.Match(command, "^\"([^\"]+)\"");
        if (quoted.Success) return quoted.Groups[1].Value;
        var token = command.Split(' ', 2)[0];
        return File.Exists(token) ? token : null;
    }

    static void WriteShortcut(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var type = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell недоступен");
        dynamic shell = Activator.CreateInstance(type)!;
        var shortcut = shell.CreateShortcut(path);
        shortcut.TargetPath = InstalledExe;
        shortcut.WorkingDirectory = InstallDir;
        shortcut.Description = ProductName;
        shortcut.IconLocation = InstalledExe;
        shortcut.Save();
    }

    static void RemoveStaleShortcuts()
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var nested = Path.Combine(programs, "Seychas");
        foreach (var extra in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Seychas.lnk"),
            Path.Combine(programs, "Seychas.lnk"),
            Path.Combine(nested, $"{ProductName}.lnk"),
            Path.Combine(nested, "Seychas.lnk"),
        })
            DeleteFile(extra);
        try
        {
            if (Directory.Exists(nested) && Directory.GetFileSystemEntries(nested).Length == 0)
                Directory.Delete(nested);
        }
        catch { }
    }

    static void WriteUninstallRegistry()
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey)
            ?? throw new InvalidOperationException("не удалось записать ключ удаления");
        var exe = InstalledExe;
        key.SetValue("DisplayName", ProductName);
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", "J0shGG");
        key.SetValue("InstallLocation", InstallDir);
        key.SetValue("DisplayIcon", exe);
        key.SetValue("UninstallString", $"\"{exe}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{exe}\" --uninstall --quiet");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("URLInfoAbout", "https://github.com/J0shGG/seychas");
        try
        {
            var kb = (int)Math.Min(int.MaxValue, new FileInfo(exe).Length / 1024);
            key.SetValue("EstimatedSize", kb, RegistryValueKind.DWord);
        }
        catch { }
    }

    static void DeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    static bool PathsEqual(string? a, string? b) =>
        !string.IsNullOrEmpty(a)
        && !string.IsNullOrEmpty(b)
        && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}

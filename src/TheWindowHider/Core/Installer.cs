using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace TheWindowHider.Core;

/// <summary>
/// Self-install support for the single-file build: copies the running exe into the user's
/// programs folder, adds a Start Menu shortcut and an Apps and features entry, and can undo
/// all of that. Everything is per-user, so no administrator rights are needed.
/// </summary>
public static class Installer
{
    public const string AppName = "The Window Hider";
    private const string ExeName = "TheWindowHider.exe";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TheWindowHider";

    public static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TheWindowHider");

    public static string InstalledExePath => Path.Combine(InstallDir, ExeName);

    public static string CurrentExePath => Environment.ProcessPath ?? "";

    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

    /// <summary>Only a single-file publish can copy itself as one exe; a dev build has loose dlls.</summary>
    public static bool IsSingleFileDeployment =>
        !File.Exists(Path.Combine(AppContext.BaseDirectory, ExeName.Replace(".exe", ".dll")));

    public static bool IsRunningFromInstall =>
        string.Equals(CurrentExePath, InstalledExePath, StringComparison.OrdinalIgnoreCase);

    public static bool IsInstalledOnDisk => File.Exists(InstalledExePath);

    public static bool IsInstalled => IsRunningFromInstall || IsInstalledOnDisk;

    /// <summary>True when it makes sense to offer installation (single-file build, not yet installed).</summary>
    public static bool CanOfferInstall => IsSingleFileDeployment && !IsInstalled;

    public static bool TryInstall(out string message)
    {
        try
        {
            Directory.CreateDirectory(InstallDir);
            if (!IsRunningFromInstall)
                File.Copy(CurrentExePath, InstalledExePath, overwrite: true);

            CreateStartMenuShortcut();
            RegisterUninstall();

            // If "start with Windows" is on, re-point it at the installed copy.
            if (StartupManager.IsEnabled())
                StartupManager.Set(true, InstalledExePath);

            message = $"Installed to:\n{InstallDir}\n\n" +
                      "A Start Menu shortcut was added, and it now shows in Apps & features. " +
                      "You can safely delete the file you downloaded.";
            return true;
        }
        catch (Exception ex)
        {
            message = "Install failed: " + ex.Message;
            return false;
        }
    }

    private static void CreateStartMenuShortcut()
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic lnk = shell.CreateShortcut(ShortcutPath);
            lnk.TargetPath = InstalledExePath;
            lnk.WorkingDirectory = InstallDir;
            lnk.IconLocation = InstalledExePath + ",0";
            lnk.Description = AppName;
            lnk.Save();
        }
        catch
        {
            // A missing shortcut is not worth failing the whole install over.
        }
    }

    private static void RegisterUninstall()
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey, writable: true);
            key.SetValue("DisplayName", AppName);
            key.SetValue("DisplayIcon", InstalledExePath);
            key.SetValue("DisplayVersion", "1.0.0");
            key.SetValue("Publisher", AppName);
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("UninstallString", $"\"{InstalledExePath}\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        catch
        {
            // Non-fatal.
        }
    }

    /// <summary>Invoked when the app is launched with --uninstall (from Apps &amp; features).</summary>
    public static void Uninstall()
    {
        try { File.Delete(ShortcutPath); } catch { }
        try { StartupManager.Set(false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); } catch { }

        // We can't delete our own running exe, so hand the folder deletion to a detached shell
        // that waits for this process to exit first.
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c timeout /t 2 >nul & rmdir /s /q \"{InstallDir}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}

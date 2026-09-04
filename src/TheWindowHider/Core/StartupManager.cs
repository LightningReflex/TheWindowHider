using Microsoft.Win32;

namespace TheWindowHider.Core;

/// <summary>Manages the HKCU "run at logon" registry entry.</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TheWindowHider";

    private static string ExePath =>
        Environment.ProcessPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "TheWindowHider.exe");

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            string? value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }

    public static void Set(bool enabled, string? exePath = null)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
                key.SetValue(ValueName, $"\"{exePath ?? ExePath}\" --tray");
            else if (key.GetValue(ValueName) != null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Non-fatal; the toggle just won't stick.
        }
    }
}

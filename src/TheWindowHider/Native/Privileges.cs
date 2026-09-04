using static TheWindowHider.Native.NativeMethods;

namespace TheWindowHider.Native;

/// <summary>
/// Best-effort SeDebugPrivilege enablement. When the app runs elevated this lets us open
/// a wider range of processes; when it doesn't, the call simply fails harmlessly and we
/// carry on opening whatever processes we're allowed to.
/// </summary>
internal static class Privileges
{
    public static bool TryEnableDebugPrivilege()
    {
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr token))
                return false;

            try
            {
                if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out LUID luid))
                    return false;

                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED
                };

                return AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                CloseHandle(token);
            }
        }
        catch
        {
            return false;
        }
    }
}

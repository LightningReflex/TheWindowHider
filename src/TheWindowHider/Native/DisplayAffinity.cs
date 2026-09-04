using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using static TheWindowHider.Native.NativeMethods;

namespace TheWindowHider.Native;

/// <summary>
/// Applies <c>SetWindowDisplayAffinity</c> to arbitrary windows.
///
/// Windows restricts that API to the process that owns the target window, so for foreign
/// windows we execute the call *inside* the owning process via a minimal remote thread.
///
/// Improvements over the naive approach:
///  * Own-process windows take a direct in-process call, with no injection at all.
///  * The address of SetWindowDisplayAffinity in the remote user32 is resolved once per
///    process and cached, instead of re-parsing the export table on every apply.
///  * Injected code uses W^X memory (allocate RW, write, flip to RX) rather than RWX.
///  * Processes that can't be opened (protected / elevated / anti-cheat) are recorded as
///    unsupported and skipped quietly instead of being retried or crashing anything.
/// </summary>
internal static class DisplayAffinity
{
    private static readonly int OwnPid = Environment.ProcessId;

    // procId -> resolved absolute address of SetWindowDisplayAffinity in that process's user32.
    private static readonly ConcurrentDictionary<int, ulong> AddressCache = new();

    // procIds we've already determined we cannot touch, so we don't hammer them.
    private static readonly ConcurrentDictionary<int, byte> Unsupported = new();

    public static bool IsHidden(IntPtr hWnd)
    {
        GetWindowDisplayAffinity(hWnd, out int affinity);
        return affinity != WDA_NONE;
    }

    /// <summary>Removes any cached state for a process id (call when a process is known to have exited).</summary>
    public static void ForgetProcess(int pid)
    {
        AddressCache.TryRemove(pid, out _);
        Unsupported.TryRemove(pid, out _);
    }

    /// <summary>
    /// Sets the display affinity of a window. Returns true on success.
    /// <paramref name="hide"/> true => WDA_EXCLUDEFROMCAPTURE, false => WDA_NONE.
    /// </summary>
    public static bool Set(IntPtr hWnd, bool hide)
    {
        int affinity = hide ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE;

        GetWindowThreadProcessId(hWnd, out int pid);
        if (pid <= 0) return false;

        // Fast path: our own window needs no injection.
        if (pid == OwnPid)
            return SetWindowDisplayAffinity(hWnd, affinity);

        if (Unsupported.ContainsKey(pid))
            return false;

        try
        {
            bool ok = SetRemote(hWnd, pid, affinity);
            if (!ok)
            {
                // One retry with a fresh address resolve in case user32 was relocated / cache stale.
                AddressCache.TryRemove(pid, out _);
                ok = SetRemote(hWnd, pid, affinity);
            }
            if (!ok)
                Unsupported.TryAdd(pid, 0);
            return ok;
        }
        catch
        {
            Unsupported.TryAdd(pid, 0);
            return false;
        }
    }

    private static bool SetRemote(IntPtr hWnd, int pid, int affinity)
    {
        IntPtr hProc = OpenProcess(INJECT_ACCESS, false, pid);
        if (hProc == IntPtr.Zero)
            return false;

        try
        {
            if (!IsWow64Process(hProc, out bool isWow64))
                isWow64 = false;
            bool is32Bit = isWow64; // on x64 Windows, WoW64 => 32-bit target

            ulong funcAddr = AddressCache.GetOrAdd(pid, _ => ResolveExportAddress(hProc, is32Bit));
            if (funcAddr == 0)
                return false;

            byte[] shellcode = BuildShellcode(hWnd, affinity, funcAddr, is32Bit);

            // W^X: allocate writable, write, then flip to executable-read.
            IntPtr code = VirtualAllocEx(hProc, IntPtr.Zero, shellcode.Length, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (code == IntPtr.Zero)
                return false;

            try
            {
                if (!WriteProcessMemory(hProc, code, shellcode, shellcode.Length, out _))
                    return false;
                if (!VirtualProtectEx(hProc, code, shellcode.Length, PAGE_EXECUTE_READ, out _))
                    return false;

                IntPtr thread = CreateRemoteThread(hProc, IntPtr.Zero, 0, code, IntPtr.Zero, 0, IntPtr.Zero);
                if (thread == IntPtr.Zero)
                    return false;

                try
                {
                    WaitForSingleObject(thread, 5000);
                }
                finally
                {
                    CloseHandle(thread);
                }
            }
            finally
            {
                VirtualFreeEx(hProc, code, 0, MEM_RELEASE);
            }

            // Verify the change actually took (cross-process read is allowed).
            GetWindowDisplayAffinity(hWnd, out int now);
            return (now != WDA_NONE) == (affinity != WDA_NONE);
        }
        finally
        {
            CloseHandle(hProc);
        }
    }

    // ---- Remote PE export-table walk to find SetWindowDisplayAffinity in the target's user32 ----

    private static ulong ResolveExportAddress(IntPtr hProc, bool is32Bit)
    {
        // Enumerate modules with a generous buffer, then trust lpcbNeeded for the real count.
        var modules = new IntPtr[1024];
        uint cb = (uint)(modules.Length * IntPtr.Size);
        if (!EnumProcessModulesEx(hProc, modules, cb, out uint needed, LIST_MODULES_ALL))
            return 0;

        int count = Math.Min((int)(needed / IntPtr.Size), modules.Length);

        for (int i = 0; i < count; i++)
        {
            IntPtr module = modules[i];
            if (module == IntPtr.Zero) continue;

            var pathSb = new StringBuilder(260);
            if (GetModuleFileNameEx(hProc, module, pathSb, (uint)pathSb.Capacity) == 0) continue;
            if (!pathSb.ToString().EndsWith("user32.dll", StringComparison.OrdinalIgnoreCase)) continue;

            if (!GetModuleInformation(hProc, module, out MODULEINFO info, (uint)Marshal.SizeOf<MODULEINFO>()))
                continue;

            return FindExport(hProc, info.lpBaseOfDll, "SetWindowDisplayAffinity");
        }
        return 0;
    }

    private static ulong FindExport(IntPtr hProc, IntPtr baseAddr, string exportName)
    {
        ulong bas = (ulong)baseAddr;

        int e_lfanew = ReadInt32(hProc, bas + 0x3C);
        ulong ntHeaders = bas + (ulong)e_lfanew;
        ulong optionalHeader = ntHeaders + 0x18;

        // Export directory RVA lives in data directory index 0. Optional header magic decides the
        // offset to the data directories (0x60 for PE32, 0x70 for PE32+).
        ushort magic = (ushort)ReadInt16(hProc, optionalHeader);
        bool pe32Plus = magic == 0x20B;
        ulong dataDirectory = optionalHeader + (ulong)(pe32Plus ? 0x70 : 0x60);

        ulong exportDir = bas + (ulong)ReadInt32(hProc, dataDirectory);
        ulong namesRva = bas + (ulong)ReadInt32(hProc, exportDir + 0x20);
        ulong ordinalsRva = bas + (ulong)ReadInt32(hProc, exportDir + 0x24);
        ulong functionsRva = bas + (ulong)ReadInt32(hProc, exportDir + 0x1C);
        int numNames = ReadInt32(hProc, exportDir + 0x18);

        for (uint i = 0; i < numNames; i++)
        {
            ulong nameOffset = (ulong)ReadInt32(hProc, namesRva + i * 4);
            string name = ReadAscii(hProc, bas + nameOffset, 40);
            if (!name.StartsWith(exportName, StringComparison.Ordinal))
                continue;

            ushort ordinal = (ushort)(ReadInt16(hProc, ordinalsRva + i * 2) & 0xFFFF);
            ulong funcRva = (ulong)ReadInt32(hProc, functionsRva + (uint)ordinal * 4);
            return bas + funcRva;
        }
        return 0;
    }

    // ---- Shellcode ----

    private static byte[] BuildShellcode(IntPtr hWnd, int affinity, ulong funcAddr, bool is32Bit)
    {
        var asm = new List<byte>();
        if (is32Bit)
        {
            // stdcall: push args right-to-left, call, callee cleans stack.
            asm.Add(0x68); asm.AddRange(BitConverter.GetBytes((uint)affinity));      // push affinity
            asm.Add(0x68); asm.AddRange(BitConverter.GetBytes((uint)(long)hWnd));     // push hWnd
            asm.Add(0xB8); asm.AddRange(BitConverter.GetBytes((uint)funcAddr));       // mov eax, funcAddr
            asm.AddRange(new byte[] { 0xFF, 0xD0 });                                  // call eax
            asm.Add(0xC3);                                                            // ret
        }
        else
        {
            // Win64 fastcall: rcx = hWnd, rdx = affinity; reserve 0x28 shadow space (keeps 16-byte align).
            asm.AddRange(new byte[] { 0x48, 0x83, 0xEC, 0x28 });                      // sub rsp, 0x28
            asm.AddRange(new byte[] { 0x48, 0xB9 }); asm.AddRange(BitConverter.GetBytes((ulong)(long)hWnd));    // mov rcx, hWnd
            asm.AddRange(new byte[] { 0x48, 0xBA }); asm.AddRange(BitConverter.GetBytes((ulong)(uint)affinity));// mov rdx, affinity
            asm.AddRange(new byte[] { 0x48, 0xB8 }); asm.AddRange(BitConverter.GetBytes(funcAddr));             // mov rax, funcAddr
            asm.AddRange(new byte[] { 0xFF, 0xD0 });                                  // call rax
            asm.AddRange(new byte[] { 0x48, 0x83, 0xC4, 0x28 });                      // add rsp, 0x28
            asm.Add(0xC3);                                                            // ret
        }
        return asm.ToArray();
    }

    // ---- Remote memory read helpers ----

    private static int ReadInt32(IntPtr hProc, ulong addr)
    {
        var buf = new byte[4];
        ReadProcessMemory(hProc, (IntPtr)(long)addr, buf, 4, out _);
        return BitConverter.ToInt32(buf, 0);
    }

    private static short ReadInt16(IntPtr hProc, ulong addr)
    {
        var buf = new byte[2];
        ReadProcessMemory(hProc, (IntPtr)(long)addr, buf, 2, out _);
        return BitConverter.ToInt16(buf, 0);
    }

    private static string ReadAscii(IntPtr hProc, ulong addr, int max)
    {
        var buf = new byte[max];
        ReadProcessMemory(hProc, (IntPtr)(long)addr, buf, max, out _);
        int len = Array.IndexOf(buf, (byte)0);
        if (len < 0) len = max;
        return Encoding.ASCII.GetString(buf, 0, len);
    }
}

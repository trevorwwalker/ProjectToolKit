// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Scheduler.Core.Contracts;

namespace Scheduler.Host.Pool;

/// <summary>The live foreground lock timeout, behind an interface so tests never touch the real one (1161-B).</summary>
public interface IForegroundLockApi
{
    /// <summary>The live <c>ForegroundLockTimeout</c> in milliseconds, or null with <paramref name="error"/> set when it cannot be read.</summary>
    uint? Get(out int error);

    /// <summary>Sets the live value only (nothing is written to the registry or the profile); true when Windows accepted it, else <paramref name="error"/> is the Win32 error.</summary>
    bool Set(uint value, out int error);
}

/// <summary>The live count of kernel token objects (pool tag <c>Toke</c>), or null when it cannot be read.</summary>
public interface ITokenCount
{
    /// <summary>Paged allocations minus frees of tag <c>Toke</c>; null when the call fails.</summary>
    long? Live();
}

/// <summary>
/// 1161-B: the workaround for the Windows 11 token leak. Windows leaks kernel token objects for every process started while
/// its starter does not own the foreground window (which is every process a worker starts; the defect is reported in
/// <c>win32kfull!CForegroundLaunch::_CheckAllowForeground</c>, Microsoft has no fix). The tokens are never freed until a
/// restart; they fill the paged pool and slow process starts (1160-B). Setting the live <c>ForegroundLockTimeout</c> to 0
/// stops the leak (measured 2026-11-23: with another app in front, +929 tokens per 300 commits at 2147483647, +41 at 0).
/// <para>
/// The set is <c>SystemParametersInfo(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, 0, 0)</c> with last argument 0: live only, nothing
/// written to the registry or the profile. Windows puts the value back at every logon, so it is applied at every start and
/// checked again hourly with the process-start check.
/// </para>
/// <para>
/// Side effect: at 0 any app may take the foreground window. To tell whether Microsoft has fixed it: switch this off
/// (<c>foregroundLockFix: false</c> in the pool file), repeat the measurement in docs/perf/process-start-log.md (300 x write +
/// git add + git commit with another app in front) and read <c>Toke</c> with bin\pool-tags.ps1; no growth means it is
/// fixed, and this class, its two interfaces and the tests are to be removed.
/// </para>
/// </summary>
public sealed class ForegroundLockFix
{
    private readonly IForegroundLockApi _api;
    private readonly ISchedulerLog _log;
    private readonly bool _enabled;

    /// <param name="api">The live value.</param>
    /// <param name="log">The pool log.</param>
    /// <param name="enabled">False = read and log the value, change nothing.</param>
    public ForegroundLockFix(IForegroundLockApi api, ISchedulerLog log, bool enabled = true)
    {
        _api = api;
        _log = log;
        _enabled = enabled;
    }

    /// <summary>
    /// Reads the live value, sets it to 0 when it is not (and the switch is on), and writes one pool-log line saying what happened.
    /// <paramref name="quietWhenZero"/>: the hourly check says nothing while the value is still 0.
    /// </summary>
    public void Apply(bool quietWhenZero = false)
    {
        var before = _api.Get(out var readError);
        if (before is null)
        {
            _log.Write($"foreground lock timeout: could not be read (Win32 error {readError})");
            return;
        }

        if (before == 0)
        {
            if (!quietWhenZero)
            {
                _log.Write("foreground lock timeout: already 0");
            }

            return;
        }

        if (!_enabled)
        {
            _log.Write($"foreground lock timeout: {before} (left alone: foregroundLockFix is off)");
            return;
        }

        if (!_api.Set(0, out var error))
        {
            _log.Write($"foreground lock timeout: {before}, set to 0 was refused (Win32 error {error}); the token leak goes on");
            return;
        }

        var after = _api.Get(out _);
        _log.Write(after == 0
            ? $"foreground lock timeout: {before} -> 0 (live only, nothing written to the registry)"
            : $"foreground lock timeout: {before}, set to 0 was accepted but reads {(after is null ? "nothing" : after.ToString())}; the token leak goes on");
    }
}

/// <summary>The real foreground lock timeout (<c>SystemParametersInfo</c> 0x2000 and 0x2001). Never used by a test.</summary>
public sealed class Win32ForegroundLockApi : IForegroundLockApi
{
    private const uint SpiGet = 0x2000;
    private const uint SpiSet = 0x2001;

    /// <inheritdoc />
    public uint? Get(out int error)
    {
        error = 0;
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        if (SystemParametersInfo(SpiGet, 0, out var value, 0))
        {
            return value;
        }

        error = Marshal.GetLastWin32Error();
        return null;
    }

    /// <inheritdoc />
    public bool Set(uint value, out int error)
    {
        error = 0;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        // pvParam is the value itself and fWinIni is 0: not saved to the user profile, no broadcast.
        if (SystemParametersInfo(SpiSet, 0, (IntPtr)value, 0))
        {
            return true;
        }

        error = Marshal.GetLastWin32Error();
        return false;
    }

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, out uint value, uint winIni);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint winIni);
}

/// <summary>
/// The live <c>Toke</c> count the way <c>bin\pool-tags.ps1</c> reads it: <c>NtQuerySystemInformation</c> class 22 (pool tags),
/// paged allocations minus frees. Any failure gives null.
/// </summary>
public sealed class PoolTokenCount : ITokenCount
{
    private const int PoolTagInformation = 22;
    private const int LengthMismatch = unchecked((int)0xC0000004);

    /// <inheritdoc />
    public long? Live()
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
        {
            return null;
        }

        try
        {
            var size = 1 << 20;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    var status = NtQuerySystemInformation(PoolTagInformation, buffer, size, out var needed);
                    if (status == LengthMismatch)
                    {
                        size = Math.Max(needed + (256 << 10), size * 2);
                        continue;
                    }

                    return status == 0 ? Find(buffer) : null;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or OutOfMemoryException or Win32Exception)
        {
            // the figure is left out, no error
        }

        return null;
    }

    private static long? Find(IntPtr buffer)
    {
        // 64-bit layout: count at 0, entries from 8, 40 bytes each: tag(4) pagedAllocs(4) pagedFrees(4) pad(4) pagedBytes(8) ...
        var count = Marshal.ReadInt32(buffer);
        var wanted = Encoding.ASCII.GetBytes("Toke");
        for (var i = 0; i < count; i++)
        {
            var entry = IntPtr.Add(buffer, 8 + (i * 40));
            var match = true;
            for (var b = 0; b < 4 && match; b++)
            {
                match = Marshal.ReadByte(entry, b) == wanted[b];
            }

            if (match)
            {
                return (long)(uint)Marshal.ReadInt32(entry, 4) - (uint)Marshal.ReadInt32(entry, 8);
            }
        }

        return null;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int cls, IntPtr buffer, int length, out int returned);
}

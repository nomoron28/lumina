using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lumina.Windows;

/// <summary>
/// While a video plays Lumina asks Windows for a little more of the CPU than ordinary programs get and opts out of power throttling,
/// so a browser, a download or a build running beside it does not make the picture stutter. Back to normal when playback ends.
/// </summary>
public static class PlaybackPriority
{
    [StructLayout(LayoutKind.Sequential)]
    struct PowerThrottling { public uint Version, ControlMask, StateMask; }

    [DllImport("kernel32.dll")] static extern bool SetProcessInformation(IntPtr process, int informationClass, ref PowerThrottling info, int size);

    const int ProcessPowerThrottling = 4;
    const uint ExecutionSpeed = 1;

    public static void Set(bool playing)
    {
        try
        {
            using var me = Process.GetCurrentProcess();
            me.PriorityClass = playing ? ProcessPriorityClass.AboveNormal : ProcessPriorityClass.Normal;
            // playing: throttling off (control it, state "not throttled"); otherwise hand the decision back to Windows
            var info = new PowerThrottling { Version = 1, ControlMask = playing ? ExecutionSpeed : 0, StateMask = 0 };
            SetProcessInformation(me.Handle, ProcessPowerThrottling, ref info, Marshal.SizeOf<PowerThrottling>());
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or EntryPointNotFoundException) { }
    }
}

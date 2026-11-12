using System.Runtime.InteropServices;

namespace FdgRaylib.Rendering;

/// <summary>
/// #310 follow-up: clears the window manager's <c>_NET_WM_STATE_FULLSCREEN</c> on our own X11 window.
/// <para>raylib's <c>ToggleBorderlessWindowed</c> makes the window undecorated and monitor-sized, and
/// Muffin/Mutter-family WMs (Cinnamon, GNOME on X11) promote such a window to fullscreen state on their
/// own ("legacy fullscreen"). Toggling back restores the decorations but never removes that state, so the
/// WM keeps the window pinned to the monitor and ignores every resize - F11 went in and never came out
/// (owner report 2026-11-03, Cinnamon/X11; reproduced: raylib reports 1280x720 while the framebuffer
/// stays 3840x2160). An EWMH remove request fixes it. GLFW's own X11 accessors are not exported from
/// the bundled libraylib.so and <c>GetWindowHandle</c> returns the GLFW pointer on Linux, so the window
/// is found by our PID in the WM's client list.</para>
/// <para>Best-effort and silent: a no-op off Linux, without an X display, or if libX11 is missing.</para>
/// </summary>
internal static class X11WindowState
{
    private const string LibX11 = "libX11.so.6";
    private const int ClientMessageType = 33;
    private const long SubstructureNotifyMask = 1L << 19;
    private const long SubstructureRedirectMask = 1L << 20;
    private const long NetWmStateRemove = 0;
    private const long SourceApplication = 1;

    public static void ClearFullscreen()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            return;
        try
        {
            IntPtr display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return;
            try
            {
                ulong window = FindOwnWindow(display);
                if (window == 0) return;
                var ev = new XClientMessageEvent
                {
                    type = ClientMessageType,
                    sendEvent = 1,
                    display = display,
                    window = window,
                    messageType = XInternAtom(display, "_NET_WM_STATE", false),
                    format = 32,
                    l0 = NetWmStateRemove,
                    l1 = (long)XInternAtom(display, "_NET_WM_STATE_FULLSCREEN", false),
                    l3 = SourceApplication,
                };
                XSendEvent(display, XDefaultRootWindow(display), false,
                    SubstructureRedirectMask | SubstructureNotifyMask, ref ev);
                XFlush(display);
            }
            finally
            {
                XCloseDisplay(display);
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private static ulong FindOwnWindow(IntPtr display)
    {
        ulong pidAtom = XInternAtom(display, "_NET_WM_PID", false);
        ulong clientList = XInternAtom(display, "_NET_CLIENT_LIST", false);
        int pid = Environment.ProcessId;
        foreach (ulong w in ReadLongs(display, XDefaultRootWindow(display), clientList))
        {
            ulong[] p = ReadLongs(display, w, pidAtom);
            if (p.Length == 1 && (int)p[0] == pid) return w;
        }
        return 0;
    }

    // A format-32 property comes back as an array of C longs (8 bytes on 64-bit Linux).
    private static ulong[] ReadLongs(IntPtr display, ulong window, ulong property)
    {
        if (XGetWindowProperty(display, window, property, 0, 4096, false, 0,
                out _, out int format, out ulong count, out _, out IntPtr data) != 0 || data == IntPtr.Zero)
            return Array.Empty<ulong>();
        try
        {
            if (format != 32) return Array.Empty<ulong>();
            var values = new ulong[(int)count];
            for (int i = 0; i < values.Length; i++)
                values[i] = (ulong)Marshal.ReadInt64(data, i * 8);
            return values;
        }
        finally
        {
            XFree(data);
        }
    }

    // XEvent is a 24-long union; XSendEvent copies the full 192 bytes.
    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct XClientMessageEvent
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(8)] public ulong serial;
        [FieldOffset(16)] public int sendEvent;
        [FieldOffset(24)] public IntPtr display;
        [FieldOffset(32)] public ulong window;
        [FieldOffset(40)] public ulong messageType;
        [FieldOffset(48)] public int format;
        [FieldOffset(56)] public long l0;
        [FieldOffset(64)] public long l1;
        [FieldOffset(72)] public long l2;
        [FieldOffset(80)] public long l3;
        [FieldOffset(88)] public long l4;
    }

    [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(LibX11)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(LibX11)] private static extern ulong XDefaultRootWindow(IntPtr display);
    [DllImport(LibX11)] private static extern ulong XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport(LibX11)] private static extern int XFlush(IntPtr display);
    [DllImport(LibX11)] private static extern int XFree(IntPtr data);
    [DllImport(LibX11)] private static extern int XSendEvent(IntPtr display, ulong window, bool propagate,
        long eventMask, ref XClientMessageEvent ev);
    [DllImport(LibX11)] private static extern int XGetWindowProperty(IntPtr display, ulong window, ulong property,
        long offset, long length, bool delete, ulong reqType, out ulong actualType, out int actualFormat,
        out ulong nItems, out ulong bytesAfter, out IntPtr data);
}

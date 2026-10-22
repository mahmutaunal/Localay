using System.Runtime.InteropServices;

namespace AlpWareStudio.Localay;

// Thin Win32 reader: portable tests exercise the policy without invoking these APIs.
internal static class GameWindowObserver
{
    internal static GameWindowIdentity Identify(nint window)
    {
        if (window == 0 || !IsWindow(window)) return default;
        GetWindowThreadProcessId(window, out uint processId);
        return new(window, processId);
    }

    internal static GameWindowObservation Read(GameWindowIdentity selected)
    {
        var current = Identify(selected.Handle);
        if (!current.IsValid || current != selected) return new(current, false, false, false, false);
        nint foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out uint foregroundProcess);
        // Only this window and its owned dialogs count; an unrelated window from the
        // same process, a launcher, or an unowned third-party overlay does not.
        bool focused = IsForeground(selected, new(foreground, foregroundProcess),
            foreground == 0 ? 0 : GetAncestor(foreground, 3));
        return new(current, true, IsWindowVisible(selected.Handle), IsIconic(selected.Handle), focused);
    }

    internal static bool IsForeground(GameWindowIdentity selected, GameWindowIdentity foreground, nint rootOwner) =>
        selected.IsValid && foreground.IsValid && foreground.ProcessId == selected.ProcessId &&
        (foreground.Handle == selected.Handle || rootOwner == selected.Handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);
}

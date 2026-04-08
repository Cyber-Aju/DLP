using System.Runtime.InteropServices;
using System.Text;
using System.Runtime.Versioning; // 1. ADD THIS

namespace dlp_agent;

[SupportedOSPlatform("windows")] // 2. ADD THIS
public class AppTracker
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    // Notice we changed the return type to string? (nullable)
    public static string? GetActiveWindowTitle()
    {
        // 1. If the PC is locked, instantly return null. Don't track anything!
        if (SessionMonitor.IsLocked)
        {
            return null;
        }

        const int nChars = 256;
        StringBuilder buff = new StringBuilder(nChars);
        IntPtr handle = GetForegroundWindow();

        if (GetWindowText(handle, buff, nChars) > 0)
        {
            return buff.ToString();
        }
        return "Unknown/Desktop";
    }
}
using System.Runtime.InteropServices;

namespace dlp_agent;

public class NotificationManager
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, int options);

    public static void ShowWarning(string message, bool isStrictBlock)
    {
        // 0x30 = Warning Icon, 0x10 = Error/Stop Icon
        int icon = isStrictBlock ? 0x10 : 0x30;
        string title = isStrictBlock ? "SECURITY VIOLATION (BLOCKED)" : "SECURITY WARNING";
        
        // Run on a background thread so it doesn't pause the DLP agent while waiting for the user to click OK
        Task.Run(() => {
            MessageBox(IntPtr.Zero, message, title, icon);
        });
    }
}
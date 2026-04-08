using System.Runtime.InteropServices;

namespace dlp_agent;

public class ClipboardMonitor
{
    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint format);

    // ADD THIS LINE BACK IN:
    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    private const uint CF_UNICODETEXT = 13;
    private static string _lastCopiedText = "";

    private List<string> _bannedKeywords = new();
    private string _mode = "WARN";

    public void UpdateRules(List<string> keywords, string mode)
    {
        _bannedKeywords = keywords;
        _mode = mode;
    }

    public string? CheckAndEnforceClipboard()
    {
        try
        {
            if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return null;
            if (!OpenClipboard(IntPtr.Zero)) return null;

            IntPtr dataHandle = GetClipboardData(CF_UNICODETEXT);
            if (dataHandle != IntPtr.Zero)
            {
                string copiedText = Marshal.PtrToStringUni(dataHandle) ?? "";
                
                if (copiedText != _lastCopiedText && !string.IsNullOrWhiteSpace(copiedText))
                {
                    _lastCopiedText = copiedText;

                    // Check against dynamic keywords from Django
                    foreach (var keyword in _bannedKeywords)
                    {
                        if (copiedText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        {
                            bool isBlock = _mode == "BLOCK";
                            
                            // We removed the EmptyClipboard() function here as requested!
                            
                           if (isBlock)
                            {
                                EmptyClipboard(); // Wipe it so they can't paste, but NO POPUP
                                CloseClipboard();
                                return $"MALPRACTICE_BLOCKED: Matched '{keyword}' (Silent Block)";
                            }
                            else
                            {
                                CloseClipboard();
                                return $"MALPRACTICE_WARNING: Matched '{keyword}' (Silent Tracking)";
                            }
                        }
                    }
                    CloseClipboard();
                    return $"COPIED_TEXT: {copiedText}";
                }
            }
            CloseClipboard();
        }
        catch { /* Ignore clipboard locking errors */ }
        
        return null;
    }
}
using System.Reflection;
using System.Runtime.Versioning; // 1. ADD THIS

namespace dlp_agent;

[SupportedOSPlatform("windows")] // 2. ADD THIS
public class DesktopEmailMonitor
{
    private readonly DatabaseManager _dbManager;
    private object? _outlookApp;

    public DesktopEmailMonitor(DatabaseManager dbManager)
    {
        _dbManager = dbManager;
    }

    public void Start()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    // If we haven't hooked into Outlook yet, try to find it
                    if (_outlookApp == null)
                    {
                        TryHookOutlook();
                    }
                }
                catch { /* Silently fail and try again later */ }

                // Check every 30 seconds to see if the user opened Outlook
                await Task.Delay(30000);
            }
        });
    }

    private void TryHookOutlook()
    {
        // 1. Get the Outlook Application type from Windows natively
        Type? outlookType = Type.GetTypeFromProgID("Outlook.Application");
        if (outlookType == null) return; // Outlook is not installed on this PC

        // 2. Try to grab the running instance
        _outlookApp = Activator.CreateInstance(outlookType);
        if (_outlookApp == null) return;

        // 3. Hook into the "ItemSend" event using Reflection
        EventInfo? itemSendEvent = outlookType.GetEvent("ItemSend");
        if (itemSendEvent != null)
        {
            // We create a delegate that points to our OnItemSend method
            Type handlerType = itemSendEvent.EventHandlerType!;
            Delegate d = Delegate.CreateDelegate(handlerType, this, nameof(OnItemSend));
            itemSendEvent.AddEventHandler(_outlookApp, d);
            
            Console.WriteLine("[DESKTOP EMAIL] Successfully hooked into Desktop Outlook.");
        }
    }

    // This method is triggered the exact moment the user clicks "Send" in Desktop Outlook
    public void OnItemSend(object item, ref bool cancel)
    {
        try
        {
            // Read the properties natively
            Type itemType = item.GetType();
            string subject = (string?)itemType.GetProperty("Subject")?.GetValue(item) ?? "No Subject";
            string to = (string?)itemType.GetProperty("To")?.GetValue(item) ?? "";
            string cc = (string?)itemType.GetProperty("CC")?.GetValue(item) ?? "";

            // Log the email metadata to the Hub
            _dbManager.LogEvent("DESKTOP_EMAIL", $"Sent to: {to} | CC: {cc} | Subject: {subject}");

            // Note: If you eventually want to block the email, you would set:
            // cancel = true; 
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DESKTOP EMAIL ERROR] {ex.Message}");
        }
    }
}
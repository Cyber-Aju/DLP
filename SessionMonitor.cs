using Microsoft.Win32;
using System.Runtime.Versioning;

namespace dlp_agent;

[SupportedOSPlatform("windows")]
public class SessionMonitor
{
    private readonly DatabaseManager _dbManager;
    
    // 1. Add this static flag so other classes can check the lock state
    public static bool IsLocked { get; private set; } = false; 

    public SessionMonitor(DatabaseManager dbManager)
    {
        _dbManager = dbManager;
    }

    public void Start()
    {
        SystemEvents.SessionSwitch += (s, e) =>
        {
            // 2. Update the flag instantly when the PC locks or unlocks
            if (e.Reason == SessionSwitchReason.SessionLock) IsLocked = true;
            if (e.Reason == SessionSwitchReason.SessionUnlock) IsLocked = false;

            string state = e.Reason switch
            {
                SessionSwitchReason.SessionLogon => "LOGIN_START",
                SessionSwitchReason.SessionLogoff => "LOGOFF_END",
                SessionSwitchReason.SessionLock => "PC_LOCKED",
                SessionSwitchReason.SessionUnlock => "PC_UNLOCKED",
                _ => e.Reason.ToString()
            };

            _dbManager.LogEvent("SESSION_EVENT", $"User State: {state}");

            // NEW: Take a snapshot of the hardware at this exact moment!
            string inventory = DeviceInventory.GetConnectedInputDevices();
            _dbManager.LogEvent("DEVICE_INVENTORY", $"Trigger ({state}) - Connected: {inventory}");
        };
    }
}
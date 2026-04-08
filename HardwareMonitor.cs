using System.Management;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace dlp_agent;

[SupportedOSPlatform("windows")]
public class HardwareMonitor
{
    private ManagementEventWatcher? _usbWatcher, _cdWatcher, _btWatcher, _phoneWatcher;
    private bool _blockUsb, _blockCd, _blockBt;
    private readonly DatabaseManager _dbManager;

    public HardwareMonitor(DatabaseManager dbManager)
    {
        _dbManager = dbManager;
    }

    public void UpdatePolicy(bool blockUsb, bool blockCd, bool blockBt)
    {
        _blockUsb = blockUsb;
        _blockCd = blockCd;
        _blockBt = blockBt;

        // 1. Block USB Pen Drives instantly via Windows Group Policy Registry
        ApplyStoragePolicy("{53f5630d-b6bf-11d0-94f2-00a0c91efb8b}", blockUsb);
        
        // 2. Block Mobile Phones (WPD/MTP) instantly via Windows Group Policy Registry
        ApplyStoragePolicy("{6AC27878-A6FA-4155-BA85-F98F491D4F33}", blockUsb);

        // 3. Block CD/DVDs instantly
        ApplyStoragePolicy("{53f56308-b6bf-11d0-94f2-00a0c91efb8b}", blockCd);

        // 4. Bluetooth doesn't have a storage policy, so we keep the Service block
        SetLegacyServicePolicy(@"SYSTEM\CurrentControlSet\Services\BTHPORT", blockBt);
    }

    private void ApplyStoragePolicy(string deviceGuid, bool block)
    {
        try
        {
            // This is the official Windows method for instantly blocking Read/Write access!
            string keyPath = $@"SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices\{deviceGuid}";
            
            // CreateSubKey ensures the folders are created if they don't exist yet
            using RegistryKey key = Registry.LocalMachine.CreateSubKey(keyPath, writable: true);
            
            int value = block ? 1 : 0;
            key.SetValue("Deny_Read", value, RegistryValueKind.DWord);
            key.SetValue("Deny_Write", value, RegistryValueKind.DWord);
            key.SetValue("Deny_Execute", value, RegistryValueKind.DWord);
        }
        catch (UnauthorizedAccessException)
        {
            // If you forget to run the agent as Administrator, it will log it here!
            System.IO.File.AppendAllText(@"C:\ProgramData\AerologueDLP\wmi_debug.txt", "[CRITICAL] Run agent as Admin to block devices!\n");
        }
        catch { /* Ignore other errors */ }
    }

    private void SetLegacyServicePolicy(string keyPath, bool block)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(keyPath, writable: true);
            if (key != null) key.SetValue("Start", block ? 4 : 3, RegistryValueKind.DWord);
        }
        catch { /* Needs Admin rights */ }
    }

    public void Start()
    {
        Task.Run(() => {
            try
            {
                // 1. USB Storage (Pen Drives)
                string usbQuery = "SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity' AND TargetInstance.Service = 'USBSTOR'";
                _usbWatcher = new ManagementEventWatcher(new WqlEventQuery(usbQuery));
                _usbWatcher.EventArrived += (s, e) => CheckHardware("USB", _blockUsb);
                _usbWatcher.Start();

                // 2. Mobile Phones (MTP / WPD Devices)
                string phoneQuery = "SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity' AND TargetInstance.PNPClass = 'WPD'";
                _phoneWatcher = new ManagementEventWatcher(new WqlEventQuery(phoneQuery));
                _phoneWatcher.EventArrived += (s, e) => CheckHardware("MOBILE_PHONE", _blockUsb); 
                _phoneWatcher.Start();

                // 3. CD/DVD
                _cdWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_CDROMDrive'"));
                _cdWatcher.EventArrived += (s, e) => CheckHardware("CD_ROM", _blockCd);
                _cdWatcher.Start();

                // 4. Bluetooth
                _btWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity' AND TargetInstance.PNPClass = 'Bluetooth'"));
                _btWatcher.EventArrived += (s, e) => CheckHardware("BLUETOOTH", _blockBt);
                _btWatcher.Start();
            }
            catch (Exception ex) 
            { 
                System.IO.File.AppendAllText(@"C:\ProgramData\AerologueDLP\wmi_debug.txt", $"WMI Crash: {ex.Message}\n"); 
            }
        });
    }

    private void CheckHardware(string deviceType, bool isBlocked)
    {
        // 1. THE BEEP TEST
        System.Console.Beep(800, 500); 

        if (isBlocked)
        {
            NotificationManager.ShowWarning($"{deviceType} Device Blocked by Corporate Policy.", true);
            _dbManager.LogEvent($"{deviceType}_BLOCKED", $"Unauthorized {deviceType} device was connected and blocked.");
        }
        else
        {
            _dbManager.LogEvent($"{deviceType}_CONNECTED", $"A {deviceType} device was connected and allowed.");
        }

        // Wait 1 second for Windows to finish installing the driver, then scan the new inventory!
        Task.Delay(1000).ContinueWith(_ => {
            string inventory = DeviceInventory.GetConnectedInputDevices();
            _dbManager.LogEvent("DEVICE_INVENTORY", $"Trigger ({deviceType} Change) - Connected: {inventory}");
        });
    }
}
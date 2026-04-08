using System.Management;
using System.Runtime.Versioning;

namespace dlp_agent;

[SupportedOSPlatform("windows")]
public static class DeviceInventory
{
    public static string GetConnectedInputDevices()
    {
        List<string> devices = new();
        
        try 
        {
            // 1. Get Keyboards
            using var kbSearcher = new ManagementObjectSearcher("SELECT Description FROM Win32_Keyboard");
            foreach (var device in kbSearcher.Get()) 
                devices.Add($"[KB] {device["Description"]}");

            // 2. Get Mice
            using var mouseSearcher = new ManagementObjectSearcher("SELECT Description FROM Win32_PointingDevice");
            foreach (var device in mouseSearcher.Get()) 
                devices.Add($"[Mouse] {device["Description"]}");

            // 3. NEW: Get Connected USB Pen Drives
            using var usbSearcher = new ManagementObjectSearcher("SELECT Model FROM Win32_DiskDrive WHERE InterfaceType='USB'");
            foreach (var device in usbSearcher.Get()) 
                devices.Add($"[USB Drive] {device["Model"]}");

            // 4. NEW: Get Mobile Phones / Portable Devices (MTP)
            using var phoneSearcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE PNPClass='WPD'");
            foreach (var device in phoneSearcher.Get()) 
                devices.Add($"[Mobile Phone] {device["Name"]}");
        }
        catch { /* Ignore WMI access errors */ }

        // Returns: "[KB] USB Keyboard, [USB Drive] SanDisk Cruzer, [Mobile Phone] Galaxy S23"
        return devices.Count > 0 ? string.Join(", ", devices) : "No devices detected";
    }
}
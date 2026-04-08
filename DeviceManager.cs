using Microsoft.Win32;
using System.Runtime.Versioning;

namespace dlp_agent;

[SupportedOSPlatform("windows")]
public class DeviceManager
{
    public static void SetUsbPolicy(bool block)
    {
        try
        {
            string keyPath = @"SYSTEM\CurrentControlSet\Services\USBSTOR";
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(keyPath, writable: true);
            if (key != null)
            {
                int value = block ? 4 : 3;
                key.SetValue("Start", value, RegistryValueKind.DWord);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Agent is not running as Admin/SYSTEM!
            Console.WriteLine("CRITICAL: Missing Admin privileges to lock USBs.");
        }
    }
}
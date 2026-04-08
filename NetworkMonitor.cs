using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning; // 1. ADD THIS

namespace dlp_agent;

[SupportedOSPlatform("windows")] // 2. ADD THIS
public class NetworkMonitor
{
    private TraceEventSession? _session;
    private readonly DatabaseManager _dbManager;
    private Dictionary<int, long> _bytesSentPerProcess = new();
    private HashSet<int> _killedProcesses = new(); 
    
    private bool _blockUploads = false;

    // Add browsers here so ETW stops killing them on page loads!
    private readonly string[] _whitelist = { "System", "Idle", "svchost", "dlp_agent", "explorer", "lsass", "csrss", "services", "chrome", "msedge", "firefox", "brave" };
    
    public NetworkMonitor(DatabaseManager dbManager)
    {
        _dbManager = dbManager;
    }

    public void UpdatePolicy(bool blockUploads)
    {
        _blockUploads = blockUploads;
        // LOUD CONSOLE: Tell us when the API syncs!
        Console.WriteLine($"[NETWORK] Hub Policy Synced: Upload Blocking is now {(_blockUploads ? "ON" : "OFF")}");
    }

    public void Start()
    {
        Task.Run(() =>
        {
            try
            {
                Console.WriteLine("[NETWORK] Attempting to start ETW Kernel Logger...");

                string sessionName = KernelTraceEventParser.KernelSessionName;

                if (TraceEventSession.GetActiveSessionNames().Contains(sessionName))
                {
                    Console.WriteLine("[NETWORK] Found a ghost session. Stopping it...");
                    var ghostSession = new TraceEventSession(sessionName);
                    ghostSession.Dispose(); 
                    Task.Delay(1000).Wait();
                }

                _session = new TraceEventSession(sessionName);
                _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

                _session.Source.Kernel.TcpIpSend += (data) => TrackBandwidth(data.ProcessID, data.size);
                _session.Source.Kernel.UdpIpSend += (data) => TrackBandwidth(data.ProcessID, data.size);

                Console.WriteLine("[NETWORK] SUCCESS! ETW Kernel Logger is ACTIVE and listening!");
                System.Console.Beep(1000, 300);

                _session.Source.Process(); 
            }
            catch (Exception ex)
            {
                // LOUD CONSOLE: Print exact error to PowerShell
                Console.WriteLine($"[NETWORK CRITICAL ERROR] {ex.Message}");
                
                try
                {
                    // Create the folder so the text file doesn't fail!
                    Directory.CreateDirectory(@"C:\ProgramData\AerologueDLP");
                    File.AppendAllText(@"C:\ProgramData\AerologueDLP\net_debug.txt", $"[STARTUP ERROR] {ex.Message}\n");
                }
                catch { /* Ignore */ }
                
                _dbManager.LogEvent("ETW_ERROR", $"Kernel tracking failed: {ex.Message}");
            }
        });
    }

    private void TrackBandwidth(int processId, int bytesSent)
    {
        lock (_bytesSentPerProcess)
        {
            if (_killedProcesses.Contains(processId)) return; 

            if (!_bytesSentPerProcess.ContainsKey(processId))
                _bytesSentPerProcess[processId] = 0;
            
            _bytesSentPerProcess[processId] += bytesSent;

            if (_bytesSentPerProcess[processId] > 100000) 
            {
                EnforcePolicy(processId, _bytesSentPerProcess[processId]);
                _bytesSentPerProcess[processId] = 0; 
            }
        }
    }

    private void EnforcePolicy(int processId, long totalBytes)
    {
        try
        {
            Process proc = Process.GetProcessById(processId);
            string processName = proc.ProcessName;

            foreach (var safeApp in _whitelist)
            {
                if (processName.Equals(safeApp, StringComparison.OrdinalIgnoreCase)) return;
            }

            long kilobytes = totalBytes / 1024;
            
            // LOUD CONSOLE: Tell us when an app uploads heavy data!
            Console.WriteLine($"[NETWORK ALERT] {processName}.exe uploaded {kilobytes} KB.");

            if (_blockUploads)
            {
                _killedProcesses.Add(processId); 
                proc.Kill(); 
                
                Console.WriteLine($"[NETWORK ENFORCEMENT] KILLED {processName}.exe!");
                NotificationManager.ShowWarning($"Network Upload Blocked. {processName}.exe was terminated for leaking data.", true);
                _dbManager.LogEvent("UPLOAD_BLOCKED", $"{processName}.exe killed for exceeding limits ({kilobytes} KB)");
            }
            else
            {
                _dbManager.LogEvent("LARGE_UPLOAD", $"{processName}.exe uploaded {kilobytes} KB");
            }
        }
        catch (Exception ex) 
        { 
            Console.WriteLine($"[NETWORK PROCESS ERROR] PID {processId}: {ex.Message}");
        }
    }

    public void ReportAndResetBandwidth()
    {
        lock (_bytesSentPerProcess)
        {
            _bytesSentPerProcess.Clear(); 
            _killedProcesses.Clear(); 
        }
    }

    public void Stop()
    {
        _session?.Dispose();
    }
}
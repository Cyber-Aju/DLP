using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
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

    private readonly HashSet<string> _trackedUploadProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome",
        "msedge",
        "firefox",
        "brave",
        "opera",
        "edge",
        "iexplore",
        "outlook",
        "thunderbird"
    };
    
    public NetworkMonitor(DatabaseManager dbManager)
    {
        _dbManager = dbManager;
    }

    public void UpdatePolicy(bool blockUploads)
    {
        _blockUploads = blockUploads;
        // LOUD CONSOLE: Tell us when the API syncs!
        LogManager.LogInfo($"[NETWORK] Hub Policy Synced: Upload Blocking is now {(_blockUploads ? "ON" : "OFF")}");
    }

    public void Start()
    {
        Task.Run(() =>
        {
            try
            {
                LogManager.LogInfo("[NETWORK] Attempting to start ETW Kernel Logger...");

                string sessionName = KernelTraceEventParser.KernelSessionName;

                if (TraceEventSession.GetActiveSessionNames().Contains(sessionName))
                {
                    LogManager.LogInfo("[NETWORK] Found a ghost session. Stopping it...");
                    var ghostSession = new TraceEventSession(sessionName);
                    ghostSession.Dispose(); 
                    Task.Delay(1000).Wait();
                }

                _session = new TraceEventSession(sessionName);
                _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

                _session.Source.Kernel.TcpIpSend += (data) => TrackBandwidth(data.ProcessID, data.size);
                _session.Source.Kernel.UdpIpSend += (data) => TrackBandwidth(data.ProcessID, data.size);

                LogManager.LogInfo("ETW Kernel Logger started successfully and listening for network events");
                _session.Source.Process(); 
            }
            catch (Exception ex)
            {
                string? accessMessage = ex.Message.Contains("Access is denied") || ex is UnauthorizedAccessException
                    ? "ETW kernel tracing requires administrator privileges. Run the agent elevated or install it as a service to enable kernel upload monitoring."
                    : null;

                if (!string.IsNullOrEmpty(accessMessage))
                {
                    LogManager.LogWarning(accessMessage);
                }

                LogManager.LogError("Kernel network tracking failed", ex);
                _dbManager.LogEvent("ETW_ERROR", $"Kernel tracking failed: {ex.Message}");

                try
                {
                    Directory.CreateDirectory(@"C:\ProgramData\AerologueDLP");
                    File.AppendAllText(@"C:\ProgramData\AerologueDLP\net_debug.txt", $"[STARTUP ERROR] {ex.Message}\n");
                }
                catch { /* Ignore */ }
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

            if (!_trackedUploadProcesses.Contains(processName))
            {
                LogManager.LogInfo($"[NETWORK IGNORE] {processName}.exe is not considered a user-facing upload process.");
                return;
            }

            long kilobytes = totalBytes / 1024;
            
            LogManager.LogInfo($"[NETWORK ALERT] {processName}.exe uploaded {kilobytes} KB.");

            if (_blockUploads)
            {
                _killedProcesses.Add(processId); 
                proc.Kill(); 
                
                LogManager.LogInfo($"[NETWORK ENFORCEMENT] KILLED {processName}.exe!");
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
            LogManager.LogInfo($"[NETWORK PROCESS ERROR] PID {processId}: {ex.Message}");
        }
    }

    public void ReportAndResetBandwidth()
    {
        lock (_bytesSentPerProcess)
        {
            if (_bytesSentPerProcess.Count > 0)
            {
                LogManager.LogInfo($"NETWORK STATUS: Tracking {_bytesSentPerProcess.Count} processes for uploads");
                foreach (var kvp in _bytesSentPerProcess)
                {
                    long kb = kvp.Value / 1024;
                    LogManager.LogInfo($"NETWORK PROC: PID {kvp.Key} has sent {kb} KB");
                }
            }
            else
            {
                LogManager.LogInfo("NETWORK STATUS: No upload activity detected in this cycle");
            }
            
            _bytesSentPerProcess.Clear(); 
            _killedProcesses.Clear(); 
        }
    }

    public void Stop()
    {
        _session?.Dispose();
    }
}
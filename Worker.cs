using System.Text;
using System.Text.Json;
using System.Runtime.Versioning;

namespace dlp_agent;

[SupportedOSPlatform("windows")]
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly HttpClient _httpClient;
    private readonly DatabaseManager _dbManager;
    private readonly TransmissionMonitor _fileMonitor;
    private readonly NetworkMonitor _networkMonitor;
    private readonly ClipboardMonitor _clipboardMon;
    
    // The new unified monitors
    private readonly HardwareMonitor _hardwareMonitor; 
    private readonly SessionMonitor _sessionMonitor;

    private readonly ProxyMonitor _proxyMonitor;
    private readonly DesktopEmailMonitor _desktopEmailMonitor;
    
    // Config
    private string _hubUrl = "";
    private string _licenseKey = "";
    private bool _isLicenseValid = false;

    // Add this line to remember the last app:
    private string _lastActiveApp = "";

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _dbManager = new DatabaseManager();
        _clipboardMon = new ClipboardMonitor();
        
        // Initialize our new monitors here
        _hardwareMonitor = new HardwareMonitor(_dbManager); 
        _sessionMonitor = new SessionMonitor(_dbManager);
        
        _fileMonitor = new TransmissionMonitor(_dbManager); 
        _networkMonitor = new NetworkMonitor(_dbManager);

        _proxyMonitor = new ProxyMonitor(_dbManager);
        _desktopEmailMonitor = new DesktopEmailMonitor(_dbManager);

        LoadLocalConfig();
    }

   protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 1. Check License FIRST before starting any monitors
        _isLicenseValid = LicenseManager.IsLicenseValid(_licenseKey);

        if (_isLicenseValid)
        {
            try { _hardwareMonitor.Start(); } catch { }
            try { _sessionMonitor.Start(); } catch { }
            try { _desktopEmailMonitor.Start(); } catch { }
            
            // ==========================================
            // PROXY BOOT DELAY FIX
            // ==========================================
            // Wait 10 seconds for the PC to fully connect to the Wi-Fi 
            // before we try to take over the network traffic!
            await Task.Delay(10000, stoppingToken);
            
            try { _networkMonitor.Start(); } catch { }
            try { _proxyMonitor.Start(); } catch (Exception ex) { _dbManager.LogEvent("SYS_ERR", ex.Message); }
        }
        else
        {
            _logger.LogWarning("AGENT DISABLED: License is missing, invalid, or expired.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try 
            {
                // 2. ONLY track data if the license is active!
                if (_isLicenseValid)
                {
                    // 1. Log active app
                    string? rawActiveApp = AppTracker.GetActiveWindowTitle();
                    
                    if (rawActiveApp != null) 
                    {
                        // Clean out any invisible Windows API characters and spaces
                        string activeApp = rawActiveApp.Replace("\0", "").Trim();

                        // Compare them safely, ignoring uppercase/lowercase differences
                        if (!string.Equals(activeApp, _lastActiveApp, StringComparison.OrdinalIgnoreCase)) 
                        {
                            _lastActiveApp = activeApp; // Update our memory
                            _dbManager.LogEvent("ACTIVE_APP", activeApp);
                        }
                    }

                    // 2. Log clipboard (Now silent!)
                    string? clipboardEvent = _clipboardMon.CheckAndEnforceClipboard();
                    if (clipboardEvent != null)
                    {
                        _dbManager.LogEvent("CLIPBOARD_EVENT", clipboardEvent);
                    }

                    // 3. Network uploads
                    _networkMonitor.ReportAndResetBandwidth();

                    // 4. Attempt Sync
                    await SyncWithHubAsync();

                    // 5. Cleanup
                    _dbManager.PurgeOldData();
                }
                else
                {
                    // Re-check the config every 15 seconds in case the Admin updated the key!
                    LoadLocalConfig(); 
                    _isLicenseValid = LicenseManager.IsLicenseValid(_licenseKey);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Agent loop encountered an error: {ex.Message}");
            }
            
            // Poll every 15 seconds for testing
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // CRITICAL: You must shut down the proxy gracefully, or the user's internet will break!
        _proxyMonitor.Stop();
        await base.StopAsync(cancellationToken);
    }

    private async Task SyncWithHubAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_hubUrl)) return; 

            var pendingEvents = _dbManager.GetUnsyncedEvents();
            if (pendingEvents.Count == 0) return;

            // 1. Turn the events into a raw JSON string FIRST
            string rawEventsJson = JsonSerializer.Serialize(pendingEvents);

            // 2. Encrypt that JSON string using the Tenant Key!
            string encryptedData = SecurityHelper.EncryptPayload(rawEventsJson, _licenseKey);

            // 3. Build the new secure payload. 
            // Notice we are no longer sending the raw 'events' array!
            var payload = new
            {
                tenant_key = _licenseKey, 
                machine_name = Environment.MachineName,
                secure_payload = encryptedData // Sent as a single scrambled string
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // We are sending data to the new PHP api.php endpoint now!
            var response = await _httpClient.PostAsync(_hubUrl, content); 
            
            if (response.IsSuccessStatusCode)
            {
                var syncedIds = pendingEvents.Select(e => Convert.ToInt32(e["id"])).ToList();
                _dbManager.MarkAsSynced(syncedIds);

                var responseString = await response.Content.ReadAsStringAsync();
                var policyData = JsonDocument.Parse(responseString);
                var policyNode = policyData.RootElement.GetProperty("policy");
                
                bool blockUsb = policyNode.GetProperty("usb_blocked").GetBoolean();
                bool blockCd = policyNode.GetProperty("cd_blocked").GetBoolean();
                bool blockBt = policyNode.GetProperty("bt_blocked").GetBoolean();
                // 1. NEW: Extract the upload_blocked boolean from your PHP JSON
                bool blockUploads = policyNode.GetProperty("upload_blocked").GetBoolean();

                string mode = policyNode.GetProperty("mode").GetString() ?? "WARN";

                // 1. Update Hardware Rules
                _hardwareMonitor.UpdatePolicy(blockUsb, blockCd, blockBt);

                // 2. NEW: Update Network Rules
                _networkMonitor.UpdatePolicy(blockUploads);
                _proxyMonitor.UpdatePolicy(blockUploads);

                // Extract Keywords
                List<string> keywords = new();
                foreach (var kw in policyNode.GetProperty("keywords").EnumerateArray())
                {
                    keywords.Add(kw.GetString()!);
                }

                // Extract Folders
                List<string> folders = new();
                foreach (var f in policyNode.GetProperty("folders").EnumerateArray())
                {
                    folders.Add(f.GetString()!);
                }

                // 2. Update File and Clipboard Rules
                _clipboardMon.UpdateRules(keywords, mode);
                _fileMonitor.UpdateRules(folders, mode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Network sync failed: {ex.Message}");
        }
    }

    private void LoadLocalConfig()
    {
        string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "agent_config.json");
        if (File.Exists(configPath))
        {
            var configStr = File.ReadAllText(configPath);
            var configDoc = JsonDocument.Parse(configStr);

            // Safely look for HubUrl
            if (configDoc.RootElement.TryGetProperty("HubUrl", out var hubUrlProp))
                _hubUrl = hubUrlProp.GetString() ?? "";
            else
                Console.WriteLine("[ERROR] 'HubUrl' is misspelled or missing in config!");

            // Safely look for LicenseKey
            if (configDoc.RootElement.TryGetProperty("LicenseKey", out var licenseProp))
                _licenseKey = licenseProp.GetString() ?? "";
            else
                Console.WriteLine("[ERROR] 'LicenseKey' is misspelled or missing in config!");
        }
        else
        {
            Console.WriteLine("[CRITICAL] Cannot find agent_config.json at " + configPath);
        }
    }
}
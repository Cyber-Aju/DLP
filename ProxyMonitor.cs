using System.Net;
using System.Text.RegularExpressions;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;
using System.Runtime.Versioning;

namespace dlp_agent;

[SupportedOSPlatform("windows")]
public class ProxyMonitor
{
    private ProxyServer _proxyServer;
    private readonly DatabaseManager _dbManager;
    private bool _blockUploads = false; 

    // NEW: "Cooldown" timers to stop UI and Database spam!
    private DateTime _lastUploadAlert = DateTime.MinValue;
    private DateTime _lastWebmailAlert = DateTime.MinValue;

    public ProxyMonitor(DatabaseManager dbManager)
    {
        _dbManager = dbManager;
        _proxyServer = new ProxyServer();
    }

    public void UpdatePolicy(bool blockUploads)
    {
        _blockUploads = blockUploads;
    }

    public void Start()
    {
        try
        {
            // 1. FORCE PROXY TO BE MACHINE-WIDE (Requires Admin/SYSTEM)
            // This ensures Chrome uses the proxy, even though the Agent is running as a Service!
            // using (var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings"))
            // {
            //     key.SetValue("ProxySettingsPerUser", 0, Microsoft.Win32.RegistryValueKind.DWord);
            // }

            _proxyServer.CertificateManager.CreateRootCertificate();
            // Tell Titanium to install the cert into the Machine store so it doesn't prompt the user
            _proxyServer.CertificateManager.TrustRootCertificate(true);

            var explicitEndPoint = new ExplicitProxyEndPoint(IPAddress.Any, 8000, true);
            _proxyServer.AddEndPoint(explicitEndPoint);

            _proxyServer.BeforeRequest += OnRequest;

            _proxyServer.Start();
            _proxyServer.SetAsSystemHttpProxy(explicitEndPoint);
            _proxyServer.SetAsSystemHttpsProxy(explicitEndPoint);
            
            Console.WriteLine("[PROXY] SSL Proxy Started System-Wide...");
        }
        catch (Exception ex)
        {
            // Log the error to DB instead of crashing the app!
            _dbManager.LogEvent("PROXY_CRASH", $"Proxy failed to start: {ex.Message}");
        }
    }

    private async Task OnRequest(object sender, SessionEventArgs e)
    {
        var request = e.HttpClient.Request;

        if (request.Method == "POST" || request.Method == "PUT")
        {
            string host = request.RequestUri.Host.ToLower();
            long contentLength = request.ContentLength;

            // ==========================================
            // 1. FILE UPLOAD TRACKING (With Cooldown)
            // ==========================================
            if (contentLength > 50000) 
            {
                long kb = contentLength / 1024;
                
                if (_blockUploads)
                {
                    // Always block the chunk to secure the data
                    e.Ok("<html><body><h2>Upload Blocked by Corporate Policy</h2></body></html>");
                    
                    // COOLDOWN: Only show popup and log to DB once every 10 seconds
                    if ((DateTime.Now - _lastUploadAlert).TotalSeconds > 10)
                    {
                        _lastUploadAlert = DateTime.Now;
                        _dbManager.LogEvent("UPLOAD_BLOCKED", $"Blocked {kb} KB upload to {host}");
                        NotificationManager.ShowWarning($"Web Upload Blocked.", true);
                        Console.WriteLine($"[PROXY] BLOCKED {kb} KB upload to {host}");
                    }
                }
                else
                {
                    // COOLDOWN: Only log tracking once every 5 seconds per major upload
                    if ((DateTime.Now - _lastUploadAlert).TotalSeconds > 5)
                    {
                        _lastUploadAlert = DateTime.Now;
                        _dbManager.LogEvent("UPLOAD_TRACKED", $"User uploaded {kb} KB to {host}");
                        Console.WriteLine($"[PROXY] TRACKED {kb} KB upload to {host}");
                    }
                }
                return; 
            }

            // ==========================================
            // 2. WEBMAIL TRACKING (Strict Outbound Filter)
            // ==========================================
            if (host.Contains("mail.google.com") || host.Contains("outlook.live.com") || host.Contains("mail.yahoo.com"))
            {
                byte[] bodyBytes = await e.GetRequestBody();
                string bodyString = System.Text.Encoding.UTF8.GetString(bodyBytes);
                bodyString = System.Web.HttpUtility.UrlDecode(bodyString);

                // 1. FILTER OUT "READS" AND INBOX SYNCS
                // If the payload contains Gmail's internal labels for "Opened" (^o), "Unread" (^u), 
                // or "Inbox" (^i), it means the user is just looking at their inbox. Ignore it!
                if (bodyString.Contains("[\"^o\"]") || bodyString.Contains("\"^u\"") || bodyString.Contains("\"^i\""))
                {
                    return; 
                }

                // 2. Enforce the 5-second cooldown for actual sends/drafts
                if ((DateTime.Now - _lastWebmailAlert).TotalSeconds > 5)
                {
                    if (bodyString.Length > 200)
                    {
                        // 3. Find all email addresses
                        MatchCollection emailMatches = Regex.Matches(bodyString, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
                        
                        // We require at least 2 emails (The sender's email + The target's email) to consider it an outbound message
                        if (emailMatches.Count > 1) 
                        {
                            HashSet<string> uniqueEmails = new HashSet<string>();
                            foreach (Match match in emailMatches) uniqueEmails.Add(match.Value.ToLower());
                            string allFoundEmails = string.Join(", ", uniqueEmails);
                            
                            // 4. GRAB THE SUBJECT & BODY
                            // We grab a massive 2000-character chunk so it doesn't cut off the subject!
                            string snippet = bodyString.Length > 2000 ? bodyString.Substring(0, 2000) : bodyString;

                            // 5. CLEAN THE JUNK
                            // We strip out all the JSON brackets and "nulls" so the Subject and Body text is easy to read in your database
                            snippet = snippet.Replace("null", "").Replace("[", "").Replace("]", "").Replace("\"", " ");
                            // Collapse multiple spaces into one
                            snippet = Regex.Replace(snippet, @"\s+", " "); 

                            _lastWebmailAlert = DateTime.Now; 
                            
                            _dbManager.LogEvent("WEBMAIL_TRACKED", $"Outbound on {host}. Emails: [{allFoundEmails}]. Payload: {snippet.Trim()}");
                            Console.WriteLine($"[PROXY] Webmail Outbound Tracked! Targets: {allFoundEmails}");
                        }
                    }
                }
            }
        }
    }

    public void Stop()
    {
        _proxyServer.RestoreOriginalProxySettings();
        _proxyServer.Stop();
        Console.WriteLine("[PROXY] SSL Proxy Stopped.");
    }
}
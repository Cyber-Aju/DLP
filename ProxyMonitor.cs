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

    private DateTime _lastUploadAlert = DateTime.MinValue;
    private DateTime _lastWebmailAlert = DateTime.MinValue;
    
    // NEW: A padlock to prevent multiple threads from spamming popups!
    private readonly object _alertLock = new object();

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
            _proxyServer.CertificateManager.CreateRootCertificate();
            _proxyServer.CertificateManager.TrustRootCertificate(true);

            var explicitEndPoint = new ExplicitProxyEndPoint(IPAddress.Parse("127.0.0.1"), 8000, true);
            _proxyServer.AddEndPoint(explicitEndPoint);

            _proxyServer.BeforeRequest += OnRequest;

            _proxyServer.Start();
            _proxyServer.SetAsSystemHttpProxy(explicitEndPoint);
            _proxyServer.SetAsSystemHttpsProxy(explicitEndPoint);
            
            Console.WriteLine("[PROXY] SSL Proxy Started System-Wide...");
        }
        catch (Exception ex)
        {
            _dbManager.LogEvent("PROXY_CRASH", $"Proxy failed to start: {ex.Message}");
            Console.WriteLine($"[PROXY ERROR] {ex.Message}");
        }
    }

    private async Task OnRequest(object sender, SessionEventArgs e)
    {
        var request = e.HttpClient.Request;
        string host = request.RequestUri.Host.ToLower();

        if (request.Method == "POST" || request.Method == "PUT")
        {
            long contentLength = request.ContentLength;

            // ==========================================
            // 1. FILE UPLOAD TRACKING 
            // ==========================================
            
            // IGNORE noisy background telemetry domains from the upload blocker
            string[] safeDomains = { "play.google.com", "googleapis.com", "youtube.com", "clients.google.com", "clients4.google.com" };
            if (safeDomains.Any(d => host.Contains(d))) return;

            // Bumped to 25 KB (25000 bytes) to ignore small JSON heartbeat syncs, but catch files!
            if (contentLength > 25000) 
            {
                long kb = contentLength / 1024;
                
                if (_blockUploads)
                {
                    e.Ok("<html><body><h2>Upload Blocked by Corporate Policy</h2></body></html>");
                    
                    lock (_alertLock) 
                    {
                        if ((DateTime.Now - _lastUploadAlert).TotalSeconds > 10)
                        {
                            _lastUploadAlert = DateTime.Now;
                            _dbManager.LogEvent("UPLOAD_BLOCKED", $"Blocked {kb} KB upload to {host}");
                            NotificationManager.ShowWarning($"Web Upload Blocked.", true);
                            Console.WriteLine($"[PROXY] BLOCKED {kb} KB upload to {host}");
                        }
                    }
                }
                else
                {
                    lock (_alertLock)
                    {
                        if ((DateTime.Now - _lastUploadAlert).TotalSeconds > 5)
                        {
                            _lastUploadAlert = DateTime.Now;
                            _dbManager.LogEvent("UPLOAD_TRACKED", $"User uploaded {kb} KB to {host}");
                            Console.WriteLine($"[PROXY] TRACKED {kb} KB upload to {host}");
                        }
                    }
                }
                return; 
            }

            // ==========================================
            // 2. WEBMAIL TRACKING (Strict Outbound Filter)
            // ==========================================
            // ADDED outlook.office.com and outlook.office365.com to catch "New Outlook" traffic!
            if (host.Contains("mail.google.com") || host.Contains("outlook.live.com") || host.Contains("outlook.office.com") || host.Contains("outlook.office365.com") || host.Contains("mail.yahoo.com"))
            {
                byte[] bodyBytes = await e.GetRequestBody();
                string bodyString = System.Text.Encoding.UTF8.GetString(bodyBytes);
                bodyString = System.Web.HttpUtility.UrlDecode(bodyString);

                if (bodyString.Contains("[\"^o\"]") || bodyString.Contains("\"^u\"") || bodyString.Contains("\"^i\""))
                {
                    return; 
                }

                lock (_alertLock) 
                {
                    if ((DateTime.Now - _lastWebmailAlert).TotalSeconds > 5)
                    {
                        if (bodyString.Length > 200)
                        {
                            MatchCollection emailMatches = Regex.Matches(bodyString, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
                            
                            if (emailMatches.Count > 1) 
                            {
                                HashSet<string> uniqueEmails = new HashSet<string>();
                                foreach (Match match in emailMatches) uniqueEmails.Add(match.Value.ToLower());
                                string allFoundEmails = string.Join(", ", uniqueEmails);
                                
                                string snippet = bodyString.Length > 2000 ? bodyString.Substring(0, 2000) : bodyString;

                                // --- THE NEW AGGRESSIVE GMAIL CLEANER ---
                                
                                // 1. Strip all HTML tags (<div...>, <br>, etc)
                                snippet = Regex.Replace(snippet, "<.*?>", " ");
                                // 2. Strip Google's thread and message IDs
                                snippet = Regex.Replace(snippet, @"(thread-[a-z]:[a-zA-Z0-9-]+)|(msg-[a-z]:[a-zA-Z0-9-]+)", "");
                                // 3. Strip Google's internal state flags (e.g. ^io_lr30s)
                                snippet = Regex.Replace(snippet, @"\^[a-zA-Z0-9_]+", "");
                                // 4. Remove all the leftover JSON junk and commas
                                snippet = snippet.Replace("null", "").Replace("[", "").Replace("]", "").Replace("\"", " ");
                                snippet = Regex.Replace(snippet, @"[,|\\/]+", " ");
                                // 5. Collapse all remaining giant gaps into a single space
                                snippet = Regex.Replace(snippet, @"\s{2,}", " ").Trim();

                                _lastWebmailAlert = DateTime.Now; 
                                
                                _dbManager.LogEvent("WEBMAIL_TRACKED", $"Outbound on {host}. Emails: [{allFoundEmails}]. Payload: {snippet}");
                                Console.WriteLine($"[PROXY] Webmail Outbound Tracked! Targets: {allFoundEmails}");
                            }
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
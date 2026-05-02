using System.Net;
using System.Text.RegularExpressions;
using System.Linq;
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
    private readonly object _alertLock = new object();

    private static readonly string[] UserUploadContentTypes = new[]
    {
        "multipart/form-data",
        "application/x-www-form-urlencoded",
        "application/octet-stream",
        "image/jpeg",
        "image/png",
        "application/pdf",
        "application/zip"
    };

    private static readonly string[] UploadPathKeywords = new[]
    {
        "upload",
        "attach",
        "attachment",
        "file",
        "media",
        "blob",
        "send"
    };

    private static readonly string[] BackgroundHostIgnore = new[]
    {
        "antigravity",
        "modelserver",
        "telemetry",
        "metrics",
        "analytics"
    };

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
            
            // Try to set system proxy, but don't fail if it doesn't work (e.g., when running as LocalSystem)
            try
            {
                _proxyServer.SetAsSystemHttpProxy(explicitEndPoint);
                _proxyServer.SetAsSystemHttpsProxy(explicitEndPoint);
                Console.WriteLine("[PROXY] SSL Proxy Started System-Wide...");
                LogManager.LogInfo("Proxy started with system-wide proxy settings");
            }
            catch (Exception)
            {
                // If system proxy fails, try to set as current user proxy
                try
                {
                    _proxyServer.SetAsSystemHttpProxy(explicitEndPoint);
                    _proxyServer.SetAsSystemHttpsProxy(explicitEndPoint);
                    Console.WriteLine("[PROXY] SSL Proxy Started (User-level proxy only - LocalSystem limitation)");
                    LogManager.LogInfo("Proxy started with user-level proxy settings only");
                }
                catch (Exception userProxyEx)
                {
                    Console.WriteLine($"[PROXY] SSL Proxy Started (No system proxy - running in limited mode): {userProxyEx.Message}");
                    LogManager.LogError("Proxy started without proxy settings", userProxyEx);
                }
            }
        }
        catch (Exception ex)
        {
            _dbManager.LogEvent("PROXY_CRASH", $"Proxy failed to start: {ex.Message}");
            LogManager.LogError("Proxy failed to start", ex);
            Console.WriteLine($"[PROXY ERROR] {ex.Message}");
        }
    }

    private DateTime _lastProxyHeartbeat = DateTime.MinValue;
    
    private async Task OnRequest(object sender, SessionEventArgs e)
    {
        dynamic request = e.HttpClient.Request;
        string host = ((Uri)request.RequestUri).Host.ToLowerInvariant();
        string method = request.Method;

        // Log a heartbeat every 30 seconds to show proxy is active
        if ((DateTime.Now - _lastProxyHeartbeat).TotalSeconds > 30)
        {
            _lastProxyHeartbeat = DateTime.Now;
            LogManager.LogInfo("PROXY HEARTBEAT: Proxy is active and intercepting requests");
        }

        if (request.Method != "POST" && request.Method != "PUT")
        {
            return;
        }

        // Always check for webmail metadata extraction on webmail hosts
        bool webmailMetadataExtracted = await TryExtractWebmailMetadataAsync(e, request, host);
        if (webmailMetadataExtracted)
        {
            // Continue to check for uploads even if metadata was extracted
        }

        long contentLength = request.ContentLength;
        if (!IsLikelyManualUploadRequest(request, host, contentLength))
        {
            return;
        }

        long kb = contentLength / 1024;

        if (_blockUploads)
        {
            e.Ok("<html><body><h2>Upload Blocked by Corporate Policy</h2></body></html>");

            lock (_alertLock)
            {
                if ((DateTime.Now - _lastUploadAlert).TotalSeconds > 10)
                {
                    _lastUploadAlert = DateTime.Now;
                    _dbManager.LogEvent("UPLOAD_BLOCKED", $"Blocked {kb} KB manual upload to {host}");
                    NotificationManager.ShowWarning($"Web Upload Blocked.", true);
                    Console.WriteLine($"[PROXY] BLOCKED {kb} KB manual upload to {host}");
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
                    _dbManager.LogEvent("UPLOAD_TRACKED", $"Manual upload tracked: {kb} KB to {host}");
                    Console.WriteLine($"[PROXY] TRACKED manual upload {kb} KB to {host}");
                }
            }
        }
    }

    private async Task<bool> TryExtractWebmailMetadataAsync(SessionEventArgs e, dynamic request, string host)
    {
        if (!host.Contains("mail.google.com") && !host.Contains("gmail.com") &&
            !host.Contains("outlook.live.com") && !host.Contains("outlook.office.com") &&
            !host.Contains("outlook.office365.com") && !host.Contains("mail.yahoo.com"))
        {
            return false;
        }

        string path = ((Uri)request.RequestUri).AbsolutePath.ToLowerInvariant();
        string contentType = request.ContentType?.ToLowerInvariant() ?? string.Empty;

        if (string.IsNullOrEmpty(contentType))
        {
            foreach (var header in request.Headers)
            {
                try
                {
                    string name = header.Name;
                    if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        contentType = ((string)header.Value).ToLowerInvariant();
                        break;
                    }
                }
                catch
                {
                    continue;
                }
            }
        }

        byte[] bodyBytes = await e.GetRequestBody();
        string bodyString = System.Net.WebUtility.UrlDecode(System.Text.Encoding.UTF8.GetString(bodyBytes));
        if (string.IsNullOrEmpty(bodyString))
        {
            return false;
        }

        // Skip Outlook's internal Exchange protocol requests - they're not real email operations
        if (host.Contains("outlook"))
        {
            // Skip known Outlook internal API calls
            if (bodyString.Contains("JsonRequest:#Exchange") || 
                bodyString.Contains("RequestServerVersion") ||
                bodyString.Contains("@odata.type") ||
                bodyString.Contains("AppName OWA") ||
                bodyString.Contains("Scenario Name") ||
                bodyString.Contains("EntityRequests") ||
                bodyString.Contains("Cvid") ||
                bodyString.Contains("EntityContext"))
            {
                LogManager.LogInfo($"WEBMAIL SKIP: Outlook internal Exchange/API request detected");
                return false;
            }
        }

        // Skip internal webmail state payloads
        if (bodyString.Contains("[\"^o\"]") || bodyString.Contains("\"^u\"") || bodyString.Contains("\"^i\""))
        {
            return false;
        }

        // Extract email addresses
        MatchCollection emailMatches = Regex.Matches(bodyString, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
        HashSet<string> uniqueEmails = new HashSet<string>();
        foreach (Match match in emailMatches) 
        {
            string email = match.Value.ToLower();
            // Skip Outlook's internal shadow addresses and generic Exchange addresses
            if (!email.Contains("shadow.outlook.com") && !email.Contains("@outlook.com"))
                uniqueEmails.Add(email);
        }
        string allFoundEmails = string.Join(", ", uniqueEmails);

        // For Outlook: Only log if there are actual recipients (not internal state)
        if (host.Contains("outlook") && uniqueEmails.Count == 0)
        {
            LogManager.LogInfo($"WEBMAIL SKIP: Outlook request with no recipients detected");
            return false;
        }

        // Extract potential subject (look for common patterns)
        string subject = ExtractSubjectFromBody(bodyString);

        // Skip if we got the Exchange internal marker
        if (subject.Contains("[Outlook Internal Request]"))
        {
            return false;
        }

        // Create metadata entry
        string metadata = $"Subject: '{subject}' | Recipients: [{allFoundEmails}] | Size: {bodyString.Length}";
        
        _dbManager.LogEvent("WEBMAIL_METADATA", $"Webmail metadata on {host}: {metadata}");
        LogManager.LogInfo($"WEBMAIL METADATA EXTRACTED: host={host} subject='{subject}' recipients=[{allFoundEmails}] size={bodyString.Length}");

        return true;
    }

    private string ExtractSubjectFromBody(string body)
    {
        // Skip Outlook's internal Exchange request types - they're not real email data
        if (body.Contains("JsonRequest:#Exchange") || 
            body.Contains("RequestServerVersion") ||
            body.Contains("TimeZoneContext") ||
            body.Contains("__type") ||
            body.Contains("@odata.type"))
        {
            LogManager.LogInfo("WEBMAIL SKIP: Outlook internal Exchange protocol data, skipping");
            return "[Outlook Internal Request]";
        }

        // Apply the same aggressive cleaning as the old webmail tracking code
        string cleanedBody = body;

        // 1. Strip all HTML tags (<div...>, <br>, etc)
        cleanedBody = Regex.Replace(cleanedBody, "<.*?>", " ");
        // 2. Strip Google's thread and message IDs
        cleanedBody = Regex.Replace(cleanedBody, @"(thread-[a-z]:[a-zA-Z0-9-]+)|(msg-[a-z]:[a-zA-Z0-9-]+)", "");
        // 3. Strip Google's internal state flags (e.g. ^io_lr30s)
        cleanedBody = Regex.Replace(cleanedBody, @"\^[a-zA-Z0-9_]+", "");
        // 4. Remove all the leftover JSON junk and commas
        cleanedBody = cleanedBody.Replace("null", "").Replace("[", "").Replace("]", "").Replace("\"", " ");
        cleanedBody = Regex.Replace(cleanedBody, @"[,|\/]+", " ");
        // 5. Collapse all remaining giant gaps into a single space
        cleanedBody = Regex.Replace(cleanedBody, @"\s{2,}", " ").Trim();

        // Look for subject patterns in the cleaned body
        var subjectPatterns = new[]
        {
            @"subject[""\s]*:[\s""]*([^""\n\r]+)",
            @"Subject[""\s]*:[\s""]*([^""\n\r]+)",
            @"subj[""\s]*:[\s""]*([^""\n\r]+)",
            @"title[""\s]*:[\s""]*([^""\n\r]+)"
        };

        foreach (var pattern in subjectPatterns)
        {
            var match = Regex.Match(cleanedBody, pattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
            {
                string subject = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(subject) && subject.Length > 3 && subject.Length < 200)
                {
                    return subject;
                }
            }
        }

        // Fallback: extract first meaningful text snippet from cleaned body
        var words = cleanedBody.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                              .Where(w => w.Length > 2 && !w.All(char.IsDigit))
                              .Take(10)
                              .ToArray();
        
        if (words.Length > 3)
        {
            return string.Join(" ", words) + "...";
        }
        
        return cleanedBody.Length > 50 ? cleanedBody.Substring(0, 50) + "..." : cleanedBody;
    }

    private bool IsLikelyManualUploadRequest(dynamic request, string host, long contentLength)
    {
        if (contentLength <= 25000) return false;

        if (BackgroundHostIgnore.Any(background => host.Contains(background))) return false;

        string contentType = request.ContentType?.ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrEmpty(contentType))
        {
            foreach (var header in request.Headers)
            {
                try
                {
                    string name = header.Name;
                    if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        contentType = ((string)header.Value).ToLowerInvariant();
                        break;
                    }
                }
                catch
                {
                    continue;
                }
            }
        }

        if (UserUploadContentTypes.Any(type => contentType.Contains(type)))
        {
            return true;
        }

        string path = request.RequestUri.AbsolutePath.ToLowerInvariant();
        string query = request.RequestUri.Query.ToLowerInvariant();

        if (UploadPathKeywords.Any(keyword => path.Contains(keyword) || query.Contains(keyword)))
        {
            return true;
        }

        if (host.Contains("mail.google.com") || host.Contains("gmail.com") ||
            host.Contains("outlook.live.com") || host.Contains("outlook.office.com") ||
            host.Contains("outlook.office365.com") || host.Contains("mail.yahoo.com"))
        {
            return path.Contains("upload") || path.Contains("attach") || contentType.Contains("multipart/form-data");
        }

        return false;
    }

    public void Stop()
    {
        try
        {
            _proxyServer.RestoreOriginalProxySettings();
            _proxyServer.Stop();
            Console.WriteLine("[PROXY] SSL Proxy Stopped.");
        }
        catch (Exception ex)
        {
            LogManager.LogInfo($"ProxyMonitor.Stop() encountered: {ex.Message}");
            Console.WriteLine($"[PROXY] Stop encountered error (this may be OK if proxy never started): {ex.Message}");
        }
    }
}
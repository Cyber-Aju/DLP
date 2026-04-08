using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace dlp_agent;

public static class LicenseManager
{
    // In a real production app, this secret is deeply hidden/obfuscated in the C# code
    private const string LICENSE_SECRET = "AEROLOGUE_MASTER_SIGNING_SECRET_999!";

    public static bool IsLicenseValid(string licenseKey)
    {
        try
        {
            // The key looks like: Base64(JSON).Base64(Signature)
            string[] parts = licenseKey.Split('.');
            if (parts.Length != 2) return false;

            string payloadBase64 = parts[0];
            string providedSignature = parts[1];

            // 1. Verify the Cryptographic Signature (Stops hackers from changing the date)
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(LICENSE_SECRET));
            byte[] computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadBase64));
            string expectedSignature = Convert.ToBase64String(computedHash);

            if (providedSignature != expectedSignature)
            {
                Console.WriteLine("[LICENSE] CRITICAL: License signature is invalid or tampered with!");
                return false;
            }

            // 2. Decode the JSON and check the Expiry Date
            string jsonPayload = Encoding.UTF8.GetString(Convert.FromBase64String(payloadBase64));
            var licenseData = JsonDocument.Parse(jsonPayload);
            
            string expiryString = licenseData.RootElement.GetProperty("expiry").GetString() ?? "";
            DateTime expiryDate = DateTime.Parse(expiryString);

            // 3. OFFLINE KILL SWITCH: Is today past the expiration date?
            if (DateTime.Now > expiryDate)
            {
                Console.WriteLine($"[LICENSE] EXPIRED! License ended on {expiryDate.ToShortDateString()}");
                return false;
            }

            Console.WriteLine($"[LICENSE] VALID. Expires on {expiryDate.ToShortDateString()}");
            return true;
        }
        catch 
        {
            return false; // If anything goes wrong parsing it, default to locked down!
        }
    }
}
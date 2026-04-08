using System.Security.Cryptography;
using System.Text;

namespace dlp_agent;

public static class SecurityHelper
{
    public static string EncryptPayload(string plainText, string secretTenantKey)
    {
        using Aes aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;

        // 1. Create a perfect 32-byte (256-bit) key by hashing your Tenant Key
        using SHA256 sha256 = SHA256.Create();
        aes.Key = sha256.ComputeHash(Encoding.UTF8.GetBytes(secretTenantKey));

        // 2. Generate a random 16-byte Initialization Vector (IV)
        aes.GenerateIV();

        using MemoryStream ms = new MemoryStream();
        // 3. Attach the plain, unencrypted IV to the very beginning of our payload
        // (The PHP server needs to know the IV to unlock it, this is perfectly secure!)
        ms.Write(aes.IV, 0, aes.IV.Length);

        // 4. Encrypt the actual JSON data and attach it after the IV
        using CryptoStream cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write);
        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        cs.Write(plainBytes, 0, plainBytes.Length);
        cs.FlushFinalBlock();

        // 5. Convert the whole chunk into a safe Base64 string so it travels over HTTP safely
        return Convert.ToBase64String(ms.ToArray());
    }
}
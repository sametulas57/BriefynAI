using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace MeetingWeb.Helpers
{
    // Static utility class for symmetric AES encryption/decryption operations.
    public static class EncryptionHelper
    {
        private static byte[] _key;
        private static byte[] _iv;

        // Reads encryption keys from configuration during application startup.
        public static void Initialize(IConfiguration configuration)
        {
            string keyString = configuration["EncryptionSettings:Key"];
            string ivString = configuration["EncryptionSettings:IV"];

            if (string.IsNullOrEmpty(keyString) || string.IsNullOrEmpty(ivString))
            {
                throw new InvalidOperationException("CRITICAL: Encryption keys are missing in appsettings.json!");
            }

            _key = Encoding.UTF8.GetBytes(keyString);
            _iv = Encoding.UTF8.GetBytes(ivString);
        }

        // Encrypts plain text into a Base64 encoded cipher string using AES-256.
        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;
            if (_key == null || _iv == null) throw new InvalidOperationException("EncryptionHelper is not initialized.");

            using Aes aes = Aes.Create();
            aes.Key = _key;
            aes.IV = _iv;

            ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

            using MemoryStream ms = new();
            using (CryptoStream cs = new(ms, encryptor, CryptoStreamMode.Write))
            {
                using (StreamWriter sw = new(cs))
                {
                    sw.Write(plainText);
                }
            }

            return Convert.ToBase64String(ms.ToArray());
        }

        // Decrypts a Base64 encoded cipher string back to plain text.
        public static string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;
            if (_key == null || _iv == null) return cipherText; // Fallback if not initialized

            try
            {
                using Aes aes = Aes.Create();
                aes.Key = _key;
                aes.IV = _iv;

                ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

                using MemoryStream ms = new(Convert.FromBase64String(cipherText));
                using CryptoStream cs = new(ms, decryptor, CryptoStreamMode.Read);
                using StreamReader sr = new(cs);

                return sr.ReadToEnd();
            }
            catch
            {
                // Graceful degradation: Return raw string if decryption fails.
                return cipherText;
            }
        }
    }
}
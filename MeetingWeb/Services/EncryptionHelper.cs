using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace MeetingWeb.Helpers
{
    // Static utility class for at-rest encryption of meeting content (used by the EF Core value converters).
    //
    // S4: current format is "v2:" + Base64(nonce[12] | ciphertext | tag[16]) using AES-256-GCM with a random nonce
    // per value, so equal plaintexts no longer produce equal ciphertexts and any modification is detected.
    // Values without the prefix are the legacy format (AES-CBC with a static IV); they stay readable with the old
    // key until the "reencrypt" command has converted them.
    public static class EncryptionHelper
    {
        public const string CurrentPrefix = "v2:";
        public const string DecryptionFailedMarker = "⚠️ şifre çözülemedi";

        private const int NonceSize = 12;
        private const int TagSize = 16;

        private static byte[] _keyV2 = null!;
        private static byte[]? _legacyKey;
        private static byte[]? _legacyIv;

        // Reads encryption keys from configuration during application startup.
        public static void Initialize(IConfiguration configuration)
        {
            string? keyV2 = configuration["EncryptionSettings:KeyV2"];
            if (string.IsNullOrEmpty(keyV2))
            {
                throw new InvalidOperationException("CRITICAL: EncryptionSettings:KeyV2 (ENCRYPTION_KEY_V2) is missing!");
            }

            _keyV2 = Convert.FromBase64String(keyV2);
            if (_keyV2.Length != 32)
            {
                throw new InvalidOperationException("CRITICAL: EncryptionSettings:KeyV2 must be 32 random bytes, Base64 encoded.");
            }

            // Legacy key/IV are only needed while old rows exist; without them those rows show the failure marker.
            string? keyString = configuration["EncryptionSettings:Key"];
            string? ivString = configuration["EncryptionSettings:IV"];
            _legacyKey = string.IsNullOrEmpty(keyString) ? null : Encoding.UTF8.GetBytes(keyString);
            _legacyIv = string.IsNullOrEmpty(ivString) ? null : Encoding.UTF8.GetBytes(ivString);
        }

        public static bool IsCurrentFormat(string? value) => value != null && value.StartsWith(CurrentPrefix, StringComparison.Ordinal);

        // Encrypts plain text into the current "v2:" format.
        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;
            if (_keyV2 == null) throw new InvalidOperationException("EncryptionHelper is not initialized.");

            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var payload = new byte[NonceSize + plainBytes.Length + TagSize];
            var nonce = payload.AsSpan(0, NonceSize);
            var cipher = payload.AsSpan(NonceSize, plainBytes.Length);
            var tag = payload.AsSpan(NonceSize + plainBytes.Length, TagSize);

            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(_keyV2, TagSize);
            aes.Encrypt(nonce, plainBytes, cipher, tag);

            return CurrentPrefix + Convert.ToBase64String(payload);
        }

        // Decrypts either format. Unreadable values (wrong key, tampering, corruption) are logged and replaced by
        // a visible marker instead of leaking ciphertext into the UI or failing the whole page.
        public static string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;
            if (TryDecrypt(cipherText, out var plainText)) return plainText;

            Serilog.Log.Warning("Encrypted value could not be decrypted (format: {Format}).",
                IsCurrentFormat(cipherText) ? "v2" : "legacy");
            return DecryptionFailedMarker;
        }

        public static bool TryDecrypt(string cipherText, out string plainText)
        {
            plainText = cipherText;
            if (string.IsNullOrEmpty(cipherText)) return true;

            try
            {
                plainText = IsCurrentFormat(cipherText) ? DecryptV2(cipherText) : DecryptLegacy(cipherText);
                return true;
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException or InvalidOperationException)
            {
                plainText = DecryptionFailedMarker;
                return false;
            }
        }

        private static string DecryptV2(string value)
        {
            var payload = Convert.FromBase64String(value.Substring(CurrentPrefix.Length));
            if (payload.Length < NonceSize + TagSize) throw new CryptographicException("Payload too short.");

            var cipherLength = payload.Length - NonceSize - TagSize;
            var plainBytes = new byte[cipherLength];
            using var aes = new AesGcm(_keyV2, TagSize);
            aes.Decrypt(payload.AsSpan(0, NonceSize), payload.AsSpan(NonceSize, cipherLength),
                payload.AsSpan(NonceSize + cipherLength, TagSize), plainBytes);

            return Encoding.UTF8.GetString(plainBytes);
        }

        private static string DecryptLegacy(string value)
        {
            if (_legacyKey == null || _legacyIv == null) throw new InvalidOperationException("Legacy key is not configured.");

            using Aes aes = Aes.Create();
            aes.Key = _legacyKey;
            aes.IV = _legacyIv;

            var cipherBytes = Convert.FromBase64String(value);
            var plainBytes = aes.CreateDecryptor().TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
    }
}

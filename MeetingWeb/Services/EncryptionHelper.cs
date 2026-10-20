using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace MeetingWeb.Services
{
    // Static utility class for at-rest encryption of meeting content (used by the EF Core value converters).
    //
    // ADR-0010: format is "v3:<key id>:" + Base64(nonce[12] | ciphertext | tag[16]) using AES-256-GCM with a random
    // nonce per value. The value is bound to its purpose (e.g. "Meetings.Title") as associated data, so ciphertext
    // copied into another column does not decrypt. The key id leaves room for a future key change.
    public static class EncryptionHelper
    {
        public const string Prefix = "v3:";
        public const int CurrentKeyId = 1;
        public const string DecryptionFailedMarker = "⚠️ şifre çözülemedi";

        private const int NonceSize = 12;
        private const int TagSize = 16;

        private static readonly Dictionary<int, byte[]> Keys = new();

        // Reads encryption keys from configuration during application startup.
        public static void Initialize(IConfiguration configuration)
        {
            string? keyV2 = configuration["EncryptionSettings:KeyV2"];
            if (string.IsNullOrEmpty(keyV2))
            {
                throw new InvalidOperationException("CRITICAL: EncryptionSettings:KeyV2 (ENCRYPTION_KEY_V2) is missing!");
            }

            var key = Convert.FromBase64String(keyV2);
            if (key.Length != 32)
            {
                throw new InvalidOperationException("CRITICAL: EncryptionSettings:KeyV2 must be 32 random bytes, Base64 encoded.");
            }

            Keys[CurrentKeyId] = key;
        }

        public static string Encrypt(string plainText, string purpose)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;
            if (!Keys.TryGetValue(CurrentKeyId, out var key)) throw new InvalidOperationException("EncryptionHelper is not initialized.");

            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var payload = new byte[NonceSize + plainBytes.Length + TagSize];
            var nonce = payload.AsSpan(0, NonceSize);
            var cipher = payload.AsSpan(NonceSize, plainBytes.Length);
            var tag = payload.AsSpan(NonceSize + plainBytes.Length, TagSize);

            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plainBytes, cipher, tag, Encoding.UTF8.GetBytes(purpose));

            return $"{Prefix}{CurrentKeyId}:{Convert.ToBase64String(payload)}";
        }

        // Unreadable values (wrong key, wrong column, tampering) are logged and replaced by a visible marker instead of
        // leaking ciphertext into the UI or failing the whole page.
        public static string Decrypt(string cipherText, string purpose)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;
            if (TryDecrypt(cipherText, purpose, out var plainText)) return plainText;

            Serilog.Log.Warning("Encrypted value could not be decrypted ({Purpose}).", purpose);
            return DecryptionFailedMarker;
        }

        public static bool TryDecrypt(string cipherText, string purpose, out string plainText)
        {
            plainText = cipherText;
            if (string.IsNullOrEmpty(cipherText)) return true;

            try
            {
                plainText = DecryptCore(cipherText, purpose);
                return true;
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException or InvalidOperationException)
            {
                plainText = DecryptionFailedMarker;
                return false;
            }
        }

        private static string DecryptCore(string value, string purpose)
        {
            var parts = value.Split(':', 3);
            if (parts.Length != 3 || parts[0] + ":" != Prefix) throw new FormatException("Not an encrypted value.");
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var keyId) || !Keys.TryGetValue(keyId, out var key))
                throw new InvalidOperationException("Unknown key id.");

            var payload = Convert.FromBase64String(parts[2]);
            if (payload.Length < NonceSize + TagSize) throw new CryptographicException("Payload too short.");

            var cipherLength = payload.Length - NonceSize - TagSize;
            var plainBytes = new byte[cipherLength];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(payload.AsSpan(0, NonceSize), payload.AsSpan(NonceSize, cipherLength),
                payload.AsSpan(NonceSize + cipherLength, TagSize), plainBytes, Encoding.UTF8.GetBytes(purpose));

            return Encoding.UTF8.GetString(plainBytes);
        }
    }
}

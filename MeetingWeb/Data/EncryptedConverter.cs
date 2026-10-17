using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MeetingWeb.Helpers;
using System.Text.Json;

namespace MeetingWeb.Data
{
    // Custom EF Core converter to seamlessly encrypt/decrypt standard strings.
    public class EncryptedConverter : ValueConverter<string, string>
    {
        public EncryptedConverter()
            : base(
                v => EncryptionHelper.Encrypt(v),
                v => EncryptionHelper.Decrypt(v)
            )
        { }
    }

    // For columns that held plain text before encryption was added (meeting comments): values are always written
    // encrypted, and old plain values are read as they are until the reencrypt command has converted them.
    public class EncryptedOrPlainTextConverter : ValueConverter<string, string>
    {
        public EncryptedOrPlainTextConverter()
            : base(
                v => EncryptionHelper.Encrypt(v),
                v => EncryptionHelper.IsCurrentFormat(v) ? EncryptionHelper.Decrypt(v) : v
            )
        { }
    }

    // Custom EF Core converter to serialize, and then encrypt List<string> objects.
    public class EncryptedStringListConverter : ValueConverter<List<string>, string>
    {
        public EncryptedStringListConverter()
            : base(
                v => SerializeAndEncrypt(v),
                v => DecryptAndDeserialize(v)
            )
        { }

        private static string SerializeAndEncrypt(List<string> list)
        {
            if (list == null || list.Count == 0) return string.Empty;

            string jsonString = JsonSerializer.Serialize(list);
            return EncryptionHelper.Encrypt(jsonString);
        }

        private static List<string> DecryptAndDeserialize(string cipherText)
        {
            if (string.IsNullOrWhiteSpace(cipherText)) return new List<string>();

            // S4: an unreadable value shows up as a visible marker instead of silently becoming an empty list.
            if (!EncryptionHelper.TryDecrypt(cipherText, out var decryptedJson))
            {
                Serilog.Log.Warning("Encrypted list could not be decrypted.");
                return new List<string> { EncryptionHelper.DecryptionFailedMarker };
            }

            try
            {
                return JsonSerializer.Deserialize<List<string>>(decryptedJson) ?? new List<string>();
            }
            catch (JsonException)
            {
                // Decrypted but not a JSON list (malformed legacy data): surface it rather than drop it.
                return new List<string> { decryptedJson };
            }
        }
    }
}
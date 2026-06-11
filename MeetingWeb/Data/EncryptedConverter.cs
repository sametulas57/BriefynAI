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

            try
            {
                string decryptedJson = EncryptionHelper.Decrypt(cipherText);
                return JsonSerializer.Deserialize<List<string>>(decryptedJson) ?? new List<string>();
            }
            catch (JsonException)
            {
                // Fallback for legacy plain-text data or malformed JSON structures.
                return new List<string>();
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // Fallback for decryption failures (e.g., changed encryption keys).
                return new List<string>();
            }
        }
    }
}
using System.Security.Cryptography;
using System.Text;

namespace MeetingWeb.Tests.Infrastructure
{
    // Fixed test keys, plus an independent re-implementation of the OLD scheme (AES-CBC, static IV)
    // so tests can produce genuine legacy ciphertext without relying on the app's own code.
    public static class TestKeys
    {
        public const string LegacyKey = "test-key-32-bytes-long-000000000";
        public const string LegacyIv = "test-iv-16-bytes";
        public static readonly string KeyV2Base64 = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

        public static string LegacyEncrypt(string plainText)
        {
            using var aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(LegacyKey);
            aes.IV = Encoding.UTF8.GetBytes(LegacyIv);
            var bytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(aes.CreateEncryptor().TransformFinalBlock(bytes, 0, bytes.Length));
        }
    }
}

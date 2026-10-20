using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MeetingWeb.Services;

namespace MeetingWeb.Data
{
    // EF Core converter that encrypts a string column; the purpose ("Table.Column") binds each value to its column (ADR-0010).
    public class EncryptedConverter : ValueConverter<string, string>
    {
        public EncryptedConverter(string purpose)
            : base(
                v => EncryptionHelper.Encrypt(v, purpose),
                v => EncryptionHelper.Decrypt(v, purpose)
            )
        { }
    }
}

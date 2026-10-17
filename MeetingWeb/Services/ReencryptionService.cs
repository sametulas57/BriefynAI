using MeetingWeb.Data;
using MeetingWeb.Helpers;
using MeetingWeb.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MeetingWeb.Services
{
    public class ReencryptionReport
    {
        public int Scanned { get; set; }
        public int Legacy { get; set; }
        public int Converted { get; set; }
        public List<string> Undecryptable { get; } = new();

        public override string ToString() =>
            $"scanned={Scanned} legacy={Legacy} converted={Converted} undecryptable={Undecryptable.Count}" +
            (Undecryptable.Count > 0 ? " [" + string.Join(", ", Undecryptable) + "]" : "");
    }

    public interface IReencryptionService
    {
        Task<ReencryptionReport> RunAsync(bool apply);
    }

    // S4 one-off migration, run by hand: `dotnet run -- reencrypt` (dry run) then `dotnet run -- reencrypt --apply`.
    // Works on raw column values (not through the EF converters) so it can tell legacy from v2 and writes back only
    // what it has verified: each new value is decrypted again and compared with the original plaintext.
    // Rows that cannot be decrypted are reported and left untouched. Running it again is a no-op.
    public class ReencryptionService : IReencryptionService
    {
        private static readonly (Type Entity, string Property)[] EncryptedColumns =
        {
            (typeof(MeetingSummary), nameof(MeetingSummary.ToplantiKonusu)),
            (typeof(MeetingSummary), nameof(MeetingSummary.GorusulenKonular)),
            (typeof(MeetingSummary), nameof(MeetingSummary.AlinanKararlar)),
            (typeof(MeetingSummary), nameof(MeetingSummary.Transkript)),
            (typeof(ActionItem), nameof(ActionItem.Text)),
        };

        // Columns that were stored as plain text before they were encrypted: any non-v2 value is plain text.
        private static readonly (Type Entity, string Property)[] FormerlyPlainColumns =
        {
            (typeof(MeetingComment), nameof(MeetingComment.Content)),
        };

        private sealed record RawValue(int Id, string? Value);

        private readonly ApplicationDbContext _context;
        private readonly ILogger<ReencryptionService> _logger;

        public ReencryptionService(ApplicationDbContext context, ILogger<ReencryptionService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<ReencryptionReport> RunAsync(bool apply)
        {
            var report = new ReencryptionReport();

            foreach (var (entity, property, wasPlain) in EncryptedColumns.Select(c => (c.Entity, c.Property, false))
                         .Concat(FormerlyPlainColumns.Select(c => (c.Entity, c.Property, true))))
            {
                var entityType = _context.Model.FindEntityType(entity)!;
                var table = entityType.GetTableName()!;
                var store = StoreObjectIdentifier.Table(table, entityType.GetSchema());
                var column = entityType.FindProperty(property)!.GetColumnName(store)!;
                var key = entityType.FindPrimaryKey()!.Properties.Single().GetColumnName(store)!;

                var rows = await _context.Database
                    .SqlQueryRaw<RawValue>($"SELECT \"{key}\" AS \"Id\", \"{column}\" AS \"Value\" FROM \"{table}\"")
                    .ToListAsync();

                // One transaction per column: a failure rolls the whole column back to its legacy values.
                await using var transaction = apply ? await _context.Database.BeginTransactionAsync() : null;

                foreach (var row in rows)
                {
                    report.Scanned++;
                    if (string.IsNullOrEmpty(row.Value) || EncryptionHelper.IsCurrentFormat(row.Value)) continue;

                    report.Legacy++;
                    var plainText = row.Value;
                    if (!wasPlain && !EncryptionHelper.TryDecrypt(row.Value, out plainText))
                    {
                        report.Undecryptable.Add($"{table}#{row.Id}.{column}");
                        continue;
                    }

                    if (!apply) continue;

                    var newValue = EncryptionHelper.Encrypt(plainText);
                    if (!EncryptionHelper.TryDecrypt(newValue, out var roundTrip) || roundTrip != plainText)
                        throw new InvalidOperationException($"Round-trip check failed for {table}#{row.Id}.{column}; nothing was written.");

                    // Optimistic guard: only replace the exact value that was read.
                    var affected = await _context.Database.ExecuteSqlRawAsync(
                        $"UPDATE \"{table}\" SET \"{column}\" = {{0}} WHERE \"{key}\" = {{1}} AND \"{column}\" = {{2}}",
                        newValue, row.Id, row.Value);
                    report.Converted += affected;
                }

                if (transaction != null) await transaction.CommitAsync();
            }

            _logger.LogInformation("Re-encryption ({Mode}) finished: {Report}", apply ? "apply" : "dry run", report);
            return report;
        }
    }
}

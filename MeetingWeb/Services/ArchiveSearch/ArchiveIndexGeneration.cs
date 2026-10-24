using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MeetingWeb.Services.ArchiveSearch
{
    // A process-wide generation also covers writes made by contexts outside the search service.
    public static class ArchiveIndexGeneration
    {
        private static long _value;
        public static long Current => Interlocked.Read(ref _value);
        public static readonly DbCommandInterceptor CommandInterceptor = new ChangeCommandInterceptor();
        public static readonly DbTransactionInterceptor TransactionInterceptor = new CommitInterceptor();

        private static void Bump() => Interlocked.Increment(ref _value);

        private sealed class ChangeCommandInterceptor : DbCommandInterceptor
        {
            private static readonly Regex Mutation = new(@"\b(?:INSERT|UPDATE|DELETE|MERGE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            private static readonly Regex Table = new(@"\b(?:Meetings|MeetingPoints|ActionItems|MeetingTranscripts|MeetingSeries|WorkspaceMembers|Workspaces|AspNetUsers)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            private static void Check(DbCommand command)
            {
                if (Mutation.IsMatch(command.CommandText) && Table.IsMatch(command.CommandText)) Bump();
            }

            public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
            { Check(command); return result; }

            public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
                DbDataReader result, CancellationToken cancellationToken = default)
            { Check(command); return ValueTask.FromResult(result); }

            public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
            { Check(command); return result; }

            public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
                int result, CancellationToken cancellationToken = default)
            { Check(command); return ValueTask.FromResult(result); }

            public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
            { Check(command); return result; }

            public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
                object? result, CancellationToken cancellationToken = default)
            { Check(command); return ValueTask.FromResult(result); }
        }

        private sealed class CommitInterceptor : DbTransactionInterceptor
        {
            public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Bump();
            public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
                CancellationToken cancellationToken = default)
            { Bump(); return Task.CompletedTask; }
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MeetingWeb.Tests.Infrastructure
{
    public sealed class UtcDateTimeGuard : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            CheckDates(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            CheckDates(eventData.Context);
            return ValueTask.FromResult(result);
        }

        private static void CheckDates(DbContext? context)
        {
            if (context == null) return;
            foreach (var entry in context.ChangeTracker.Entries())
            {
                if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
                foreach (var property in entry.Properties)
                {
                    if (entry.State == EntityState.Modified && !property.IsModified) continue;
                    if ((property.Metadata.ClrType == typeof(DateTime) || property.Metadata.ClrType == typeof(DateTime?))
                        && property.CurrentValue is DateTime value && value.Kind != DateTimeKind.Utc)
                        throw new InvalidOperationException($"Non-UTC DateTime written: {entry.Metadata.ClrType.Name}.{property.Metadata.Name} (Kind={value.Kind})");
                }
            }
        }
    }
}

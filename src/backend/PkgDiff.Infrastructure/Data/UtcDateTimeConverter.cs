using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PkgDiff.Infrastructure.Data;

// Columns hold UTC: a value without a kind is taken as UTC, a local one converted.
public class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(value => ToUtc(value), value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
    {
    }

    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

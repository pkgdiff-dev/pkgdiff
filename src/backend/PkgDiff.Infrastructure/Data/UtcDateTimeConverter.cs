using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PkgDiff.Infrastructure.Data;

// Every DateTime column holds UTC. A value without a kind is taken as UTC, which is what the columns
// have always held; a local one is converted. What is read comes back as UTC.
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

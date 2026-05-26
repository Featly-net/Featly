using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Featly.Storage.Sqlite.Configurations;

/// <summary>
/// Persists <see cref="DateTimeOffset"/> as 64-bit UTC ticks. EF Core's default
/// SQLite mapping uses TEXT (ISO 8601) which silently drops <c>ORDER BY</c>
/// and aggregate functions like <c>MAX</c>. Storing ticks pushes those
/// operations back into SQL.
/// </summary>
/// <remarks>
/// <para>
/// Ticks are read back as UTC (<see cref="DateTimeKind.Utc"/> via
/// <c>new DateTimeOffset(ticks, TimeSpan.Zero)</c>). Featly stamps every
/// audit timestamp with <see cref="DateTimeOffset.UtcNow"/> so the original
/// offset is always zero — no precision is lost on the round-trip.
/// </para>
/// <para>
/// Applied to <c>CreatedAt</c> / <c>UpdatedAt</c> across Project, Environment,
/// Flag, Segment, Config. The migration that introduced this conversion
/// drops and recreates the columns, so any history in pre-v0.0.2-preview
/// databases is wiped.
/// </para>
/// </remarks>
internal sealed class DateTimeOffsetTicksConverter : ValueConverter<DateTimeOffset, long>
{
    public DateTimeOffsetTicksConverter()
        : base(
            static value => value.UtcTicks,
            static ticks => new DateTimeOffset(ticks, TimeSpan.Zero))
    {
    }
}

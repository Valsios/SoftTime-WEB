using System.Globalization;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore;

namespace SoftTime.Infrastructure.Persistence;

internal static class AuditValueFormat
{

    internal static string TableName(EntityEntry entry)
        => entry.Metadata.GetTableName() ?? entry.Metadata.ClrType.Name;

    internal static bool ValuesDiffer(PropertyEntry property)
    {
        var comparer = property.Metadata.GetValueComparer();
        return comparer is null
            ? !Equals(property.OriginalValue, property.CurrentValue)
            : !comparer.Equals(property.OriginalValue, property.CurrentValue);
    }

    internal static string? KeyFor(EntityEntry entry, EntityState state)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
            return null;
        return string.Join(";", key.Properties.Select(property =>
        {
            var value = entry.Property(property.Name);
            var source = state == EntityState.Deleted ? value.OriginalValue : value.CurrentValue;
            return $"{property.GetColumnName() ?? property.Name}={Format(source)}";
        }));
    }

    internal static string? ValueFor(PropertyEntry property, bool original)
        => Format(original ? property.OriginalValue : property.CurrentValue);

    internal static string? OldValueFor(PropertyEntry property, EntityState state)
        => state == EntityState.Added ? null : ValueFor(property, true);

    internal static string? NewValueFor(PropertyEntry property, EntityState state)
        => state == EntityState.Deleted ? null : ValueFor(property, false);

    internal static string? Format(object? value)
        => value switch
        {
            null => null,
            DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
            DateTimeOffset date => date.ToString("yyyy-MM-dd HH:mm:ss.fffffff zzz", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

    internal static string ActionFor(EntityState state) => state switch
    {
        EntityState.Added => "Insertion",
        EntityState.Deleted => "Suppression",
        _ => "Modification"
    };
}

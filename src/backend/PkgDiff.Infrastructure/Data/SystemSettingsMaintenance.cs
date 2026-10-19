using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Entities;
using PkgDiff.Shared.Constants;

namespace PkgDiff.Infrastructure.Data;

public static class SystemSettingsMaintenance
{
    public static IReadOnlyList<SystemSettings> CreateMissing(IEnumerable<string> existingKeys)
    {
        var existing = new HashSet<string>(existingKeys, StringComparer.Ordinal);

        return SettingKeys.All
            .Where(d => !existing.Contains(d.Key))
            .Select(d => new SystemSettings
            {
                Key = d.Key,
                Value = d.DefaultValue,
                Description = d.Description,
                DataType = d.DataType,
                Category = d.Category,
                IsSecret = d.IsSecret
            })
            .ToList();
    }

    public static int Apply(IEnumerable<SystemSettings> rows, ISecretProtector protector)
    {
        var changed = 0;

        foreach (var row in rows)
        {
            var rowChanged = false;
            var definition = SettingKeys.Find(row.Key);

            if (definition != null)
            {
                if (!string.Equals(row.Category, definition.Category, StringComparison.Ordinal))
                {
                    row.Category = definition.Category;
                    rowChanged = true;
                }

                if (row.IsSecret != definition.IsSecret)
                {
                    row.IsSecret = definition.IsSecret;
                    rowChanged = true;
                }
            }

            // Prefixed values are never touched: an undecryptable one must survive until an admin re-enters it
            if (row.IsSecret && !string.IsNullOrEmpty(row.Value) && !protector.IsProtected(row.Value))
            {
                row.Value = protector.Protect(row.Value);
                rowChanged = true;
            }

            if (rowChanged)
            {
                changed++;
            }
        }

        return changed;
    }
}

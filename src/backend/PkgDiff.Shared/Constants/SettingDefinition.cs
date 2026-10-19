namespace PkgDiff.Shared.Constants;

public sealed record SettingDefinition(
    string Key,
    string DefaultValue,
    string Description,
    string DataType,
    string Category,
    bool IsSecret);

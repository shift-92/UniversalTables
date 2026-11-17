namespace UniversalTables.Models;

public static class TooltipText
{
    public static string Format(string? description) => string.IsNullOrEmpty(description)
        ? "не указано"
        : description.Replace(" / ", "\n", StringComparison.Ordinal);
}
using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace UniversalTables.Configuration;

public sealed class TableOptions
{
    public RenderFragment? CellLoadingTemplate { get; set; }
    public RenderFragment? BlockingOperationTemplate { get; set; }
    public CultureInfo? Culture { get; set; }
    public CultureInfo EffectiveCulture => Culture ?? CultureInfo.CurrentCulture;
    public string Format(object? value) => value is IFormattable formatted
        ? formatted.ToString(null, EffectiveCulture) ?? ""
        : value?.ToString() ?? "";
}
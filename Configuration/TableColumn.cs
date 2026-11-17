using Microsoft.AspNetCore.Components;
using UniversalTables.Models;

namespace UniversalTables.Configuration;

public sealed record ReferenceBinding(Type TargetType, string ForeignKeyPath, bool Nullable);
public sealed record CollectionBinding(Type ElementType, string ForeignKey);
public sealed record CellContext(object Row, EntityKey Entity, TableColumn Column, object? Value, CellDifference Difference);
public sealed record FilterChoiceContext(TableColumn Column, object? Row, System.Text.Json.JsonElement Value, string Label, bool Available);
public sealed class FieldDraft
{
    public required TableColumn Column { get; init; }
    public object? OriginalValue { get; init; }
    public object? Value { get; set; }
    public bool ReadOnly { get; set; }
    public bool Busy { get; set; }
    public string? Error { get; set; }
    public bool IncludeInSubmission { get; set; } = true;
    public Dictionary<string, object?> State { get; } = [];
}

public sealed class TableColumn
{
    public IReadOnlyList<GlobalFilterOperator> CustomOperators { get; set; } = [];
    public required string Id { get; init; }
    public required string PropertyPath { get; init; }
    public required Type ValueType { get; init; }
    public required Func<object, object?> Read { get; init; }
    public required Func<object, object?> FilterValue { get; set; }
    public required Type FilterType { get; set; }
    public required Func<object, object?> SortValue { get; set; }
    public FilterKind FilterKind { get; set; }
    public ReferenceBinding? Reference { get; set; }
    public CollectionBinding? Collection { get; set; }
    public bool HasCustomFilter { get; set; }
    public bool HasCustomSort { get; set; }
    public Func<object,object?>? GroupValue { get; set; }
    public string Title { get; set; } = "";
    public double Width { get; set; } = 160;
    public double MinWidth { get; set; } = 48;
    public int Order { get; set; }
    public bool Visible { get; set; } = true;
    public bool CellEditable { get; set; }
    public bool FormVisible { get; set; } = true;
    public bool? FormEditable { get; set; }
    public bool Required { get; set; }
    public bool CopyValue { get; set; } = true;
    public bool OneToOne { get; set; }
    public Func<object, RenderFragment>? Display { get; set; }
    public RenderFragment<CellContext>? CellTemplate { get; set; }
    public RenderFragment<CellContext>? TooltipTemplate { get; set; }
    public RenderFragment<FilterChoiceContext>? FilterTemplate { get; set; }
    public Func<object,object?>? FilterChoiceValue { get; set; }
    public FilterOperator? DefaultFilterOperator { get; set; }
    public RenderFragment<FieldDraft>? EditorTemplate { get; set; }
}
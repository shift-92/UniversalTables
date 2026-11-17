using System.Text.Json;

namespace UniversalTables.Models;

public enum FilterOperator
{
    Equal, NotEqual, Contains, StartsWith, Greater, GreaterOrEqual,
    Less, LessOrEqual, Between, Empty, NotEmpty, Any, None, All, Custom, DateTree, Array
}
public enum FilterKind { Scalar, Reference, CollectionElements }
public sealed record FilterRule(string ColumnId, FilterOperator Operator, List<JsonElement> Values, string? CustomOperatorId = null);
public sealed record SortRule(string ColumnId, bool Descending);
public enum FilterJunction { And, Or }
public sealed class FilterGroup
{
    public FilterJunction Junction { get; set; }
    public List<FilterRule> Rules { get; set; } = [];
    public List<FilterGroup> Groups { get; set; } = [];
    public FilterGroup Copy() => new()
    {
        Junction = Junction,
        Rules = Rules.Select(f => new FilterRule(f.ColumnId, f.Operator, f.Values.Select(v => v.Clone()).ToList(), f.CustomOperatorId)).ToList(),
        Groups = Groups.Select(g => g.Copy()).ToList()
    };
}
public sealed class QueryState
{
    public int Version { get; set; } = 1;
    public List<FilterRule> Filters { get; set; } = [];
    public List<SortRule> Sorts { get; set; } = [];
    public FilterGroup? AdvancedFilter { get; set; }
    public QueryState Copy() => new()
    {
        Version = Version,
        AdvancedFilter = AdvancedFilter?.Copy(),
        Filters = Filters.Select(f => new FilterRule(f.ColumnId, f.Operator, f.Values.Select(v => v.Clone()).ToList(), f.CustomOperatorId)).ToList(),
        Sorts = Sorts.ToList()
    };
}
public sealed record ColumnLayout(string ColumnId, int? Order = null, double? Width = null, bool? Visible = null, string? Title = null);
public sealed record LayoutSettings(IReadOnlyList<ColumnLayout> Global, IReadOnlyList<ColumnLayout> User);
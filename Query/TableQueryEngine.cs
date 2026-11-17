using System.Collections;
using System.Text.Json;
using UniversalTables.Configuration;
using UniversalTables.Models;

namespace UniversalTables.Query;

public sealed record FilterChoice(string Key, System.Text.Json.JsonElement Value, string Label, bool Available, object? Row = null, int Count = 0);
public sealed record QueryResult(IReadOnlyList<object> Rows, int ContextCount);
public sealed record RestoredQuery(QueryState State, IReadOnlyList<string> Warnings);

public sealed class TableQueryEngine
{
    private readonly IReadOnlyDictionary<string,TableColumn> _columns;
    public TableQueryEngine(IEnumerable<TableColumn> columns) => _columns=columns.ToDictionary(c=>c.Id,StringComparer.Ordinal);
    public QueryResult Apply(IEnumerable<object> rows, QueryState query, Func<object,bool>? fixedFilter=null, string? exceptColumn=null)
    {
        EnsureVersion(query);
        var contextual=(fixedFilter is null ? rows : rows.Where(fixedFilter)).ToList();
        IEnumerable<object> result=contextual;
        foreach(var rule in query.Filters.Where(r=>r.ColumnId!=exceptColumn))
        {
            if(!_columns.TryGetValue(rule.ColumnId,out var column)) throw new ArgumentException("Unknown column: "+rule.ColumnId);
            var predicate=Predicate(column,rule);
            result=result.Where(predicate);
        }
        if(query.AdvancedFilter is { } group) result=result.Where(GroupPredicate(group));
        IOrderedEnumerable<object>? ordered=null;
        foreach(var sort in query.Sorts)
        {
            if(!_columns.TryGetValue(sort.ColumnId,out var column)) throw new ArgumentException("Unknown sort column: "+sort.ColumnId);
            ordered=ordered is null
                ? sort.Descending ? result.OrderByDescending(column.SortValue,ScalarOrder.Instance) : result.OrderBy(column.SortValue,ScalarOrder.Instance)
                : sort.Descending ? ordered.ThenByDescending(column.SortValue,ScalarOrder.Instance) : ordered.ThenBy(column.SortValue,ScalarOrder.Instance);
        }
        var final=ordered is null ? result.OrderBy(Id) : ordered.ThenBy(Id);
        return new(final.ToArray(),contextual.Count);
    }
    private Func<object,bool> GroupPredicate(FilterGroup group, int depth=0)
    {
        if(depth>32 || group.Rules is null || group.Groups is null || !Enum.IsDefined(group.Junction))
            throw new ArgumentException("Некорректная группа фильтров.");
        var predicates=new List<Func<object,bool>>();
        foreach(var rule in group.Rules)
        {
            if(!_columns.TryGetValue(rule.ColumnId,out var column)) throw new ArgumentException("Unknown column: "+rule.ColumnId);
            predicates.Add(Predicate(column,rule));
        }
        foreach(var child in group.Groups) predicates.Add(GroupPredicate(child,depth+1));
        if(predicates.Count==0) throw new ArgumentException("Добавьте условие в каждую группу или удалите пустую группу.");
        return group.Junction==FilterJunction.And ? row=>predicates.All(p=>p(row)) : row=>predicates.Any(p=>p(row));
    }
    private static Guid Id(object row) => row is IWebModel model ? model.Id : throw new InvalidOperationException("Row does not implement IWebModel.");
    private static IEnumerable<IWebModel> Elements(object? value)
    {
        if(value is null) return [];
        if(value is not IEnumerable sequence) throw new InvalidOperationException("Expected collection filter value.");
        return sequence.Cast<object>().Select(x=>x as IWebModel ?? throw new InvalidOperationException("Collection item does not implement IWebModel."));
    }
    private static Func<object,bool> Predicate(TableColumn column,FilterRule rule)
    {
        FilterSchema.Validate(column,rule);
        if(rule.Operator == FilterOperator.Custom)
        {
            var custom = column.CustomOperators.Single(x => x.Id == rule.CustomOperatorId);
            return row => custom.Matches(column.Read(row));
        }
        if(rule.Operator == FilterOperator.DateTree)
        {
            var empty = rule.Values.Any(x => x.ValueKind == JsonValueKind.Null);
            var periods = rule.Values.Where(x => x.ValueKind != JsonValueKind.Null)
                .Select(x => x.Deserialize<CalendarPeriod>(TableJson.Options)!).ToArray();
            return row => CalendarPeriod.Date(column.FilterValue(row)) is { } date ? periods.Any(p => p.Contains(date)) : empty;
        }
        if(column.FilterKind==FilterKind.CollectionElements)
        {
            var selected=rule.Values.Select(v=>(Guid)FilterSchema.ReadValue(v,typeof(Guid))!).ToHashSet();
            return row=>
            {
                var ids=Elements(column.FilterValue(row)).Select(x=>x.Id).ToHashSet();
                return rule.Operator switch
                {
                    FilterOperator.Empty=>ids.Count==0,
                    FilterOperator.NotEmpty=>ids.Count!=0,
                    _ when selected.Count==0=>true,
                    FilterOperator.Any or FilterOperator.Array=>ids.Overlaps(selected),
                    FilterOperator.None=>!ids.Overlaps(selected),
                    FilterOperator.All=>selected.IsSubsetOf(ids),
                    _=>throw new InvalidOperationException()
                };
            };
        }
        var type=column.FilterKind==FilterKind.Reference ? typeof(Guid) : column.FilterType;
        var values=rule.Values.Select(v=>FilterSchema.ReadValue(v,type)).ToArray();
        return row=>
        {
            var value=column.FilterValue(row);
            if(rule.Operator==FilterOperator.Empty) return value is null || value is string {Length:0};
            if(rule.Operator==FilterOperator.NotEmpty) return value is not null && value is not string {Length:0};
            if(values.Length==0) return true;
            if(rule.Operator is FilterOperator.Equal or FilterOperator.Array) return values.Any(v=>ScalarOrder.Instance.Compare(value,v)==0);
            if(rule.Operator==FilterOperator.NotEqual) return values.All(v=>ScalarOrder.Instance.Compare(value,v)!=0);
            if(value is null) return false;
            return rule.Operator switch
            {
                FilterOperator.Contains=>((string)value).Contains((string?)values[0] ?? "",StringComparison.CurrentCultureIgnoreCase),
                FilterOperator.StartsWith=>((string)value).StartsWith((string?)values[0] ?? "",StringComparison.CurrentCultureIgnoreCase),
                FilterOperator.Greater=>ScalarOrder.Instance.Compare(value,values[0])>0,
                FilterOperator.GreaterOrEqual=>ScalarOrder.Instance.Compare(value,values[0])>=0,
                FilterOperator.Less=>ScalarOrder.Instance.Compare(value,values[0])<0,
                FilterOperator.LessOrEqual=>ScalarOrder.Instance.Compare(value,values[0])<=0,
                FilterOperator.Between=>ScalarOrder.Instance.Compare(value,values[0])>=0 && ScalarOrder.Instance.Compare(value,values[1])<=0,
                _=>throw new InvalidOperationException()
            };
        };
    }
    public IReadOnlyList<FilterChoice> Choices(IEnumerable<object> rows,QueryState query,string columnId,
        Func<object,bool>? fixedFilter=null,Func<Type,Guid,string?>? referenceLabel=null)
    {
        var column=_columns[columnId];
        var contextQuery=query.Copy(); contextQuery.Sorts.Clear();
        var source=Apply(rows,contextQuery,fixedFilter,columnId).Rows;
        var choices=new Dictionary<string,FilterChoice>(StringComparer.Ordinal);
        void Add(object? value,Type type,string label,bool available,object? row=null)
        {
            var json=TableJson.Value(value,type);
            var key=json.GetRawText();
            if(!choices.TryGetValue(key,out var choice)) choices.Add(key,new(key,json,label,available,row,available ? 1 : 0));
            else if(available) choices[key]=choice with { Count=choice.Count+1 };
        }
        foreach(var row in source)
        {
            if(column.FilterKind==FilterKind.CollectionElements)
                foreach(var item in Elements(column.FilterValue(row)).DistinctBy(item=>item.Id)) Add(item.Id,typeof(Guid),item.ToString() ?? item.Id.ToString(),true,row);
            else
            {
                var value=column.FilterValue(row);
                if(column.FilterKind==FilterKind.Reference)
                {
                    if(value is Guid id) Add(id,typeof(Guid),referenceLabel?.Invoke(column.Reference!.TargetType,id) ?? id.ToString(),true,row);
                }
                else Add(value,column.FilterType,value?.ToString() ?? "(пусто)",true,row);
            }
        }
        foreach(var rule in query.Filters.Where(f=>f.ColumnId==columnId && f.Operator is FilterOperator.Array or FilterOperator.Equal or FilterOperator.NotEqual or FilterOperator.Any or FilterOperator.None or FilterOperator.All))
        {
            FilterSchema.Validate(column,rule);
            foreach(var json in rule.Values)
            {
                var type=column.FilterKind is FilterKind.CollectionElements or FilterKind.Reference ? typeof(Guid) : column.FilterType;
                var value=FilterSchema.ReadValue(json,type);
                var label=value?.ToString() ?? "(пусто)";
                if(column.Reference is not null && value is Guid id) label=referenceLabel?.Invoke(column.Reference.TargetType,id) ?? label;
                Add(value,type,label+" (нет в текущей выборке)",false);
            }
        }
        return choices.Values.OrderBy(c=>c.Label,StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
    public RestoredQuery Restore(QueryState saved)
    {
        EnsureVersion(saved);
        var result=new QueryState(); var warnings=new List<string>();
        if(saved.AdvancedFilter is { } advanced)
        {
            try { GroupPredicate(advanced); result.AdvancedFilter=advanced.Copy(); }
            catch(Exception ex) when(ex is ArgumentException or JsonException or NotSupportedException or InvalidOperationException or FormatException or OverflowException)
            { warnings.Add("Сложный фильтр пропущен целиком: "+ex.Message); }
        }
        foreach(var rule in saved.Filters)
        {
            try
            {
                if(!_columns.TryGetValue(rule.ColumnId,out var column)) throw new ArgumentException("Column no longer exists.");
                FilterSchema.Validate(column,rule);
                result.Filters.RemoveAll(f=>f.ColumnId==rule.ColumnId);
                result.Filters.Add(new(rule.ColumnId,rule.Operator,rule.Values.Select(v=>v.Clone()).ToList(),rule.CustomOperatorId));
            }
            catch(Exception ex) when(ex is ArgumentException or System.Text.Json.JsonException or NotSupportedException or InvalidOperationException or FormatException or OverflowException)
            { warnings.Add($"Фильтр {rule.ColumnId} пропущен: {ex.Message}"); }
        }
        foreach(var sort in saved.Sorts)
        {
            if(!_columns.ContainsKey(sort.ColumnId)) warnings.Add("Сортировка пропущена: "+sort.ColumnId);
            else if(result.Sorts.All(s=>s.ColumnId!=sort.ColumnId)) result.Sorts.Add(sort);
        }
        return new(result,warnings);
    }
    public static void ToggleSort(QueryState state,string columnId,bool additive)
    {
        var index=state.Sorts.FindIndex(s=>s.ColumnId==columnId);
        var old=index<0 ? null : state.Sorts[index];
        if(!additive)
        {
            state.Sorts.Clear();
            if(old is null) state.Sorts.Add(new(columnId,false));
            else if(!old.Descending) state.Sorts.Add(new(columnId,true));
        }
        else if(old is null) state.Sorts.Add(new(columnId,false));
        else if(!old.Descending) state.Sorts[index]=new(columnId,true);
        else state.Sorts.RemoveAt(index);
    }
    private static void EnsureVersion(QueryState query)
    {
        if(query.Version!=1) throw new NotSupportedException("Unsupported query version.");
        if(query.Filters is null || query.Sorts is null) throw new ArgumentException("Invalid query state.");
    }
}
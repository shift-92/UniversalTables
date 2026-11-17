using System.Text.Json;
using UniversalTables.Configuration;
using UniversalTables.Models;

namespace UniversalTables.Query;

public static class FilterSchema
{
    public static bool IsDate(TableColumn column) => ScalarType(column) == typeof(DateOnly) || ScalarType(column) == typeof(DateTime);
    public static Type ScalarType(TableColumn column) => Nullable.GetUnderlyingType(column.FilterType) ?? column.FilterType;
    public static bool IsNumeric(Type type) => Type.GetTypeCode(type) is TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;
    public static IReadOnlyList<FilterOperator> Operators(TableColumn column)
    {
        var operations=StandardOperators(column);
        return operations.Count==0 ? operations : new[] { FilterOperator.Array }.Concat(operations).ToArray();
    }
    public static FilterOperator DefaultOperator(TableColumn column)
    {
        var operations=Operators(column);
        if(column.DefaultFilterOperator is { } configured)
        {
            if(!operations.Contains(configured)) throw new ArgumentException($"Column {column.Id}: unsupported default filter operator {configured}.");
            return configured;
        }
        return operations.Count==0 ? FilterOperator.Custom : IsDate(column) ? FilterOperator.DateTree : FilterOperator.Array;
    }
    private static IReadOnlyList<FilterOperator> StandardOperators(TableColumn column)
    {
        if(column.FilterKind == FilterKind.CollectionElements)
            return [FilterOperator.Any, FilterOperator.None, FilterOperator.All, FilterOperator.Empty, FilterOperator.NotEmpty];
        var type=ScalarType(column);
        if(column.FilterKind == FilterKind.Reference)
            return [FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.Empty, FilterOperator.NotEmpty];
        if(type == typeof(string))
            return [FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.Contains, FilterOperator.StartsWith, FilterOperator.Empty, FilterOperator.NotEmpty];
        if(IsDate(column))
            return [FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.Greater, FilterOperator.GreaterOrEqual, FilterOperator.Less, FilterOperator.LessOrEqual, FilterOperator.Between, FilterOperator.Empty, FilterOperator.NotEmpty, FilterOperator.DateTree];
        if(IsNumeric(type) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan))
            return [FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.Greater, FilterOperator.GreaterOrEqual, FilterOperator.Less, FilterOperator.LessOrEqual, FilterOperator.Between, FilterOperator.Empty, FilterOperator.NotEmpty];
        if(type == typeof(bool) || type == typeof(Guid) || type.IsEnum)
            return [FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.Empty, FilterOperator.NotEmpty];
        if(column.CustomOperators.Count>0) return [];
        throw new NotSupportedException($"Column {column.Id}: provide a scalar FilterValue for {type.Name}.");
    }
    public static object? ReadValue(JsonElement value, Type type)
    {
        if(value.ValueKind == JsonValueKind.Null)
        {
            if(type.IsValueType && Nullable.GetUnderlyingType(type) is null) throw new ArgumentException($"Null is not valid for {type.Name}.");
            return null;
        }
        if(value.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Undefined filter value.");
        var result=value.Deserialize(type, TableJson.Options);
        if(result is double d && !double.IsFinite(d) || result is float f && !float.IsFinite(f)) throw new ArgumentException("Non-finite filter value.");
        return result;
    }
    public static void Validate(TableColumn column, FilterRule rule)
    {
        if(rule.Operator == FilterOperator.Custom)
        {
            if(rule.Values is null || rule.Values.Count != 0 || !column.CustomOperators.Any(x => x.Id == rule.CustomOperatorId))
                throw new ArgumentException("Пользовательский оператор недоступен для столбца.");
            return;
        }
        if(!Operators(column).Contains(rule.Operator)) throw new ArgumentException($"Unsupported operator for {column.Id}.");
        if(rule.Values is null) throw new ArgumentException("Values cannot be null.");
        if(rule.Operator == FilterOperator.DateTree)
        {
            foreach(var value in rule.Values)
            {
                if(value.ValueKind == JsonValueKind.Null)
                {
                    if(Nullable.GetUnderlyingType(column.FilterType) is null) throw new ArgumentException("Столбец не допускает пустую дату.");
                    continue;
                }
                var period = value.Deserialize<CalendarPeriod>(TableJson.Options) ?? throw new ArgumentException("Некорректный период.");
                if(period.Start > period.End) throw new ArgumentException("Некорректные границы периода.");
            }
            return;
        }
        if(rule.Operator is FilterOperator.Empty or FilterOperator.NotEmpty)
        {
            if(rule.Values.Count != 0) throw new ArgumentException("Empty/NotEmpty must not contain values.");
            return;
        }
        if(rule.Operator == FilterOperator.Between && rule.Values.Count != 2) throw new ArgumentException("Between requires two values.");
        if(rule.Operator is FilterOperator.Contains or FilterOperator.StartsWith or FilterOperator.Greater or FilterOperator.GreaterOrEqual or FilterOperator.Less or FilterOperator.LessOrEqual)
            if(rule.Values.Count != 1) throw new ArgumentException("This operator requires one value.");
        var type=column.FilterKind is FilterKind.CollectionElements or FilterKind.Reference ? typeof(Guid) : column.FilterType;
        var values=rule.Values.Select(v=>ReadValue(v,type)).ToArray();
        if(rule.Operator is FilterOperator.Between or FilterOperator.Greater or FilterOperator.GreaterOrEqual or FilterOperator.Less or FilterOperator.LessOrEqual)
            if(values.Any(v=>v is null)) throw new ArgumentException("Comparison boundaries cannot be null.");
        if(rule.Operator == FilterOperator.Between && ScalarOrder.Instance.Compare(values[0],values[1]) > 0) throw new ArgumentException("Lower boundary exceeds upper boundary.");
    }
}

public sealed class ScalarOrder : IComparer<object?>
{
    public static ScalarOrder Instance { get; }=new();
    public int Compare(object? x, object? y)
    {
        if(ReferenceEquals(x,y)) return 0;
        if(x is null) return -1;
        if(y is null) return 1;
        if(x is string a && y is string b) return StringComparer.CurrentCultureIgnoreCase.Compare(a,b);
        if(x.GetType()!=y.GetType()) throw new InvalidOperationException("Sort/filter values must have the same scalar type.");
        if(x is IComparable comparable) return comparable.CompareTo(y);
        throw new NotSupportedException("Provide SortValue for " + x.GetType().Name);
    }
}
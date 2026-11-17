using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using UniversalTables.Models;

namespace UniversalTables.Configuration;

public interface ITableConfiguration<T> where T : class, IWebModel
{
    void Configure(Columns<T> columns);
}

public sealed class Columns<T> where T : class, IWebModel
{
    private readonly List<TableColumn> _items = [];
    public IReadOnlyList<TableColumn> Items => _items;
    public List<TableMenuItem> ContextMenu { get; }=[];
    public ColumnBuilder<T,TValue> Add<TValue>(Expression<Func<T,TValue>> expression, string? id = null)
    {
        var path = PropertyPath(expression);
        var key = id ?? path;
        if (_items.Any(x => x.Id == key)) throw new ArgumentException("Duplicate column: " + key);
        var getter = NullSafe(expression);
        var column = new TableColumn
        {
            Id = key, PropertyPath = path, ValueType = typeof(TValue), Title = path,
            Order = _items.Count, Read = getter, FilterValue = getter,
            FilterType = typeof(TValue), SortValue = getter
        };
        _items.Add(column);
        return new(column);
    }
    public static Func<object,object?> NullSafe<TValue>(Expression<Func<T,TValue>> expression)
    {
        var compiled = expression.Compile();
        return item => { try { return compiled((T)item); } catch (NullReferenceException) { return null; } };
    }
    private static string PropertyPath(LambdaExpression expression)
    {
        Expression? node = expression.Body;
        while (node is UnaryExpression unary && unary.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked) node = unary.Operand;
        var parts = new Stack<string>();
        while (node is MemberExpression member && member.Member is PropertyInfo)
        {
            parts.Push(member.Member.Name); node = member.Expression;
        }
        if (node != expression.Parameters[0] || parts.Count == 0) throw new ArgumentException("Expected a property path, not a computed column expression.");
        return string.Join('.', parts);
    }
}

public sealed class ColumnBuilder<T,TValue>(TableColumn column) where T : class, IWebModel
{
    public TableColumn Definition => column;
    public ColumnBuilder<T,TValue> Title(string title) { column.Title = title; return this; }
    public ColumnBuilder<T,TValue> Width(double width) { column.Width = width; return this; }
    public ColumnBuilder<T,TValue> Editable(bool value = true) { column.CellEditable = value; return this; }
    public ColumnBuilder<T,TValue> FilterValue<TFilter>(Expression<Func<T,TFilter>> expression)
    {
        column.FilterValue = Columns<T>.NullSafe(expression); column.FilterType = typeof(TFilter);
        column.FilterKind = FilterKind.Scalar; column.HasCustomFilter = true; return this;
    }
    public ColumnBuilder<T,TValue> SortValue<TSort>(Expression<Func<T,TSort>> expression)
    { column.SortValue = Columns<T>.NullSafe(expression); column.HasCustomSort = true; return this; }
    public ColumnBuilder<T,TValue> Display(Func<T,string> display)
    { column.Display = item => builder => builder.AddContent(0, display((T)item)); return this; }
    public ColumnBuilder<T,TValue> Display(Func<T,RenderFragment> display)
    { column.Display = item => display((T)item); return this; }
    public ColumnBuilder<T,TValue> Tooltip(Func<T,RenderFragment> tooltip)
    { column.TooltipTemplate = context => tooltip((T)context.Row); return this; }
    public ColumnBuilder<T,TValue> FilterTemplate(RenderFragment<FilterChoiceContext> template)
    { column.FilterTemplate = template; return this; }
    public ColumnBuilder<T,TValue> FilterTemplate<TProperty>(RenderFragment<FilterChoiceContext> template, Expression<Func<T,TProperty>> property)
    { column.FilterTemplate = template; column.FilterChoiceValue = Columns<T>.NullSafe(property); return this; }
    public ColumnBuilder<T,TValue> DefaultFilterOperator(FilterOperator operation)
    { column.DefaultFilterOperator = operation; return this; }
}
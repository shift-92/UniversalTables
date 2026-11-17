using System.Collections;
using System.Reflection;
using UniversalTables.Configuration;
using UniversalTables.Models;

namespace UniversalTables.Services;

public static class ColumnMetadata
{
    private static PropertyInfo[] Resolve(Type root, string path)
    {
        var parts = new List<PropertyInfo>();
        foreach (var name in path.Split('.'))
        {
            var property = root.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"Property {root.Name}.{name} not found.");
            parts.Add(property); root = property.PropertyType;
        }
        return parts.ToArray();
    }
    private static object? Read(object root, IEnumerable<PropertyInfo> properties)
    {
        object? value = root;
        foreach (var property in properties) { if (value is null) return null; value = property.GetValue(value); }
        return value;
    }
    private static string? StringArgument(PropertyInfo property, string attributeName) => property.CustomAttributes
        .FirstOrDefault(a => a.AttributeType.Name == attributeName)?.ConstructorArguments
        .LastOrDefault(a => a.ArgumentType == typeof(string)).Value as string;

    public static void Apply(Type modelType, IReadOnlyList<TableColumn> columns)
    {
        foreach (var column in columns)
        {
            var path = Resolve(modelType, column.PropertyPath);
            var leaf = path[^1];
            var ownerType = path.Length == 1 ? modelType : path[^2].PropertyType;
            var oneToOne = path.Length > 1 && path.Take(path.Length - 1).All(p => p.CustomAttributes.Any(a => a.AttributeType.Name == nameof(OneToOneAttribute)));
            column.OneToOne = oneToOne;
            if (path.Length > 1 && !oneToOne) { column.CellEditable = false; column.FormEditable = false; }
            if (column.PropertyPath == nameof(IWebModel.Id)) { column.CellEditable = false; column.FormEditable = false; }
            var type = leaf.PropertyType;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) && typeof(IWebModel).IsAssignableFrom(type.GetGenericArguments()[0]))
            {
                var element = type.GetGenericArguments()[0];
                var fk = StringArgument(leaf, nameof(PredictFieldAttribute))
                    ?? throw new InvalidOperationException("Missing PredictField: " + column.PropertyPath);
                var fkProperty = element.GetProperty(fk);
                if (fkProperty is null || (fkProperty.PropertyType != typeof(Guid) && fkProperty.PropertyType != typeof(Guid?)))
                    throw new InvalidOperationException("Invalid collection FK: " + fk);
                column.Collection = new(element, fk);
                if (!column.HasCustomFilter) column.FilterKind = FilterKind.CollectionElements;
                if (!column.HasCustomSort) column.SortValue = row => (column.Read(row) as IEnumerable)?.Cast<object>().Count() ?? 0;
                continue;
            }
            PropertyInfo? foreignKey = null;
            PropertyInfo? navigation = null;
            foreach (var candidate in ownerType.GetProperties())
            {
                var navigationName = StringArgument(candidate, nameof(MainObjectAttribute));
                if (navigationName is null || (candidate.Name != leaf.Name && navigationName != leaf.Name)) continue;
                if (foreignKey is not null) throw new InvalidOperationException("Ambiguous navigation: " + column.PropertyPath);
                foreignKey = candidate; navigation = ownerType.GetProperty(navigationName);
            }
            if (foreignKey is null) continue;
            if (navigation is null || !typeof(IWebModel).IsAssignableFrom(navigation.PropertyType)) throw new InvalidOperationException("Invalid navigation: " + column.PropertyPath);
            if (foreignKey.PropertyType != typeof(Guid) && foreignKey.PropertyType != typeof(Guid?)) throw new InvalidOperationException("FK must be Guid or Guid?.");
            var prefix = path.Take(path.Length - 1).ToArray();
            var fkPath = string.Join('.', prefix.Select(p => p.Name).Append(foreignKey.Name));
            column.Reference = new(navigation.PropertyType, fkPath, foreignKey.PropertyType == typeof(Guid?));
            var keyProperty = foreignKey;
            if (!column.HasCustomFilter)
            {
                column.FilterKind = FilterKind.Reference;
                column.FilterType = foreignKey.PropertyType;
                column.FilterValue = row => { var owner = Read(row, prefix); return owner is null ? null : keyProperty.GetValue(owner); };
            }
            if (!column.HasCustomSort)
            {
                var navigationProperty = navigation;
                column.SortValue = row => { var owner = Read(row, prefix); return owner is null ? null : navigationProperty.GetValue(owner)?.ToString(); };
            }
        }
    }

    public static void ApplyLayout(IReadOnlyList<TableColumn> columns, LayoutSettings settings)
    {
        void Apply(IEnumerable<ColumnLayout> source, bool global)
        {
            foreach (var value in source)
            {
                var column = columns.FirstOrDefault(c => c.Id == value.ColumnId);
                if (column is null) continue;
                if (value.Order is int order) column.Order = order;
                if (value.Width is double width && double.IsFinite(width)) column.Width = Math.Max(column.MinWidth, width);
                if (value.Visible is bool visible) column.Visible = visible;
                if (global && value.Title is not null) column.Title = value.Title;
            }
        }
        Apply(settings.Global, true); Apply(settings.User, false);
        foreach (var column in columns)
        {
            if (!double.IsFinite(column.Width) || !double.IsFinite(column.MinWidth) || column.MinWidth < 24)
                throw new InvalidOperationException("Invalid column width: " + column.Id);
            column.Width = Math.Max(column.MinWidth, column.Width);
        }
    }
}
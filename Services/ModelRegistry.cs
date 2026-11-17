using UniversalTables.Configuration;
using UniversalTables.Models;

namespace UniversalTables.Services;

public sealed class ModelRegistration(Type type, string objectType, string title, Func<IServiceProvider,IReadOnlyList<TableColumn>> configure)
{
    public Type Type { get; } = type;
    public string ObjectType { get; } = objectType;
    public string Title { get; } = title;
    public IReadOnlyList<TableColumn> CreateColumns(IServiceProvider services) => configure(services);
    public Func<IServiceProvider,IReadOnlyList<TableMenuItem>>? ConfigureMenu { get; init; }
}

public sealed class ModelRegistry
{
    public GlobalFilterOperators FilterOperators { get; } = new();
    private readonly Dictionary<Type,ModelRegistration> _byType = [];
    private readonly Dictionary<string,ModelRegistration> _byName = new(StringComparer.Ordinal);
    public void Add<T>(string objectType, string title, Action<IServiceProvider,Columns<T>> configure) where T : class, IWebModel
    {
        if (_byType.ContainsKey(typeof(T)) || _byName.ContainsKey(objectType)) throw new ArgumentException("Duplicate model registration.");
        var registration = new ModelRegistration(typeof(T), objectType, title, services =>
        {
            var columns = new Columns<T>(); configure(services, columns);
            ColumnMetadata.Apply(typeof(T), columns.Items);
            foreach (var column in columns.Items)
                column.CustomOperators = FilterOperators.For(column.ValueType);
            return columns.Items;
        }) { ConfigureMenu = services =>
        {
            var columns=new Columns<T>(); configure(services,columns); return columns.ContextMenu;
        } };
        _byType.Add(typeof(T), registration); _byName.Add(objectType, registration);
    }
    public ModelRegistration Get(Type type) => _byType.TryGetValue(type, out var result) ? result : throw new InvalidOperationException("Unregistered model: " + type.FullName);
    public ModelRegistration Get(string objectType) => _byName.TryGetValue(objectType, out var result) ? result : throw new InvalidOperationException("Unregistered ObjectType: " + objectType);
    public IReadOnlyCollection<ModelRegistration> All => _byType.Values;
}
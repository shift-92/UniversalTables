namespace UniversalTables.Configuration;

public sealed record GlobalFilterOperator(string Id, string Title, Type ValueType, Func<object?, bool> Matches);

public sealed class GlobalFilterOperators
{
    private readonly List<GlobalFilterOperator> _items = [];
    public void Add<T>(string id, string title, Func<T, bool> predicate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(predicate);
        if (_items.Any(x => x.Id == id)) throw new ArgumentException("Duplicate filter operator: " + id);
        _items.Add(new(id, title, typeof(T), value => value is T typed && predicate(typed)));
    }
    public IReadOnlyList<GlobalFilterOperator> For(Type type) =>
        _items.Where(x => x.ValueType.IsAssignableFrom(Nullable.GetUnderlyingType(type) ?? type)).ToArray();
}
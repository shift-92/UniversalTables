namespace UniversalTables.Models;

public sealed record TableWindowRequest(Guid WindowId, Type ModelType, string ObjectType,
    string Title, string? ForeignKey = null, Guid? ParentId = null)
{
    public IReadOnlyDictionary<string,object?> FixedValues => ForeignKey is not null && ParentId is Guid id
        ? new Dictionary<string,object?> { [ForeignKey]=id }
        : new Dictionary<string,object?>();
}
namespace UniversalTables.Models;

public enum RowOperation { Edit, Copy, Delete }
public sealed record RowOperationRequest(object Row, EntityKey Entity, RowOperation Operation);
public sealed record TableSelection(IReadOnlyList<object> Objects, IReadOnlyList<EntityKey> Keys)
{
    public IReadOnlyList<Guid> Ids => Keys.Select(key=>key.Id).ToArray();
}
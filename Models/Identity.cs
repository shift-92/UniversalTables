namespace UniversalTables.Models;

public enum TableMode { Live, Ticket }
public readonly record struct EntityKey(string ObjectType, Guid Id);
public readonly record struct CellKey(EntityKey Entity, string ColumnId);
public sealed record TableContext(TableMode Mode, string UserId, Guid? TicketId = null)
{
    public string SettingsType(string objectType) => Mode == TableMode.Ticket ? "Ticket" + objectType : objectType;
    public string QueryStorageKey(string objectType) => "UniversalTables:query:v1:"
        + Uri.EscapeDataString(UserId) + ":" + Mode + ":" + Uri.EscapeDataString(objectType);
    public string OperationScope(EntityKey entity) => Mode == TableMode.Ticket
        ? "ticket:" + (TicketId ?? throw new InvalidOperationException("TicketId is required."))
        : "live:" + entity.ObjectType + ":" + entity.Id;
}

public sealed record CellDifference(bool Changed, object? Before, object? After)
{
    public static CellDifference None { get; } = new(false, null, null);
}
public sealed record RowState(bool IsDeleted, bool IsAdded);
public sealed record TableNotice(DateTimeOffset Time, string Message, bool IsError, Guid? ActionId = null);
public sealed record ReferenceOption(Guid Id, string Label, bool Selectable = true)
{
    public string? Description { get; init; }
}
public sealed record DependencyGroup(string Title, string ChildObjectType, string ForeignKey, Guid ParentId, int Count);
public sealed record DeletePreview(bool Allowed, string? Reason, IReadOnlyList<DependencyGroup> Dependencies,
    IReadOnlyList<EntityKey> Affected, string? Version = null);
public sealed record DataInvalidation(IReadOnlySet<string> ObjectTypes, long Version);
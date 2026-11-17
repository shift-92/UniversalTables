using System.Text.Json;
using System.Text.Json.Serialization;

namespace UniversalTables.Models;

public enum ChangeKind { Add, Edit, Copy, Delete, Restore }
public sealed record PathSegment(string Property, Guid? ElementId = null);
public sealed record FieldChange(string ColumnId, IReadOnlyList<PathSegment> Path, JsonElement Value);
public sealed record ChildChange(ChangeKind Kind, Guid Id, IReadOnlyList<FieldChange> Fields);
public sealed record CollectionChange(string Property, string ChildObjectType, string ForeignKey,
    IReadOnlyList<ChildChange> Items);
public sealed record EditRequest(ChangeKind Kind, EntityKey Root, Guid? SourceId,
    IReadOnlyList<FieldChange> Fields, IReadOnlyList<CollectionChange> Collections,
    IReadOnlyDictionary<string, JsonElement> FixedValues);

public sealed record CommandEnvelope(Guid ActionId, string Scope, TableContext Context, JsonElement Request)
{
    public static CommandEnvelope Create(TableContext context, EditRequest request, Guid? actionId = null)
    {
        var id = actionId ?? Guid.NewGuid();
        if (id == Guid.Empty) throw new ArgumentException("ActionId must not be empty.");
        return new(id, context.OperationScope(request.Root), context,
            JsonSerializer.SerializeToElement(request, TableJson.Options).Clone());
    }
}

public enum CompletionStatus { Confirmed, NotChanged, Rejected }
public sealed record CommandCompletion(Guid ActionId, CompletionStatus Status, string? Message = null);
public enum TransportStatus { Accepted, Rejected, Unknown }
public sealed record TransportReceipt(TransportStatus Status, string? Message = null);
public enum DispatchStatus { Confirmed, NotChanged, Rejected, Unknown }
public sealed record DispatchResult(Guid ActionId, DispatchStatus Status, string? Message);

public static class TableJson
{
    public static JsonSerializerOptions Options { get; } = Create();
    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
    public static JsonElement Value(object? value, Type type) => JsonSerializer.SerializeToElement(value, type, Options);
}
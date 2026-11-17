using UniversalTables.Models;

namespace UniversalTables.Services;

// All methods accessing cache/state run on the Blazor circuit unless the application explicitly provides synchronization.
public interface ITableDataSource
{
    TableContext Context { get; }
    IReadOnlyList<object> Rows(Type registeredType);
    // In Ticket mode: merge cache and state by Id, state wins, tombstones suppress cache entries.
    IReadOnlyList<ReferenceOption> ReferenceOptions(Type registeredType);
    ReferenceOption? ResolveReference(Type registeredType, Guid id);
    RowState GetRowState(EntityKey key);
    CellDifference GetDifference(CellKey key);
    bool CanEdit(EntityKey key);
    event Action<DataInvalidation>? Invalidated;
}

public interface ICommandTransport
{
    // Rejected is definitive. A lost response must be Unknown, not Rejected.
    Task<TransportReceipt> SendAsync(CommandEnvelope command, CancellationToken cancellationToken);
    // Raised after local cache/state has applied the corresponding server event.
    event Action<CommandCompletion>? Completed;
    // Null means not yet known, not "rejected". Must not mutate the request.
    Task<CommandCompletion?> GetOutcomeAsync(CommandEnvelope command, CancellationToken cancellationToken);
}

public interface ITableLayoutStore
{
    Task<LayoutSettings> LoadAsync(string settingsType, string userId, CancellationToken cancellationToken);
    Task SaveUserAsync(string settingsType, string userId, IReadOnlyList<ColumnLayout> columns, CancellationToken cancellationToken);
}

public interface IDeletionService
{
    // Live: inspect immediate child collections. Ticket: delegate to history planners.
    Task<DeletePreview> PreviewDeleteAsync(EntityKey root, CancellationToken cancellationToken);
    Task<DeletePreview> PreviewRestoreAsync(EntityKey root, CancellationToken cancellationToken);
}

public interface ITableDefaultsProvider
{
    // Explicit field defaults only. UI does not instantiate or mutate domain models.
    IReadOnlyDictionary<string, object?> Defaults(Type modelType, ChangeKind kind);
}

public sealed class EmptyTableDefaultsProvider : ITableDefaultsProvider
{
    public IReadOnlyDictionary<string, object?> Defaults(Type modelType, ChangeKind kind) => new Dictionary<string, object?>();
}
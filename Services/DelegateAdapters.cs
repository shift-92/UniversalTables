using UniversalTables.Models;

namespace UniversalTables.Services;

// Explicit application boundaries; no fake success or reflection-based calls into unknown services.
public sealed class DelegateCommandTransport(
    Func<CommandEnvelope,CancellationToken,Task<TransportReceipt>> send,
    Func<CommandEnvelope,CancellationToken,Task<CommandCompletion?>> outcome) : ICommandTransport
{
    public event Action<CommandCompletion>? Completed;
    public Task<TransportReceipt> SendAsync(CommandEnvelope command,CancellationToken token)=>send(command,token);
    public Task<CommandCompletion?> GetOutcomeAsync(CommandEnvelope command,CancellationToken token)=>outcome(command,token);
    public void PublishAfterStateUpdate(CommandCompletion completion)=>Completed?.Invoke(completion);
}
public sealed class DelegateLayoutStore(
    Func<string,string,CancellationToken,Task<LayoutSettings>> load,
    Func<string,string,IReadOnlyList<ColumnLayout>,CancellationToken,Task> save) : ITableLayoutStore
{
    public Task<LayoutSettings> LoadAsync(string settingsType,string userId,CancellationToken token)=>load(settingsType,userId,token);
    public Task SaveUserAsync(string settingsType,string userId,IReadOnlyList<ColumnLayout> columns,CancellationToken token)=>save(settingsType,userId,columns,token);
}
public sealed class DelegateDeletionService(
    Func<EntityKey,CancellationToken,Task<DeletePreview>> delete,
    Func<EntityKey,CancellationToken,Task<DeletePreview>> restore) : IDeletionService
{
    public Task<DeletePreview> PreviewDeleteAsync(EntityKey root,CancellationToken token)=>delete(root,token);
    public Task<DeletePreview> PreviewRestoreAsync(EntityKey root,CancellationToken token)=>restore(root,token);
}
using UniversalTables.Models;

namespace UniversalTables.Services;

public sealed record PendingOperation(CommandEnvelope Command, bool Active);

// Circuit-scoped. Owns pending operations independently from transient editors/dialogs.
public sealed class CommandDispatcher : IDisposable
{
    private sealed class Entry(CommandEnvelope command)
    {
        public CommandEnvelope Command { get; } = command;
        public bool Active { get; set; } = true;
        public CommandCompletion? Outcome { get; set; }
        public TaskCompletionSource<CommandCompletion> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly object _gate = new();
    private readonly Dictionary<Guid,Entry> _entries = [];
    private readonly ICommandTransport _transport;
    private bool _disposed;
    public CommandDispatcher(ICommandTransport transport) { _transport = transport; transport.Completed += Receive; }
    public IReadOnlyList<PendingOperation> Pending
    {
        get { lock (_gate) return _entries.Values.Select(e => new PendingOperation(e.Command, e.Active)).ToArray(); }
    }
    public bool IsBlocked(string scope) { lock (_gate) return _entries.Values.Any(e => e.Command.Scope == scope); }

    public async Task<DispatchResult> SendAsync(CommandEnvelope command, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        Entry entry;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.Values.Any(e => e.Command.Scope == command.Scope)) throw new InvalidOperationException("An active or unconfirmed operation exists in this scope.");
            if (command.ActionId == Guid.Empty || _entries.ContainsKey(command.ActionId)) throw new ArgumentException("Invalid or duplicate ActionId.");
            entry = new(command with { Request = command.Request.Clone() });
            _entries.Add(command.ActionId, entry);
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            var response = await _transport.SendAsync(entry.Command, deadline.Token).WaitAsync(deadline.Token);
            if (response.Status == TransportStatus.Rejected)
                return Finish(entry, new(command.ActionId, CompletionStatus.Rejected, response.Message));
            if (response.Status == TransportStatus.Unknown) return Unknown(entry, response.Message ?? "Результат отправки неизвестен.");
            var completion = await entry.Completion.Task.WaitAsync(deadline.Token);
            return Finish(entry, completion);
        }
        catch (OperationCanceledException)
        {
            return Unknown(entry, "Подтверждение не получено. Операция могла выполниться.");
        }
        catch (Exception ex)
        {
            return Unknown(entry, ex.Message);
        }
    }
    private DispatchResult Unknown(Entry entry, string message)
    {
        lock (_gate)
        {
            if (entry.Outcome is not null) return FinishLocked(entry, entry.Outcome);
            entry.Active = false;
            return new(entry.Command.ActionId, DispatchStatus.Unknown, message);
        }
    }
    private DispatchResult Finish(Entry entry, CommandCompletion completion)
    {
        lock (_gate) return FinishLocked(entry, entry.Outcome ?? completion);
    }
    private DispatchResult FinishLocked(Entry entry, CommandCompletion completion)
    {
        _entries.Remove(entry.Command.ActionId);
        return new(completion.ActionId, completion.Status switch
        {
            CompletionStatus.Confirmed => DispatchStatus.Confirmed,
            CompletionStatus.NotChanged => DispatchStatus.NotChanged,
            _ => DispatchStatus.Rejected
        }, completion.Message);
    }
    private void Receive(CommandCompletion completion)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(completion.ActionId, out var entry)) return;
            entry.Outcome = completion;
            entry.Completion.TrySetResult(completion);
            if (!entry.Active) _entries.Remove(completion.ActionId);
        }
    }
    public async Task<CommandCompletion?> ReconcileAsync(Guid actionId, CancellationToken cancellationToken = default)
    {
        Entry? entry;
        lock (_gate)
        {
            _entries.TryGetValue(actionId, out entry);
            if (entry?.Active == true) throw new InvalidOperationException("Operation is still active.");
        }
        if (entry is null) return null;
        var result = await _transport.GetOutcomeAsync(entry.Command, cancellationToken);
        if (result is not null)
        {
            if (result.ActionId != actionId) throw new InvalidOperationException("Outcome ActionId mismatch.");
            Receive(result);
        }
        return result;
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _transport.Completed -= Receive;
            foreach (var entry in _entries.Values) entry.Completion.TrySetCanceled();
        }
    }
}
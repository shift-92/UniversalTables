using System.Text.Json;
using Microsoft.JSInterop;
using UniversalTables.Models;

namespace UniversalTables.Services;

// Call only after interactive rendering; not during server prerendering.
public sealed class BrowserQueryStore(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private readonly SemaphoreSlim _initialization=new(1,1);
    private async Task<IJSObjectReference> ModuleAsync()
    {
        await _initialization.WaitAsync();
        try { return _module ??= await js.InvokeAsync<IJSObjectReference>("import","./_content/UniversalTables/query-storage.js"); }
        finally { _initialization.Release(); }
    }
    public async Task<QueryState?> LoadAsync(TableContext context,string objectType)
    {
        var module=await ModuleAsync();
        var json=await module.InvokeAsync<string?>("load",context.QueryStorageKey(objectType));
        return json is null ? null : JsonSerializer.Deserialize<QueryState>(json,TableJson.Options) ?? throw new JsonException("Null query document.");
    }
    public async Task SaveAsync(TableContext context,string objectType,QueryState query)
    {
        var module=await ModuleAsync();
        await module.InvokeVoidAsync("save",context.QueryStorageKey(objectType),JsonSerializer.Serialize(query,TableJson.Options));
    }
    public async Task ClearAsync(TableContext context,string objectType)
    {
        var module=await ModuleAsync();
        await module.InvokeVoidAsync("remove",context.QueryStorageKey(objectType));
    }
    public async ValueTask DisposeAsync()
    {
        if(_module is null) return;
        try { await _module.DisposeAsync(); } catch(JSDisconnectedException) { }
    }
}
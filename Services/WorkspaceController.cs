using UniversalTables.Models;
using UniversalTables.Drafts;

namespace UniversalTables.Services;

public enum WorkspaceWindowKind { Table, Form, Delete, Recovery }
public sealed class WorkspaceWindow
{
    public Guid Id { get; }=Guid.NewGuid();
    public required WorkspaceWindowKind Kind {get;init;}
    public required string Title {get;init;}
    public TableWindowRequest? Table {get;init;}
    public FormSession? Form {get;init;}
    public EntityKey? Entity {get;init;}
    public bool Restore {get;init;}
    public bool Busy {get;internal set;}
}

// One controller per page/workspace. Do not register a global singleton.
public sealed class WorkspaceController(ModelRegistry registry,DraftFactory factory,ITableDataSource data)
{
    private readonly List<WorkspaceWindow> _windows=[];
    public IReadOnlyList<WorkspaceWindow> Windows=>_windows;
    public ITableDataSource Data=>data;
    public event Action? Changed;
    public event Action<DispatchResult>? OperationFinished;
    private void Push(WorkspaceWindow window)
    {
        if(_windows.LastOrDefault()?.Busy==true) throw new InvalidOperationException("Дождитесь завершения операции.");
        _windows.Add(window); Changed?.Invoke();
    }
    public void OpenTable(Type type,string? foreignKey=null,Guid? parentId=null,string? title=null)
    {
        if((foreignKey is null)!=(parentId is null)) throw new ArgumentException("ForeignKey and ParentId must be supplied together.");
        var model=registry.Get(type);
        if(foreignKey is not null) CollectionRelations.FixedFilter(type,foreignKey,parentId!.Value);
        var request=new TableWindowRequest(Guid.NewGuid(),type,model.ObjectType,title ?? model.Title,foreignKey,parentId);
        Push(new(){Kind=WorkspaceWindowKind.Table,Title=request.Title,Table=request});
    }
    public void OpenDependency(DependencyGroup dependency)=>OpenTable(registry.Get(dependency.ChildObjectType).Type,
        dependency.ForeignKey,dependency.ParentId,dependency.Title);
    public void OpenForm(Type type,ChangeKind kind,object? source=null,IReadOnlyDictionary<string,object?>? fixedValues=null)
    {
        var model=registry.Get(type);
        var session=factory.Create(type,data,kind,source,fixedValues);
        var title=(kind switch {ChangeKind.Add=>"Добавление",ChangeKind.Copy=>"Копирование",_=>"Редактирование"})+": "+model.Title;
        Push(new(){Kind=WorkspaceWindowKind.Form,Title=title,Form=session});
    }
    public void OpenDeletion(EntityKey entity,bool restore=false)
    {
        registry.Get(entity.ObjectType);
        if(restore && data.Context.Mode!=TableMode.Ticket) throw new InvalidOperationException("Восстановление доступно только в заявке.");
        Push(new(){Kind=WorkspaceWindowKind.Delete,Title=restore ? "Восстановление" : "Удаление",Entity=entity,Restore=restore});
    }
    public void OpenRecovery()=>Push(new(){Kind=WorkspaceWindowKind.Recovery,Title="Неподтверждённые операции"});
    public void SetBusy(Guid id,bool busy)
    {
        var window=_windows.FirstOrDefault(w=>w.Id==id);
        if(window is null) return;
        window.Busy=busy; Changed?.Invoke();
    }
    public bool Close(Guid id)
    {
        var top=_windows.LastOrDefault();
        if(top is null || top.Id!=id || top.Busy) return false;
        _windows.RemoveAt(_windows.Count-1); Changed?.Invoke(); return true;
    }
    public void Complete(Guid id,DispatchResult result)
    {
        if(result.Status is not (DispatchStatus.Confirmed or DispatchStatus.NotChanged)) return;
        Close(id); OperationFinished?.Invoke(result);
    }
}
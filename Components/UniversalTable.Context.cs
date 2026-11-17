using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using UniversalTables.Configuration;
using UniversalTables.Drafts;
using UniversalTables.Models;
using UniversalTables.Query;

namespace UniversalTables.Components;

public partial class UniversalTable
{
    [Parameter] public EventCallback AddRequested {get;set;}
    [Parameter] public EventCallback<EntityKey> RestoreRequested {get;set;}
    private IReadOnlyList<ColumnLayout> _defaultLayout=[];
    private IReadOnlyList<TableMenuItem> _customMenu=[];
    private IReadOnlyList<TableMenuItem> _menuItems=[];
    private TableMenuContext? _contextMenu;
    private readonly List<string> _groups=[];
    private readonly HashSet<string> _collapsed=[];
    private List<FlatTableRow> _flatRows=[];
    private bool _dialogEditor,_bulkBusy;

    private object? GroupValue(object row,TableColumn column)=>column.GroupValue is { } custom ? custom(row)
        : column.Reference is { } reference ? FormDraft.ReadPath(row,reference.ForeignKeyPath) : column.Read(row);
    private void BuildGroups()=>_flatRows=FlatGroups.Build(_rows,_groups.Select(id=>_columns.Single(c=>c.Id==id)).ToArray(),
        _collapsed,GroupValue,(row,column)=>column.GroupValue is not null ? GroupValue(row,column)?.ToString() ?? "(пусто)" : DisplayText(row,column) is {Length:>0} text ? text : "(пусто)");
    private void ToggleGroup(string path) { if(!_collapsed.Remove(path)) _collapsed.Add(path); BuildGroups(); }
    private bool Editable(CellContext cell)=>cell.Column.PropertyPath!=nameof(IWebModel.Id) && cell.Column.Collection is null && cell.Column.CellEditable && CanOperate(cell.Entity)
        && !(Data.Context.Mode==TableMode.Ticket && cell.Column.OneToOne);

    [JSInvokable]
    public void OpenContextMenu(string? rowId,string? columnId,bool group,double x,double y)
    {
        if(_bulkBusy) return;
        _hover=null; _menuX=x; _menuY=y;
        var row=_rows.FirstOrDefault(r=>((IWebModel)r).Id.ToString()==rowId);
        var column=_columns.FirstOrDefault(c=>c.Id==columnId);
        _menuEntity=row is null ? null : Key(row);
        var area=TableMenuArea.Table | (row is null ? 0 : TableMenuArea.Row) | (column is null ? 0 : TableMenuArea.Column)
            | (row is not null && column is not null ? TableMenuArea.Cell : 0) | (group ? TableMenuArea.Group : 0);
        _contextMenu=new(Data,row,column,SelectedObjects,area);
        var items=new List<TableMenuItem>();
        TableMenuItem Item(string id,string title,int order,Func<Task> run,Func<bool>? enabled=null)=>new()
        { Id=id,Title=title,Order=order,Execute=_=>run(),Enabled=_=>enabled?.Invoke() ?? true };
        if(_field is not null)
        {
            items.Add(Item("cancel-edit","Отменить изменение",0,CloseEditorAsync,()=>_field?.Busy!=true));
            _menuItems=items; StateHasChanged(); return;
        }
        items.Add(Item("reset-query","Сбросить фильтры и сортировку",100,()=>QueryChangedAsync(new())));
        items.Add(Item("reset-columns","Сбросить настройки колонок",200,async()=>
        {
            foreach(var c in _columns)
            {
                var layout=_defaultLayout.Single(d=>d.ColumnId==c.Id);
                c.Order=layout.Order!.Value; c.Width=layout.Width!.Value; c.Visible=layout.Visible!.Value;
            }
            await SaveLayoutAsync();
        }));
        items.Add(new() { Id="columns",Title="Видимость колонок",Order=300,Separator=TableMenuSeparator.Bottom,Children=_columns.OrderBy(c=>c.Order)
            .Select(VisibilityMenuItem).ToArray() });
        items.Add(Item("add","Добавить объект",400,()=>AddRequested.InvokeAsync(),()=>AddRequested.HasDelegate));
        items.Add(Item("ungroup","Отменить все группировки",500,()=>{_groups.Clear();_collapsed.Clear();BuildGroups();return Task.CompletedTask;},()=>_groups.Count>0));
        if(row is not null)
        {
            items.Add(new() {Id="object",Title="Объект",Order=600,Children=ObjectMenuItems(row)});
        }
        if(group && column is not null)
            items.Add(Item("ungroup-column",$"Убрать группировку по «{column.Title}»",510,()=>
            {_groups.Remove(column.Id);_collapsed.Clear();BuildGroups();return Task.CompletedTask;},()=>_groups.Contains(column.Id)));
        if(column is not null && !group)
        {
            items.Add(Item("reset-column-filter","Сбросить фильтр столбца",700,()=>
            {var query=_query.Copy();query.Filters.RemoveAll(f=>f.ColumnId==column.Id);return QueryChangedAsync(query);}));
            items.Add(Item("hide-column","Скрыть столбец",800,()=>VisibilityAsync(column,false)));
            items.Add(Item("group-column","Сгруппировать",900,()=>{_groups.Add(column.Id);BuildGroups();return Task.CompletedTask;},
                ()=>!_groups.Contains(column.Id) && (column.Collection is null || column.GroupValue is not null)));
        }
        if(row is not null && column is not null)
        {
            var cell=new CellContext(row,Key(row),column,column.Read(row),Data.GetDifference(new(Key(row),column.Id)));
            if(column.Collection is null)
            {
                items.Add(Item("copy-value","Копировать значение",1000,()=>JS.InvokeVoidAsync("navigator.clipboard.writeText",DisplayText(row,column)).AsTask()));
                var selected=SelectedKeys.ToArray();
                items.Add(Item("apply-value","Применить значение к выделенным строкам",1100,()=>ApplyValueAsync(cell,selected),()=>Editable(cell) && selected.Length>0));
            }
            items.Add(Item("edit-value","Изменить",1200,async()=>
            {
                if(column.Collection is not null) {await CollectionRequested.InvokeAsync(cell);return;}
                await ActivateAsync(cell);
                if(_field is not null) {_dialogEditor=true;_editorStyle="left:50%;top:20%;transform:translateX(-50%);width:min(640px,90vw)";}
            },()=>column.Collection is not null ? CollectionRequested.HasDelegate : Editable(cell)));
        }
        items.AddRange(_customMenu); _menuItems=items; StateHasChanged();
    }
    private IReadOnlyList<TableMenuItem> ObjectMenuItems(object row)
    {
        var key=Key(row);
        TableMenuItem Item(string id,string title,int order,Func<Task> run,Func<bool> enabled)=>new()
        { Id=id,Title=title,Order=order,Execute=_=>run(),Enabled=_=>enabled() };
        return
        [
            Item("edit-object","Изменить",100,()=>RowOperationRequested.InvokeAsync(new(row,key,RowOperation.Edit)),()=>RowOperationRequested.HasDelegate && CanOperate(key)),
            Item("delete-object","Удалить",200,()=>RowOperationRequested.InvokeAsync(new(row,key,RowOperation.Delete)),()=>RowOperationRequested.HasDelegate && CanOperate(key)),
            Item("copy-object","Копировать",300,()=>RowOperationRequested.InvokeAsync(new(row,key,RowOperation.Copy)),()=>RowOperationRequested.HasDelegate && CanOperate(key)),
            Item("restore-object","Восстановить",400,()=>RestoreRequested.InvokeAsync(key),()=>RestoreRequested.HasDelegate && Data.Context.Mode==TableMode.Ticket && Data.GetRowState(key).IsDeleted && Data.CanEdit(key) && !Dispatcher.IsBlocked(Data.Context.OperationScope(key)))
        ];
    }
    private TableMenuItem VisibilityMenuItem(TableColumn column)
    {
        TableMenuItem? item=null;
        item=new() {Id="visible:"+column.Id,Title=(column.Visible ? "✓ " : "")+column.Title,
            Order=column.Order,CloseOnClick=false,Execute=async _=>
            {
                await VisibilityAsync(column,!column.Visible);
                item!.Title=(column.Visible ? "✓ " : "")+column.Title;
            }};
        return item;
    }
    private void CancelMenu(MouseEventArgs e)=>OpenContextMenu(null,null,false,e.ClientX,e.ClientY);
    private async Task ExecuteMenuAsync(TableMenuItem item)
    {
        var context=_contextMenu;
        if(context is null || !(item.Enabled?.Invoke(context) ?? true)) return;
        if(item.CloseOnClick) CloseRowMenu();
        try {if(item.Execute is not null) await item.Execute(context);}
        catch(Exception ex) {Error(ex.Message);}
    }
    private async Task ApplyValueAsync(CellContext source,IReadOnlyList<EntityKey> selected)
    {
        if(_bulkBusy || !Editable(source)) return;
        var value=source.Column.Reference is { } reference ? FormDraft.ReadPath(source.Row,reference.ForeignKeyPath) : source.Value;
        CommandEnvelope Build(EntityKey key)
        {
            var row=ReadRows().Single(r=>Key(r)==key);
            if(!CanOperate(key)) throw new InvalidOperationException("Одна из выделенных строк недоступна для изменения.");
            var draft=new FormDraft(_registration,[source.Column],Data.Context,ChangeKind.Edit,row,cellOperation:true);
            var field=draft.Fields.Single();
            if(field.ReadOnly) throw new InvalidOperationException("Поле недоступно для изменения.");
            field.Value=value;field.IncludeInSubmission=true;
            return CommandEnvelope.Create(Data.Context,draft.Build());
        }
        foreach(var key in selected) Build(key);
        if(!await JS.InvokeAsync<bool>("confirm",$"Применить значение к {selected.Count} строкам? Операции выполняются последовательно; при ошибке уже подтверждённые изменения сохранятся.")) return;
        _bulkBusy=true;
        var completed=0;
        try
        {
            foreach(var key in selected)
            {
                var result=await Dispatcher.SendAsync(Build(key),ConfirmationTimeout,_lifetime.Token);
                if(result.Status is not (DispatchStatus.Confirmed or DispatchStatus.NotChanged))
                    throw new InvalidOperationException(result.Message ?? "Операция не подтверждена; обработка остановлена.");
                completed++;
            }
        }
        finally {_bulkBusy=false;Rebuild();Log($"Обработано строк: {completed} из {selected.Count}",completed!=selected.Count);}
    }
}
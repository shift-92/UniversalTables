using UniversalTables.Configuration;
using UniversalTables.Models;
using UniversalTables.Services;

namespace UniversalTables.Drafts;

public sealed class ChildDraftRow(FormDraft form,bool added)
{
    public FormDraft Form { get; }=form;
    public bool Added { get; }=added;
    public bool Deleted { get; internal set; }
}
public sealed class CollectionDraft
{
    public TableColumn Column { get; }
    public FormDraft Parent { get; }
    public List<ChildDraftRow> Rows { get; }=[];
    private readonly ModelRegistration _child;
    private readonly IReadOnlyList<TableColumn> _columns;
    private readonly ITableDefaultsProvider _defaults;
    private readonly string _fk;
    public CollectionDraft(FormDraft parent,TableColumn column,ModelRegistration child,IReadOnlyList<TableColumn> childColumns,
        IEnumerable<object> source,ITableDefaultsProvider defaults)
    {
        var relation=column.Collection ?? throw new ArgumentException("Not a collection column.");
        if(relation.ElementType!=child.Type) throw new ArgumentException("Child type mismatch.");
        Parent=parent; Column=column; _child=child; _columns=childColumns; _defaults=defaults; _fk=relation.ForeignKey;
        // Copying child collections is explicit, not automatic. Add/edit uses root Id; copy starts empty.
        if(parent.Kind!=ChangeKind.Edit) return;
        var fk=child.Type.GetProperty(_fk) ?? throw new InvalidOperationException("Missing child FK.");
        foreach(var item in source.Where(x=>fk.GetValue(x) is Guid id && id==parent.Root.Id))
            Rows.Add(new(new(child,childColumns,parent.Context,ChangeKind.Edit,item,fixedValues:Fixed()),false));
    }
    private IReadOnlyDictionary<string,object?> Fixed()=>new Dictionary<string,object?> {[_fk]=Parent.Root.Id};
    public ChildDraftRow Add()
    {
        var form=new FormDraft(_child,_columns,Parent.Context,ChangeKind.Add,defaults:_defaults.Defaults(_child.Type,ChangeKind.Add),fixedValues:Fixed());
        var row=new ChildDraftRow(form,true); Rows.Add(row); return row;
    }
    public void Delete(ChildDraftRow row)
    {
        if(!Rows.Contains(row)) throw new ArgumentException("Unknown row.");
        if(row.Added) Rows.Remove(row); else row.Deleted=true;
    }
    public void Restore(ChildDraftRow row)
    {
        if(!Rows.Contains(row)) throw new ArgumentException("Unknown row.");
        row.Deleted=false;
    }
    public CollectionChange Build()
    {
        var changes=new List<ChildChange>();
        foreach(var row in Rows)
        {
            if(row.Deleted) { changes.Add(new(ChangeKind.Delete,row.Form.Root.Id,[])); continue; }
            if(row.Form.Collections.Count>0) throw new InvalidOperationException("Only one child collection level is supported in a parent form.");
            var request=row.Form.Build();
            if(row.Added || request.Fields.Count>0) changes.Add(new(row.Added ? ChangeKind.Add : ChangeKind.Edit,row.Form.Root.Id,request.Fields));
        }
        return new(Column.PropertyPath,_child.ObjectType,_fk,changes);
    }
}
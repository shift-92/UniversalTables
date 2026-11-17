using UniversalTables.Configuration;
using UniversalTables.Drafts;
using UniversalTables.Models;

namespace UniversalTables.Services;

public sealed record FormSession(FormDraft Draft,IReadOnlyList<TableColumn> Columns);

public sealed class DraftFactory(ModelRegistry registry,IServiceProvider services,ITableDefaultsProvider defaults)
{
    public FormSession Create(Type type,ITableDataSource data,ChangeKind kind,object? source=null,
        IReadOnlyDictionary<string,object?>? fixedValues=null,bool includeCollections=true)
    {
        var registration=registry.Get(type);
        var columns=registration.CreateColumns(services);
        var draft=new FormDraft(registration,columns,data.Context,kind,source,
            defaults.Defaults(type,kind),fixedValues);
        foreach(var field in draft.Fields)
        {
            if(field.Column.Reference is {Nullable:false} && field.Value is Guid id && id==Guid.Empty)
            {
                // Do not silently submit Guid.Empty when a required FK has not been selected.
                field.Value=null;
            }
        }
        if(includeCollections)
        {
            foreach(var column in columns.Where(c=>c.FormVisible && c.Collection is not null))
            {
                if(column.PropertyPath.Contains('.')) continue;
                var child=registry.Get(column.Collection!.ElementType);
                var children=data.Rows(child.Type).Where(row=>
                    !data.GetRowState(new EntityKey(child.ObjectType,((IWebModel)row).Id)).IsDeleted);
                draft.Collections.Add(new CollectionDraft(draft,column,child,child.CreateColumns(services),children,defaults));
            }
        }
        return new(draft,columns);
    }
    public void ValidateSubmission(FormDraft draft,ITableDataSource data)
    {
        if(draft.Context!=data.Context) throw new InvalidOperationException("Контекст формы изменился. Откройте форму заново.");
        if(draft.Kind==ChangeKind.Edit && (!data.CanEdit(draft.Root) || data.GetRowState(draft.Root).IsDeleted))
            throw new InvalidOperationException("Редактирование объекта больше недоступно.");
        ValidateFields(draft);
        foreach(var collection in draft.Collections)
        {
            foreach(var row in collection.Rows.Where(r=>!r.Deleted)) ValidateFields(row.Form);
        }
    }
    private static void ValidateFields(FormDraft draft)
    {
        foreach(var field in draft.Fields.Where(f=>!f.ReadOnly))
        {
            if(field.Error is not null) throw new InvalidOperationException(field.Column.Title+": "+field.Error);
            if(!field.IncludeInSubmission) continue;
            if(field.Value is null && (field.Column.Required || field.Column.Reference is {Nullable:false}))
                throw new InvalidOperationException(field.Column.Title+": значение обязательно.");
        }
    }
}
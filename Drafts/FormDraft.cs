using System.Reflection;
using System.Text.Json;
using UniversalTables.Configuration;
using UniversalTables.Models;
using UniversalTables.Services;

namespace UniversalTables.Drafts;

public sealed class FormDraft
{
    public Type ModelType { get; }
    public EntityKey Root { get; }
    public Guid? SourceId { get; }
    public ChangeKind Kind { get; }
    public TableContext Context { get; }
    public List<FieldDraft> Fields { get; }=[];
    public List<CollectionDraft> Collections { get; }=[];
    private readonly Dictionary<string,JsonElement> _fixed=[];
    public FormDraft(ModelRegistration registration,IReadOnlyList<TableColumn> columns,TableContext context,
        ChangeKind kind,object? source=null,IReadOnlyDictionary<string,object?>? defaults=null,
        IReadOnlyDictionary<string,object?>? fixedValues=null,Guid? newId=null,bool cellOperation=false)
    {
        if(kind is not (ChangeKind.Add or ChangeKind.Copy or ChangeKind.Edit)) throw new ArgumentException("Invalid form kind.");
        if(kind!=ChangeKind.Add && (source is not IWebModel || !registration.Type.IsInstanceOfType(source))) throw new ArgumentException("A source model is required.");
        ModelType=registration.Type; Context=context; Kind=kind;
        SourceId=(source as IWebModel)?.Id;
        Root=new(registration.ObjectType,kind==ChangeKind.Edit ? SourceId!.Value : newId ?? Guid.NewGuid());
        if(Root.Id==Guid.Empty) throw new ArgumentException("Empty entity Id.");
        if(fixedValues is not null)
            foreach(var pair in fixedValues)
            {
                var property=ModelType.GetProperty(pair.Key) ?? throw new ArgumentException("Invalid fixed property: "+pair.Key);
                _fixed.Add(pair.Key,TableJson.Value(pair.Value,property.PropertyType));
            }
        foreach(var column in columns.Where(c=>(cellOperation || c.FormVisible) && c.Collection is null && c.PropertyPath!=nameof(IWebModel.Id)).OrderBy(c=>c.Order))
        {
            var target=column.Reference?.ForeignKeyPath ?? column.PropertyPath;
            var type=EditType(column);
            if(!Scalar(type)) throw new NotSupportedException("Provide a scalar/reference column instead of "+column.PropertyPath);
            var original=source is null ? null : ReadPath(source,target);
            object? value=original;
            var include=true;
            if(kind==ChangeKind.Add || kind==ChangeKind.Copy && !column.CopyValue)
            {
                include=defaults is not null && defaults.ContainsKey(target);
                value=include ? defaults![target] : type.IsValueType ? Activator.CreateInstance(type) : null;
            }
            var readOnly=!(cellOperation ? column.CellEditable : column.FormEditable ?? column.CellEditable) || context.Mode==TableMode.Ticket && column.OneToOne;
            if(_fixed.TryGetValue(target,out var forced)) { value=forced.Deserialize(type,TableJson.Options); readOnly=true; }
            Fields.Add(new(){Column=column,OriginalValue=original,Value=value,ReadOnly=readOnly,IncludeInSubmission=include});
        }
    }
    public static Type EditType(TableColumn column) => column.Reference is { } reference ? reference.Nullable ? typeof(Guid?) : typeof(Guid) : column.ValueType;
    public static object? ReadPath(object row,string path)
    {
        object? current=row;
        foreach(var part in path.Split('.'))
        {
            if(current is null) return null;
            current=current.GetType().GetProperty(part)?.GetValue(current);
        }
        return current;
    }
    private static bool Scalar(Type type)
    {
        type=Nullable.GetUnderlyingType(type) ?? type;
        return type==typeof(string) || type.IsValueType;
    }
    // Every editor must use this method (or set IncludeInSubmission itself).
    public void SetValue(FieldDraft field,object? value)
    {
        if(!Fields.Contains(field) || field.ReadOnly || field.Busy) throw new InvalidOperationException("Field is not editable.");
        field.Value=value; field.IncludeInSubmission=true; field.Error=null;
    }
    public EditRequest Build()
    {
        var changes=new List<FieldChange>();
        var targets=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in Fields.Where(f=>!f.ReadOnly))
        {
            if(field.Error is not null) throw new InvalidOperationException(field.Column.Title+": "+field.Error);
            if(!field.IncludeInSubmission) continue;
            var column=field.Column; var target=column.Reference?.ForeignKeyPath ?? column.PropertyPath;
            if(Kind==ChangeKind.Edit && Equals(field.OriginalValue,field.Value)) continue;
            if(!targets.Add(target)) throw new InvalidOperationException("Duplicate editable target: "+target);
            var type=EditType(column); var actual=Nullable.GetUnderlyingType(type) ?? type;
            if(field.Value is null && (type.IsValueType && Nullable.GetUnderlyingType(type) is null || column.Required)) throw new InvalidOperationException(column.Title+": значение обязательно.");
            if(column.Required && field.Value is string text && string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException(column.Title+": значение обязательно.");
            if(field.Value is not null && !actual.IsInstanceOfType(field.Value)) throw new InvalidOperationException(column.Title+": неверный тип.");
            if(column.Reference is {Nullable:false} && field.Value is Guid id && id==Guid.Empty) throw new InvalidOperationException(column.Title+": выберите объект.");
            changes.Add(new(column.Id,target.Split('.').Select(p=>new PathSegment(p)).ToArray(),TableJson.Value(field.Value,type)));
        }
        var collectionChanges=Collections.Select(c=>c.Build()).Where(c=>c.Items.Count>0).ToArray();
        if(collectionChanges.Select(c=>c.Property).Distinct(StringComparer.Ordinal).Count()!=collectionChanges.Length) throw new InvalidOperationException("Duplicate collection draft.");
        return new(Kind,Root,SourceId,changes,collectionChanges,_fixed.ToDictionary(p=>p.Key,p=>p.Value.Clone()));
    }
}
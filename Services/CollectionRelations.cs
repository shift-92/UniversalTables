using UniversalTables.Models;

namespace UniversalTables.Services;

public static class CollectionRelations
{
    public static Func<object,bool> FixedFilter(Type childType,string foreignKey,Guid parentId)
    {
        var property=childType.GetProperty(foreignKey) ?? throw new ArgumentException("Unknown FK: "+foreignKey);
        if(property.PropertyType!=typeof(Guid) && property.PropertyType!=typeof(Guid?)) throw new ArgumentException("FK must be Guid/Guid?.");
        return row=>property.GetValue(row) is Guid id && id==parentId;
    }
    public static IReadOnlyList<DependencyGroup> LiveDependencies(EntityKey root,ModelRegistry registry,ITableDataSource data)
    {
        if(data.Context.Mode!=TableMode.Live) throw new InvalidOperationException("Ticket deletion must use its history planner.");
        var model=registry.Get(root.ObjectType);
        var groups=new List<DependencyGroup>();
        foreach(var property in model.Type.GetProperties())
        {
            var type=property.PropertyType;
            if(!type.IsGenericType || type.GetGenericTypeDefinition()!=typeof(List<>) || !typeof(IWebModel).IsAssignableFrom(type.GetGenericArguments()[0])) continue;
            var attribute=property.CustomAttributes.FirstOrDefault(a=>a.AttributeType.Name==nameof(PredictFieldAttribute))
                ?? throw new InvalidOperationException("Missing PredictField on "+property.Name);
            var fk=attribute.ConstructorArguments.LastOrDefault(a=>a.ArgumentType==typeof(string)).Value as string
                ?? throw new InvalidOperationException("Invalid PredictField on "+property.Name);
            var child=registry.Get(type.GetGenericArguments()[0]);
            var predicate=FixedFilter(child.Type,fk,root.Id);
            var count=data.Rows(child.Type).Count(predicate);
            if(count>0) groups.Add(new(property.Name,child.ObjectType,fk,root.Id,count));
        }
        return groups;
    }
}
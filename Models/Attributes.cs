namespace UniversalTables.Models;

[AttributeUsage(AttributeTargets.Property)]
public sealed class MainObjectAttribute(string navigation) : Attribute
{
    public string Navigation { get; } = navigation;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class PredictFieldAttribute(string foreignKey) : Attribute
{
    public string ForeignKey { get; } = foreignKey;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class OneToOneAttribute : Attribute;
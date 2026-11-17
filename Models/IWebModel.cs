namespace UniversalTables.Models;

public interface IWebModel
{
    Guid Id { get; set; }
    string ToDescription();
}
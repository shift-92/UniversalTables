using UniversalTables.Models;
using UniversalTables.Services;

namespace UniversalTables.Configuration;

[Flags]
public enum TableMenuArea { Table=1, Row=2, Column=4, Cell=8, Group=16 }
public enum TableMenuSeparator { None, Top, Bottom, Both }
public sealed record TableMenuContext(ITableDataSource Data, object? Row, TableColumn? Column,
    IReadOnlyList<object> Selection, TableMenuArea Area);
public sealed class TableMenuItem
{
    public required string Id { get; init; }
    public required string Title { get; set; }
    public bool CloseOnClick { get; init; }=true;
    public TableMenuSeparator Separator { get; init; }=TableMenuSeparator.None;
    public int Order { get; init; }
    public TableMenuArea Areas { get; init; }=TableMenuArea.Table;
    public Func<TableMenuContext,bool>? Visible { get; init; }
    public Func<TableMenuContext,bool>? Enabled { get; init; }
    public Func<TableMenuContext,Task>? Execute { get; init; }
    public IReadOnlyList<TableMenuItem> Children { get; init; }=[];
}
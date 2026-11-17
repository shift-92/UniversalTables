using System.Text.Json;
using UniversalTables.Configuration;

namespace UniversalTables.Query;

public sealed record FlatTableRow(object? Row, string Path, int Depth=0, string? Label=null, int Count=0);
public static class FlatGroups
{
    public static List<FlatTableRow> Build(IReadOnlyList<object> rows, IReadOnlyList<TableColumn> columns,
        ISet<string> collapsed, Func<object,TableColumn,object?> value, Func<object,TableColumn,string> label)
    {
        var result=new List<FlatTableRow>();
        void Append(IEnumerable<object> source,int depth,string path)
        {
            if(depth==columns.Count) { result.AddRange(source.Select(row=>new FlatTableRow(row,"",depth))); return; }
            var column=columns[depth];
            foreach(var group in source.GroupBy(row=>JsonSerializer.Serialize(value(row,column))))
            {
                var items=group.ToArray();
                var key=path+"/"+Uri.EscapeDataString(column.Id)+":"+Uri.EscapeDataString(group.Key);
                result.Add(new(null,key,depth,label(items[0],column),items.Length));
                if(!collapsed.Contains(key)) Append(items,depth+1,key);
            }
        }
        Append(rows,0,""); return result;
    }
}
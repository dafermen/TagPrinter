using System.Text;
namespace TagPrinter;

/// <summary>One-row-at-a-time CSV writer with same-directory staging; cancellation/failure preserves the previous destination.</summary>
internal static class CsvFile
{
    /// <summary>Streams quoted UTF-8/BOM cells; progress never retains rows and replacement occurs only after a complete flush.</summary>
    internal static long Write(string path,IEnumerable<string> headers,IEnumerable<IEnumerable<string>> rows,CancellationToken token=default,IProgress<long>? progress=null)
    {
        path=Path.GetFullPath(path);if(!Path.GetExtension(path).Equals(".csv",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Choose a .csv output file.");
        string temp=Path.Combine(Path.GetDirectoryName(path)!,".TagPrinter-export-"+Guid.NewGuid().ToString("N")+".tmp");long count=0;
        try
        {
            token.ThrowIfCancellationRequested();using(var writer=new StreamWriter(temp,false,new UTF8Encoding(true),65536))
            {
                void Row(IEnumerable<string> values)
                {
                    bool first=true;foreach(string value in values)
                    {
                        if(!first)writer.Write(';');first=false;writer.Write('"');
                        // Write spans around quotes instead of allocating a second full copy of a large JSON cell.
                        int start=0;for(int i=0;i<value.Length;i++)if(value[i]=='"'){writer.Write(value.AsSpan(start,i-start));writer.Write("\"\"");start=i+1;}writer.Write(value.AsSpan(start));writer.Write('"');
                    }
                    writer.WriteLine();
                }
                Row(headers);foreach(var row in rows){token.ThrowIfCancellationRequested();Row(row);count++;if(count%1000==0)progress?.Report(count);}writer.Flush();
            }
            progress?.Report(count);token.ThrowIfCancellationRequested();File.Move(temp,path,true);return count;
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}

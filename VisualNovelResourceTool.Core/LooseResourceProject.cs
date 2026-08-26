namespace VisualNovelResourceTool.Core;

public sealed class LooseResourceProject
{
    private static readonly HashSet<string> VisualExtensions=new(StringComparer.OrdinalIgnoreCase){".png",".jpg",".jpeg",".bmp",".webp",".gif",".apng",".avif",".jxl",".tga",".dds",".webm",".mp4",".ogv",".avi"};
    public string Root { get; }
    public IReadOnlyList<LooseEntry> Entries { get; }
    private LooseResourceProject(string root,IReadOnlyList<LooseEntry> entries)=>(Root,Entries)=(root,entries);
    public static LooseResourceProject Open(string root)
    {
        var content=Directory.Exists(Path.Combine(root,"game"))?Path.Combine(root,"game"):root;
        var entries=Directory.EnumerateFiles(content,"*",SearchOption.AllDirectories).Where(f=>!f.Split(Path.DirectorySeparatorChar).Any(p=>p.Equals("解包结果",StringComparison.OrdinalIgnoreCase))&&VisualExtensions.Contains(Path.GetExtension(f))).Select(f=>new LooseEntry(f,Path.GetRelativePath(content,f),new FileInfo(f).Length)).ToArray();
        return new(content,entries);
    }
    public async Task<byte[]> ReadEntryAsync(LooseEntry entry,int maximumBytes=64*1024*1024,CancellationToken token=default){if(entry.Length>maximumBytes)throw new InvalidDataException($"文件过大，无法预览（{entry.Length:N0} 字节）。");return await File.ReadAllBytesAsync(entry.SourcePath,token);}
    public async Task<ExtractionSummary> ExtractAsync(IEnumerable<LooseEntry> selected,string output,bool overwrite,IProgress<ExtractionProgress>? progress=null,CancellationToken token=default,bool addChineseDirectoryLabels=false)
    {
        var items=selected.ToArray();var paths=new SafeExtractionPath(output,addChineseDirectoryLabels);var done=0;var skipped=0;var failed=0;long written=0;var failures=new List<string>();
        for(var i=0;i<items.Length;i++){token.ThrowIfCancellationRequested();var e=items[i];string? temp=null;try{var target=paths.Resolve(e.Name);if(!overwrite&&File.Exists(target)){skipped++;continue;}Directory.CreateDirectory(Path.GetDirectoryName(target)!);temp=target+".vnrt-part";await using var source=new FileStream(e.SourcePath,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.Asynchronous|FileOptions.SequentialScan);await using var destination=File.Create(temp);await source.CopyToAsync(destination,token);written+=source.Length;await destination.DisposeAsync();File.Move(temp,target,true);done++;}catch(OperationCanceledException){if(temp is not null&&File.Exists(temp))File.Delete(temp);throw;}catch(Exception ex){if(temp is not null&&File.Exists(temp))File.Delete(temp);failed++;failures.Add($"{e.Name}: {ex.Message}");}finally{progress?.Report(new(i+1,items.Length,e.Name,written));}}
        paths.WriteReport(failures);return new(done,skipped,written,output,failed,paths.Renamed);
    }
}

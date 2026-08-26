using System.Text.Json;

namespace VisualNovelResourceTool.Core;

public sealed record RpgMakerEntry(string SourcePath, string RelativeOutputPath, long Length, bool Encrypted);

public sealed class RpgMakerProject
{
    public string Root { get; }
    public IReadOnlyList<RpgMakerEntry> Entries { get; }
    public byte[]? EncryptionKey { get; }
    private RpgMakerProject(string root, IReadOnlyList<RpgMakerEntry> entries, byte[]? key) => (Root, Entries, EncryptionKey) = (root, entries, key);
    public static RpgMakerProject Open(string root)
    {
        var content = Directory.Exists(Path.Combine(root,"www")) ? Path.Combine(root,"www") : Directory.Exists(Path.Combine(root,"content")) ? Path.Combine(root,"content") : root;
        var system = Directory.EnumerateFiles(content,"System.json",SearchOption.AllDirectories).FirstOrDefault(); byte[]? key=null;
        if(system is not null) try { using var doc=JsonDocument.Parse(File.ReadAllText(system)); if(doc.RootElement.TryGetProperty("encryptionKey",out var k)&&k.GetString() is {Length:>0} hex) key=Convert.FromHexString(hex); } catch { }
        var encryptedExtensions=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{".rpgmvp",".png"},{".rpgmvo",".ogg"},{".rpgmvm",".m4a"},{".png_",".png"},{".ogg_",".ogg"},{".m4a_",".m4a"}};
        var plainExtensions=new HashSet<string>(StringComparer.OrdinalIgnoreCase){".png",".jpg",".jpeg",".bmp",".webp",".gif",".apng",".avif",".ogg",".m4a",".mp3",".wav",".webm",".mp4",".ogv",".avi"};
        var entries=new List<RpgMakerEntry>(); foreach(var file in Directory.EnumerateFiles(content,"*",SearchOption.AllDirectories)){var ext=Path.GetExtension(file);var rel=Path.GetRelativePath(content,file);if(encryptedExtensions.TryGetValue(ext,out var targetExt)){rel=Path.ChangeExtension(rel,targetExt);entries.Add(new(file,rel,new FileInfo(file).Length,true));}else if(plainExtensions.Contains(ext))entries.Add(new(file,rel,new FileInfo(file).Length,false));}
        return new(root,entries,key);
    }
    public async Task<ExtractionSummary> ExtractAsync(IEnumerable<RpgMakerEntry> selected,string output,bool overwrite,IProgress<ExtractionProgress>? progress=null,CancellationToken token=default,bool addChineseDirectoryLabels=false)
    {
        var items=selected.ToArray();var paths=new SafeExtractionPath(output,addChineseDirectoryLabels);var done=0;var skipped=0;var failed=0;long written=0;var failures=new List<string>();
        if(items.Any(e=>e.Encrypted)&&EncryptionKey is null)throw new InvalidDataException("发现 RPG Maker 加密资源，但 System.json 中没有可用的 encryptionKey。");
        for(var i=0;i<items.Length;i++){token.ThrowIfCancellationRequested();var e=items[i];string? temp=null;try{var target=paths.Resolve(e.RelativeOutputPath);Directory.CreateDirectory(Path.GetDirectoryName(target)!);if(!overwrite&&File.Exists(target)){skipped++;continue;}temp=target+".vnrt-part";if(e.Encrypted){var data=await File.ReadAllBytesAsync(e.SourcePath,token);if(data.Length<16)throw new InvalidDataException("加密文件过短。");var plain=data[16..];for(var n=0;n<Math.Min(16,plain.Length);n++)plain[n]^=EncryptionKey![n%EncryptionKey.Length];await File.WriteAllBytesAsync(temp,plain,token);written+=plain.Length;}else{await using var source=new FileStream(e.SourcePath,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.Asynchronous|FileOptions.SequentialScan);await using var destination=File.Create(temp);await source.CopyToAsync(destination,token);written+=source.Length;}File.Move(temp,target,true);done++;}catch(OperationCanceledException){if(temp is not null&&File.Exists(temp))File.Delete(temp);throw;}catch(Exception ex){if(temp is not null&&File.Exists(temp))File.Delete(temp);failed++;failures.Add($"{e.RelativeOutputPath}: {ex.Message}");}finally{progress?.Report(new(i+1,items.Length,e.RelativeOutputPath,written));}}
        paths.WriteReport(failures);return new(done,skipped,written,output,failed,paths.Renamed);
    }
}

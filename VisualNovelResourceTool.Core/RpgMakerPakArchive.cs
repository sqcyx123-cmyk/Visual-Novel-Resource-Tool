using System.Text;
using System.Text.RegularExpressions;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace VisualNovelResourceTool.Core;

public sealed class RpgMakerPakArchive
{
    private readonly string _password;
    public string Path { get; }
    public IReadOnlyList<PakEntry> Entries { get; }

    private RpgMakerPakArchive(string path,string password,IReadOnlyList<PakEntry> entries){Path=path;_password=password;Entries=entries;}

    public static RpgMakerPakArchive Open(string path)
    {
        var index=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!,"index.html");
        if(!File.Exists(index))throw new InvalidDataException("data.pak 旁没有找到 index.html，无法取得资源密码。");
        var html=File.ReadAllText(index);
        var encoded=Regex.Match(html,"packageKey\\s*=\\s*['\"](?<key>[^'\"]+)['\"]",RegexOptions.IgnoreCase).Groups["key"].Value;
        if(encoded.Length==0)throw new InvalidDataException("index.html 中没有找到 packageKey。");
        var password=DecodeKey(encoded);
        using var archive=ArchiveFactory.OpenArchive(path,new ReaderOptions{Password=password});
        var entries=archive.Entries.Where(e=>!e.IsDirectory&&e.Key is not null).Select(e=>new PakEntry(e.Key!.Replace('\\','/'),checked((long)e.Size))).ToArray();
        return new(path,password,entries);
    }

    public async Task<byte[]> ReadEntryAsync(PakEntry entry,int maximumBytes=64*1024*1024,CancellationToken token=default)
    {
        if(entry.Length>maximumBytes)throw new InvalidDataException($"文件过大，无法预览（{entry.Length:N0} 字节）。");
        using var archive=ArchiveFactory.OpenArchive(Path,new ReaderOptions{Password=_password});
        var source=Find(archive,entry.Name);
        await using var input=source.OpenEntryStream();using var output=new MemoryStream((int)entry.Length);await input.CopyToAsync(output,token);return output.ToArray();
    }

    public async Task<ExtractionSummary> ExtractAsync(IEnumerable<PakEntry> selected,string output,bool overwrite,IProgress<ExtractionProgress>? progress=null,CancellationToken token=default,bool addChineseDirectoryLabels=false)
    {
        var items=selected.ToArray();var paths=new SafeExtractionPath(output,addChineseDirectoryLabels);var done=0;var skipped=0;var failed=0;long written=0;var failures=new List<string>();
        using var archive=ArchiveFactory.OpenArchive(Path,new ReaderOptions{Password=_password});
        var lookup=archive.Entries.Where(e=>!e.IsDirectory&&e.Key is not null).ToDictionary(e=>e.Key!.Replace('\\','/'),StringComparer.Ordinal);
        for(var i=0;i<items.Length;i++)
        {
            token.ThrowIfCancellationRequested();var item=items[i];string? temp=null;
            try
            {
                var target=paths.Resolve(item.Name);if(!overwrite&&File.Exists(target)){skipped++;continue;}Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);temp=target+".vnrt-part";
                await using var input=lookup[item.Name].OpenEntryStream();await using var destination=File.Create(temp);await input.CopyToAsync(destination,token);written+=destination.Length;await destination.DisposeAsync();File.Move(temp,target,true);done++;
            }
            catch(OperationCanceledException){if(temp is not null&&File.Exists(temp))File.Delete(temp);throw;}
            catch(Exception ex){if(temp is not null&&File.Exists(temp))File.Delete(temp);failed++;failures.Add($"{item.Name}: {ex.Message}");}
            finally{progress?.Report(new(i+1,items.Length,item.Name,written));}
        }
        paths.WriteReport(failures);return new(done,skipped,written,output,failed,paths.Renamed);
    }

    private static IArchiveEntry Find(IArchive archive,string name)=>archive.Entries.FirstOrDefault(e=>!e.IsDirectory&&string.Equals(e.Key?.Replace('\\','/'),name,StringComparison.Ordinal))??throw new FileNotFoundException("资源包中找不到条目："+name);
    private static string DecodeKey(string encoded)
    {
        var once=Encoding.UTF8.GetString(Convert.FromBase64String(encoded));var coded=Encoding.UTF8.GetString(Convert.FromBase64String(once));var result=new StringBuilder();
        for(var i=0;i<coded.Length;i++){if(!char.IsDigit(coded[i]))throw new InvalidDataException("packageKey 格式无效。");var length=coded[i]-'0'+1;if(i+length>=coded.Length)throw new InvalidDataException("packageKey 长度无效。");result.Append((char)int.Parse(coded.AsSpan(i+1,length)));i+=length;}
        return result.ToString();
    }
}

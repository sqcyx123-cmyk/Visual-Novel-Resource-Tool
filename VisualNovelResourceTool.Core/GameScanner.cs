namespace VisualNovelResourceTool.Core;

public sealed class GameScanner
{
    public async Task<IReadOnlyList<ScanResult>> ScanAsync(string path, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Scan(path, cancellationToken), cancellationToken);
    }

    public IReadOnlyList<ScanResult> Scan(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path)) return [InspectFile(path, cancellationToken)];
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"找不到路径：{path}");

        var results = new List<ScanResult>();
        foreach (var file in GameFiles.Enumerate(path, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (extension is ".rpa" or ".xp3") results.Add(InspectFile(file));
            else if (extension == ".dat" && IsDpmx(file)) results.Add(InspectFile(file));
            else if (extension == ".pak" && IsRpgMakerPak(file)) results.Add(new(file,Path.GetFileName(file),ResourceKind.RpgMakerPak,SupportLevel.Extractable,"RPG Maker MV 加密 ZIP","已识别 data.pak 和内嵌 packageKey，可浏览、预览并提取图片和视频。",new FileInfo(file).Length));
        }

        DetectDirectoryEngine(path, results, cancellationToken);
        if (!results.Any(r => r.Kind == ResourceKind.RpgMaker)) DetectPackagedExecutable(path, results, cancellationToken);
        if (results.Count == 0)
            results.Add(new(path, Path.GetFileName(path), ResourceKind.Unknown, SupportLevel.Unsupported,
                "未知", "未发现受支持的资源包；可能是自定义封包、资源已嵌入 EXE，或资源已经直接存放在目录中。"));
        return results.OrderBy(r => r.Kind).ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public ScanResult InspectFile(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new FileInfo(path);
        try
        {
            var ext = info.Extension.ToLowerInvariant();
            if (ext == ".rpa")
            {
                var header = ReadAsciiHeader(path, 40);
                if (!header.StartsWith("RPA-", StringComparison.Ordinal))
                    return new(path, info.Name, ResourceKind.RenPyRpa, SupportLevel.Error, "伪装或损坏的 RPA", "扩展名是 .rpa，但文件头不是标准 Ren’Py RPA。", info.Length);
                var version = header.Split(' ', '\n')[0];
                var supported = version is "RPA-2.0" or "RPA-3.0" or "RPA-3.2";
                return new(path, info.Name, ResourceKind.RenPyRpa, supported ? SupportLevel.Extractable : SupportLevel.Unsupported,
                    version, supported ? "标准 Ren’Py 资源包，可浏览并安全提取。" : "识别为 Ren’Py 资源包，但该版本暂不支持提取。", info.Length);
            }
            if (ext == ".xp3")
            {
                using var stream = File.OpenRead(path);
                var signature = new byte[Math.Min(11L, stream.Length)];
                stream.ReadExactly(signature);
                var valid = signature.SequenceEqual(new byte[] { 0x58,0x50,0x33,0x0d,0x0a,0x20,0x0a,0x1a,0x8b,0x67,0x01 });
                return new(path, info.Name, ResourceKind.KirikiriXp3, valid ? SupportLevel.Recognized : SupportLevel.Error,
                    valid ? "KiriKiri XP3" : "伪装或非标准 XP3",
                    valid ? "标准 XP3，可浏览并提取；使用专用加密过滤器的游戏会给出明确错误。" : "扩展名是 .xp3，但文件头不符合标准 XP3。", info.Length);
            }
            if(ext==".exe"&&ContainsAllAscii(path,cancellationToken,"rpg_core.js","RPG Maker"))
                return new(path,info.Name,ResourceKind.EnigmaExecutable,SupportLevel.Recognized,"RPG Maker 单文件 EXE","检测到内嵌 RPG Maker MV 资源，疑似 Enigma Virtual Box 封装；可使用专用解包器提取。",info.Length);
            if (ext == ".dat" && IsDpmx(path))
                return new(path, info.Name, ResourceKind.Dpmx, SupportLevel.Extractable, "DPMX", "已识别 DPMX 资源包，可浏览和提取。", info.Length);
            if (ext == ".pak" && IsRpgMakerPak(path))
                return new(path, info.Name, ResourceKind.RpgMakerPak, SupportLevel.Extractable, "RPG Maker MV 加密 ZIP", "已识别 data.pak 和 packageKey，可浏览和提取。", info.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(path, info.Name, ResourceKind.Unknown, SupportLevel.Error, "读取失败", ex.Message, info.Exists ? info.Length : 0);
        }
        return new(path, info.Name, ResourceKind.Unknown, SupportLevel.Unsupported, "未知", "暂不支持此文件格式。", info.Exists ? info.Length : 0);
    }

    private static void DetectDirectoryEngine(string root, List<ScanResult> results, CancellationToken token)
    {
        var isRpgMaker = false;
        var hasScriptEngine = false;
        var looseCount = 0;
        var looseRoot = Directory.Exists(Path.Combine(root,"game")) ? Path.Combine(root,"game") : root;
        var loosePrefix = Path.GetFullPath(looseRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var file in GameFiles.Enumerate(root, token))
        {
            var name = Path.GetFileName(file).ToLowerInvariant();
            isRpgMaker |= name is "rpg_core.js" or "rmmz_core.js";
            hasScriptEngine |= name is "tyrano.ks" or "config.tjs";
            if (Path.GetFullPath(file).StartsWith(loosePrefix, StringComparison.OrdinalIgnoreCase) && AssetClassifier.Matches(file,AssetCategory.Images)) looseCount++;
        }
        if (isRpgMaker)
            results.Add(new(root, Path.GetFileName(root), ResourceKind.RpgMaker, SupportLevel.Extractable, "RPG Maker MV/MZ",
                "已识别 RPG Maker；可从 System.json 读取项目密钥并还原加密图片和音频。"));
        if (hasScriptEngine)
            results.Add(new(root, Path.GetFileName(root), ResourceKind.TyranoScript, SupportLevel.Recognized, "TyranoScript / KiriKiri",
                "已识别脚本引擎；请检查 data、www 或 resources 目录中的普通资源。"));
        if (!isRpgMaker&&looseCount>0)
            results.Add(new(root, Path.GetFileName(root), ResourceKind.AlreadyUnpacked, SupportLevel.NoExtractionNeeded, "散装资源",
                $"目录中存在 {looseCount:N0} 个可直接查看的图片或视频；无需解包，但可浏览、分类和复制。"));
    }

    private static string ReadAsciiHeader(string path, int length)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        var buffer = new byte[Math.Min(length, (int)Math.Min(stream.Length, int.MaxValue))];
        stream.ReadExactly(buffer);
        return System.Text.Encoding.ASCII.GetString(buffer);
    }
    private static bool IsDpmx(string path) { try { using var f=File.OpenRead(path); Span<byte> b=stackalloc byte[4]; return f.Read(b)==4&&b.SequenceEqual("DPMX"u8); } catch { return false; } }
    private static bool IsRpgMakerPak(string path){try{if(!System.IO.Path.GetFileName(path).Equals("data.pak",StringComparison.OrdinalIgnoreCase))return false;using var f=File.OpenRead(path);Span<byte>b=stackalloc byte[4];if(f.Read(b)!=4||!b.SequenceEqual(new byte[]{0x50,0x4b,0x03,0x04}))return false;var index=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!,"index.html");return File.Exists(index)&&File.ReadAllText(index).Contains("packageKey",StringComparison.OrdinalIgnoreCase);}catch{return false;}}
    private static void DetectPackagedExecutable(string root,List<ScanResult> results,CancellationToken token)
    {
        foreach(var exe in Directory.EnumerateFiles(root,"*.exe",SearchOption.TopDirectoryOnly).OrderByDescending(f=>new FileInfo(f).Length).Take(3))
            if(ContainsAllAscii(exe,token,"rpg_core.js","RPG Maker")){var info=new FileInfo(exe);results.Add(new(exe,info.Name,ResourceKind.EnigmaExecutable,SupportLevel.Recognized,"RPG Maker 单文件 EXE","检测到内嵌 RPG Maker MV 资源，疑似 Enigma Virtual Box 封装；可使用专用解包器提取。",info.Length));break;}
    }
    private static bool ContainsAllAscii(string path,CancellationToken token,params string[] texts)
    {
        var needles=texts.Select(System.Text.Encoding.ASCII.GetBytes).ToArray();var found=new bool[needles.Length];var overlap=needles.Max(n=>n.Length)-1;var buffer=new byte[1024*1024+overlap];var carry=0;using var stream=File.OpenRead(path);
        while(true){token.ThrowIfCancellationRequested();var read=stream.Read(buffer,carry,buffer.Length-carry);if(read==0)return found.All(x=>x);var total=carry+read;for(var i=0;i<needles.Length;i++)if(!found[i]&&buffer.AsSpan(0,total).IndexOf(needles[i])>=0)found[i]=true;if(found.All(x=>x))return true;carry=Math.Min(overlap,total);buffer.AsSpan(total-carry,carry).CopyTo(buffer);}
    }
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using VisualNovelResourceTool.App;
using VisualNovelResourceTool.Core;

internal static class ReviewRegressionTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All =>
    [
        ("散装单张图片可识别且忽略旧输出", ScanLoose),
        ("目录识别不被旧输出中的引擎文件污染", ScanGenerated),
        ("RPG Maker 重扫不包含解包结果", RpgRescan),
        ("同名改名候选再次冲突不会覆盖", PathCollision),
        ("提取报告不覆盖已有文件", ReportPreservation),
        ("临时文件不覆盖用户同名文件", TemporaryFilePreservation),
        ("大 XP3 识别仅分配文件头所需内存", Xp3HeaderAllocation),
        ("DPMX 偏移加法不发生 32 位回绕", DpmxOverflow),
        ("取消扫描单文件有效", CancelScan),
        ("XP3 压缩索引与分段提取内容正确", Xp3RoundTrip),
        ("XP3 拒绝谎报的解压长度", Xp3InvalidLength),
        ("XP3 后续索引按偏移读取且拒绝循环", Xp3Continuation),
        ("输出目录完整名称及路径边界", OutputSettings),
        ("取消外部进程后释放占用文件", CancelProcess),
        ("RPG Maker 缺密钥不阻断普通文件", RpgMissingKey),
        ("输出路径拒绝目录联接越界", JunctionOutput),
        ("PAK 重名条目不阻断其余文件", PakDuplicate),
        ("含零散图片时目录仍识别封装 EXE", PackagedExe),
        ("RPA 多数据块明确拒绝而非静默截断", RpaMultipleBlocks),
        ("DPMX 正常提取及不覆盖已有目标", DpmxRoundTrip),
        ("自定义输出不能覆盖游戏原图片", SourceProtection)
    ];

    private static async Task RunInTemp(Func<string, Task> run)
    {
        var root = Path.Combine(Path.GetTempPath(), "vnrt-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { await run(root); }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Put(string path, string text = "image") { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }

    private static Task ScanLoose() => RunInTemp(root =>
    {
        Put(Path.Combine(root, "game", "single.png"));
        var scan = new GameScanner().Scan(root);
        Check(scan.Any(r => r.Kind == ResourceKind.AlreadyUnpacked), "只有一张图片被漏报");
        return Task.CompletedTask;
    });
    private static Task ScanGenerated() => RunInTemp(root =>
    {
        Put(Path.Combine(root, "解包结果", "old", "rpg_core.js"));
        for (var i = 0; i < 5; i++) Put(Path.Combine(root, "解包结果", "old", $"{i}.png"));
        Check(new GameScanner().Scan(root).All(r => r.Kind == ResourceKind.Unknown), "旧输出改变了游戏识别结果");
        return Task.CompletedTask;
    });
    private static Task RpgRescan() => RunInTemp(root =>
    {
        Put(Path.Combine(root, "img", "scene.png"));
        Put(Path.Combine(root, "解包结果", "old", "scene.png"));
        Check(RpgMakerProject.Open(root).Entries.Count == 1, "RPG Maker 重扫包含上次输出");
        return Task.CompletedTask;
    });
    private static Task PathCollision() => RunInTemp(root =>
    {
        var paths = new SafeExtractionPath(root);
        paths.Resolve("a:b.png");
        const string second = "a?b.png";
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(second)))[..8].ToLowerInvariant();
        var occupied = paths.Resolve($"a＿b~{suffix}.png");
        var resolved = paths.Resolve(second);
        Check(!resolved.Equals(occupied, StringComparison.OrdinalIgnoreCase), "哈希改名覆盖了已分配条目");
        return Task.CompletedTask;
    });
    private static Task ReportPreservation() => RunInTemp(root =>
    {
        var path = Path.Combine(root, "_提取记录.txt");
        Put(path, "original");
        var paths = new SafeExtractionPath(root);
        paths.WriteReport(["synthetic failure"]);
        Check(File.ReadAllText(path) == "original", "报告覆盖了已有文件");
        Check(Directory.GetFiles(root).Length == 2, "未写入独立报告");
        return Task.CompletedTask;
    });
    private static Task TemporaryFilePreservation() => RunInTemp(async root =>
    {
        var source = Path.Combine(root, "source");
        Put(Path.Combine(source, "image.png"));
        var output = Path.Combine(root, "out");
        var existing = Path.Combine(output, "image.png.vnrt-part");
        Put(existing, "user data");
        var project = LooseResourceProject.Open(source);
        var result = await project.ExtractAsync(project.Entries, output, true);
        Check(result.Extracted == 1, "提取失败");
        Check(File.Exists(existing) && File.ReadAllText(existing) == "user data", "固定临时文件覆盖或删除已有文件");
    });
    private static Task Xp3HeaderAllocation() => RunInTemp(root =>
    {
        var path = Path.Combine(root, "large.xp3");
        using (var stream = File.Create(path))
        {
            stream.Write(new byte[] { 0x58,0x50,0x33,0x0d,0x0a,0x20,0x0a,0x1a,0x8b,0x67,0x01 });
            stream.SetLength(32 * 1024 * 1024);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        var result = new GameScanner().InspectFile(path);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"XP3 32 MiB header scan: allocated={allocated:N0} bytes, elapsed={timer.Elapsed.TotalMilliseconds:F2} ms");
        Check(result.Support == SupportLevel.Recognized, "文件头识别失败");
        Check(allocated < 1024 * 1024, "识别文件头却读取了完整 XP3");
        return Task.CompletedTask;
    });
    private static Task DpmxOverflow() => RunInTemp(root =>
    {
        var path = Path.Combine(root, "bad.dat");
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write("DPMX"u8); writer.Write(48u); writer.Write(1u); writer.Write(0u);
            writer.Write(new byte[24]); writer.Write(uint.MaxValue - 47); writer.Write(1u);
        }
        try { DpmxArchive.Open(path); }
        catch (InvalidDataException) { return Task.CompletedTask; }
        throw new Exception("越界数据偏移回绕为文件头，被错误接受");
    });
    private static Task CancelScan() => RunInTemp(root =>
    {
        var path = Path.Combine(root, "x.rpa"); Put(path, "RPA-3.0");
        try { new GameScanner().Scan(path, new CancellationToken(true)); }
        catch (OperationCanceledException) { return Task.CompletedTask; }
        throw new Exception("已取消的单文件扫描仍然成功返回");
    });

    private static byte[] Compressed(byte[] bytes)
    {
        using var buffer = new MemoryStream();
        using (var z = new ZLibStream(buffer, CompressionLevel.Optimal, true)) z.Write(bytes);
        return buffer.ToArray();
    }
    private static byte[] Chunk(string name, byte[] data)
    {
        using var buffer = new MemoryStream(); using var writer = new BinaryWriter(buffer);
        writer.Write(Encoding.ASCII.GetBytes(name)); writer.Write((ulong)data.Length); writer.Write(data); return buffer.ToArray();
    }
    private static string CreateXp3(string root, bool wrongIndex = false, bool wrongSegment = false)
    {
        var path = Path.Combine(root,"sample.xp3");
        var packed = Compressed("def"u8.ToArray());
        using var info = new MemoryStream();
        using (var writer = new BinaryWriter(info,Encoding.UTF8,true))
        {
            writer.Write(0u);writer.Write(6ul);writer.Write((ulong)(3+packed.Length));writer.Write((ushort)9);writer.Write(Encoding.Unicode.GetBytes("image.png"));
        }
        using var segments = new MemoryStream();
        using (var writer = new BinaryWriter(segments,Encoding.UTF8,true))
        {
            writer.Write(0u);writer.Write(19ul);writer.Write(3ul);writer.Write(3ul);
            writer.Write(1u);writer.Write(22ul);writer.Write(3ul);writer.Write((ulong)packed.Length);
        }
        var index = Chunk("File",Chunk("info",info.ToArray()).Concat(Chunk("segm",segments.ToArray())).ToArray());
        if (wrongSegment) packed = Compressed("def-more"u8.ToArray());
        // Rebuild the stored-size field when intentionally inflating the segment.
        if (wrongSegment)
        {
            segments.Position = 48;
            using(var w = new BinaryWriter(segments,Encoding.UTF8,true)) w.Write((ulong)packed.Length);
            index = Chunk("File",Chunk("info",info.ToArray()).Concat(Chunk("segm",segments.ToArray())).ToArray());
        }
        var compressedIndex = Compressed(index);
        using var output = new BinaryWriter(File.Create(path));
        output.Write(new byte[]{0x58,0x50,0x33,0x0d,0x0a,0x20,0x0a,0x1a,0x8b,0x67,0x01});
        output.Write((ulong)(22+packed.Length));output.Write("abc"u8);output.Write(packed);
        output.Write((byte)1);output.Write((ulong)compressedIndex.Length);output.Write((ulong)(wrongIndex?1:index.Length));output.Write(compressedIndex);
        return path;
    }
    private static Task Xp3RoundTrip() => RunInTemp(async root =>
    {
        var archive = Xp3Archive.Open(CreateXp3(root));
        Check((await archive.ReadEntryAsync(archive.Entries.Single())).SequenceEqual("abcdef"u8.ToArray()),"XP3 分段预览内容错误");
        var output = Path.Combine(root,"out");
        var result = await archive.ExtractAsync(archive.Entries,output,false);
        Check(result.Extracted==1 && File.ReadAllText(Path.Combine(output,"image.png"))=="abcdef","XP3 提取错误");
        Check((await archive.ExtractAsync(archive.Entries,output,false)).Skipped==1,"覆盖关闭时未跳过");
    });
    private static Task Xp3InvalidLength() => RunInTemp(async root =>
    {
        try { Xp3Archive.Open(CreateXp3(root,wrongIndex:true)); throw new Exception("谎报索引长度被接受"); }
        catch (InvalidDataException) { }
        var archive = Xp3Archive.Open(CreateXp3(root,wrongSegment:true));
        var output = Path.Combine(root,"out");Put(Path.Combine(output,"image.png"),"preserve");
        var result = await archive.ExtractAsync(archive.Entries,output,true);
        Check(result.Failed==1 && result.Extracted==0,"解压超长未报失败");
        Check(File.ReadAllText(Path.Combine(output,"image.png"))=="preserve","失败破坏了原目标");
        Check(!Directory.GetFiles(output,"*.part").Any(),"失败留下临时文件");
    });
    private static Task Xp3Continuation() => RunInTemp(root =>
    {
        var path = Path.Combine(root,"chain.xp3");
        using(var w = new BinaryWriter(File.Create(path)))
        {
            w.Write(new byte[]{0x58,0x50,0x33,0x0d,0x0a,0x20,0x0a,0x1a,0x8b,0x67,0x01});w.Write(19ul);
            w.Write((byte)0x80);w.Write(0ul);w.Write(48ul);w.Write(new byte[12]);w.Write((byte)0);w.Write(0ul);
        }
        Check(Xp3Archive.Open(path).Entries.Count==0,"后续索引读取错误");
        using(var f=File.OpenWrite(path)){f.Position=28;using var w=new BinaryWriter(f);w.Write(19ul);}
        try { Xp3Archive.Open(path); } catch(InvalidDataException) { return Task.CompletedTask; }
        throw new Exception("循环索引未被拒绝");
    });
    private static Task OutputSettings() => RunInTemp(root =>
    {
        var game=Path.Combine(root,"Game1.2");Directory.CreateDirectory(game);
        var settings=new AppSettings();
        Check(settings.GetOutput(game,game)==Path.Combine(game,"解包结果","Game1.2"),"目录名称被当作扩展名截断");
        var other=Path.Combine(root,"Game1.20");Directory.CreateDirectory(other);
        Check(settings.GetOutput(Path.Combine(other,"images.rpa"),game)==Path.Combine(other,"解包结果","images"),"路径前缀被误当成父目录");
        Check(new RpgMakerEntry("x","img/a.png",1,false).Name=="img/a.png","RPG Maker 列表显示字段缺失");
        return Task.CompletedTask;
    });
    private static Task CancelProcess() => RunInTemp(async root =>
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var start = new ProcessStartInfo(Environment.ProcessPath!);
        if(string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),"dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(typeof(ReviewRegressionTests).Assembly.Location);
        start.ArgumentList.Add("--process-probe");start.ArgumentList.Add(root);
        var running=ExternalProcess.RunAsync(start,cancellation.Token);
        var ready=Path.Combine(root,"ready.txt");
        while(!File.Exists(ready)&&!running.IsCompleted)await Task.Delay(20);
        Check(File.Exists(ready),"进程未就绪");
        cancellation.Cancel();
        try { await running;throw new Exception("取消未返回取消异常"); } catch(OperationCanceledException) { }
        using var held=File.Open(Path.Combine(root,"held.bin"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        Console.WriteLine("Process cancellation: child exited and file lock released");
    });
    private static Task RpgMissingKey() => RunInTemp(async root =>
    {
        Put(Path.Combine(root,"img","good.png"));Put(Path.Combine(root,"img","encrypted.rpgmvp"));
        var project=RpgMakerProject.Open(root);
        var result=await project.ExtractAsync(project.Entries,Path.Combine(root,"out"),false);
        Check(result.Extracted==1&&result.Failed==1,"缺少密钥阻断了普通文件的提取");
    });
    private static Task JunctionOutput() => RunInTemp(async root =>
    {
        var output=Path.Combine(root,"out");var outside=Path.Combine(root,"outside");
        Directory.CreateDirectory(output);Directory.CreateDirectory(outside);
        var link=Path.Combine(output,"linked");
        var start=new ProcessStartInfo("cmd.exe");start.ArgumentList.Add("/c");start.ArgumentList.Add("mklink");start.ArgumentList.Add("/J");start.ArgumentList.Add(link);start.ArgumentList.Add(outside);
        var result=await ExternalProcess.RunAsync(start,CancellationToken.None);
        Check(result.ExitCode==0,"无法建立测试联接："+result.Error);
        try
        {
            try { new SafeExtractionPath(output).Resolve("linked/escape.png"); }
            catch(InvalidDataException) { return; }
            throw new Exception("输出沿目录联接越界");
        }
        finally { Directory.Delete(link); }
    });
    private static Task PakDuplicate() => RunInTemp(async root =>
    {
        var path=Path.Combine(root,"data.pak");
        var password=Convert.ToBase64String(Encoding.UTF8.GetBytes(Convert.ToBase64String("2120"u8.ToArray())));
        Put(Path.Combine(root,"index.html"),$"packageKey='{password}'");
        using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))
            foreach(var name in new[]{"same.png","same.png","good.png"})
            { using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write("image"); }
        Check(new GameScanner().InspectFile(path).Kind==ResourceKind.RpgMakerPak,"单独打开 PAK 未识别");
        var archive=RpgMakerPakArchive.Open(path);
        var result=await archive.ExtractAsync(archive.Entries,Path.Combine(root,"out"),false);
        Check(result.Extracted==1&&result.Failed==2,"重复名称中断整个 PAK 提取");
    });
    private static Task PackagedExe() => RunInTemp(root =>
    {
        var exe=Path.Combine(root,"game.exe");Put(exe,"synthetic rpg_core.js RPG Maker");Put(Path.Combine(root,"icon.png"));
        var scanner=new GameScanner();
        Check(scanner.InspectFile(exe).Kind==ResourceKind.EnigmaExecutable,"单文件未识别");
        Check(scanner.Scan(root).Any(r=>r.Kind==ResourceKind.EnigmaExecutable),"零散图片掩盖封装 EXE");
        return Task.CompletedTask;
    });
    private static Task RpaMultipleBlocks() => RunInTemp(root =>
    {
        var path=Path.Combine(root,"multi.rpa");
        using(var file=File.Create(path))
        {
            file.Write(Encoding.ASCII.GetBytes("RPA-2.0 000000000000001c\nabc"));
            using var z=new ZLibStream(file,CompressionLevel.Optimal);
            // {'x.png': [(24,1),(25,2)]}; protocol 2, all values inert.
            z.Write(new byte[]{0x80,2,(byte)'}',(byte)'U',5,(byte)'x',(byte)'.',(byte)'p',(byte)'n',(byte)'g',(byte)']',(byte)'K',24,(byte)'K',1,0x86,(byte)'a',(byte)'K',25,(byte)'K',2,0x86,(byte)'a',(byte)'s',(byte)'.'});
        }
        try { RpaArchive.Open(path); } catch(InvalidDataException ex) when(ex.Message.Contains("多个 RPA 数据块")) { return Task.CompletedTask; }
        throw new Exception("多数据块被静默截断");
    });
    private static Task DpmxRoundTrip() => RunInTemp(async root =>
    {
        var path=Path.Combine(root,"good.dat");
        using(var writer=new BinaryWriter(File.Create(path)))
        {
            writer.Write("DPMX"u8);writer.Write(48u);writer.Write(1u);writer.Write(0u);
            var name=new byte[24];Encoding.ASCII.GetBytes("image.png").CopyTo(name,0);
            writer.Write(name);writer.Write(0u);writer.Write(3u);writer.Write("abc"u8);
        }
        var archive=DpmxArchive.Open(path);var output=Path.Combine(root,"out");
        var result=await archive.ExtractAsync(archive.Entries,output,false);
        Check(result.Extracted==1&&File.ReadAllText(Path.Combine(output,"image.png"))=="abc","DPMX 内容不正确");
        Check((await archive.ExtractAsync(archive.Entries,output,false)).Skipped==1,"DPMX 不覆盖选项失效");
    });
    private static Task SourceProtection() => RunInTemp(async root =>
    {
        Put(Path.Combine(root,"image.png"),"source");
        var rpg=RpgMakerProject.Open(root);
        var result=await rpg.ExtractAsync(rpg.Entries,root,true);
        Check(result.Extracted==0&&result.Failed==1,"RPG 输出覆盖游戏原文件");
        var loose=LooseResourceProject.Open(root);
        result=await loose.ExtractAsync(loose.Entries,root,true);
        Check(result.Extracted==0&&result.Failed==1,"散装输出覆盖游戏原文件");
        Check(File.ReadAllText(Path.Combine(root,"image.png"))=="source","原文件内容变化");
    });
}

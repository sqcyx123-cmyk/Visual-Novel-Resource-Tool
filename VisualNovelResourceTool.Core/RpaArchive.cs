using System.IO.Compression;
using System.Text;

namespace VisualNovelResourceTool.Core;

public sealed class RpaArchive
{
    public string Path { get; }
    public string Version { get; }
    public IReadOnlyList<RpaEntry> Entries { get; }

    private RpaArchive(string path, string version, IReadOnlyList<RpaEntry> entries) => (Path, Version, Entries) = (path, version, entries);

    public static async Task<RpaArchive> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try{return await Task.Run(()=>Open(path,timeout.Token),timeout.Token);}
        catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested){throw new InvalidDataException("RPA 索引读取超过 30 秒，已自动停止；该资源包可能损坏、加密或包含异常索引。");}
    }

    public static RpaArchive Open(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        var header = ReadLine(file);
        var parts = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[0] is not ("RPA-2.0" or "RPA-3.0" or "RPA-3.2")) throw new InvalidDataException("不是受支持的 RPA 2.0/3.0/3.2 文件。");
        var version = parts[0];
        var offset = Convert.ToInt64(parts[1], 16);
        long key = 0;
        if (version == "RPA-3.0") for (var i = 2; i < parts.Length; i++) key ^= Convert.ToInt64(parts[i], 16);
        else if (version == "RPA-3.2") for (var i = 3; i < parts.Length; i++) key ^= Convert.ToInt64(parts[i], 16);
        if (offset < 0 || offset >= file.Length) throw new InvalidDataException("RPA 索引偏移无效。");
        file.Position = offset;
        using var zlib = new ZLibStream(file, CompressionMode.Decompress, leaveOpen: true);
        var root = new SafePickleReader(zlib,cancellationToken).Read() as Dictionary<object, object?> ?? throw new InvalidDataException("RPA 索引根节点不是字典。");
        var entries = new List<RpaEntry>(root.Count);
        foreach (var pair in root)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = pair.Key switch { string s => s, byte[] b => Encoding.UTF8.GetString(b), _ => throw new InvalidDataException("RPA 文件名类型不受支持。") };
            var blocks = pair.Value as List<object?> ?? throw new InvalidDataException($"{name} 的索引结构无效。");
            if (blocks.Count == 0) continue;
            if (blocks.Count != 1) throw new InvalidDataException($"{name} 使用多个 RPA 数据块，暂不支持；已停止读取以避免静默丢失内容。");
            var tuple = blocks[0] as object?[] ?? throw new InvalidDataException($"{name} 的数据块结构无效。");
            if (tuple.Length is not (2 or 3)) throw new InvalidDataException($"{name} 的数据块字段数量无效。");
            var entryOffset = Convert.ToInt64(tuple[0]); var length = Convert.ToInt64(tuple[1]);
            if (version is "RPA-3.0" or "RPA-3.2") { entryOffset ^= key; length ^= key; }
            var prefix = tuple.Length == 3 ? tuple[2] as byte[] ?? [] : [];
            if (entryOffset < 0 || length < prefix.Length || entryOffset > file.Length || length > file.Length - entryOffset + prefix.Length)
                throw new InvalidDataException($"{name} 的偏移或长度越界。");
            entries.Add(new(name.Replace('\\', '/'), entryOffset, length, prefix));
        }
        return new(path, version, entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<ExtractionSummary> ExtractAllAsync(string outputDirectory, bool overwrite, IProgress<ExtractionProgress>? progress = null, CancellationToken cancellationToken = default)
        => await ExtractAsync(Entries, outputDirectory, overwrite, progress, cancellationToken);

    public async Task<byte[]> ReadEntryAsync(RpaEntry entry, int maximumBytes = 64 * 1024 * 1024, CancellationToken cancellationToken = default)
    {
        if (entry.Length > maximumBytes) throw new InvalidDataException("文件过大，已停止生成预览。");
        var result = new byte[checked((int)entry.Length)]; entry.Prefix.CopyTo(result, 0);
        await using var source = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess); source.Position = entry.Offset;
        await source.ReadExactlyAsync(result.AsMemory(entry.Prefix.Length), cancellationToken); return result;
    }

    public async Task<ExtractionSummary> ExtractAsync(IEnumerable<RpaEntry> selectedEntries, string outputDirectory, bool overwrite, IProgress<ExtractionProgress>? progress = null, CancellationToken cancellationToken = default, bool addChineseDirectoryLabels = false)
    {
        var items = selectedEntries.ToArray();
        var paths = new SafeExtractionPath(outputDirectory, addChineseDirectoryLabels);
        var extracted = 0; var skipped = 0; var failed = 0; long written = 0; var failures = new List<string>();
        await using var source = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        var buffer = new byte[1024 * 1024];
        for (var index = 0; index < items.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = items[index];
            string target;
            try { target = paths.Resolve(entry.Name); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!); }
            catch (Exception ex) { failed++; failures.Add($"{entry.Name}: {ex.Message}"); progress?.Report(new(index + 1, items.Length, entry.Name, written)); continue; }
            if (!overwrite && File.Exists(target)) { skipped++; progress?.Report(new(index + 1, items.Length, entry.Name, written)); continue; }
            var temp = SafeExtractionPath.TemporaryPath(target);
            try
            {
                await using var destination = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (entry.Prefix.Length > 0) await destination.WriteAsync(entry.Prefix, cancellationToken);
                source.Position = entry.Offset;
                var remaining = entry.Length - entry.Prefix.Length;
                while (remaining > 0)
                {
                    var count = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
                    if (count == 0) throw new EndOfStreamException($"提取 {entry.Name} 时档案意外结束。");
                    await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken); remaining -= count; written += count;
                }
                await destination.FlushAsync(cancellationToken);
                await destination.DisposeAsync();
                File.Move(temp, target, overwrite); extracted++;
            }
            catch (OperationCanceledException) { if (File.Exists(temp)) File.Delete(temp); throw; }
            catch (Exception ex) { if (File.Exists(temp)) File.Delete(temp); failed++; failures.Add($"{entry.Name}: {ex.Message}"); }
            progress?.Report(new(index + 1, items.Length, entry.Name, written));
        }
        paths.WriteReport(failures); return new(extracted, skipped, written, outputDirectory, failed, paths.Renamed);
    }

    private static string ReadLine(Stream stream)
    {
        var bytes = new List<byte>(64); int value;
        while ((value = stream.ReadByte()) >= 0 && value != '\n') { if (bytes.Count >= 4096) throw new InvalidDataException("RPA 文件头过长。"); bytes.Add((byte)value); }
        return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
    }
}

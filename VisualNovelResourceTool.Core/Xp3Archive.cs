using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace VisualNovelResourceTool.Core;

public sealed class Xp3Archive
{
    private static readonly byte[] Signature = [0x58,0x50,0x33,0x0d,0x0a,0x20,0x0a,0x1a,0x8b,0x67,0x01];
    public string Path { get; }
    public IReadOnlyList<Xp3Entry> Entries { get; }
    private Xp3Archive(string path, IReadOnlyList<Xp3Entry> entries) => (Path, Entries) = (path, entries);

    public static Xp3Archive Open(string path)
    {
        using var file = File.OpenRead(path); var signature = Read(file, Signature.Length);
        if (!signature.SequenceEqual(Signature)) throw new InvalidDataException("不是标准 KiriKiri XP3 文件。");
        var indexOffset = checked((long)ReadUInt64(file));
        if (indexOffset < Signature.Length + 8 || indexOffset >= file.Length) throw new InvalidDataException("XP3 索引偏移无效。");
        file.Position = indexOffset; using var index = new MemoryStream();
        bool more;
        do
        {
            var flag = ReadByte(file); more = (flag & 0x80) != 0; var compressed = (flag & 7) == 1;
            var stored = checked((long)ReadUInt64(file)); var original = compressed ? checked((long)ReadUInt64(file)) : stored;
            if (stored < 0 || original < 0 || stored > file.Length - file.Position || original > 512L * 1024 * 1024) throw new InvalidDataException("XP3 索引大小异常。");
            using var slice = new LimitedStream(file, stored); if (compressed) { using var z = new ZLibStream(slice, CompressionMode.Decompress); z.CopyTo(index); } else slice.CopyTo(index);
        } while (more);
        index.Position = 0; var entries = new List<Xp3Entry>();
        while (index.Position < index.Length)
        {
            var tag = Encoding.ASCII.GetString(Read(index, 4)); var size = checked((long)ReadUInt64(index)); var end = checked(index.Position + size);
            if (tag != "File" || end > index.Length) throw new InvalidDataException("XP3 File 索引块无效。");
            string? name = null; long length = 0; var segments = new List<Xp3Segment>();
            while (index.Position < end)
            {
                var subTag = Encoding.ASCII.GetString(Read(index, 4)); var subSize = checked((long)ReadUInt64(index)); var subEnd = checked(index.Position + subSize); if (subEnd > end) throw new InvalidDataException("XP3 子索引块越界。");
                if (subTag == "info") { ReadUInt32(index); length = checked((long)ReadUInt64(index)); ReadUInt64(index); var chars = ReadUInt16(index); name = Encoding.Unicode.GetString(Read(index, chars * 2)); }
                else if (subTag == "segm") while (index.Position + 28 <= subEnd) { var flags = ReadUInt32(index); var offset = checked((long)ReadUInt64(index)); var original = checked((long)ReadUInt64(index)); var stored = checked((long)ReadUInt64(index)); if (offset < 0 || stored < 0 || offset > file.Length || stored > file.Length - offset) throw new InvalidDataException("XP3 数据段越界。"); segments.Add(new((flags & 7) == 1, offset, original, stored)); }
                index.Position = subEnd;
            }
            index.Position = end; if (!string.IsNullOrWhiteSpace(name) && segments.Count > 0) entries.Add(new(name.Replace('\\','/'), length, segments));
        }
        return new(path, entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<ExtractionSummary> ExtractAsync(IEnumerable<Xp3Entry> selectedEntries, string outputDirectory, bool overwrite, IProgress<ExtractionProgress>? progress = null, CancellationToken cancellationToken = default, bool addChineseDirectoryLabels = false)
    {
        var items = selectedEntries.ToArray(); var paths = new SafeExtractionPath(outputDirectory, addChineseDirectoryLabels);
        var extracted = 0; var skipped = 0; var failed = 0; long written = 0; var failures = new List<string>(); await using var source = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        for (var i = 0; i < items.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested(); var entry = items[i]; string target;
            try { target = paths.Resolve(entry.Name); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!); }
            catch (Exception ex) { failed++; failures.Add($"{entry.Name}: {ex.Message}"); progress?.Report(new(i + 1, items.Length, entry.Name, written)); continue; }
            if (!overwrite && File.Exists(target)) { skipped++; progress?.Report(new(i + 1, items.Length, entry.Name, written)); continue; }
            var temp = target + ".vnrt-part";
            try { await using var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous); await CopyEntryAsync(source, entry, output, cancellationToken); await output.FlushAsync(cancellationToken); await output.DisposeAsync(); File.Move(temp, target, true); written += entry.Length; extracted++; }
            catch (OperationCanceledException) { if (File.Exists(temp)) File.Delete(temp); throw; }
            catch (Exception ex) { if (File.Exists(temp)) File.Delete(temp); failed++; failures.Add($"{entry.Name}: {ex.Message}"); } progress?.Report(new(i + 1, items.Length, entry.Name, written));
        }
        paths.WriteReport(failures); return new(extracted, skipped, written, outputDirectory, failed, paths.Renamed);
    }

    public async Task<byte[]> ReadEntryAsync(Xp3Entry entry, int maximumBytes = 64 * 1024 * 1024, CancellationToken cancellationToken = default)
    {
        if (entry.Length > maximumBytes) throw new InvalidDataException("文件过大，已停止生成预览。");
        await using var source = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        using var output = new MemoryStream(checked((int)entry.Length)); await CopyEntryAsync(source, entry, output, cancellationToken); return output.ToArray();
    }

    private static async Task CopyEntryAsync(Stream source, Xp3Entry entry, Stream output, CancellationToken token)
    {
        foreach (var segment in entry.Segments) { source.Position = segment.Offset; using var limited = new LimitedStream(source, segment.StoredSize); if (segment.Compressed) { using var z = new ZLibStream(limited, CompressionMode.Decompress, true); await z.CopyToAsync(output, token); } else await limited.CopyToAsync(output, token); }
        if (output.CanSeek && output.Position != entry.Length) throw new InvalidDataException($"{entry.Name} 解压后的大小与索引不一致。");
    }

    private static byte ReadByte(Stream s) { var value = s.ReadByte(); return value < 0 ? throw new EndOfStreamException() : (byte)value; }
    private static byte[] Read(Stream s, int n) { var b = new byte[n]; s.ReadExactly(b); return b; }
    private static ushort ReadUInt16(Stream s) => BinaryPrimitives.ReadUInt16LittleEndian(Read(s, 2));
    private static uint ReadUInt32(Stream s) => BinaryPrimitives.ReadUInt32LittleEndian(Read(s, 4));
    private static ulong ReadUInt64(Stream s) => BinaryPrimitives.ReadUInt64LittleEndian(Read(s, 8));

    private sealed class LimitedStream : Stream
    {
        private readonly Stream source; private readonly long length; private long _remaining; public LimitedStream(Stream source,long remaining){this.source=source;length=remaining;_remaining=remaining;} public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false; public override long Length => length; public override long Position { get => length - _remaining; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { if (_remaining <= 0) return 0; var read = source.Read(buffer, offset, (int)Math.Min(count, _remaining)); _remaining -= read; return read; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { if (_remaining <= 0) return 0; var read = await source.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _remaining)], cancellationToken); _remaining -= read; return read; }
        public override void Flush() { } public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
}

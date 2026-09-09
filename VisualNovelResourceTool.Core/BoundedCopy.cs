namespace VisualNovelResourceTool.Core;

internal static class BoundedCopy
{
    // Check the actual decoded length while reading, before an invalid compressed
    // stream can grow a preview/index buffer or fill the output disk.
    public static void Copy(Stream source, Stream target, long length, CancellationToken token)
    {
        var buffer = new byte[64 * 1024];
        while (length > 0)
        {
            token.ThrowIfCancellationRequested();
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, length));
            if (read == 0) throw new InvalidDataException("解压后的大小小于索引声明。");
            target.Write(buffer, 0, read);
            length -= read;
        }
        token.ThrowIfCancellationRequested();
        if (source.ReadByte() != -1) throw new InvalidDataException("解压后的大小超过索引声明。");
    }

    public static async Task CopyAsync(Stream source, Stream target, long length, CancellationToken token)
    {
        var buffer = new byte[64 * 1024];
        while (length > 0)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length)), token);
            if (read == 0) throw new InvalidDataException("解压后的大小小于索引声明。");
            await target.WriteAsync(buffer.AsMemory(0, read), token);
            length -= read;
        }
        if (await source.ReadAsync(buffer.AsMemory(0, 1), token) != 0) throw new InvalidDataException("解压后的大小超过索引声明。");
    }
}

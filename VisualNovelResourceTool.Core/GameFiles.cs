namespace VisualNovelResourceTool.Core;

// One traversal policy for scanning and loose/RPG Maker indexing. Never follow
// junctions back into a game or re-import this application's generated output.
public static class GameFiles
{
    public static IEnumerable<string> Enumerate(string root, CancellationToken token = default)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            foreach (var file in Directory.EnumerateFiles(directory, "*", options))
            {
                token.ThrowIfCancellationRequested();
                yield return file;
            }
            foreach (var child in Directory.EnumerateDirectories(directory, "*", options))
            {
                token.ThrowIfCancellationRequested();
                if (!Path.GetFileName(child).Equals("解包结果", StringComparison.OrdinalIgnoreCase)) pending.Push(child);
            }
        }
    }
}

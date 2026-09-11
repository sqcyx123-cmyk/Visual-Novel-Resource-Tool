using System.Diagnostics;
using System.IO;
using VisualNovelResourceTool.Core;

namespace VisualNovelResourceTool.App;

public static class UnrpycService
{
    public static async Task<(int Files, string Log)> DecompileAsync(string source, string output, CancellationToken token)
    {
        var sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        var outputRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        if (sourceRoot.Equals(outputRoot, StringComparison.OrdinalIgnoreCase) || sourceRoot.StartsWith(outputRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("反编译输出必须独立于原脚本目录。");
        var python = FindPython(source) ?? throw new InvalidOperationException("没有找到可用的 Python 3；请选择 Ren’Py 游戏根目录、game 目录或其解包结果。");
        var script = Path.Combine(AppContext.BaseDirectory, "third_party", "unrpyc", "unrpyc.py");
        if (!File.Exists(script)) throw new FileNotFoundException("发布包中缺少 unrpyc。", script);
        var files = await Task.Run(() => GameFiles.Enumerate(source, token)
            .Where(f => Path.GetExtension(f).Equals(".rpyc", StringComparison.OrdinalIgnoreCase) && !Path.GetFullPath(f).StartsWith(outputRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToArray(), token);
        if (files.Length == 0) throw new InvalidOperationException("所选目录中没有 .rpyc 文件。");
        var paths = new SafeExtractionPath(output);
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var target = paths.Resolve(Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temp = SafeExtractionPath.TemporaryPath(target);
            try
            {
                await using (var input = File.OpenRead(file))
                await using (var destination = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
                    await input.CopyToAsync(destination, token);
                File.Move(temp, target, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        var start = new ProcessStartInfo(python) { WorkingDirectory = Path.GetDirectoryName(script)! };
        start.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        start.ArgumentList.Add(script); start.ArgumentList.Add(output); start.ArgumentList.Add("--try-harder");
        var result = await ExternalProcess.RunAsync(start, token);
        var log = result.Output + result.Error;
        if (result.ExitCode != 0) throw new InvalidOperationException($"unrpyc 返回错误 {result.ExitCode}：\n{log}");
        paths.WriteReport([]);
        return (files.Length, log);
    }

    private static string? FindPython(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
        {
            var lib = Path.Combine(current.FullName, "lib");
            if (Directory.Exists(lib))
            {
                var hit = Directory.EnumerateFiles(lib, "python.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (hit is not null) return hit;
            }
        }
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(folder.Trim(), "python.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}

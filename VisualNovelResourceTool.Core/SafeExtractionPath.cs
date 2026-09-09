using System.Security.Cryptography;
using System.Text;

namespace VisualNovelResourceTool.Core;

public sealed class SafeExtractionPath
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase) { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
    private readonly string _root;
    private readonly Dictionary<string, string> _claimed = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _renames = [];
    public int Renamed => _renames.Count;

    private readonly bool _addChineseLabels;
    public SafeExtractionPath(string root, bool addChineseLabels = false)
    {
        RejectReparsePoints(Path.GetFullPath(root));
        Directory.CreateDirectory(root);
        _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        _addChineseLabels = addChineseLabels;
    }

    public string Resolve(string archiveName)
    {
        var original = archiveName.Replace('\\', '/');
        var sourceParts = original.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (sourceParts.Any(p => p == "..")) throw new InvalidDataException($"已阻止越界路径：{archiveName}");
        var parts = sourceParts.Select(Sanitize).ToArray();
        if (_addChineseLabels) for(var i=0;i<parts.Length-1;i++)parts[i]=AddChineseLabel(parts[i]);
        if (parts.Length == 0) parts = ["未命名文件"];
        var relative = Path.Combine(parts);
        var target = Path.GetFullPath(Path.Combine(_root, relative));
        if (!target.StartsWith(_root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"已阻止越界路径：{archiveName}");
        if (_claimed.TryGetValue(target, out var prior) && !prior.Equals(original, StringComparison.Ordinal))
        {
            var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original)))[..8].ToLowerInvariant();
            var directory = Path.GetDirectoryName(target)!; var extension = Path.GetExtension(target); var stem = Path.GetFileNameWithoutExtension(target);
            target = Path.Combine(directory, $"{stem}~{suffix}{extension}");
            var number = 2;
            while (_claimed.TryGetValue(target, out prior) && !prior.Equals(original, StringComparison.Ordinal))
                target = Path.Combine(directory, $"{stem}~{suffix}-{number++}{extension}");
        }
        RejectReparsePoints(target);
        _claimed[target] = original;
        var finalRelative = Path.GetRelativePath(_root, target); var expectedParts=sourceParts.ToArray();
        if(_addChineseLabels)for(var i=0;i<expectedParts.Length-1;i++)expectedParts[i]=AddChineseLabel(expectedParts[i]);
        var expected=string.Join('/',expectedParts);
        if (!finalRelative.Replace('\\', '/').Equals(expected, StringComparison.Ordinal)) _renames.Add($"{original} => {finalRelative.Replace('\\', '/')}");
        return target;
    }

    public void WriteReport(IEnumerable<string> failures)
    {
        var errors = failures.ToArray();
        if (_renames.Count == 0 && errors.Length == 0) return;
        var lines = new List<string> { "视觉小说资源工具提取记录", "" };
        if (_renames.Count > 0) { lines.Add("[为适配 Windows 而重命名]"); lines.AddRange(_renames); lines.Add(""); }
        if (errors.Length > 0) { lines.Add("[提取失败但未中断其他文件]"); lines.AddRange(errors); }
        // A game may itself contain a file with the report's name. Never replace it,
        // nor replace a previous report, even when resource overwrite is enabled.
        for (var number = 1; ; number++)
        {
            var report = Path.Combine(_root, number == 1 ? "_提取记录.txt" : $"_提取记录-{number}.txt");
            RejectReparsePoints(report);
            if (File.Exists(report) || Directory.Exists(report)) continue;
            FileStream stream;
            try { stream = new FileStream(report, FileMode.CreateNew, FileAccess.Write); }
            catch (IOException) when (File.Exists(report)) { continue; }
            using (stream)
            using (var writer = new StreamWriter(stream, new UTF8Encoding(true)))
                foreach (var line in lines) writer.WriteLine(line);
            return;
        }
    }

    public static string TemporaryPath(string target) => Path.Combine(Path.GetDirectoryName(target)!, $".vnrt-{Guid.NewGuid():N}.part");

    private static void RejectReparsePoints(string target)
    {
        for (var current = target; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"输出路径包含符号链接或目录联接，已停止写入：{current}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => c < 32 || invalid.Contains(c) ? '＿' : c).ToArray();
        var result = new string(chars).TrimEnd(' ', '.');
        if (result.Length == 0) result = "未命名";
        if (Reserved.Contains(result.Split('.')[0])) result = "_" + result;
        if (result.Length > 180)
        {
            var ext = Path.GetExtension(result); if (ext.Length > 32) ext = ext[..32]; var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8].ToLowerInvariant();
            result = result[..Math.Max(1, 170 - ext.Length)] + "~" + hash + ext;
        }
        return result;
    }

    private static string AddChineseLabel(string name) => name.ToLowerInvariant() switch
    {
        "bg" or "bgs" or "bgimage" => name + "-背景",
        "cg" or "cgs" or "event" or "gallery" => name + "-事件图",
        "sprites" or "sprite" or "character" or "characters" or "chara" or "fgimage" or "faces" or "enemies" or "sv_actors" or "sv_enemies" => name + "-角色立绘",
        "ui" or "gui" => name + "-界面",
        "fx" or "effect" or "effects" => name + "-特效",
        "video" or "movie" or "movies" => name + "-动画视频",
        "pictures" => name + "-事件图",
        "battlebacks1" or "battlebacks2" or "parallaxes" or "tilesets" => name + "-背景",
        "image" or "images" or "img" or "art" => name + "-图片",
        _ => name
    };
}

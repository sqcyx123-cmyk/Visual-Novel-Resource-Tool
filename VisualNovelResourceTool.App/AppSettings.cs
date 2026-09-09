using System.Text.Json;
using System.IO;

namespace VisualNovelResourceTool.App;

public sealed class AppSettings
{
    public bool UseCustomOutputRoot { get; set; }
    public string CustomOutputRoot { get; set; } = "";
    public bool AddChineseDirectoryLabels { get; set; }
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "视觉小说资源工具", "settings.json");
    public static AppSettings Load()
    {
        try { var settings = File.Exists(FilePath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new() : new(); settings.CustomOutputRoot ??= ""; return settings; }
        catch { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, FilePath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public string GetOutput(string archivePath, string? selectedGameRoot = null)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(archivePath));
        var isDirectory = Directory.Exists(full);
        var archiveName = isDirectory ? Path.GetFileName(full) : Path.GetFileNameWithoutExtension(full);
        var parent = Directory.GetParent(full);
        var gameRoot = isDirectory ? full : parent?.FullName ?? Path.GetPathRoot(full)!;
        if (!string.IsNullOrWhiteSpace(selectedGameRoot) && Directory.Exists(selectedGameRoot))
        {
            var selected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selectedGameRoot));
            if (full.Equals(selected, StringComparison.OrdinalIgnoreCase) || full.StartsWith(selected + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) gameRoot = selected;
        }
        else if (!isDirectory && parent?.Name.Equals("game", StringComparison.OrdinalIgnoreCase) == true && parent.Parent is not null) gameRoot = parent.Parent.FullName;
        var root = UseCustomOutputRoot && !string.IsNullOrWhiteSpace(CustomOutputRoot) ? CustomOutputRoot : Path.Combine(gameRoot, "解包结果");
        return Path.Combine(root, Sanitize(string.IsNullOrWhiteSpace(archiveName) ? "资源" : archiveName));
    }
    private static string Sanitize(string value) { foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_'); return value; }
}

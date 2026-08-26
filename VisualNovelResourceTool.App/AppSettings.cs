using System.Text.Json;
using System.IO;

namespace VisualNovelResourceTool.App;

public sealed class AppSettings
{
    public bool UseCustomOutputRoot { get; set; }
    public string CustomOutputRoot { get; set; } = "";
    public bool AddChineseDirectoryLabels { get; set; }
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "视觉小说资源工具", "settings.json");
    public static AppSettings Load() { try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new() : new(); } catch { return new(); } }
    public void Save() { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
    public string GetOutput(string archivePath, string? selectedGameRoot = null)
    {
        var archiveName = Path.GetFileNameWithoutExtension(archivePath); var parent = Directory.GetParent(archivePath)!;
        var gameRoot = !string.IsNullOrWhiteSpace(selectedGameRoot) && Directory.Exists(selectedGameRoot) && Path.GetFullPath(archivePath).StartsWith(Path.GetFullPath(selectedGameRoot), StringComparison.OrdinalIgnoreCase)
            ? selectedGameRoot : parent.Name.Equals("game", StringComparison.OrdinalIgnoreCase) && parent.Parent is not null ? parent.Parent.FullName : parent.FullName;
        var root = UseCustomOutputRoot && !string.IsNullOrWhiteSpace(CustomOutputRoot) ? CustomOutputRoot : Path.Combine(gameRoot, "解包结果");
        return Path.Combine(root, Sanitize(archiveName));
    }
    private static string Sanitize(string value) { foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_'); return value; }
}

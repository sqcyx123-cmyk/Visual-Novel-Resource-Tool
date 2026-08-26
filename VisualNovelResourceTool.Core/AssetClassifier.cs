namespace VisualNovelResourceTool.Core;

public enum AssetCategory { All, Images, EventCg, Background, CharacterLayer, Animation, Ui, OtherImage, NonVisual }

public static class AssetClassifier
{
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif", ".apng", ".avif", ".jxl", ".tga", ".dds" };
    private static readonly HashSet<string> Animations = new(StringComparer.OrdinalIgnoreCase) { ".gif", ".apng", ".webp", ".mng", ".webm", ".mp4", ".ogv", ".avi" };

    public static bool IsImage(string path) => Images.Contains(Path.GetExtension(path));
    public static AssetCategory Classify(string path)
    {
        var normalized = path.Replace('\\', '/').ToLowerInvariant();
        if (Animations.Contains(Path.GetExtension(path))) return AssetCategory.Animation;
        if (!IsImage(path)) return AssetCategory.NonVisual;
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        bool Has(params string[] words) => parts.Any(p => words.Any(w => p.Contains(w, StringComparison.Ordinal)));
        if (Has("background", "bgimage", "背景") || parts.Any(p => p is "bg" or "bgs")) return AssetCategory.Background;
        if (Has("event", "gallery", "illust", "hcg", "cgimage", "事件") || parts.Any(p => p is "cg" or "cgs" or "ev")) return AssetCategory.EventCg;
        if (Has("sprite", "character", "chara", "fgimage", "foreground", "standing", "portrait", "layer", "expression", "立绘", "差分")) return AssetCategory.CharacterLayer;
        if (Has("gui", "interface", "button", "menu", "system", "title", "icon", "界面" ) || parts.Any(p => p is "ui")) return AssetCategory.Ui;
        return AssetCategory.OtherImage;
    }

    public static bool Matches(string path, AssetCategory filter) => filter switch
    {
        AssetCategory.All => true,
        AssetCategory.Images => IsImage(path) || Classify(path) == AssetCategory.Animation,
        _ => Classify(path) == filter
    };
}

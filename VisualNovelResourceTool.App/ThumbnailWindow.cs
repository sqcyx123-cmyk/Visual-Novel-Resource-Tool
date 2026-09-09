using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VisualNovelResourceTool.App;

internal sealed class ThumbnailWindow : Window
{
    private readonly WrapPanel _panel = new() { Margin = new Thickness(10) };
    private bool _closed;
    public ThumbnailWindow(IReadOnlyList<object> entries, Func<object, Task<byte[]?>> reader, Func<object, string> getName)
    {
        Title = $"缩略图预览（最多显示 200 张，共 {entries.Count:N0} 张）"; Width = 1050; Height = 760; Background = Brushes.White;
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _panel };
        Loaded += async (_, _) => await LoadAsync(entries.Take(200), reader, getName);
        Closed += (_, _) => _closed = true;
    }

    private async Task LoadAsync(IEnumerable<object> entries, Func<object, Task<byte[]?>> reader, Func<object, string> getName)
    {
        foreach (var entry in entries)
        {
            if (_closed) return;
            var name = getName(entry); var image = new Image { Width = 170, Height = 130, Stretch = Stretch.Uniform };
            var tile = new StackPanel { Width = 190, Margin = new Thickness(5) };
            tile.Children.Add(new Border { Width = 180, Height = 140, Background = new SolidColorBrush(Color.FromRgb(242, 244, 247)), Child = image });
            tile.Children.Add(new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, MaxHeight = 38, ToolTip = name, Margin = new Thickness(2, 4, 2, 0) }); _panel.Children.Add(tile);
            try { var bytes = await reader(entry); if (_closed) return; if (bytes is null) continue; using var stream = new MemoryStream(bytes); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 220; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); image.Source = bitmap; } catch { }
            await Task.Yield();
        }
    }
}

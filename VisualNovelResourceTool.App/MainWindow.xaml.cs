using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VisualNovelResourceTool.Core;

namespace VisualNovelResourceTool.App;

public partial class MainWindow : Window
{
    private readonly HashSet<Task> _videoTasks = [];
    private TaskCompletionSource? _operationCompletion;
    private bool _closing;
    private bool _closeReady;
    private readonly GameScanner _scanner = new(); private readonly ObservableCollection<ResultRow> _results = []; private readonly ObservableCollection<object> _visibleEntries = []; private readonly ObservableCollection<NearbyPreview> _nearby = [];
    private readonly AppSettings _settings = AppSettings.Load(); private RpaArchive? _rpa; private Xp3Archive? _xp3; private RpgMakerProject? _rpg; private RpgMakerPakArchive? _pak; private LooseResourceProject? _loose; private DpmxArchive? _dpmx; private CancellationTokenSource? _operation; private string? _archivePath; private string? _selectedGameRoot; private string? _enigmaExe; private DispatcherTimer? _gifTimer; private IReadOnlyList<BitmapFrame>? _gifFrames; private int _gifFrame;private int _nearbyVersion;private int _videoVersion;private CancellationTokenSource? _videoOperation;private int _previewVersion;private bool _busy;private bool _closed;private Point _dragStart;private bool _dragging;private bool _dragCandidate;
    public MainWindow() { InitializeComponent(); ResultsGrid.ItemsSource = _results; EntriesList.ItemsSource = _visibleEntries; NearbyStrip.ItemsSource=_nearby;EntriesList.PreviewMouseLeftButtonDown+=EntriesList_PreviewMouseLeftButtonDown;EntriesList.PreviewMouseLeftButtonUp+=(_,_)=>_dragCandidate=false;EntriesList.PreviewMouseMove+=EntriesList_PreviewMouseMove;Closing+=Window_Closing;Closed+=(_,_)=>{_closed=true;Cancel();ClearArchive();};CustomOutputBox.IsChecked = _settings.UseCustomOutputRoot; ChineseFolderBox.IsChecked = _settings.AddChineseDirectoryLabels; OutputRootBox.Text = _settings.UseCustomOutputRoot ? _settings.CustomOutputRoot : "所选游戏根目录\\解包结果\\资源包名";CleanDragCache(); }
    private async void ChooseFolder_Click(object s, RoutedEventArgs e) { if(_busy)return; var d = new OpenFolderDialog { Title="选择视觉小说目录" }; if(d.ShowDialog()==true) await ScanAsync(d.FolderName); }
    private async void ChooseArchive_Click(object s, RoutedEventArgs e) { if(_busy)return; var d = new OpenFileDialog { Filter="支持的资源包或单文件游戏|*.rpa;*.xp3;*.exe|所有文件|*.*" }; if(d.ShowDialog()==true) await ScanAsync(d.FileName); }
    private async void Decompile_Click(object s,RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new OpenFolderDialog { Title = "选择含有 RPyC 的 Ren’Py 游戏目录" };
        if (dialog.ShowDialog() != true) return;
        using var operation = BeginOperation("正在复制并反编译脚本……");
        try
        {
            var output = _settings.GetOutput(Path.Combine(dialog.FolderName,"scripts.rpa"), dialog.FolderName) + "_反编译脚本";
            var result = await UnrpycService.DecompileAsync(dialog.FolderName, output, operation.Token);
            StatusText.Text = $"已反编译 {result.Files} 个脚本；位置：{output}";
        }
        catch (OperationCanceledException) { StatusText.Text = "已取消反编译。"; }
        catch (Exception ex) { Error("反编译失败", ex); }
        finally { EndOperation(operation); }
    }
    private async void Window_Drop(object s, DragEventArgs e) { if(!_busy&&e.Data.GetData(DataFormats.FileDrop) is string[] {Length:>0} p) await ScanAsync(p[0]); }
    private void Window_DragOver(object s, DragEventArgs e) => e.Effects=!_busy&&e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;
    private void ChooseOutput_Click(object s, RoutedEventArgs e) { var d=new OpenFolderDialog{Title="选择固定的默认输出位置"}; if(d.ShowDialog()==true){_settings.CustomOutputRoot=d.FolderName;_settings.UseCustomOutputRoot=true;CustomOutputBox.IsChecked=true;OutputRootBox.Text=d.FolderName;SaveSettings();UpdateOutputPreview();} }
    private void OutputSetting_Changed(object s, RoutedEventArgs e) { if(!IsLoaded)return;_settings.UseCustomOutputRoot=CustomOutputBox.IsChecked==true; OutputRootBox.Text=_settings.UseCustomOutputRoot?(_settings.CustomOutputRoot.Length>0?_settings.CustomOutputRoot:"请点击“更改位置”"):"所选游戏根目录\\解包结果\\资源包名";SaveSettings();UpdateOutputPreview(); }
    private void ChineseFolder_Changed(object s,RoutedEventArgs e){if(!IsLoaded)return;_settings.AddChineseDirectoryLabels=ChineseFolderBox.IsChecked==true;SaveSettings();}
    private async Task ScanAsync(string path)
    {
        if (_busy || _closed) return;
        using var operation = BeginOperation("正在扫描……");
        PathBox.Text = path;
        _selectedGameRoot = Directory.Exists(path) ? Path.GetFullPath(path) : null;
        _results.Clear();
        ClearArchive();
        try
        {
            var results = await _scanner.ScanAsync(path, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            foreach (var result in results) _results.Add(new(result));
            StatusText.Text = $"发现 {_results.Count} 项。";
        }
        catch (OperationCanceledException) { StatusText.Text = "已取消扫描。"; }
        catch (Exception ex) { Error("扫描失败", ex); }
        finally { EndOperation(operation); }
        if (!_closed && !operation.IsCancellationRequested && _results.Count > 0)
        {
            var preferred = _results.FirstOrDefault(r => r.Source.Kind == ResourceKind.AlreadyUnpacked) ?? _results[0];
            ResultsGrid.SelectedItem = preferred;
        }
    }
    private async void ResultsGrid_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (_busy || _closed || ResultsGrid.SelectedItem is not ResultRow row) return;
        ClearArchive();
        _archivePath = row.Source.Path;
        DetailTitle.Text = row.DisplayName;
        DetailDescription.Text = row.Source.Description;
        UpdateOutputPreview();
        using var operation = BeginOperation("正在读取资源索引……");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        try
        {
            var source = await Task.Run<object?>(() => row.Source.Kind switch
            {
                ResourceKind.RenPyRpa when row.Source.Support == SupportLevel.Extractable => RpaArchive.Open(row.Source.Path, token),
                ResourceKind.KirikiriXp3 when row.Source.Support == SupportLevel.Recognized => Xp3Archive.Open(row.Source.Path, token),
                ResourceKind.RpgMaker => RpgMakerProject.Open(row.Source.Path, token),
                ResourceKind.RpgMakerPak => RpgMakerPakArchive.Open(row.Source.Path, token),
                ResourceKind.AlreadyUnpacked => LooseResourceProject.Open(row.Source.Path, token),
                ResourceKind.Dpmx => DpmxArchive.Open(row.Source.Path, token),
                _ => null
            }, token);
            token.ThrowIfCancellationRequested();
            if (_closed) return;
            switch (source)
            {
                case RpaArchive r: _rpa = r; ShowEntries(r.Entries); break;
                case Xp3Archive x: _xp3 = x; ShowEntries(x.Entries); break;
                case RpgMakerProject r: _rpg = r; ShowEntries(r.Entries); DetailDescription.Text = r.Entries.Any(x=>x.Encrypted) ? (r.EncryptionKey is {Length:16} ? "已找到项目密钥，可还原加密图片和音频。" : "发现加密资源，但缺少有效密钥；普通文件仍可提取。") : "资源已经解开，可直接预览、分类、复制或拖出。"; break;
                case RpgMakerPakArchive p: _pak = p; ShowEntries(p.Entries); break;
                case LooseResourceProject l: _loose = l; ShowEntries(l.Entries); break;
                case DpmxArchive d: _dpmx = d; ShowEntries(d.Entries); break;
            }
            if (row.Source.Kind == ResourceKind.EnigmaExecutable)
            {
                _enigmaExe = row.Source.Path;
                ExtractAllButton.Content = "提取 EXE 中的图片";
                EntryCountText.Text = "单文件封装将在提取时读取；只保留图片和动图。";
            }
        }
        catch (OperationCanceledException) { DetailDescription.Text = operation.IsCancellationRequested ? "已取消索引读取。" : "索引读取超过 30 秒，已停止。"; }
        catch (Exception ex) { DetailDescription.Text = "读取失败：" + ex.Message; }
        finally { EndOperation(operation); }
    }
    private void ShowEntries(IEnumerable<object> entries){ExtractAllButton.Content="提取当前分类";_visibleEntries.Clear();foreach(var x in entries)_visibleEntries.Add(x);EntryCountText.Text=$"{_visibleEntries.Count:N0} 个文件；可按 Ctrl/Shift 多选";ExtractAllButton.IsEnabled=_visibleEntries.Count>0;ApplyFilter();StatusText.Text=_visibleEntries.Count==0?"索引读取完成，但当前“图片”分类中没有文件；可切换到“全部资源（高级）”查看。":$"索引读取完成，当前显示 {_visibleEntries.Count:N0} 个文件。";}
    private void Filter_Changed(object s, TextChangedEventArgs e)=>ApplyFilter();
    private void Category_Changed(object s, SelectionChangedEventArgs e)=>ApplyFilter();
    private void ApplyFilter(){if(!IsLoaded)return;IEnumerable<object> source=_rpa?.Entries.Cast<object>()??_xp3?.Entries.Cast<object>()??_rpg?.Entries.Cast<object>()??_pak?.Entries.Cast<object>()??_loose?.Entries.Cast<object>()??_dpmx?.Entries.Cast<object>()??[];var q=FilterBox.Text.Trim();var category=Enum.TryParse<AssetCategory>(CategoryBox.SelectedValue?.ToString(),out var c)?c:AssetCategory.Images;_visibleEntries.Clear();foreach(var x in source.Where(x=>AssetClassifier.Matches(GetName(x),category)&&(q.Length==0||GetName(x).Contains(q,StringComparison.OrdinalIgnoreCase))))_visibleEntries.Add(x);EntryCountText.Text=$"显示 {_visibleEntries.Count:N0} 个文件";ExtractAllButton.IsEnabled=_visibleEntries.Count>0;ThumbnailButton.IsEnabled=_visibleEntries.Any(x=>AssetClassifier.IsImage(GetName(x)));}
    private async void Entries_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        ExtractSelectedButton.IsEnabled = !_busy && EntriesList.SelectedItems.Count > 0;
        var version = ++_previewVersion;
        _nearbyVersion++;
        _nearby.Clear();
        StopGif(); StopVideoPreview(); PreviewImage.Source = null;
        var selected = EntriesList.SelectedItem;
        if (selected is null || _closed) return;
        var name = GetName(selected);
        _ = LoadNearbyAsync(selected);
        if (IsVideo(name))
        {
            var task = PreviewVideoAsync(selected, name);
            _videoTasks.Add(task);
            try { await task; } finally { _videoTasks.Remove(task); }
            return;
        }
        if (!AssetClassifier.IsImage(name)) return;
        try
        {
            var bytes = await ReadPreviewBytesAsync(selected);
            if (version != _previewVersion || _closed || bytes is null) return;
            using var stream = new MemoryStream(bytes);
            if (Path.GetExtension(name).Equals(".gif", StringComparison.OrdinalIgnoreCase)) ShowAnimatedGif(stream);
            else
            {
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream; image.EndInit(); image.Freeze(); PreviewImage.Source = image;
            }
        }
        catch (Exception ex) { if (version == _previewVersion && !_closed) DetailDescription.Text = $"该图片暂时无法预览（仍可正常提取）：{ex.Message}"; }
    }
    private void ShowAnimatedGif(Stream stream){var decoder=new GifBitmapDecoder(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);_gifFrames=decoder.Frames.ToArray();_gifFrame=0;PreviewImage.Source=_gifFrames[0];if(_gifFrames.Count<=1)return;_gifTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(140)};_gifTimer.Tick+=(_,_)=>{if(_gifFrames is {Count:>0})PreviewImage.Source=_gifFrames[_gifFrame=(_gifFrame+1)%_gifFrames.Count];};_gifTimer.Start();}
    private void StopGif(){_gifTimer?.Stop();_gifTimer=null;_gifFrames=null;}
    private static bool IsVideo(string name)=>Path.GetExtension(name).ToLowerInvariant() is ".webm" or ".mp4" or ".ogv" or ".avi";
    private async Task PreviewVideoAsync(object entry, string name)
    {
        var version = ++_videoVersion;
        using var operation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _videoOperation = operation;
        var cache = Path.Combine(Path.GetTempPath(), "vnrt-preview-" + Guid.NewGuid().ToString("N"));
        DetailDescription.Text = "正在生成轻量动态预览……";
        try
        {
            var result = await ExtractEntriesAsync([entry], cache, true, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (version != _videoVersion || _closed) return;
            if (result.Extracted != 1) throw new InvalidDataException("临时视频提取失败。");
            var file = Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories).First(f => Path.GetExtension(f).Equals(Path.GetExtension(name), StringComparison.OrdinalIgnoreCase));
            var gif = Path.Combine(cache, "preview.gif");
            var start = new ProcessStartInfo("ffmpeg.exe") { ArgumentList = { "-v", "error", "-ss", "0", "-i", file, "-t", "6", "-vf", "fps=7,scale=560:-2:flags=lanczos", "-an", "-y", gif } };
            var process = await ExternalProcess.RunAsync(start, operation.Token);
            if (process.ExitCode != 0 || !File.Exists(gif)) throw new InvalidDataException(string.IsNullOrWhiteSpace(process.Error) ? "FFmpeg 无法解码该视频。" : process.Error.Trim());
            if (version != _videoVersion || _closed) return;
            using var stream = File.OpenRead(gif);
            ShowAnimatedGif(stream);
            DetailDescription.Text = "视频预览：前 6 秒、静音循环（原文件不受影响）。";
        }
        catch (OperationCanceledException) { if (version == _videoVersion && !_closed) DetailDescription.Text = "视频预览已超时；仍可提取原视频。"; }
        catch (System.ComponentModel.Win32Exception) { if (version == _videoVersion && !_closed) DetailDescription.Text = "无法预览视频：未找到 FFmpeg。仍可正常提取原视频。"; }
        catch (Exception ex) { if (version == _videoVersion && !_closed) DetailDescription.Text = $"该视频暂时无法预览（仍可正常提取）：{ex.Message}"; }
        finally
        {
            if (ReferenceEquals(_videoOperation, operation)) _videoOperation = null;
            try { if (Directory.Exists(cache)) Directory.Delete(cache, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    private void PreviewVideo_Ended(object s,RoutedEventArgs e){}
    private void PreviewVideo_Failed(object s,ExceptionRoutedEventArgs e){}
    private void StopVideoPreview() { _videoVersion++; _videoOperation?.Cancel(); }
    private async Task LoadNearbyAsync(object selected){var version=++_nearbyVersion;var reader=CreatePreviewReader();var images=_visibleEntries.Where(x=>AssetClassifier.IsImage(GetName(x))).ToArray();var at=Array.IndexOf(images,selected);if(at<0){_nearby.Clear();return;}var start=Math.Clamp(at-2,0,Math.Max(0,images.Length-5));var items=images.Skip(start).Take(5).ToArray();_nearby.Clear();foreach(var item in items)_nearby.Add(new(item,null,GetName(item)));for(var i=0;i<items.Length;i++){try{var bytes=await reader(items[i],4*1024*1024);if(version!=_nearbyVersion||bytes is null)return;using var stream=new MemoryStream(bytes);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=100;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();_nearby[i]=new(items[i],bitmap,GetName(items[i]));}catch{}}}
    private void Nearby_Click(object s,RoutedEventArgs e){if(s is Button{Tag:object entry}){EntriesList.SelectedItem=entry;EntriesList.ScrollIntoView(entry);}}
    private async void ExtractAll_Click(object s,RoutedEventArgs e)=>await ExtractAsync(false);
    private async void ExtractSelected_Click(object s,RoutedEventArgs e)=>await ExtractAsync(true);
    private void Thumbnail_Click(object s,RoutedEventArgs e)
    {
        if (_busy) return;
        var images = _visibleEntries.Where(x => AssetClassifier.IsImage(GetName(x))).ToArray();
        var reader = CreatePreviewReader();
        new ThumbnailWindow(images, x => reader(x, 16 * 1024 * 1024), GetName) { Owner = this }.Show();
    }
    private Task<byte[]?> ReadPreviewBytesAsync(object entry,int maximumBytes=16*1024*1024) => CreatePreviewReader()(entry,maximumBytes);
    private Func<object,int,Task<byte[]?>> CreatePreviewReader()
    {
        var rpa = _rpa; var xp3 = _xp3; var pak = _pak; var loose = _loose; var key = _rpg?.EncryptionKey;
        return async (entry, maximumBytes) => entry switch
        {
            RpaEntry r when rpa is not null => await rpa.ReadEntryAsync(r, maximumBytes),
            Xp3Entry x when xp3 is not null => await xp3.ReadEntryAsync(x, maximumBytes),
            PakEntry p when pak is not null => await pak.ReadEntryAsync(p, maximumBytes),
            RpgMakerEntry g => await ReadRpgMakerPreviewAsync(g, maximumBytes, key),
            LooseEntry l when loose is not null => await loose.ReadEntryAsync(l, maximumBytes),
            _ => null
        };
    }
    private static async Task<byte[]> ReadRpgMakerPreviewAsync(RpgMakerEntry entry,int maximumBytes,byte[]? encryptionKey){if(entry.Length>maximumBytes)throw new InvalidDataException($"文件过大，无法预览（{entry.Length:N0} 字节）。");if(!entry.Encrypted)return await File.ReadAllBytesAsync(entry.SourcePath);var key=encryptionKey??throw new InvalidDataException("缺少 RPG Maker 解密密钥。");var data=await File.ReadAllBytesAsync(entry.SourcePath);if(data.Length<16)throw new InvalidDataException("加密文件过短。");var plain=data[16..];for(var i=0;i<Math.Min(16,plain.Length);i++)plain[i]^=key[i%key.Length];return plain;}
    private async Task ExtractAsync(bool selected)
    {
        if (_busy || _closed || _archivePath is null) return;
        using var operation = BeginOperation(_enigmaExe is null ? "准备提取……" : "正在展开单文件 EXE 并筛选图片，可能需要几分钟……");
        Progress.Visibility = Visibility.Visible;
        Progress.IsIndeterminate = _enigmaExe is not null;
        try
        {
            var output = _settings.GetOutput(_archivePath, _selectedGameRoot);
            var list = selected ? EntriesList.SelectedItems.Cast<object>().ToArray() : _visibleEntries.ToArray();
            var reporter = new Progress<ExtractionProgress>(p =>
            {
                if (!ReferenceEquals(_operation, operation) || _closed) return;
                Progress.Value = p.Total == 0 ? 0 : p.Completed * 100d / p.Total;
                StatusText.Text = $"{p.Completed:N0}/{p.Total:N0} {p.CurrentFile}";
            });
            var result = _enigmaExe is not null
                ? await EnigmaExtractorService.ExtractVisualsAsync(_enigmaExe, output, OverwriteBox.IsChecked == true, _settings.AddChineseDirectoryLabels, operation.Token)
                : await ExtractEntriesAsync(list, output, OverwriteBox.IsChecked == true, operation.Token, _settings.AddChineseDirectoryLabels, reporter);
            StatusText.Text = $"完成：提取 {result.Extracted}，跳过 {result.Skipped}，失败 {result.Failed}，重命名 {result.Renamed}。位置：{output}";
            OutputPreviewText.Text = $"最近完成：\n{output}\n提取 {result.Extracted}，跳过 {result.Skipped}，失败 {result.Failed}";
        }
        catch (OperationCanceledException) { StatusText.Text = "已取消。"; }
        catch (Exception ex) { Error("提取失败", ex); }
        finally { EndOperation(operation); Progress.IsIndeterminate = false; Progress.Visibility = Visibility.Collapsed; }
    }
    private Task<ExtractionSummary> ExtractEntriesAsync(object[] entries, string output, bool overwrite, CancellationToken token, bool labels = false, IProgress<ExtractionProgress>? progress = null)
    {
        if (_rpa is not null) return _rpa.ExtractAsync(entries.Cast<RpaEntry>(),output,overwrite,progress,token,labels);
        if (_xp3 is not null) return _xp3.ExtractAsync(entries.Cast<Xp3Entry>(),output,overwrite,progress,token,labels);
        if (_rpg is not null) return _rpg.ExtractAsync(entries.Cast<RpgMakerEntry>(),output,overwrite,progress,token,labels);
        if (_pak is not null) return _pak.ExtractAsync(entries.Cast<PakEntry>(),output,overwrite,progress,token,labels);
        if (_loose is not null) return _loose.ExtractAsync(entries.Cast<LooseEntry>(),output,overwrite,progress,token,labels);
        if (_dpmx is not null) return _dpmx.ExtractAsync(entries.Cast<DpmxEntry>(),output,overwrite,progress,token);
        throw new InvalidOperationException("没有可提取的资源。");
    }
    private void SaveSettings() { try { _settings.Save(); } catch (Exception ex) { Error("保存设置失败", ex); } }
    private void UpdateOutputPreview()
    {
        try { OutputPreviewText.Text = _archivePath is null ? "选择资源包后显示输出位置" : $"将提取到：\n{_settings.GetOutput(_archivePath,_selectedGameRoot)}"; }
        catch (Exception ex) { OutputPreviewText.Text = "输出路径无效：" + ex.Message; }
    }
    private void ClearArchive(){_previewVersion++;StopGif();StopVideoPreview();_nearbyVersion++;_nearby.Clear();_rpa=null;_xp3=null;_rpg=null;_pak=null;_loose=null;_dpmx=null;_enigmaExe=null;_archivePath=null;_visibleEntries.Clear();PreviewImage.Source=null;ExtractAllButton.Content="提取当前分类";ExtractAllButton.IsEnabled=ExtractSelectedButton.IsEnabled=ThumbnailButton.IsEnabled=false;}
    private static string GetName(object x)=>x switch{RpaEntry r=>r.Name,Xp3Entry p=>p.Name,RpgMakerEntry g=>g.RelativeOutputPath,PakEntry p=>p.Name,LooseEntry l=>l.Name,DpmxEntry d=>d.Name,_=>""};
    private void EntriesList_PreviewMouseLeftButtonDown(object s,System.Windows.Input.MouseButtonEventArgs e){var source=e.OriginalSource as DependencyObject;var point=e.GetPosition(EntriesList);_dragCandidate=FindAncestor<ScrollBar>(source) is null&&FindAncestor<ListBoxItem>(source) is not null&&FindAncestor<TextBlock>(source) is not null&&point.X<EntriesList.ActualWidth-SystemParameters.VerticalScrollBarWidth-8;if(_dragCandidate)_dragStart=point;}
    private static T? FindAncestor<T>(DependencyObject? value)where T:DependencyObject{while(value is not null){if(value is T match)return match;value=VisualTreeHelper.GetParent(value);}return null;}
    private async void EntriesList_PreviewMouseMove(object s,System.Windows.Input.MouseEventArgs e)
    {
        if(_busy||!_dragCandidate||_dragging||e.LeftButton!=System.Windows.Input.MouseButtonState.Pressed||EntriesList.SelectedItems.Count==0)return;var point=e.GetPosition(EntriesList);var dx=Math.Abs(point.X-_dragStart.X);var dy=Math.Abs(point.Y-_dragStart.Y);if(dx<SystemParameters.MinimumHorizontalDragDistance||dx<dy*1.5)return;
        _dragCandidate=false;_dragging=true;using var operation=BeginOperation("正在准备拖出……");try{var selected=EntriesList.SelectedItems.Cast<object>().ToArray();var cache=Path.Combine(DragCacheRoot,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(cache);StatusText.Text=$"正在准备拖出 {selected.Length} 个文件……";await ExtractEntriesAsync(selected,cache,true,operation.Token);operation.Token.ThrowIfCancellationRequested();if(_closed)return;var files=Directory.EnumerateFiles(cache,"*",SearchOption.AllDirectories).Where(f=>!Path.GetFileName(f).Equals("_提取记录.txt",StringComparison.OrdinalIgnoreCase)).ToArray();if(files.Length==0)return;var collection=new StringCollection();collection.AddRange(files);var data=new DataObject();data.SetFileDropList(collection);StatusText.Text="可拖放到资源管理器或桌面。";DragDrop.DoDragDrop(EntriesList,data,DragDropEffects.Copy);}catch(OperationCanceledException){StatusText.Text="已取消拖出。";}catch(Exception ex){Error("拖出失败",ex);}finally{_dragging=false;EndOperation(operation);}}
    private static string DragCacheRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"视觉小说资源工具","拖出缓存");
    private static void CleanDragCache(){try{if(!Directory.Exists(DragCacheRoot))return;foreach(var directory in Directory.EnumerateDirectories(DragCacheRoot)){var info=new DirectoryInfo(directory);if(info.CreationTimeUtc<DateTime.UtcNow.AddDays(-1))Directory.Delete(directory,true);}}catch{}}
    private void Cancel_Click(object s,RoutedEventArgs e) => Cancel();
    private void Cancel() => _operation?.Cancel();
    private CancellationTokenSource BeginOperation(string text)
    {
        var operation = new CancellationTokenSource();
        _operation = operation;
        _operationCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetBusy(true, text);
        return operation;
    }
    private void EndOperation(CancellationTokenSource operation)
    {
        if (!ReferenceEquals(_operation, operation)) return;
        _operation = null;
        _operationCompletion?.TrySetResult();
        _operationCompletion = null;
        SetBusy(false);
    }
    private void SetBusy(bool busy,string? text=null)
    {
        _busy = busy;
        CancelButton.IsEnabled = busy;
        InputActions.IsEnabled = OutputSettings.IsEnabled = FilterControls.IsEnabled = OutputOptions.IsEnabled = !busy;
        ResultsGrid.IsEnabled = EntriesList.IsEnabled = NearbyStrip.IsEnabled = !busy;
        ExtractAllButton.IsEnabled = !busy && (_enigmaExe is not null || _visibleEntries.Count > 0);
        ExtractSelectedButton.IsEnabled = !busy && EntriesList.SelectedItems.Count > 0;
        ThumbnailButton.IsEnabled = !busy && _visibleEntries.Any(x => AssetClassifier.IsImage(GetName(x)));
        if(text is not null) StatusText.Text = text;
    }
    private void Error(string title,Exception ex)
    {
        if (_closed) return;
        StatusText.Text = title + "：" + ex.Message;
        MessageBox.Show(this, ex.Message, title);
    }
    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closing) { e.Cancel = !_closeReady; return; }
        _closing = true;
        _closed = true;
        Cancel(); StopGif(); StopVideoPreview();
        var pending = _videoTasks.ToList();
        if (_operationCompletion is not null) pending.Add(_operationCompletion.Task);
        if (pending.Count == 0) { _closeReady = true; return; }
        e.Cancel = true;
        IsEnabled = false;
        await Task.WhenAll(pending);
        _closeReady = true;
        Close();
    }
}
public sealed class ResultRow{public ScanResult Source{get;}public string DisplayName=>Source.DisplayName;public string Format=>Source.Format;public string StatusText=>Source.Support switch{SupportLevel.Extractable=>"可提取",SupportLevel.Recognized=>"已识别",SupportLevel.NoExtractionNeeded=>"无需解包",SupportLevel.Error=>"异常",_=>"暂不支持"};public ResultRow(ScanResult s)=>Source=s;}
public sealed record NearbyPreview(object Entry,ImageSource? Source,string Name);

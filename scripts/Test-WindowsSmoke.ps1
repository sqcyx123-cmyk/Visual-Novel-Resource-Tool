# Run after the Release build, in PowerShell 7 with -STA on Windows.
# All game assets are synthetic. Output settings are changed only in memory.
param(
 [string]$ProjectRoot=(Split-Path $PSScriptRoot -Parent),
 [string]$ArtifactRoot=(Join-Path $env:TEMP 'vnrt-review-artifacts'),
 [string]$FfmpegPath='ffmpeg.exe'
)
$ErrorActionPreference='Stop'
[void][IO.Directory]::CreateDirectory($ArtifactRoot)
$ffmpeg=Get-Command $FfmpegPath -ErrorAction SilentlyContinue
$hasVideo=$null -ne $ffmpeg
if($hasVideo){
 $env:PATH=(Split-Path $ffmpeg.Source -Parent)+[IO.Path]::PathSeparator+$env:PATH
 & $ffmpeg.Source -v error -f lavfi -i color=c=blue:s=64x64:r=10 -t 2 -c:v libvpx -y (Join-Path $ArtifactRoot 'review-sample.webm')
 if($LASTEXITCODE -ne 0){throw 'Cannot create synthetic video'}
}else{Write-Output 'SKIP UI video: FFmpeg unavailable'}
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
$bin=Join-Path $ProjectRoot 'VisualNovelResourceTool.App/bin/Release/net8.0-windows'
[void][Reflection.Assembly]::LoadFrom((Join-Path $bin 'VisualNovelResourceTool.Core.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $bin 'SharpCompress.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $bin '视觉小说资源工具.dll'))
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$app=[Windows.Application]::new()
$app.ShutdownMode=[Windows.ShutdownMode]::OnExplicitShutdown
$window=[VisualNovelResourceTool.App.MainWindow]::new()
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
function Field($name){$window.GetType().GetField($name,$flags).GetValue($window)}
function Call($name,[object[]]$values){$unwrapped=[object[]]::new($values.Count);for($i=0;$i -lt $values.Count;$i++){$unwrapped[$i]=$values[$i].PSObject.BaseObject};$window.GetType().GetMethod($name,$flags).Invoke($window,$unwrapped)}
function Pump {
 $frame=[Windows.Threading.DispatcherFrame]::new()
 [void][Windows.Threading.Dispatcher]::CurrentDispatcher.BeginInvoke([Windows.Threading.DispatcherPriority]::Background,[Action]{$frame.Continue=$false}.GetNewClosure())
 [Windows.Threading.Dispatcher]::PushFrame($frame)
}
function Wait-For([scriptblock]$condition,[string]$description){
 $timer=[Diagnostics.Stopwatch]::StartNew()
 while(-not (& $condition)){Pump;Start-Sleep -Milliseconds 10;if($timer.Elapsed.TotalSeconds -gt 15){throw "Timeout: $description"}}
 Pump
}
function Check($value,$message){if(-not $value){throw $message};Write-Output "PASS UI $message"}
$root=Join-Path $env:TEMP ('vnrt-ui-'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory((Join-Path $root 'game'))
try {
 for($i=0;$i -lt 30;$i++){
  $pixels=[byte[]]@(0,($i*7),255,255)
  $bitmap=[Windows.Media.Imaging.BitmapSource]::Create(1,1,96,96,[Windows.Media.PixelFormats]::Bgra32,$null,$pixels,4)
  $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
  $stream=[IO.File]::Create((Join-Path $root "game/image-$i.png"));try{$encoder.Save($stream)}finally{$stream.Dispose()}
 }
 if($hasVideo){Copy-Item -LiteralPath (Join-Path $ArtifactRoot 'review-sample.webm') -Destination (Join-Path $root 'game/zz-clip.webm')}
 $settings=Field '_settings';$settings.UseCustomOutputRoot=$true;$settings.CustomOutputRoot=Join-Path $root 'output'
 $window.Show();Pump
 $scan=Call 'ScanAsync' @($root)
 Check (Field '_busy') '扫描进入忙碌状态'
 Check (-not (Field 'InputActions').IsEnabled) '扫描期间阻止重复输入'
 Wait-For {$scan.IsCompleted -and -not (Field '_busy')} 'scan and index'
 [void]$scan.GetAwaiter().GetResult()
 $entries=Field 'EntriesList'
 Check ($entries.Items.Count -eq (30+[int]$hasVideo)) '散装资源索引与默认图片分类'
 $entries.SelectedIndex=0
 Wait-For {(Field 'PreviewImage').Source -ne $null} 'first preview'
 Check ((Field '_nearby').Count -eq 5) '前后五张缩略图'
 $entries.SelectedIndex=1;$entries.SelectedIndex=2;$entries.SelectedIndex=-1
 for($i=0;$i -lt 25;$i++){Pump;Start-Sleep -Milliseconds 10}
 Check ($null -eq (Field 'PreviewImage').Source) '快速切换并取消选择后无过期预览'
 Check ((Field '_nearby').Count -eq 0) '清空选择后缩略图同步清空'
 $entries.SelectedItems.Add($entries.Items[0]);$entries.SelectedItems.Add($entries.Items[1])
 $extract=Call 'ExtractAsync' @($true)
 $duplicate=Call 'ExtractAsync' @($true)
 Wait-For {$extract.IsCompleted} 'selected extraction';[void]$extract.GetAwaiter().GetResult()
 Check ($duplicate.IsCompleted) '重复提取请求直接返回'
 Check (@(Get-ChildItem -LiteralPath $settings.CustomOutputRoot -Recurse -Filter *.png).Count -eq 2) '多选仅提取两项'
 if($hasVideo){
 $videoEntry=$entries.Items | Where-Object Name -eq 'zz-clip.webm' | Select-Object -First 1
 $entries.SelectedItem=$videoEntry
 Wait-For {(Field '_gifFrames').Count -gt 1 -and $null -eq (Field '_videoOperation')} 'FFmpeg preview'
 Check ((Field '_gifFrames').Count -gt 1) 'FFmpeg 真实视频转换及 GIF 播放'
 $entries.SelectedIndex=-1
 Check ($null -eq (Field '_gifTimer')) '离开视频停止 GIF 计时器'
 }
 $rpg=Join-Path $root 'rpg1.2';[void][IO.Directory]::CreateDirectory((Join-Path $rpg 'js'));[IO.File]::WriteAllText((Join-Path $rpg 'js/rpg_core.js'),'RPG Maker')
 Copy-Item -LiteralPath (Join-Path $root 'game/image-0.png') -Destination (Join-Path $rpg 'scene.png')
 $scan=Call 'ScanAsync' @($rpg);Wait-For {$scan.IsCompleted -and -not (Field '_busy')} 'RPG index';[void]$scan.GetAwaiter().GetResult()
 Check ($entries.Items.Count -eq 1 -and $entries.Items[0].Name -eq 'scene.png') 'RPG Maker 列表名称绑定'
 $entries.SelectedIndex=0;Wait-For {(Field 'PreviewImage').Source -ne $null} 'RPG preview'
 $window.UpdateLayout()
 [void][IO.Directory]::CreateDirectory($ArtifactRoot)
 $image=[Windows.Media.Imaging.RenderTargetBitmap]::new([int]$window.ActualWidth,[int]$window.ActualHeight,96,96,[Windows.Media.PixelFormats]::Pbgra32)
 $image.Render($window);$encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($image))
 $stream=[IO.File]::Create((Join-Path $ArtifactRoot 'review-ui.png'));try{$encoder.Save($stream)}finally{$stream.Dispose()}
 $scan=Call 'ScanAsync' @($root)
 [void](Call 'Cancel' @())
 Wait-For {$scan.IsCompleted -and -not (Field '_busy')} 'cancel scan'
 Check ((Field 'StatusText').Text -like '*取消*') '取消扫描恢复空闲状态'
 $scan=Call 'ScanAsync' @($root)
 $window.Close()
 Wait-For {$scan.IsCompleted -and -not $window.IsVisible} 'close during scan'
 Check ($scan.IsCompleted) '关闭窗口等待当前操作取消'
 Write-Output 'UI smoke completed'
} finally {
 $window.Close();Pump;$app.Shutdown()
 if([IO.Path]::GetFullPath($root).StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath())) -and [IO.Path]::GetFileName($root).StartsWith('vnrt-ui-')){Remove-Item -LiteralPath $root -Recurse -Force}
}

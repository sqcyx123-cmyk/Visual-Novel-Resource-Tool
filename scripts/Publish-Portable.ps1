# Development command. The application folder is the one used both daily and for sharing.
[CmdletBinding()]
param([string]$Destination = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) '视觉小说资源工具'))
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$destinationFull = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
$artifacts = Join-Path $project '.artifacts'
$stage = Join-Path $artifacts ('portable-stage-' + [guid]::NewGuid().ToString('N'))
$manifestPath = Join-Path $artifacts 'portable-manifest.json'
$previous = Join-Path $artifacts 'portable-previous'
if ([IO.Path]::GetFileName($destinationFull) -ne '视觉小说资源工具') { throw '成品目录必须命名为“视觉小说资源工具”，避免误操作其他目录。' }
if (Test-Path -LiteralPath $previous) { throw "存在待处理的更新备份：$previous，请先核对。" }
if (Get-Process | Where-Object { $_.Path -and $_.Path.StartsWith($destinationFull + '\', [StringComparison]::OrdinalIgnoreCase) }) { throw '请先关闭成品目录中运行的程序。' }
if (Test-Path -LiteralPath $destinationFull) {
    if (!(Test-Path -LiteralPath $manifestPath)) { throw '现有成品没有维护清单；请先核对其内容，不能直接覆盖。' }
    $old = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($old.Destination -ne $destinationFull) { throw '现有维护清单属于其他目录。' }
    foreach ($file in Get-ChildItem -LiteralPath $destinationFull -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($destinationFull, $file.FullName)
        $record = $old.Files | Where-Object Path -EQ $relative | Select-Object -First 1
        if (!$record -or (Get-FileHash -LiteralPath $file.FullName).Hash -ne $record.SHA256) { throw "成品内有新增或修改的文件，已保留，需先核对：$relative" }
    }
}
[void][IO.Directory]::CreateDirectory($artifacts)
$env:DOTNET_CLI_HOME = Join-Path $project '.dotnet-home'
try {
    dotnet publish (Join-Path $project 'VisualNovelResourceTool.App/VisualNovelResourceTool.App.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $stage
    if ($LASTEXITCODE -ne 0) { throw '发布构建失败，现有成品未改动。' }
    Copy-Item -LiteralPath (Join-Path $project 'docs/PORTABLE-README.txt') -Destination (Join-Path $stage '使用说明.txt')
    foreach ($required in @('视觉小说资源工具.exe', 'third_party/evbunpack/evbunpack.exe', 'third_party/unrpyc/unrpyc.py', 'LICENSE.txt', 'THIRD_PARTY_NOTICES.md', 'DISCLAIMER.md')) {
        if (!(Test-Path -LiteralPath (Join-Path $stage $required))) { throw "成品缺少文件：$required" }
    }
    if (Test-Path -LiteralPath $destinationFull) { Move-Item -LiteralPath $destinationFull -Destination $previous }
    try { Move-Item -LiteralPath $stage -Destination $destinationFull }
    catch { if (Test-Path -LiteralPath $previous) { Move-Item -LiteralPath $previous -Destination $destinationFull }; throw }
    $files = @(Get-ChildItem -LiteralPath $destinationFull -Recurse -File | ForEach-Object { [ordered]@{ Path=[IO.Path]::GetRelativePath($destinationFull,$_.FullName); SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash } })
    [ordered]@{ Destination=$destinationFull; BuiltAt=(Get-Date).ToString('o'); Files=$files } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    Write-Output "成品：$destinationFull"
    if (Test-Path -LiteralPath $previous) { Write-Output "待确认本次成品正常后，清理暂存旧版：$previous" }
} finally {
    if ((Test-Path -LiteralPath $stage) -and [IO.Path]::GetFullPath($stage).StartsWith([IO.Path]::GetFullPath($artifacts) + '\', [StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $stage -Recurse -Force }
}

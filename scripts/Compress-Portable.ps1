# On-demand only: validate the maintained application files, then create one archive.
[CmdletBinding()]
param([string]$ZipPath)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content -LiteralPath (Join-Path $project '.artifacts/portable-manifest.json') -Raw | ConvertFrom-Json
$folder = $manifest.Destination
if (!$ZipPath) { $ZipPath = Join-Path (Split-Path $folder -Parent) '视觉小说资源工具.zip' }
$ZipPath = [IO.Path]::GetFullPath($ZipPath)
if ($ZipPath.StartsWith([IO.Path]::GetFullPath($folder).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '压缩包应写在成品目录外，避免把压缩包混入成品。' }
if (Test-Path -LiteralPath $ZipPath) { throw "压缩包已存在，请先确认是否仍需保留：$ZipPath" }
$actual = @(Get-ChildItem -LiteralPath $folder -Recurse -File)
if ($actual.Count -ne $manifest.Files.Count) { throw '成品中存在新增或缺失文件，请先核对，避免误分享个人文件。' }
foreach ($file in $actual) {
    $relative = [IO.Path]::GetRelativePath($folder, $file.FullName)
    $expected = $manifest.Files | Where-Object Path -EQ $relative | Select-Object -First 1
    if (!$expected -or (Get-FileHash -LiteralPath $file.FullName).Hash -ne $expected.SHA256) { throw "成品文件已变化，停止打包：$relative" }
}
Compress-Archive -LiteralPath $folder -DestinationPath $ZipPath
Write-Output "分享压缩包：$ZipPath"

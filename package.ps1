param([string]$Version = '1.3.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# ---- A: 배포용 ZIP 만들기 ------------------------------------------------
# 런타임에 필요한 파일만 골라 '틈' 폴더로 모은 뒤 Teum-v<버전>.zip 하나로 묶는다.
# 사용자 설정(settings.json, user-themes.json, knox-token.txt, localStorage)은
# %LOCALAPPDATA%\DeskHooker와 WebView2 프로필에 있으므로 ZIP에 포함하지 않는다.

$runtimeFiles = @(
    'DeskHooker.exe',
    'Microsoft.Web.WebView2.Core.dll',
    'Microsoft.Web.WebView2.WinForms.dll',
    'WebView2Loader.dll',
    'app.html', 'app.js', 'app.css', 'app-core.js',
    'theme-variants.js', 'games-content.js', 'themes-data.js',
    'themes.json', 'manifest.json',
    'desk-stretch-motion.gif', 'desk-stretch-guide.png',
    '사용안내.txt'
)
$runtimeDirs = @('themes', 'assets')

$stage = Join-Path $env:TEMP "teum-pack"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
$dest = Join-Path $stage '틈'
New-Item -ItemType Directory -Path $dest | Out-Null

foreach ($name in $runtimeFiles) {
    $src = Join-Path $root $name
    if (!(Test-Path $src)) { throw "Missing runtime file: $name" }
    Copy-Item -LiteralPath $src -Destination $dest -Force
}
foreach ($dir in $runtimeDirs) {
    $src = Join-Path $root $dir
    if (!(Test-Path $src)) { throw "Missing runtime folder: $dir" }
    Copy-Item -LiteralPath $src -Destination $dest -Recurse -Force
}

$zipPath = Join-Path $root ("Teum-v" + $Version + ".zip")
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
# Compress-Archive(PowerShell 5)는 한글 이름 항목을 시스템 코드 페이지로 기록해
# 압축 내용이 깨지므로, UTF-8 이름을 그대로 보존하는 .NET API로 직접 묶는다.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in (Get-ChildItem -LiteralPath $stage -Recurse -File)) {
        $rel = $file.FullName.Substring($stage.Length + 1)
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $rel, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose() }
Remove-Item $stage -Recurse -Force

$size = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Output ("Created: " + $zipPath + " (" + $size + " MB)")
Write-Output "Contents: run the app from the extracted '틈' folder. Settings live in %LOCALAPPDATA%\DeskHooker and are kept on upgrades."

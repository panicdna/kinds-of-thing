param([switch]$Test)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (!(Test-Path $csc)) { throw '.NET Framework 4.x C# compiler was not found.' }
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll')
foreach ($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')) {
    $source = Join-Path $root "lib\$name"
    if (!(Test-Path $source)) { throw "Missing WebView2 SDK file: $source" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $root $name) -Force
}
$references += @((Join-Path $root 'Microsoft.Web.WebView2.Core.dll'), (Join-Path $root 'Microsoft.Web.WebView2.WinForms.dll'))
$argsList = @('/nologo','/target:winexe','/platform:x64','/optimize+',('/out:' + (Join-Path $root 'shim.exe')))
# 앱 아이콘(바로가기·작업표시줄·트레이가 기본으로 보는 exe 아이콘)을 themes\아이콘.png에서
# 만든 app.ico(256~16px)로 심는다. 바로가기(.lnk)는 exe의 Win32 아이콘을 그대로 쓴다.
$appIcon = Join-Path $root 'app.ico'
if (Test-Path $appIcon) { $argsList += ('/win32icon:' + $appIcon) }
foreach ($reference in $references) { $argsList += '/reference:' + $reference }
$argsList += @((Join-Path $root 'ReminderCore.cs'),(Join-Path $root 'DeskHooker.cs'))
& $csc @argsList
if ($LASTEXITCODE -ne 0) { throw 'C# build failed.' }
Write-Output "Built: $(Join-Path $root 'shim.exe')"
if ($Test) {
    $process = Start-Process -FilePath (Join-Path $root 'shim.exe') -ArgumentList '--ui-test' -PassThru
    if (!$process.WaitForExit(120000)) { $process.Kill(); throw 'UI smoke test timed out.' }
    $result = Join-Path $root 'tests\ui\results.json'
    if (!(Test-Path $result)) { throw 'UI smoke test produced no results.' }
    $report = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
    $report | ConvertTo-Json -Depth 5
    if (!$report.passed) { throw 'UI smoke test failed.' }
}

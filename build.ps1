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
$argsList = @('/nologo','/target:winexe','/platform:x64','/optimize+',('/out:' + (Join-Path $root 'DeskHooker.exe')))
foreach ($reference in $references) { $argsList += '/reference:' + $reference }
$argsList += @((Join-Path $root 'ReminderCore.cs'),(Join-Path $root 'DeskHooker.cs'))
& $csc @argsList
if ($LASTEXITCODE -ne 0) { throw 'C# build failed.' }
Write-Output "Built: $(Join-Path $root 'DeskHooker.exe')"
if ($Test) {
    $process = Start-Process -FilePath (Join-Path $root 'DeskHooker.exe') -ArgumentList '--ui-test' -PassThru
    if (!$process.WaitForExit(60000)) { $process.Kill(); throw 'UI smoke test timed out.' }
    $result = Join-Path $root 'tests\ui\results.json'
    if (!(Test-Path $result)) { throw 'UI smoke test produced no results.' }
    $report = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
    $report | ConvertTo-Json -Depth 5
    if (!$report.passed) { throw 'UI smoke test failed.' }
}

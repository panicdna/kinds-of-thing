$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# No packages, OS settings, startup changes, or background processes.
# All generated build output stays in the tests directory beside this script.
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw "The .NET Framework C# compiler was not found: $compiler"
}

$outputDirectory = Join-Path $PSScriptRoot 'tests'
[void][System.IO.Directory]::CreateDirectory($outputDirectory)
$executable = Join-Path $outputDirectory 'ReminderCore.Tests.exe'
$sources = @((Join-Path $PSScriptRoot 'ReminderCore.cs'), (Join-Path $PSScriptRoot 'ReminderCore.Tests.cs'))
foreach ($source in $sources) {
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Required source file was not found: $source"
    }
}

Write-Host "PowerShell: $($PSVersionTable.PSVersion)"
Write-Host "Compiler: $compiler (C# 5, warnings as errors)"
Write-Host "Test executable: $executable"
# System.Web.Extensions is a built-in Framework assembly, used only by JSON compatibility tests.
& $compiler /nologo /langversion:5 /warn:4 /warnaserror+ /target:exe /optimize+ /platform:anycpu /reference:System.Web.Extensions.dll "/out:$executable" $sources
if ($LASTEXITCODE -ne 0) {
    throw "Core test compilation failed with exit code $LASTEXITCODE."
}

& $executable
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
exit 0

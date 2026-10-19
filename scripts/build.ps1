<#
.SYNOPSIS
Builds dist\PingTray.exe and runs the tests.

.DESCRIPTION
Uses the C# compiler of .NET Framework 4.8, which is part of Windows 10 and 11, so nothing has
to be installed and the exe runs on any such Windows. That compiler understands C# 5 only:
no $"..." strings, ?. or => members.
The exe is built into obj\ first, and the tests check it the way an update would; only then
is it copied to dist\.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$src = Join-Path $root 'src'
$obj = Join-Path $root 'obj'
$dist = Join-Path $root 'dist'
$exe = Join-Path $dist 'PingTray.exe'
New-Item -ItemType Directory -Force $obj, $dist | Out-Null

$sources = @(Get-ChildItem $src -Filter *.cs | ForEach-Object { $_.FullName })
$tests = @(Get-ChildItem (Join-Path $root 'tests') -Filter *.cs | ForEach-Object { $_.FullName })
$common = @('/nologo', '/codepage:65001', '/optimize+', '/warnaserror+',
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
    '/r:System.Runtime.Serialization.dll')

function Invoke-Csc([string[]] $arguments) {
    & $csc @common @arguments
    if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE." }
}

# The exe's icon is the green tray icon, drawn by the same code.
$ico = Join-Path $obj 'app.ico'
Add-Type -Path (Join-Path $src 'StatusIcons.cs') -ReferencedAssemblies System.Drawing
[PingTray.StatusIcons]::WriteIco($ico, [PingTray.Status]::Good, [int[]](16, 20, 24, 32, 40, 48, 64, 256))

$built = Join-Path $obj 'PingTray.exe'
Invoke-Csc (@('/target:winexe', "/win32manifest:$src\app.manifest", "/win32icon:$ico", "/out:$built") + $sources)

$testExe = Join-Path $obj 'Tests.exe'
Invoke-Csc (@('/target:exe', '/main:PingTray.Tests.TestMain', "/out:$testExe") + $sources + $tests)
& $testExe $built
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

if (Test-Path $exe) {
    try { [IO.File]::Open($exe, 'Open', 'ReadWrite', 'None').Close() }
    catch { throw "$exe is in use. Exit Ping Tray (right-click its icon, Exit), then build again." }
}
Copy-Item $built $exe -Force
Write-Host "Built $exe"

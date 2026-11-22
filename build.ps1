# Builds Deblowin.exe with the C# compiler that ships with Windows (.NET Framework 4.8).
# No SDK or Visual Studio needed.
#
#   .\build.ps1                 -> dist\Deblowin.exe (asks for admin on launch)
#   .\build.ps1 -AsInvoker      -> dist\Deblowin-user.exe (no UAC prompt; for UI testing)
#   .\build.ps1 -UpdateCatalog C:\path\to\winscript   -> re-extract data\catalog.json first (needs Node.js)

param(
    [switch]$AsInvoker,
    [string]$UpdateCatalog
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc (.NET Framework 4.8 required)" }

if ($UpdateCatalog) {
    node (Join-Path $root 'tools\extract-catalog.mjs') $UpdateCatalog
    if ($LASTEXITCODE -ne 0) { throw 'catalog extraction failed' }
}

$dist = Join-Path $root 'dist'
$obj = Join-Path $root 'obj'
New-Item -ItemType Directory -Force $dist, $obj | Out-Null

$level = if ($AsInvoker) { 'asInvoker' } else { 'requireAdministrator' }
$name = if ($AsInvoker) { 'Deblowin-user.exe' } else { 'Deblowin.exe' }
$manifest = Join-Path $obj "app.$level.manifest"
(Get-Content (Join-Path $root 'src\app.manifest') -Raw).Replace('$LEVEL$', $level) |
    Set-Content -Path $manifest -Encoding UTF8

$wpf = Join-Path $fw 'WPF'
$refs = @(
    (Join-Path $wpf 'PresentationFramework.dll'),
    (Join-Path $wpf 'PresentationCore.dll'),
    (Join-Path $wpf 'WindowsBase.dll'),
    (Join-Path $fw 'System.Xaml.dll'),
    (Join-Path $fw 'System.Web.Extensions.dll'),
    'System.dll', 'System.Core.dll'
) | ForEach-Object { "/r:$_" }

$iconArg = @()
$icon = Join-Path $root 'src\app.ico'
if (Test-Path $icon) { $iconArg = @("/win32icon:$icon") }

$cscArgs = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/langversion:5',
    "/out:$(Join-Path $dist $name)",
    "/win32manifest:$manifest",
    "/resource:$(Join-Path $root 'data\catalog.json'),catalog.json",
    "/resource:$(Join-Path $root 'src\Theme.xaml'),Theme.xaml"
) + $iconArg + $refs + @(
    (Join-Path $root 'src\AssemblyInfo.cs'),
    (Join-Path $root 'src\Core.cs'),
    (Join-Path $root 'src\MainWindow.cs')
)

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }
$out = Get-Item (Join-Path $dist $name)
"Built {0} ({1:N0} KB)" -f $out.FullName, ($out.Length / 1KB)

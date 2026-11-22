# Builds Deblowin with the C# compiler that ships with Windows (.NET Framework 4.8).
# No SDK or Visual Studio needed.
#
#   .\build.ps1                 -> dist\Deblowin.exe (portable) and dist\Deblowin-Setup-<version>.exe (installer)
#   .\build.ps1 -AsInvoker      -> same, without the UAC prompt (for UI testing only; tweaks need admin)
#   .\build.ps1 -UpdateCatalog C:\path\to\source   -> re-extract data\catalog.json first (needs Node.js)

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

$version = ([regex]'AssemblyVersion\("(\d+\.\d+\.\d+)').Match((Get-Content (Join-Path $root 'src\AssemblyInfo.cs') -Raw)).Groups[1].Value
$level = if ($AsInvoker) { 'asInvoker' } else { 'requireAdministrator' }
$suffix = if ($AsInvoker) { '-user' } else { '' }
$manifest = Join-Path $obj "app.$level.manifest"
(Get-Content (Join-Path $root 'src\app.manifest') -Raw).Replace('$LEVEL$', $level) |
    Set-Content -Path $manifest -Encoding UTF8

$icon = Join-Path $root 'src\app.ico'
if (-not (Test-Path $icon)) { & (Join-Path $root 'tools\make-icon.ps1') | Out-Null }

$wpf = Join-Path $fw 'WPF'
$refs = @(
    (Join-Path $wpf 'PresentationFramework.dll'),
    (Join-Path $wpf 'PresentationCore.dll'),
    (Join-Path $wpf 'WindowsBase.dll'),
    (Join-Path $fw 'System.Xaml.dll'),
    'System.dll', 'System.Core.dll'
)
$common = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/langversion:5',
    "/win32manifest:$manifest", "/win32icon:$icon")

function Invoke-Csc([string[]]$cscArgs) {
    & $csc @cscArgs
    if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }
}

# ---- app
$app = Join-Path $dist "Deblowin$suffix.exe"
Invoke-Csc ($common + @(
    "/out:$app",
    "/resource:$(Join-Path $root 'data\catalog.json'),catalog.json",
    "/resource:$(Join-Path $root 'src\Theme.xaml'),Theme.xaml",
    "/resource:$(Join-Path $root 'NOTICE'),NOTICE"
) + (($refs + (Join-Path $fw 'System.Web.Extensions.dll')) | ForEach-Object { "/r:$_" }) + @(
    (Join-Path $root 'src\AssemblyInfo.cs'),
    (Join-Path $root 'src\Core.cs'),
    (Join-Path $root 'src\MainWindow.cs')
))

# ---- installer (embeds the app exe)
$setup = Join-Path $dist "Deblowin-Setup-$version$suffix.exe"
Invoke-Csc ($common + @(
    "/out:$setup",
    "/resource:$app,payload.exe",
    "/resource:$(Join-Path $root 'LICENSE'),LICENSE",
    "/resource:$(Join-Path $root 'NOTICE'),NOTICE",
    "/resource:$(Join-Path $root 'src\Theme.xaml'),Theme.xaml",
    "/resource:$icon,app.ico"
) + (($refs + 'System.Windows.Forms.dll', 'Microsoft.CSharp.dll') | ForEach-Object { "/r:$_" }) + @(
    (Join-Path $root 'src\AssemblyInfo.cs'),
    (Join-Path $root 'src\Setup\Setup.cs')
))

Remove-Item -Recurse -Force $obj
foreach ($f in $app, $setup) { "Built {0} ({1:N0} KB)" -f $f, ((Get-Item $f).Length / 1KB) }

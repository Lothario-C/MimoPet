# Build MimoPet.exe
#
# Uses the C# compiler that ships with .NET Framework 4.x (present on every
# Windows 10/11 machine), so no SDK or runtime install is required to build.
#
#   powershell -ExecutionPolicy Bypass -File build.ps1
#
# Output: dist\MimoPet.exe

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
$out  = Join-Path $dist 'MimoPet.exe'

# Locate csc.exe. Prefer the 64-bit .NET Framework 4.x compiler.
$csc = $null
$candidates = @(
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
    'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
)
foreach ($c in $candidates) {
    if (Test-Path $c) { $csc = $c; break }
}

if (-not $csc) {
    # Fall back to whatever framework directory exists
    $found = Get-ChildItem 'C:\Windows\Microsoft.NET' -Filter csc.exe -Recurse -ErrorAction SilentlyContinue |
             Where-Object { $_.FullName -match 'v4\.' } |
             Select-Object -First 1
    if ($found) { $csc = $found.FullName }
}

if (-not $csc) {
    Write-Error 'Could not find the .NET Framework 4.x C# compiler (csc.exe).'
    exit 1
}

Write-Host "Compiler: $csc"

if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

# 按依赖顺序列出：主题 → 控件 → 业务 → 界面
$files = @(
    'Asar.cs',
    'SkinPatcher.cs',
    'SkinConfig.cs',
    'PetState.cs',
    'CssBuilder.cs',
    'Wallpaper.cs',
    'MiMoApp.cs',
    'Theme.cs',
    'Controls.cs',
    'Preview.cs',
    'PetForm.cs',
    'PanelForm.cs',
    'GatewaySource.cs',
    'TokenMonitor.cs',
    'BalanceSource.cs',
    'BalancePanel.cs',
    'ThinkSource.cs',
    'ThinkPatch.cs',
    'ThinkPanel.cs',
    'WallpaperNewLayout.cs',
    'BubbleForm.cs',
    'ToolboxForm.cs',
    'Program.cs'
) | ForEach-Object { Join-Path $src $_ }

# 桌宠贴图作为嵌入资源打进 exe，保持单文件分发
$petPng = Join-Path $src 'pet.png'
if (-not (Test-Path $petPng)) {
    Write-Error "Missing pet sprite: $petPng"
    exit 1
}

# /langversion:5 is required: the in-box compiler predates C# 6 syntax.
$args = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/langversion:5',
    "/out:$out",
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    "/resource:$petPng,MiMoSkin.pet.png",

    "/resource:$src\think-patch.mjs,MiMoSkin.think-patch.mjs",
    "/resource:$src\efforts.json,MiMoSkin.efforts.json"
) + $files

& $csc $args

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

$info = Get-Item $out
Write-Host ''
Write-Host "Built: $($info.FullName)" -ForegroundColor Green
Write-Host "Size : $([math]::Round($info.Length / 1KB, 1)) KB"

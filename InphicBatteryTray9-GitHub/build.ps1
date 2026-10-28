# 编译 InphicBatteryTray9 (只需要 Windows 自带的 .NET Framework)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { throw "找不到 csc.exe，请确认已安装 .NET Framework 4.x" }

New-Item -ItemType Directory -Force -Path "$root\dist" | Out-Null
$src = Get-ChildItem "$root\src" -Filter *.cs | ForEach-Object FullName

& $csc /nologo /target:winexe /platform:x64 /codepage:65001 `
    /win32manifest:"$root\app.manifest" `
    "/out:$root\dist\InphicBatteryTray9.exe" `
    /reference:System.dll /reference:System.Windows.Forms.dll `
    /reference:System.Drawing.dll /reference:System.Core.dll `
    $src

if ($LASTEXITCODE -ne 0) { throw "编译失败 (exit $LASTEXITCODE)" }

$exe = "$root\dist\InphicBatteryTray9.exe"
Write-Host ("[OK] " + $exe + "  (" + [math]::Round((Get-Item $exe).Length / 1KB) + " KB)")
Write-Host "提示: 运行 --test 可先确认能否读到电量；--notify-test 可预览静默提醒。"

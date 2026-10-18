# 一键编译 BrotatoTrainer.exe（无需 Visual Studio，使用系统自带 csc）
# 用法: .\build.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw 'csc.exe not found: ' + $csc }

$csPath = Join-Path $root 'src\Program.cs'
$gdPath = Join-Path $root 'src\trainer.gd'
$cs = [System.IO.File]::ReadAllText($csPath, [System.Text.Encoding]::UTF8)
$gd = [System.IO.File]::ReadAllText($gdPath, [System.Text.Encoding]::UTF8)

# 把 trainer.gd 转义后内嵌为 C# 逐字字符串
$gdEscaped = $gd.Replace('"', '""')

$marker = 'public static readonly string TrainerScript = @"'
$start = $cs.IndexOf($marker)
if ($start -lt 0) { throw 'marker not found in Program.cs' }
$start = $start + $marker.Length
$end = $cs.IndexOf('"' + ';', $start)
if ($end -lt 0) { throw 'closing quote not found' }

$merged = $cs.Substring(0, $start) + $gdEscaped + $cs.Substring($end)

$tmp = Join-Path $env:TEMP ('bt_' + [guid]::NewGuid().ToString('N') + '.cs')
[System.IO.File]::WriteAllText($tmp, $merged, (New-Object System.Text.UTF8Encoding($false)))

$outDir = Join-Path $root 'releases'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
$out = Join-Path $outDir 'BrotatoTrainer.exe'

$refs = @(
    '/reference:System.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Core.dll'
)
$args = @('/nologo', '/target:winexe', ('/out:' + $out), '/platform:anycpu', '/optimize+') + $refs + @($tmp)

& $csc $args
$code = $LASTEXITCODE
Remove-Item $tmp -Force -ErrorAction SilentlyContinue

if ($code -ne 0) { throw ('compile failed, exit code ' + $code) }
if (-not (Test-Path $out)) { throw 'exe not produced' }
$size = (Get-Item $out).Length
Write-Host ('OK: ' + $out + ' (' + $size + ' bytes)') -ForegroundColor Green
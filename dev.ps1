# Hot reload for development. Run from the repo root:  .\dev.ps1
# - UI files (src\web): served from disk, the open page reloads itself when one changes.
# - Go files (src\*.go): the app is rebuilt and restarted automatically.
$ErrorActionPreference = 'Stop'
$go = (Get-Command go -ErrorAction SilentlyContinue).Source
if (-not $go) { $go = 'C:\Program Files\Go\bin\go.exe' }
$src = Join-Path $PSScriptRoot 'src'
$exe = Join-Path $env:TEMP 'toolbox-dev.exe'
$port = 49200
function Stamp { (Get-ChildItem $src -Filter *.go -File | Measure-Object LastWriteTime -Maximum).Maximum }
$proc = $null; $seen = $null
try {
  while ($true) {
    $now = Stamp
    if ($now -ne $seen) {
      $seen = $now
      if ($proc -and -not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit() }
      Push-Location $src
      & $go build -o $exe . 
      $ok = $LASTEXITCODE -eq 0
      Pop-Location
      if ($ok) { $proc = Start-Process $exe -ArgumentList '-dev', (Join-Path $src 'web'), '-port', $port, '-nobrowser' -PassThru; Write-Host "Running on http://127.0.0.1:$port/ (UI hot reload on)" }
      else { Write-Host 'Build failed; fix the error and save.' -ForegroundColor Red }
    }
    Start-Sleep -Milliseconds 700
  }
} finally { if ($proc -and -not $proc.HasExited) { $proc.Kill() } }

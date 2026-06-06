$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSCommandPath
$source = Join-Path $root 'native\SimpleProgram.cs'
$output = Join-Path $root 'DesktopPetMVP.exe'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
  $compiler = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
}

if (-not (Test-Path -LiteralPath $compiler)) {
  throw 'Could not find the .NET Framework C# compiler.'
}

Get-Process DesktopPetMVP -ErrorAction SilentlyContinue | Stop-Process -Force

& $compiler `
  /target:winexe `
  /out:$output `
  /reference:System.dll `
  /reference:System.Drawing.dll `
  /reference:System.Windows.Forms.dll `
  $source

if ($LASTEXITCODE -ne 0) {
  throw 'Build failed.'
}

Write-Host "Built: $output"

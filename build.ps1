$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSCommandPath
$source = Join-Path $root 'native\WpfProgram.cs'
$output = Join-Path $root 'DesktopPetMVP.exe'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$systemXaml = Join-Path $framework 'System.Xaml.dll'
$windowsBase = Join-Path $wpf 'WindowsBase.dll'
$presentationCore = Join-Path $wpf 'PresentationCore.dll'
$presentationFramework = Join-Path $wpf 'PresentationFramework.dll'

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
  /reference:System.Core.dll `
  /reference:$systemXaml `
  /reference:$windowsBase `
  /reference:$presentationCore `
  /reference:$presentationFramework `
  $source

if ($LASTEXITCODE -ne 0) {
  throw 'Build failed.'
}

Write-Host "Built: $output"

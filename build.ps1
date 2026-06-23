param(
  [switch]$KillRunning,
  [switch]$RunTests
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSCommandPath
$sources = Get-ChildItem -LiteralPath (Join-Path $root 'native') -Filter '*.cs' | Sort-Object FullName | ForEach-Object { $_.FullName }
$output = Join-Path $root 'DesktopPetMVP.exe'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$systemXaml = Join-Path $framework 'System.Xaml.dll'
$systemWebExtensions = Join-Path $framework 'System.Web.Extensions.dll'
$systemSecurity = Join-Path $framework 'System.Security.dll'
$windowsBase = Join-Path $wpf 'WindowsBase.dll'
$presentationCore = Join-Path $wpf 'PresentationCore.dll'
$presentationFramework = Join-Path $wpf 'PresentationFramework.dll'

if (-not (Test-Path -LiteralPath $compiler)) {
  $compiler = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
}

if (-not (Test-Path -LiteralPath $compiler)) {
  throw 'Could not find the .NET Framework C# compiler.'
}

$running = Get-Process DesktopPetMVP -ErrorAction SilentlyContinue
if ($running) {
  if ($KillRunning) {
    $running | Stop-Process -Force
  } else {
    Write-Warning 'DesktopPetMVP is running. Close it first, or rerun with -KillRunning if the output file is locked.'
  }
}

$compilerArgs = @(
  '/target:winexe',
  "/out:$output",
  '/reference:System.dll',
  '/reference:System.Core.dll',
  "/reference:$systemXaml",
  "/reference:$systemWebExtensions",
  "/reference:$systemSecurity",
  "/reference:$windowsBase",
  "/reference:$presentationCore",
  "/reference:$presentationFramework"
) + $sources

& $compiler @compilerArgs

if ($LASTEXITCODE -ne 0) {
  throw 'Build failed.'
}

Write-Host "Built: $output"

if ($RunTests) {
  $testSource = Join-Path $root 'tests\RiskAnalyzerTests.cs'
  if (-not (Test-Path -LiteralPath $testSource)) {
    throw 'Could not find RiskAnalyzerTests.cs.'
  }

  $testDir = Join-Path $root '.localappdata'
  if (-not (Test-Path -LiteralPath $testDir)) {
    New-Item -ItemType Directory -Path $testDir | Out-Null
  }

  $testOutput = Join-Path $testDir 'RiskAnalyzerTests.exe'
  $riskAnalyzerSource = Join-Path $root 'native\RiskAnalyzer.cs'
  & $compiler `
    /target:exe `
    /out:$testOutput `
    /reference:System.dll `
    /reference:System.Core.dll `
    $riskAnalyzerSource `
    $testSource

  if ($LASTEXITCODE -ne 0) {
    throw 'Risk analyzer test build failed.'
  }

  & $testOutput
  if ($LASTEXITCODE -ne 0) {
    throw 'Risk analyzer tests failed.'
  }
}

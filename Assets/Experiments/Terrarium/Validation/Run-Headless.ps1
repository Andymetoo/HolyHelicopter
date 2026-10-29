[CmdletBinding()]
param(
    [int]$Ticks = 12000,
    [int[]]$Seeds = @(7, 19, 42, 103, 211, 509, 997, 2026),
    [string]$OutputDirectory = '',
    [int]$TraceSeed = 7,
    [int]$TraceTicks = 2000,
    [switch]$SkipStress,
    [switch]$CompileOnly
)

$ErrorActionPreference = 'Stop'
$experimentDirectory = Split-Path -Parent $PSScriptRoot
$workspaceDirectory = [IO.Path]::GetFullPath((Join-Path $experimentDirectory '../../..'))
$buildDirectory = Join-Path $workspaceDirectory 'Temp/Terrarium/Headless'
New-Item -ItemType Directory -Force -Path $buildDirectory | Out-Null
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $workspaceDirectory 'Temp/Terrarium/results'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

# Invoke the SDK compiler directly: no project generation, NuGet restore, Unity process,
# global dotnet configuration, or dependency download is necessary.
$dotnetCommand = Get-Command dotnet -ErrorAction Stop
$sdkLines = @(& $dotnetCommand.Source --list-sdks)
if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate installed .NET SDKs.' }
$compiler = $null
foreach ($sdkLine in $sdkLines) {
    if ($sdkLine -match '^([^ ]+) \[([^\]]+)\]$') {
        $candidateCompiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
        if (Test-Path -LiteralPath $candidateCompiler) { $compiler = $candidateCompiler }
    }
}
if (-not $compiler) { throw 'An installed .NET SDK with Roslyn csc.dll is required.' }
$frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$referenceNames = @('mscorlib.dll', 'System.dll', 'System.Core.dll')
$compilerArguments = @($compiler, '/nologo', '/noconfig', '/nostdlib+', '/target:exe', '/optimize+', '/langversion:latest', '/define:TERRARIUM_HEADLESS')
$executable = Join-Path $buildDirectory 'Terrarium.Headless.exe'
$compilerArguments += '/out:' + $executable
foreach ($referenceName in $referenceNames) {
    $referencePath = Join-Path $frameworkDirectory $referenceName
    if (-not (Test-Path -LiteralPath $referencePath)) { throw ('Missing .NET Framework reference: ' + $referencePath) }
    $compilerArguments += '/reference:' + $referencePath
}
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $experimentDirectory 'Core') -Filter '*.cs' -Recurse | Sort-Object FullName)
$sourceFiles += @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -Recurse | Sort-Object FullName)
$compilerArguments += @($sourceFiles | ForEach-Object { $_.FullName })
$compiledSourceHashes = New-Object System.Collections.Generic.List[string]
foreach ($sourceFile in $sourceFiles) {
    $relativePath = $sourceFile.FullName.Substring($workspaceDirectory.Length + 1).Replace('\', '/')
    $sourceHash = Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256
    $compiledSourceHashes.Add($sourceHash.Hash.ToLowerInvariant() + '  ' + $relativePath)
}
$runnerHash = Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256
$compiledSourceHashes.Add($runnerHash.Hash.ToLowerInvariant() + '  Assets/Experiments/Terrarium/Validation/Run-Headless.ps1')
& $dotnetCommand.Source @compilerArguments
if ($LASTEXITCODE -ne 0) { throw ('Terrarium compilation failed with exit code ' + $LASTEXITCODE) }
Write-Output ('Compiled rendering-independent simulation: ' + $executable)
if ($CompileOnly) { return }

$runArguments = @('--ticks', $Ticks.ToString(), '--seeds', ($Seeds -join ','), '--output', $OutputDirectory, '--trace-seed', $TraceSeed.ToString(), '--trace-ticks', $TraceTicks.ToString())
if ($SkipStress) { $runArguments += '--skip-stress' }
& $executable @runArguments
if ($LASTEXITCODE -ne 0) { throw ('Terrarium validation failed with exit code ' + $LASTEXITCODE) }

$manifest = New-Object System.Collections.Generic.List[string]
$manifest.Add('Terrarium compiled-source manifest (SHA-256)')
$manifest.Add('Arguments: ' + ($runArguments -join ' '))
$manifest.Add('Compiler: ' + $compiler)
$manifest.Add('Framework references: ' + $frameworkDirectory)
$manifest.Add('PowerShell: ' + $PSVersionTable.PSVersion.ToString())
$manifest.Add('')
foreach ($compiledSourceHash in $compiledSourceHashes) { $manifest.Add($compiledSourceHash) }
[IO.File]::WriteAllLines((Join-Path $OutputDirectory 'source-manifest.txt'), $manifest, (New-Object Text.UTF8Encoding($false)))

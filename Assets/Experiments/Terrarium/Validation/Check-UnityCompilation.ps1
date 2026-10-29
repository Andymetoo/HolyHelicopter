param(
    [string]$UnityEditorRoot = '',
    [string]$BuildDirectory = ''
)
$ErrorActionPreference = 'Stop'
$experimentRoot = Split-Path $PSScriptRoot -Parent
$projectRoot = [IO.Path]::GetFullPath((Join-Path $experimentRoot '../../..'))
if ([string]::IsNullOrWhiteSpace($BuildDirectory)) { $BuildDirectory = Join-Path $projectRoot 'Temp/Terrarium/static-compile' }
$buildPath = [IO.Path]::GetFullPath($BuildDirectory)
New-Item -ItemType Directory -Force -Path $buildPath | Out-Null
if ([string]::IsNullOrWhiteSpace($UnityEditorRoot)) {
    $editors = @(Get-ChildItem 'C:/Program Files/Unity/Hub/Editor' -Directory | Sort-Object Name -Descending)
    if ($editors.Count -eq 0) { throw 'Specify -UnityEditorRoot pointing to an installed Unity editor folder.' }
    $UnityEditorRoot = $editors[0].FullName
}
$managedPath = Join-Path $UnityEditorRoot 'Editor/Data/Managed/UnityEngine'
$frameworkPath = Join-Path $UnityEditorRoot 'Editor/Data/MonoBleedingEdge/lib/mono/4.8-api'
if (!(Test-Path -LiteralPath $managedPath) -or !(Test-Path -LiteralPath $frameworkPath)) { throw 'Unity managed reference assemblies were not found.' }
$sdkLine = @(& dotnet --list-sdks)[-1]
if ($sdkLine -notmatch '^([\d.]+) \[(.+)\]') { throw 'A .NET SDK is required for the standalone C# compiler.' }
$compiler = Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn/bincore/csc.dll'
$references = @('mscorlib.dll', 'System.dll', 'System.Core.dll') | ForEach-Object { '/reference:"' + (Join-Path $frameworkPath $_) + '"' }
$references += Get-ChildItem (Join-Path $frameworkPath 'Facades') -Filter '*.dll' | ForEach-Object { '/reference:"' + $_.FullName + '"' }
$coreOutput = Join-Path $buildPath 'Terrarium.Core.dll'
$coreArgs = @('/nologo', '/nostdlib+', '/target:library', '/langversion:7.3', '/warnaserror+', ('/out:"' + $coreOutput + '"'))
$coreArgs += $references
$coreArgs += Get-ChildItem (Join-Path $experimentRoot 'Core') -Filter '*.cs' | ForEach-Object { '"' + $_.FullName + '"' }
$coreResponse = Join-Path $buildPath 'core.rsp'
[IO.File]::WriteAllLines($coreResponse, [string[]]$coreArgs)
& dotnet $compiler /noconfig ('@' + $coreResponse)
if ($LASTEXITCODE -ne 0) { throw 'Core static compilation failed.' }
$uiArgs = @('/nologo', '/nostdlib+', '/target:library', '/langversion:7.3', '/warnaserror+', ('/out:"' + (Join-Path $buildPath 'Terrarium.Presentation.dll') + '"'))
$uiArgs += $references
$uiArgs += '/reference:"' + $coreOutput + '"'
$uiArgs += Get-ChildItem $managedPath -Filter 'UnityEngine*.dll' | ForEach-Object { '/reference:"' + $_.FullName + '"' }
$uiArgs += Get-ChildItem (Join-Path $experimentRoot 'Presentation') -Filter '*.cs' | ForEach-Object { '"' + $_.FullName + '"' }
$uiResponse = Join-Path $buildPath 'presentation.rsp'
[IO.File]::WriteAllLines($uiResponse, [string[]]$uiArgs)
& dotnet $compiler /noconfig ('@' + $uiResponse)
if ($LASTEXITCODE -ne 0) { throw 'Observer static compilation failed.' }
Write-Output ('Static compilation passed against: ' + $UnityEditorRoot)
Write-Output 'This compiles code only. Unity scene import, visual layout and Play Mode remain untested.'

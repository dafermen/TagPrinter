$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
& dotnet build (Join-Path $projectRoot 'TagPrinter.sln') -c Release
if($LASTEXITCODE -ne 0){throw 'Build failed.'}
$dll=Join-Path $projectRoot 'src/TagPrinter/bin/Release/net8.0-windows/TagPrinter.dll'
& dotnet $dll --self-test
if($LASTEXITCODE -ne 0){throw 'Verification failed.'}
Get-Content (Join-Path (Split-Path $dll) 'verification/results.txt')

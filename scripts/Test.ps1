$ErrorActionPreference='Stop'
# Resolve the repository from the script location, so callers can run this from any working directory.
$projectRoot=Split-Path -Parent $PSScriptRoot
# Compile Release before verification; stop on failure instead of running an older executable.
& dotnet build (Join-Path $projectRoot 'TagPrinter.sln') -c Release
if($LASTEXITCODE -ne 0){throw 'Build failed.'}
$dll=Join-Path $projectRoot 'src/TagPrinter/bin/Release/net8.0-windows/TagPrinter.dll'
# Execute managed self-tests with isolated data. No test sends printer jobs.
& dotnet $dll --self-test
if($LASTEXITCODE -ne 0){throw 'Verification failed.'}
# Print the human-readable verification summary.
Get-Content (Join-Path (Split-Path $dll) 'verification/results.txt')

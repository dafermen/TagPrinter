param([Parameter(Mandatory=$true)][string]$PortablePath)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
# Only mutate a newly created test copy. Never modify the original portable or user-data paths.
$testPath=Join-Path $projectRoot ('artifacts/integrity-tests/'+[guid]::NewGuid().ToString()+'/package with spaces')
New-Item -ItemType Directory -Path $testPath -Force | Out-Null
foreach($name in @('TagPrinter.exe','integrity-manifest.xml','config.json')){Copy-Item -LiteralPath (Join-Path $PortablePath $name) -Destination (Join-Path $testPath $name)}
[xml]$manifest=Get-Content -LiteralPath (Join-Path $PortablePath 'integrity-manifest.xml') -Raw
foreach($entry in $manifest.package.file) {
    $destination=Join-Path $testPath $entry.path
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PortablePath $entry.path) -Destination $destination
}
function Verify-Package([int]$Expected,[string]$Name,[string]$ExpectedText='') {
    $report=Join-Path $testPath 'verification-report.txt'
    $process=Start-Process -FilePath (Join-Path $testPath 'TagPrinter.exe') -ArgumentList @('--verify-only','--report',('"'+$report+'"')) -WindowStyle Hidden -PassThru -Wait
    if($process.ExitCode -ne $Expected){throw "Integrity case failed: $Name, exit $($process.ExitCode)"}
    $details=Get-Content -LiteralPath $report -Raw
    if($ExpectedText -and -not $details.Contains($ExpectedText)){throw "Missing diagnostic for $Name"}
    Write-Output "PASS: $Name"
}
Verify-Package 0 'complete package in a path with spaces'
foreach($relative in @('app/e_sqlite3.dll','app/hostfxr.dll','app/TagPrinter.dll','integrity-manifest.xml','config.json')) {
    $path=Join-Path $testPath $relative
    Move-Item -LiteralPath $path -Destination ($path+'.test-backup')
    try { Verify-Package 1 ('missing '+$relative) $relative } finally { Move-Item -LiteralPath ($path+'.test-backup') -Destination $path }
}
$library=Join-Path $testPath 'app/coreclr.dll'
$bytes=[System.IO.File]::ReadAllBytes($library)
try {
    $changed=[byte[]]$bytes.Clone();$changed[100]=$changed[100] -bxor 1
    [System.IO.File]::WriteAllBytes($library,$changed)
    Verify-Package 1 'same-length runtime corruption' 'app/coreclr.dll'
} finally { [System.IO.File]::WriteAllBytes($library,$bytes) }
$manifestPath=Join-Path $testPath 'integrity-manifest.xml'
$original=[System.IO.File]::ReadAllBytes($manifestPath)
try { Add-Content -LiteralPath $manifestPath -Value ' ';Verify-Package 1 'changed manifest' 'integrity-manifest.xml' } finally { [System.IO.File]::WriteAllBytes($manifestPath,$original) }
$configPath=Join-Path $testPath 'config.json'
$original=[System.IO.File]::ReadAllBytes($configPath)
try {
    Add-Content -LiteralPath $configPath -Value ' '
    Verify-Package 0 'legitimate editable configuration change'
    [System.IO.File]::WriteAllText($configPath,'{invalid')
    Verify-Package 1 'malformed editable configuration'
} finally { [System.IO.File]::WriteAllBytes($configPath,$original) }
Verify-Package 0 'restored package'

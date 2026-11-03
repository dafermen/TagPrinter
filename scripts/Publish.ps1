$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$project=Join-Path $projectRoot 'src/TagPrinter/TagPrinter.csproj'
# Read the project version; derive the folder/ZIP name from major and minor parts.
[xml]$projectXml=Get-Content -LiteralPath $project
$version=[string]$projectXml.Project.PropertyGroup.Version
$shortVersion=($version -split '\.')[0..1] -join '.'
$publishPath=Join-Path $projectRoot "artifacts/portable/TagPrinter-v$shortVersion-win-x64"
# Use the self-contained Windows x64 profile: the portable includes its .NET runtime.
$appPath=Join-Path $publishPath 'app'
& dotnet publish $project -p:PublishProfile=Portable -o $appPath
if($LASTEXITCODE -ne 0){throw 'Publish failed.'}
# Keep editable configuration at the portable root, outside immutable payload hashes.
Copy-Item -LiteralPath (Join-Path $appPath 'config.json') -Destination (Join-Path $publishPath 'config.json') -Force
Remove-Item -LiteralPath (Join-Path $appPath 'config.json') -Force
& (Join-Path $PSScriptRoot 'Build-Launcher.ps1') -PortablePath $publishPath
# Verify through the same root launcher users open; --self-test never accesses the live user database.
$result=Start-Process -FilePath (Join-Path $publishPath 'TagPrinter.exe') -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -Wait
if($result.ExitCode -ne 0){Get-Content (Join-Path $appPath 'verification/self-test-error.txt') -ErrorAction SilentlyContinue;throw 'Portable launcher/application verification failed.'}
& (Join-Path $PSScriptRoot 'Test-Integrity.ps1') -PortablePath $publishPath
# Ship the project license and user-facing guides alongside the executable.
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $publishPath 'LICENSE') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/USER_GUIDE.md') -Destination (Join-Path $publishPath 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/CONFIGURATION.md') -Destination (Join-Path $publishPath 'CONFIGURATION.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/HISTORY.md') -Destination (Join-Path $publishPath 'HISTORY.md') -Force
# Include all English manuals and their images so links in the portable guide work offline.
$manuals=Get-ChildItem -LiteralPath (Join-Path $projectRoot 'docs') -File | Where-Object Name -ne 'README.md'
$manuals | Copy-Item -Destination $publishPath -Force
$imagePath=Join-Path $publishPath 'images'
New-Item -ItemType Directory -Path $imagePath -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'docs/images') -File | Copy-Item -Destination $imagePath -Force
$instructions=@('TagPrinter portable - Windows x64','Extract ALL files and run TagPrinter.exe. .NET is included.','Extrae TODOS los archivos y abre TagPrinter.exe. Incluye .NET.','Export Tag = full label / etiqueta completa.','Export Code = QR or barcode only / solo QR o codigo de barras.','Install the Windows printer driver / Instala el controlador de impresora.','RFID encoding is not enabled / No incluye grabacion del chip RFID.','Start TagPrinter.exe at the ROOT; it validates app/ before loading.','Open config.json at the ROOT to customize presets.','Startup logs: %LOCALAPPDATA%/TagPrinter/logs.')
$instructions | Set-Content -LiteralPath (Join-Path $publishPath 'START-HERE.txt')
$zipPath=Join-Path $projectRoot "artifacts/TagPrinter-v$shortVersion-PORTABLE-Windows-x64.zip"
# Exclude synthetic verification files/databases from the distributable ZIP.
# Package the complete runtime folder; distributing only TagPrinter.exe would not work.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipStream=[System.IO.File]::Open($zipPath,[System.IO.FileMode]::Create)
$archive=[System.IO.Compression.ZipArchive]::new($zipStream,[System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach($file in (Get-ChildItem -LiteralPath $publishPath -File -Recurse | Where-Object { $_.FullName -notlike '*\verification\*' })) {
        $entry=$file.FullName.Substring($publishPath.TrimEnd('\').Length+1).Replace('\','/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$entry) | Out-Null
    }
} finally {$archive.Dispose();$zipStream.Dispose()}
Get-Content (Join-Path $appPath 'verification/results.txt')
Write-Output "Portable: $zipPath"

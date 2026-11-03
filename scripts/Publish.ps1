$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$project=Join-Path $projectRoot 'src/TagPrinter/TagPrinter.csproj'
# Read the project version; derive the folder/ZIP name from major and minor parts.
[xml]$projectXml=Get-Content -LiteralPath $project
$version=[string]$projectXml.Project.PropertyGroup.Version
$shortVersion=($version -split '\.')[0..1] -join '.'
$publishPath=Join-Path $projectRoot "artifacts/portable/TagPrinter-v$shortVersion-win-x64"
# Use the self-contained Windows x64 profile: the portable includes its .NET runtime.
& dotnet publish $project -p:PublishProfile=Portable
if($LASTEXITCODE -ne 0){throw 'Publish failed.'}
# Test the published native executable, not only the development DLL. Keep the helper window hidden.
$result=Start-Process -FilePath (Join-Path $publishPath 'TagPrinter.exe') -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -Wait
if($result.ExitCode -ne 0){throw 'Portable verification failed.'}
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
$instructions=@('TagPrinter portable - Windows x64','Extract ALL files and run TagPrinter.exe. .NET is included.','Extrae TODOS los archivos y abre TagPrinter.exe. Incluye .NET.','Export Tag = full label / etiqueta completa.','Export Code = QR or barcode only / solo QR o codigo de barras.','Install the Windows printer driver / Instala el controlador de impresora.','RFID encoding is not enabled / No incluye grabacion del chip RFID.')
$instructions | Set-Content -LiteralPath (Join-Path $publishPath 'START-HERE.txt')
$zipPath=Join-Path $projectRoot "artifacts/TagPrinter-v$shortVersion-PORTABLE-Windows-x64.zip"
# Exclude synthetic verification files/databases from the distributable ZIP.
$files=Get-ChildItem -LiteralPath $publishPath | Where-Object Name -ne 'verification' | Select-Object -ExpandProperty FullName
# Package the complete runtime folder; distributing only TagPrinter.exe would not work.
Compress-Archive -Path $files -DestinationPath $zipPath -Force
Get-Content (Join-Path $publishPath 'verification/results.txt')
Write-Output "Portable: $zipPath"

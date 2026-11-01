$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$project=Join-Path $projectRoot 'src/TagPrinter/TagPrinter.csproj'
[xml]$projectXml=Get-Content -LiteralPath $project
$version=[string]$projectXml.Project.PropertyGroup.Version
$shortVersion=($version -split '\.')[0..1] -join '.'
$publishPath=Join-Path $projectRoot "artifacts/portable/TagPrinter-v$shortVersion-win-x64"
& dotnet publish $project -p:PublishProfile=Portable
if($LASTEXITCODE -ne 0){throw 'Publish failed.'}
$result=Start-Process -FilePath (Join-Path $publishPath 'TagPrinter.exe') -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -Wait
if($result.ExitCode -ne 0){throw 'Portable verification failed.'}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/USER_GUIDE.md') -Destination (Join-Path $publishPath 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/CONFIGURATION.md') -Destination (Join-Path $publishPath 'CONFIGURATION.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/HISTORY.md') -Destination (Join-Path $publishPath 'HISTORY.md') -Force
$instructions=@('TagPrinter portable - Windows x64','Extract ALL files and run TagPrinter.exe. .NET is included.','Extrae TODOS los archivos y abre TagPrinter.exe. Incluye .NET.','Export Tag = full label / etiqueta completa.','Export Code = QR or barcode only / solo QR o codigo de barras.','Install the Windows printer driver / Instala el controlador de impresora.','RFID encoding is not enabled / No incluye grabacion del chip RFID.')
$instructions | Set-Content -LiteralPath (Join-Path $publishPath 'START-HERE.txt')
$zipPath=Join-Path $projectRoot "artifacts/TagPrinter-v$shortVersion-PORTABLE-Windows-x64.zip"
$files=Get-ChildItem -LiteralPath $publishPath | Where-Object Name -ne 'verification' | Select-Object -ExpandProperty FullName
Compress-Archive -Path $files -DestinationPath $zipPath -Force
Get-Content (Join-Path $publishPath 'verification/results.txt')
Write-Output "Portable: $zipPath"

param([Parameter(Mandatory=$true)][string]$PortablePath)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$portable= [System.IO.Path]::GetFullPath($PortablePath)
$appPath=Join-Path $portable 'app'
# The embedded list is built only from the final published payload, never from arbitrary user data.
foreach($required in @('TagPrinter.exe','TagPrinter.dll','TagPrinter.deps.json','TagPrinter.runtimeconfig.json','coreclr.dll','hostfxr.dll','hostpolicy.dll','e_sqlite3.dll')) {
    if(-not (Test-Path -LiteralPath (Join-Path $appPath $required) -PathType Leaf)){throw "Missing published dependency: $required"}
}
$manifestPath=Join-Path $portable 'integrity-manifest.xml'
$settings=[System.Xml.XmlWriterSettings]::new()
$settings.Encoding=[System.Text.UTF8Encoding]::new($false)
$settings.Indent=$true
$writer=[System.Xml.XmlWriter]::Create($manifestPath,$settings)
try {
    $writer.WriteStartDocument();$writer.WriteStartElement('package');$writer.WriteAttributeString('schema','1')
    foreach($file in (Get-ChildItem -LiteralPath $appPath -File -Recurse | Where-Object { $_.Name -ne 'config.json' -and $_.FullName -notlike '*\verification\*' } | Sort-Object FullName)) {
        $relative=$file.FullName.Substring($portable.TrimEnd('\').Length+1).Replace('\','/')
        $writer.WriteStartElement('file')
        $writer.WriteAttributeString('path',$relative)
        $writer.WriteAttributeString('length',$file.Length.ToString([System.Globalization.CultureInfo]::InvariantCulture))
        $writer.WriteAttributeString('sha256',(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)
        $writer.WriteEndElement()
    }
    $writer.WriteEndElement();$writer.WriteEndDocument()
} finally {$writer.Dispose()}
# Windows 10/11 ship .NET Framework 4.x. Its launcher does not load the .NET 8 payload being verified.
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if(-not (Test-Path -LiteralPath $compiler)){throw 'The Windows .NET Framework C# compiler is unavailable.'}
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$portable\TagPrinter.exe" "/win32icon:$projectRoot\src\TagPrinter\TagPrinter.ico" "/resource:$manifestPath,TagPrinter.Integrity.xml" /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll (Join-Path $projectRoot 'src/TagPrinter.Launcher/Launcher.cs')
if($LASTEXITCODE -ne 0){throw 'Launcher compilation failed.'}

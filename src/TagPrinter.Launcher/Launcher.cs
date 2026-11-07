using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;

[assembly: AssemblyTitle("TagPrinter Startup Validator")]
[assembly: AssemblyVersion("0.23.0.0")]

namespace TagPrinter.Launcher
{
    /// <summary>Uses the Windows .NET Framework, independently of the portable .NET 8 files it checks.</summary>
    internal static class Launcher
    {
        // Tests use verify-only to exercise real packaged files without opening forms or printing.
        [STAThread]
        private static int Main(string[] args)
        {
            string root=AppDomain.CurrentDomain.BaseDirectory;
            bool verifyOnly=args.Contains("--verify-only");
            bool spanish=ReadLanguage()=="es";
            var errors=new List<string>();
            try { Validate(root,errors,spanish); }
            catch(Exception ex) { errors.Add(Text(spanish,"No se pudo completar la validación: ","Validation could not complete: ")+ex.Message); }
            if(errors.Count==0 && !verifyOnly)
            {
                try
                {
                    // The runtime stays in app/, while the editable configuration remains beside this launcher.
                    var start=new ProcessStartInfo(Path.Combine(root,"app","TagPrinter.exe"));
                    start.WorkingDirectory=root;start.UseShellExecute=false;
                    // Publication verifies the real launch hand-off with isolated application self-tests.
                    if(args.Contains("--self-test"))
                    {
                        start.Arguments="--self-test";
                        using(var child=Process.Start(start)){child.WaitForExit();return child.ExitCode;}
                    }
                    Process.Start(start);return 0;
                }
                catch(Exception ex) { errors.Add(Text(spanish,"No se pudo abrir TagPrinter: ","Could not start TagPrinter: ")+ex.Message); }
            }
            string report=errors.Count==0?"PASS: package integrity and config JSON syntax.":string.Join(Environment.NewLine,errors);
            if(verifyOnly)
            {
                int index=Array.IndexOf(args,"--report");
                if(index>=0 && index+1<args.Length)File.WriteAllText(args[index+1],report,Encoding.UTF8);
            }
            if(verifyOnly)return errors.Count==0?0:1;
            if(errors.Count==0)return 0;
            string log=WriteLog(report);
            if(!verifyOnly)
            {
                Application.EnableVisualStyles();
                MessageBox.Show(Text(spanish,"El paquete está incompleto, alterado o no se puede leer.\n\n","The package is incomplete, changed or unreadable.\n\n")+
                    string.Join(Environment.NewLine,errors.Take(12))+"\n\n"+
                    Text(spanish,"Extrae TODO el ZIP en una carpeta nueva. Conserva tus datos y tu config.json personalizado. No copies solo el EXE.\nDiagnóstico: ","Extract the ENTIRE ZIP into a new folder. Preserve your user data and customized config.json. Do not copy only the EXE.\nDiagnostics: ")+log,
                    "TagPrinter",MessageBoxButtons.OK,MessageBoxIcon.Error);
            }
            return 1;
        }

        /// <summary>Checks every embedded manifest entry with SHA-256 before loading any portable runtime file.</summary>
        private static void Validate(string root,List<string> errors,bool spanish)
        {
            byte[] expected;
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("TagPrinter.Integrity.xml"))
            {
                if(stream==null)throw new InvalidDataException("Embedded integrity manifest missing.");
                using(var buffer=new MemoryStream()){stream.CopyTo(buffer);expected=buffer.ToArray();}
            }
            string external=Path.Combine(root,"integrity-manifest.xml");
            if(!File.Exists(external))errors.Add(Text(spanish,"Falta: ","Missing: ")+"integrity-manifest.xml");
            else if(!File.ReadAllBytes(external).SequenceEqual(expected))errors.Add(Text(spanish,"Manifiesto alterado: ","Changed manifest: ")+"integrity-manifest.xml");
            var document=new XmlDocument();document.XmlResolver=null;
            using(var stream=new MemoryStream(expected))document.Load(stream);
            if(document.DocumentElement==null || document.DocumentElement.GetAttribute("schema")!="1")throw new InvalidDataException("Unsupported manifest.");
            var files=document.SelectNodes("/package/file");
            if(files==null || files.Count==0)throw new InvalidDataException("Empty manifest.");
            foreach(XmlElement entry in files)
            {
                string relative=entry.GetAttribute("path");
                try
                {
                    string path=SafePath(root,relative);
                    if(!File.Exists(path)){errors.Add(Text(spanish,"Falta: ","Missing: ")+relative);continue;}
                    long length=long.Parse(entry.GetAttribute("length"),CultureInfo.InvariantCulture);
                    string hash=entry.GetAttribute("sha256");
                    if(hash.Length!=64 || length<0)throw new InvalidDataException("Invalid manifest entry.");
                    using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
                    using(var sha=SHA256.Create())
                    {
                        string actual=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");
                        if(input.Length!=length || !string.Equals(actual,hash,StringComparison.OrdinalIgnoreCase))errors.Add(Text(spanish,"Archivo alterado o corrupto: ","Changed or corrupt file: ")+relative);
                    }
                }
                catch(Exception ex){errors.Add(Text(spanish,"No se pudo verificar ","Could not verify ")+relative+": "+ex.Message);}
            }
            // Config is intentionally not hashed: users may edit it. The application performs full schema/range validation.
            try
            {
                string config=SafePath(root,"config.json");
                if(!File.Exists(config))throw new FileNotFoundException("config.json");
                if(new FileInfo(config).Length>1024*1024)throw new InvalidDataException("config.json exceeds 1 MB.");
                var serializer=new JavaScriptSerializer();serializer.MaxJsonLength=1024*1024;
                if(!(serializer.DeserializeObject(File.ReadAllText(config)) is IDictionary<string,object>))throw new InvalidDataException("Expected a JSON object.");
            }
            catch(Exception ex){errors.Add(Text(spanish,"Configuración inválida o ausente: ","Invalid or missing configuration: ")+ex.Message);}
        }

        /// <summary>Rejects absolute/traversing paths. Cloud-backed files are allowed and their actual bytes are hashed.</summary>
        private static string SafePath(string root,string relative)
        {
            string prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            string full=Path.GetFullPath(Path.Combine(prefix,relative));
            if(Path.IsPathRooted(relative) || !full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Path outside package.");
            return full;
        }

        /// <summary>Reads only the saved locale; corrupt/missing settings use the default English diagnostics.</summary>
        private static string ReadLanguage()
        {
            try
            {
                string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"InventoryLabels","settings.json");
                var data=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path)) as IDictionary<string,object>;
                return data!=null && data.ContainsKey("Language")?Convert.ToString(data["Language"]):"en";
            }
            catch{return "en";}
        }

        /// <summary>Appends a timestamped diagnostic in user data, never beside possibly read-only program files.</summary>
        private static string WriteLog(string message)
        {
            try
            {
                string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TagPrinter","logs");Directory.CreateDirectory(directory);
                string path=Path.Combine(directory,"startup-"+DateTime.UtcNow.ToString("yyyyMMdd",CultureInfo.InvariantCulture)+".log");
                File.AppendAllText(path,DateTimeOffset.UtcNow.ToString("O")+Environment.NewLine+message+Environment.NewLine,Encoding.UTF8);return path;
            }
            catch{return "Log could not be written; copy the error details.";}
        }

        /// <summary>Uses saved Spanish or default English without loading the application assemblies.</summary>
        private static string Text(bool spanish,string es,string en){return spanish?es:en;}
    }
}

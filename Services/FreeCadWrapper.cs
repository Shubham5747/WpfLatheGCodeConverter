using System;
using System.Diagnostics;
using System.IO;

namespace WpfLatheGCodeConverter.Services
{
    // Wrapper that invokes FreeCAD (command-line) to convert STEP -> DXF or to re-save DXF to a normalized form.
    public class FreeCadWrapper
    {
        private readonly string toolsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");

        public string GetFreeCadCmdPath()
        {
            var candidates = new[] {
                Path.Combine(toolsFolder, "freecadcmd.exe"),
                Path.Combine(toolsFolder, "freecadcmd"),
                "FreeCADCmd.exe",
                "freecadcmd"
            };

            foreach (var c in candidates)
            {
                try
                {
                    if (File.Exists(c)) return c;
                    // return bare name so Process can find it in PATH
                    if (c.Equals("freecadcmd", StringComparison.OrdinalIgnoreCase) || c.Equals("FreeCADCmd.exe", StringComparison.OrdinalIgnoreCase))
                        return c;
                }
                catch { }
            }

            throw new FileNotFoundException("FreeCAD command-line not found. Place FreeCADCmd in ./tools or install FreeCAD and add to PATH.");
        }

        // Convert STEP to DXF (existing)
        public string ConvertStepToDxfOrSvg(string stepPath)
        {
            if (!File.Exists(stepPath)) throw new FileNotFoundException(nameof(stepPath));
            var fc = GetFreeCadCmdPath();

            var outDxf = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(stepPath) + "_fc.dxf");
            var macro = Path.Combine(Path.GetTempPath(), "export_to_dxf.py");

            var macroContent = $@"
import Import, Part, FreeCAD
doc = FreeCAD.newDocument()
shape = Part.Shape()
shape = Part.read(r'{stepPath}')
obj = doc.addObject('Part::Feature','Imported')
obj.Shape = shape
doc.recompute()
Import.export(doc.Objects, r'{outDxf}')
";
            File.WriteAllText(macro, macroContent);

            var args = $"-c \"exec( open(r'{macro}').read() )\"";

            var psi = new ProcessStartInfo
            {
                FileName = fc,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi) ?? throw new Exception("Failed to start FreeCAD process.");
            proc.WaitForExit();
            var stderr = proc.StandardError.ReadToEnd();
            var stdout = proc.StandardOutput.ReadToEnd();

            if (!File.Exists(outDxf))
            {
                throw new Exception("FreeCAD conversion failed. Stdout: " + stdout + Environment.NewLine + "Stderr: " + stderr);
            }

            return outDxf;
        }

        // New: re-save or normalize an existing DXF using FreeCAD
        // Tries to import the input DXF and export a new DXF that netDxf is more likely to accept.
        // Returns path to converted DXF.
        public string ConvertDxfToSupported(string dxfPath)
        {
            if (!File.Exists(dxfPath)) throw new FileNotFoundException(nameof(dxfPath));
            var fc = GetFreeCadCmdPath();

            var outDxf = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(dxfPath) + "_fc_normalized.dxf");
            var macro = Path.Combine(Path.GetTempPath(), "dxf_normalize.py");

            // Use Import.insert for DXF import, then Export to DXF
            var macroContent = $@"
import Import, FreeCAD
doc = FreeCAD.newDocument()
# Insert the DXF into the document
Import.insert(r'{dxfPath}', doc.Name)
doc.recompute()
# Export all objects to a new DXF
Import.export(doc.Objects, r'{outDxf}')
";
            File.WriteAllText(macro, macroContent);

            var args = $"-c \"exec( open(r'{macro}').read() )\"";

            var psi = new ProcessStartInfo
            {
                FileName = fc,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi) ?? throw new Exception("Failed to start FreeCAD process.");
            proc.WaitForExit();
            var stderr = proc.StandardError.ReadToEnd();
            var stdout = proc.StandardOutput.ReadToEnd();

            if (!File.Exists(outDxf))
            {
                throw new Exception("FreeCAD DXF normalization failed. Stdout: " + stdout + Environment.NewLine + "Stderr: " + stderr);
            }

            return outDxf;
        }
    }
}
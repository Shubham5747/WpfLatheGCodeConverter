using System;
using System.Diagnostics;
using System.IO;

namespace WpfLatheGCodeConverter.Services
{
    // Wrapper for ODA/Teigha File Converter (if installed). Converts DXF versions.
    // Typical executable names: TeighaFileConverter.exe or OdaFileConverter.exe
    public class OdaFileConverterWrapper
    {
        private readonly string toolsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");

        public string? GetConverterPath()
        {
            var candidates = new[] {
                Path.Combine(toolsFolder, "TeighaFileConverter.exe"),
                Path.Combine(toolsFolder, "OdaFileConverter.exe"),
                "TeighaFileConverter.exe",
                "OdaFileConverter.exe"
            };

            foreach (var c in candidates)
            {
                try
                {
                    if (File.Exists(c)) return c;
                    // If bare name, assume in PATH
                    if (c.StartsWith("Teigha") || c.StartsWith("Oda")) return c;
                }
                catch { }
            }
            return null;
        }

        // Convert a single DXF to a target version (e.g., "AC1027" for AutoCAD 2013).
        // Returns path to converted file or null on failure.
        public string? ConvertDxf(string inputPath, string targetVersion = "AC1027")
        {
            var exe = GetConverterPath();
            if (string.IsNullOrEmpty(exe)) return null;

            // ODA File Converter expects input dir and output dir with options
            var inputDir = Path.GetDirectoryName(inputPath) ?? ".";
            var outputDir = Path.Combine(Path.GetTempPath(), "oda_dxf_out");
            Directory.CreateDirectory(outputDir);

            // Build args: <inputDir> <outputDir> <inVersion> <outVersion> <options>
            // We'll let the converter autodetect input version; set output version:
            var args = $"\"{inputDir}\" \"{outputDir}\" -d \"{targetVersion}\" -r";

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi) ?? throw new Exception("Failed to start ODA File Converter.");
            proc.WaitForExit();
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();

            // The converter will output files with same name into outputDir; search for input filename match
            var outFile = Path.Combine(outputDir, Path.GetFileName(inputPath));
            if (File.Exists(outFile)) return outFile;

            // If not found, try to find any DXF in outputDir
            var any = Directory.GetFiles(outputDir, "*.dxf", SearchOption.AllDirectories);
            if (any.Length > 0) return any[0];

            return null;
        }
    }
}
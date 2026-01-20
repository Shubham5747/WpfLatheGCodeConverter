using System;
using System.Diagnostics;
using System.IO;

namespace WpfLatheGCodeConverter.Services
{
    // Wrapper that invokes potrace CLI to convert bitmaps to SVG.
    // Looks for potrace.exe / potrace in ./tools first, then PATH.
    public class PotraceWrapper
    {
        private readonly string toolsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");

        public string GetPotracePath()
        {
            var candidates = new[] {
                Path.Combine(toolsFolder, "potrace.exe"),
                Path.Combine(toolsFolder, "potrace"),
                "potrace.exe",
                "potrace"
            };

            foreach (var c in candidates)
            {
                try
                {
                    // If absolute path exists return it
                    if (File.Exists(c)) return c;
                    // Otherwise if bare name, assume in PATH (let Process start it and let it fail if not found)
                    if (c == "potrace" || c == "potrace.exe")
                    {
                        return c;
                    }
                }
                catch { }
            }

            throw new FileNotFoundException("potrace executable not found. Place potrace in ./tools or add to PATH.");
        }

        // Trace a bitmap and return path to created SVG in temp folder.
        public string TraceBitmapToSvg(string bitmapPath)
        {
            if (!File.Exists(bitmapPath)) throw new FileNotFoundException("Bitmap not found: " + bitmapPath);
            var potrace = GetPotracePath();
            var outSvg = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(bitmapPath) + "_potrace.svg");

            var args = $"-s -o \"{outSvg}\" \"{bitmapPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = potrace,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi) ?? throw new Exception("Failed to start potrace process.");
            proc.WaitForExit();
            var stderr = proc.StandardError.ReadToEnd();
            var stdout = proc.StandardOutput.ReadToEnd();

            if (!File.Exists(outSvg))
            {
                throw new Exception("Potrace failed to produce SVG. Stdout: " + stdout + Environment.NewLine + "Stderr: " + stderr);
            }

            return outSvg;
        }
    }
}
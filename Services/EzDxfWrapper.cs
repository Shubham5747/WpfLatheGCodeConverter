using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using WpfLatheGCodeConverter.Models;

namespace WpfLatheGCodeConverter.Services
{
    public class EzDxfWrapper
    {
        private readonly string scriptRelPath = Path.Combine("tools", "python", "ezdxf_normalize.py");

        private enum PythonType { None, PythonExe, PyLauncher }

        public string? LastJsonPath { get; private set; }
        public string? LastLogPath { get; private set; }

        private (PythonType type, string exe) FindPython(StringBuilder log)
        {
            var direct = new[] { "python", "python3" };
            foreach (var c in direct)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = c,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    if (p == null) continue;
                    p.WaitForExit(1500);
                    log.AppendLine($"Tried python exe '{c}', exit={p.ExitCode}");
                    return (PythonType.PythonExe, c);
                }
                catch (Exception ex)
                {
                    log.AppendLine($"python check '{c}' failed: {ex.Message}");
                }
            }

            try
            {
                var psi2 = new ProcessStartInfo
                {
                    FileName = "py",
                    Arguments = "-3 --version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p2 = Process.Start(psi2);
                if (p2 != null)
                {
                    p2.WaitForExit(1500);
                    log.AppendLine($"Tried py launcher, exit={p2.ExitCode}");
                    return (PythonType.PyLauncher, "py");
                }
            }
            catch (Exception ex)
            {
                log.AppendLine("py launcher check failed: " + ex.Message);
            }

            log.AppendLine("No python runtime found on PATH.");
            return (PythonType.None, string.Empty);
        }

        private string? FindScriptPath(StringBuilder log)
        {
            // 1) explicit env var
            var env = Environment.GetEnvironmentVariable("EZDXF_SCRIPT_PATH");
            if (!string.IsNullOrEmpty(env) && File.Exists(env))
            {
                log.AppendLine("Found script via EZDXF_SCRIPT_PATH: " + env);
                return env;
            }

            // 2) search upwards from app base dir
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? Directory.GetCurrentDirectory();
            string dir = baseDir;
            for (int i = 0; i < 8; i++)
            {
                try
                {
                    var candidate = Path.GetFullPath(Path.Combine(dir, scriptRelPath));
                    log.AppendLine("Checking for script: " + candidate);
                    if (File.Exists(candidate))
                    {
                        log.AppendLine("Found script: " + candidate);
                        return candidate;
                    }
                }
                catch (Exception ex)
                {
                    log.AppendLine("Path check error: " + ex.Message);
                }

                // move one directory up
                dir = Path.GetFullPath(Path.Combine(dir, ".."));
            }

            // 3) check working directory relative path
            try
            {
                var cand2 = Path.GetFullPath(scriptRelPath);
                log.AppendLine("Checking relative working dir path: " + cand2);
                if (File.Exists(cand2)) return cand2;
            }
            catch (Exception ex)
            {
                log.AppendLine("Relative path check failed: " + ex.Message);
            }

            log.AppendLine("Script not found in candidate locations.");
            return null;
        }

        public GeometryModel? ExtractGeometryViaEzDxf(string dxfPath)
        {
            LastJsonPath = null;
            LastLogPath = null;

            if (string.IsNullOrWhiteSpace(dxfPath) || !File.Exists(dxfPath))
                throw new FileNotFoundException("DXF not found", dxfPath);

            var tempLog = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(dxfPath) + "_ezdxf_wrapper.log");
            var logSb = new StringBuilder();
            logSb.AppendLine("EzDxfWrapper starting at " + DateTime.UtcNow.ToString("o"));
            logSb.AppendLine("AppBase: " + (AppDomain.CurrentDomain.BaseDirectory ?? "(null)"));
            logSb.AppendLine("CWD: " + Directory.GetCurrentDirectory());
            try
            {
                // find the script
                var scriptPath = FindScriptPath(logSb);
                if (string.IsNullOrEmpty(scriptPath))
                {
                    File.WriteAllText(tempLog, logSb.ToString());
                    LastLogPath = tempLog;
                    return null;
                }

                // find python
                var py = FindPython(logSb);
                if (py.type == PythonType.None)
                {
                    File.WriteAllText(tempLog, logSb.ToString());
                    LastLogPath = tempLog;
                    return null;
                }

                var outJson = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(dxfPath) + "_ezdxf.json");
                var logPath = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(dxfPath) + "_ezdxf.log");

                string exec = py.exe;
                string args;
                if (py.type == PythonType.PyLauncher)
                    args = $"-3 \"{scriptPath}\" \"{dxfPath}\" \"{outJson}\" --approx-segs 48 --explode-inserts --verbose";
                else
                    args = $"\"{scriptPath}\" \"{dxfPath}\" \"{outJson}\" --approx-segs 48 --explode-inserts --verbose";

                logSb.AppendLine("Running: " + exec + " " + args);
                File.WriteAllText(tempLog, logSb.ToString()); // write intermediate log

                var psi = new ProcessStartInfo
                {
                    FileName = exec,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi) ?? throw new Exception("Failed to start python process.");
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(120000);

                // combine logs
                var combinedLog = new StringBuilder();
                combinedLog.AppendLine("Wrapper internal log:");
                combinedLog.AppendLine(logSb.ToString());
                combinedLog.AppendLine("=== Python STDOUT ===");
                combinedLog.AppendLine(stdout);
                combinedLog.AppendLine("=== Python STDERR ===");
                combinedLog.AppendLine(stderr);

                try
                {
                    File.WriteAllText(logPath, combinedLog.ToString());
                }
                catch { /* best-effort */ }

                LastLogPath = File.Exists(logPath) ? logPath : tempLog;
                LastJsonPath = File.Exists(outJson) ? outJson : null;

                if (!File.Exists(outJson))
                {
                    // Save combinedLog to tempLog too if outJson missing
                    try { File.WriteAllText(tempLog, combinedLog.ToString()); LastLogPath = tempLog; } catch { }
                    return null;
                }

                // parse JSON to GeometryModel (reuse earlier parsing approach)
                var gm = new GeometryModel();
                string jsonText = File.ReadAllText(outJson);
                if (string.IsNullOrWhiteSpace(jsonText)) return gm;

                using var doc = JsonDocument.Parse(jsonText);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return gm;

                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    if (!elem.TryGetProperty("type", out var tProp)) continue;
                    var t = tProp.GetString()?.ToLowerInvariant() ?? string.Empty;

                    if (t == "polyline")
                    {
                        if (elem.TryGetProperty("points", out var ptsElem) && ptsElem.ValueKind == JsonValueKind.Array)
                        {
                            var pl = new List<(double X, double Y)>();
                            foreach (var p in ptsElem.EnumerateArray())
                            {
                                if (p.ValueKind == JsonValueKind.Array && p.GetArrayLength() >= 2)
                                {
                                    if (p[0].TryGetDouble(out var x) && p[1].TryGetDouble(out var y))
                                        pl.Add((x, y));
                                    else
                                    {
                                        // try parse as string
                                        double xx = double.NaN, yy = double.NaN;
                                        if (p[0].ValueKind == JsonValueKind.String) double.TryParse(p[0].GetString(), out xx);
                                        if (p[1].ValueKind == JsonValueKind.String) double.TryParse(p[1].GetString(), out yy);
                                        if (!double.IsNaN(xx) && !double.IsNaN(yy)) pl.Add((xx, yy));
                                    }
                                }
                                else if (p.ValueKind == JsonValueKind.Object)
                                {
                                    double x = SafeGetDoubleFromProperty(p, "x", "X", "cx");
                                    double y = SafeGetDoubleFromProperty(p, "y", "Y", "cy");
                                    if (!double.IsNaN(x) && !double.IsNaN(y)) pl.Add((x, y));
                                }
                            }
                            if (pl.Count > 0) gm.Polylines.Add(pl);
                        }
                    }
                    else if (t == "circle")
                    {
                        if (elem.TryGetProperty("cx", out var cxp) && elem.TryGetProperty("cy", out var cyp) && elem.TryGetProperty("r", out var rp))
                        {
                            double cx = SafeGetDoubleFromJson(cxp);
                            double cy = SafeGetDoubleFromJson(cyp);
                            double r = SafeGetDoubleFromJson(rp);
                            if (!double.IsNaN(cx) && !double.IsNaN(cy) && !double.IsNaN(r))
                                gm.Polylines.Add(ApproximateCircle(cx, cy, r, 48));
                        }
                    }
                    else if (t == "arc")
                    {
                        if (elem.TryGetProperty("cx", out var cxp) && elem.TryGetProperty("cy", out var cyp)
                            && elem.TryGetProperty("r", out var rp) && elem.TryGetProperty("start", out var sp) && elem.TryGetProperty("end", out var ep))
                        {
                            double cx = SafeGetDoubleFromJson(cxp);
                            double cy = SafeGetDoubleFromJson(cyp);
                            double r = SafeGetDoubleFromJson(rp);
                            double sa = SafeGetDoubleFromJson(sp);
                            double ea = SafeGetDoubleFromJson(ep);
                            if (!double.IsNaN(cx) && !double.IsNaN(cy) && !double.IsNaN(r))
                                gm.Polylines.Add(ApproximateArc(cx, cy, r, sa, ea, 36));
                        }
                    }
                }

                return gm;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(tempLog, "Exception: " + ex.ToString()); LastLogPath = tempLog; } catch { }
                return null;
            }
        }

        private static double SafeGetDoubleFromJson(JsonElement el)
        {
            try
            {
                return el.ValueKind switch
                {
                    JsonValueKind.Number => el.GetDouble(),
                    JsonValueKind.String when double.TryParse(el.GetString(), out var v) => v,
                    _ => double.NaN
                };
            }
            catch
            {
                return double.NaN;
            }
        }

        private static double SafeGetDoubleFromProperty(JsonElement obj, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (obj.TryGetProperty(k, out var prop))
                {
                    var v = SafeGetDoubleFromJson(prop);
                    if (!double.IsNaN(v)) return v;
                }
            }
            return double.NaN;
        }

        private static List<(double X, double Y)> ApproximateCircle(double cx, double cy, double r, int segments)
        {
            var pts = new List<(double X, double Y)>();
            for (int i = 0; i <= segments; i++)
            {
                double theta = 2.0 * Math.PI * i / segments;
                pts.Add((cx + r * Math.Cos(theta), cy + r * Math.Sin(theta)));
            }
            return pts;
        }

        private static List<(double X, double Y)> ApproximateArc(double cx, double cy, double r, double startDeg, double endDeg, int segments)
        {
            double start = double.IsNaN(startDeg) ? 0.0 : startDeg * Math.PI / 180.0;
            double end = double.IsNaN(endDeg) ? 2.0 * Math.PI : endDeg * Math.PI / 180.0;
            if (end < start) end += 2.0 * Math.PI;
            var pts = new List<(double X, double Y)>();
            for (int i = 0; i <= segments; i++)
            {
                double t = start + (end - start) * i / segments;
                pts.Add((cx + r * Math.Cos(t), cy + r * Math.Sin(t)));
            }
            return pts;
        }
    }
}
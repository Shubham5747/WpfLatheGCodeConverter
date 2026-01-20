using System;
using System.IO;
using WpfLatheGCodeConverter.Models;

namespace WpfLatheGCodeConverter.Services
{
    // ImportService: orchestrates SVG/DXF import and exposes last diagnostic paths.
    public class ImportService
    {
        private readonly SvgImporter svgImporter = new();
        private readonly DxfImporter dxfImporter = new();
        private readonly EzDxfWrapper ezWrapper = new();

        public GeometryModel? LastGeometry { get; private set; }

        // Diagnostics paths (may be null)
        public string? LastDiagnosticsJsonPath { get; private set; }
        public string? LastDiagnosticsLogPath { get; private set; }

        public GeometryModel ImportFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            var ext = Path.GetExtension(path).ToLowerInvariant();
            GeometryModel gm = new GeometryModel();

            // Clear previous diagnostics
            LastDiagnosticsJsonPath = null;
            LastDiagnosticsLogPath = null;

            if (ext == ".svg")
            {
                gm = svgImporter.Import(path);
            }
            else if (ext == ".dxf")
            {
                // 1) Try the existing DxfImporter (netDxf + ascii fallback)
                try
                {
                    var gmNet = dxfImporter.Import(path);
                    if (gmNet != null && gmNet.Polylines.Count > 0)
                    {
                        gm = gmNet;
                    }
                }
                catch
                {
                    // ignore and continue to ezdxf step
                }

                // 2) Explicitly call EzDxfWrapper (force run) and prefer its output if it contains geometry
                try
                {
                    var gmEz = ezWrapper.ExtractGeometryViaEzDxf(path);
                    // store diagnostic paths from wrapper (even if null)
                    LastDiagnosticsJsonPath = ezWrapper.LastJsonPath;
                    LastDiagnosticsLogPath = ezWrapper.LastLogPath;

                    if (gmEz != null && gmEz.Polylines.Count > 0)
                    {
                        gm = gmEz;
                    }
                    else
                    {
                        // if gm was empty but ez produced JSON on disk, keep the diagnostic paths
                        // (LastDiagnostics* already set). If no JSON, they remain null.
                    }
                }
                catch
                {
                    // ignore ezdxf failures and fall back to whatever gm has
                }

                // If neither produced geometry, try a final ASCII fallback via dxfImporter (if implemented)
                if (gm == null) gm = new GeometryModel();
            }
            else if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
            {
                throw new NotSupportedException("Raster import not implemented in this build.");
            }
            else if (ext == ".step" || ext == ".stp")
            {
                throw new NotSupportedException("STEP import not implemented in this build.");
            }
            else
            {
                throw new NotSupportedException("Unsupported file type: " + ext);
            }

            LastGeometry = gm;
            return gm;
        }
    }
}
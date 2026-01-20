using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using WpfLatheGCodeConverter.Models;
using netDxf;
using netDxf.IO;

namespace WpfLatheGCodeConverter.Services
{
    public class DxfImporter
    {
        private readonly EzDxfWrapper ezdxf = new();

        // Expose last ezdxf-produced diagnostic file paths
        public string? LastEzJsonPath { get; private set; }
        public string? LastEzLogPath { get; private set; }

        public GeometryModel Import(string dxfPath)
        {
            if (string.IsNullOrWhiteSpace(dxfPath)) throw new ArgumentNullException(nameof(dxfPath));
            if (!File.Exists(dxfPath)) throw new FileNotFoundException("DXF not found", dxfPath);

            // 1) netDxf attempt (reflection-safe)
            try
            {
                var gm = ImportWithNetDxfReflection(dxfPath);
                if (gm != null && gm.Polylines.Count > 0)
                {
                    LastEzJsonPath = null;
                    LastEzLogPath = null;
                    return gm;
                }
            }
            catch (DxfVersionNotSupportedException)
            {
                System.Diagnostics.Debug.WriteLine("netDxf reported unsupported DXF version.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("netDxf initial load failed: " + ex);
            }

            // 2) ezdxf (Python) fallback
            try
            {
                var gm2 = ezdxf.ExtractGeometryViaEzDxf(dxfPath);
                // copy diagnostic paths
                LastEzJsonPath = ezdxf.LastJsonPath;
                LastEzLogPath = ezdxf.LastLogPath;

                if (gm2 != null && gm2.Polylines.Count > 0) return gm2;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ezdxf fallback failed: " + ex);
            }

            // 3) ASCII-R12 fallback
            try
            {
                LastEzJsonPath = ezdxf.LastJsonPath;
                LastEzLogPath = ezdxf.LastLogPath;
                return ImportAsciiDxfFallback(dxfPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ASCII fallback failed: " + ex);
                return new GeometryModel();
            }
        }

        // --- (ImportWithNetDxfReflection, ProcessEntityReflection, GetEnumeratorForObject, helpers, ImportAsciiDxfFallback, ApproximateCircle/Arc)
        // Include your previously working implementations here (unchanged). For brevity they are omitted in this paste,
        // but you should reuse the robust reflection + ASCII fallback code you already have in your Services/DxfImporter.cs.
        //
        // If you previously used the long DxfImporter version provided earlier in the chat, keep those helper methods here.
        //
        // Example placeholder to avoid compile error - replace with your implementations:
        private GeometryModel ImportWithNetDxfReflection(string dxfPath) { throw new NotImplementedException("Paste your reflection-based netDxf helpers here."); }
        private GeometryModel ImportAsciiDxfFallback(string dxfPath) { throw new NotImplementedException("Paste your ASCII fallback here."); }
    }
}
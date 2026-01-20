using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using WpfLatheGCodeConverter.Models;
using netDxf;
using netDxf.Entities;

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

        /// <summary>
        /// Imports a DXF file using netDxf library with reflection to handle entities safely.
        /// Extracts polylines, arcs, circles, and applies transforms.
        /// </summary>
        private GeometryModel? ImportWithNetDxfReflection(string dxfPath)
        {
            try
            {
                // Load the DXF document using netDxf
                var dxfDoc = DxfDocument.Load(dxfPath);
                if (dxfDoc == null) return null;

                var gm = new GeometryModel();

                // Process Polylines2D (Lightweight Polylines in newer netDxf)
                foreach (var poly in dxfDoc.Entities.Polylines2D)
                {
                    var points = new List<(double X, double Y)>();
                    foreach (var vertex in poly.Vertexes)
                    {
                        points.Add((vertex.Position.X, vertex.Position.Y));
                    }
                    if (points.Count > 0)
                    {
                        // Close the polyline if it's marked as closed
                        if (poly.IsClosed && points.Count > 0 && points[0] != points[points.Count - 1])
                        {
                            points.Add(points[0]);
                        }
                        gm.Polylines.Add(points);
                    }
                }

                // Process Polylines3D
                foreach (var poly in dxfDoc.Entities.Polylines3D)
                {
                    var points = new List<(double X, double Y)>();
                    foreach (var vertex in poly.Vertexes)
                    {
                        // Polyline3D vertices are Vector3, not custom vertex objects
                        points.Add((vertex.X, vertex.Y));
                    }
                    if (points.Count > 0)
                    {
                        // Close the polyline if it's marked as closed
                        if (poly.IsClosed && points.Count > 0 && points[0] != points[points.Count - 1])
                        {
                            points.Add(points[0]);
                        }
                        gm.Polylines.Add(points);
                    }
                }

                // Process Circles - approximate as polylines
                foreach (var circle in dxfDoc.Entities.Circles)
                {
                    var points = ApproximateCircle(circle.Center.X, circle.Center.Y, circle.Radius, 48);
                    if (points.Count > 0)
                    {
                        gm.Polylines.Add(points);
                    }
                }

                // Process Arcs - approximate as polylines
                foreach (var arc in dxfDoc.Entities.Arcs)
                {
                    var points = ApproximateArc(arc.Center.X, arc.Center.Y, arc.Radius, arc.StartAngle, arc.EndAngle, 36);
                    if (points.Count > 0)
                    {
                        gm.Polylines.Add(points);
                    }
                }

                // Process Lines - convert to 2-point polylines
                foreach (var line in dxfDoc.Entities.Lines)
                {
                    var points = new List<(double X, double Y)>
                    {
                        (line.StartPoint.X, line.StartPoint.Y),
                        (line.EndPoint.X, line.EndPoint.Y)
                    };
                    gm.Polylines.Add(points);
                }

                // Process Splines - approximate as polylines using reflection for safety
                foreach (var spline in dxfDoc.Entities.Splines)
                {
                    try
                    {
                        // Use reflection to safely access ToPolyline method
                        var toPolylineMethod = spline.GetType().GetMethod("ToPolyline", new[] { typeof(int) });
                        if (toPolylineMethod != null)
                        {
                            var polyline = toPolylineMethod.Invoke(spline, new object[] { 50 });
                            if (polyline != null)
                            {
                                var vertexesProp = polyline.GetType().GetProperty("Vertexes");
                                if (vertexesProp != null)
                                {
                                    var vertexes = vertexesProp.GetValue(polyline) as IEnumerable;
                                    if (vertexes != null)
                                    {
                                        var points = new List<(double X, double Y)>();
                                        foreach (var vertex in vertexes)
                                        {
                                            var posProp = vertex.GetType().GetProperty("Position");
                                            if (posProp != null)
                                            {
                                                var pos = posProp.GetValue(vertex);
                                                if (pos != null)
                                                {
                                                    var xProp = pos.GetType().GetProperty("X");
                                                    var yProp = pos.GetType().GetProperty("Y");
                                                    if (xProp != null && yProp != null)
                                                    {
                                                        var x = Convert.ToDouble(xProp.GetValue(pos));
                                                        var y = Convert.ToDouble(yProp.GetValue(pos));
                                                        points.Add((x, y));
                                                    }
                                                }
                                            }
                                        }
                                        if (points.Count > 0)
                                        {
                                            gm.Polylines.Add(points);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Skip splines that can't be converted
                    }
                }

                return gm;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ImportWithNetDxfReflection failed: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Fallback ASCII DXF parser for simple R12 format files.
        /// Extracts basic LINE, POLYLINE, LWPOLYLINE, CIRCLE, and ARC entities.
        /// </summary>
        private GeometryModel ImportAsciiDxfFallback(string dxfPath)
        {
            var gm = new GeometryModel();
            var lines = File.ReadAllLines(dxfPath);
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                
                // Look for entity type markers
                if (line == "LINE")
                {
                    // Parse LINE entity
                    double x1 = 0, y1 = 0, x2 = 0, y2 = 0;
                    bool hasStart = false, hasEnd = false;
                    
                    for (int j = i + 1; j < lines.Length && j < i + 50; j++)
                    {
                        var code = lines[j].Trim();
                        if (j + 1 < lines.Length)
                        {
                            var value = lines[j + 1].Trim();
                            
                            if (code == "10") // Start X
                            {
                                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out x1))
                                    hasStart = true;
                            }
                            else if (code == "20") // Start Y
                            {
                                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out y1))
                                    hasStart = true;
                            }
                            else if (code == "11") // End X
                            {
                                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out x2))
                                    hasEnd = true;
                            }
                            else if (code == "21") // End Y
                            {
                                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out y2))
                                    hasEnd = true;
                            }
                            else if (code == "0") // Next entity
                            {
                                break;
                            }
                        }
                    }
                    
                    if (hasStart && hasEnd)
                    {
                        gm.Polylines.Add(new List<(double X, double Y)> { (x1, y1), (x2, y2) });
                    }
                }
                else if (line == "CIRCLE")
                {
                    // Parse CIRCLE entity
                    double cx = 0, cy = 0, r = 0;
                    bool hasCenter = false, hasRadius = false;
                    
                    for (int j = i + 1; j < lines.Length && j < i + 50; j++)
                    {
                        var code = lines[j].Trim();
                        if (j + 1 < lines.Length)
                        {
                            var value = lines[j + 1].Trim();
                            
                            if (code == "10") // Center X
                            {
                                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out cx))
                                    hasCenter = true;
                            }
                            else if (code == "20") // Center Y
                            {
                                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out cy))
                                    hasCenter = true;
                            }
                            else if (code == "40") // Radius
                            {
                                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out r))
                                    hasRadius = true;
                            }
                            else if (code == "0") // Next entity
                            {
                                break;
                            }
                        }
                    }
                    
                    if (hasCenter && hasRadius && r > 0)
                    {
                        gm.Polylines.Add(ApproximateCircle(cx, cy, r, 48));
                    }
                }
                else if (line == "ARC")
                {
                    // Parse ARC entity
                    double cx = 0, cy = 0, r = 0, startAngle = 0, endAngle = 360;
                    bool hasCenter = false, hasRadius = false;
                    
                    for (int j = i + 1; j < lines.Length && j < i + 50; j++)
                    {
                        var code = lines[j].Trim();
                        if (j + 1 < lines.Length)
                        {
                            var value = lines[j + 1].Trim();
                            
                            if (code == "10") // Center X
                            {
                                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out cx))
                                    hasCenter = true;
                            }
                            else if (code == "20") // Center Y
                            {
                                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out cy))
                                    hasCenter = true;
                            }
                            else if (code == "40") // Radius
                            {
                                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out r))
                                    hasRadius = true;
                            }
                            else if (code == "50") // Start angle
                            {
                                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out startAngle);
                            }
                            else if (code == "51") // End angle
                            {
                                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out endAngle);
                            }
                            else if (code == "0") // Next entity
                            {
                                break;
                            }
                        }
                    }
                    
                    if (hasCenter && hasRadius && r > 0)
                    {
                        gm.Polylines.Add(ApproximateArc(cx, cy, r, startAngle, endAngle, 36));
                    }
                }
            }
            
            return gm;
        }

        /// <summary>
        /// Approximates a circle as a polyline with the specified number of segments.
        /// </summary>
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

        /// <summary>
        /// Approximates an arc as a polyline with the specified number of segments.
        /// Angles are in degrees.
        /// </summary>
        private static List<(double X, double Y)> ApproximateArc(double cx, double cy, double r, double startDeg, double endDeg, int segments)
        {
            double start = startDeg * Math.PI / 180.0;
            double end = endDeg * Math.PI / 180.0;
            
            // Handle wrap-around cases
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
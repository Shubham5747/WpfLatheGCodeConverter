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

        private GeometryModel? ImportWithNetDxfReflection(string dxfPath)
        {
            try
            {
                // Attempt to load using netDxf directly
                var doc = DxfDocument.Load(dxfPath);
                if (doc == null) return null;

                var gm = new GeometryModel();
                
                // Process entities from modelspace
                foreach (var entity in doc.Entities.All)
                {
                    ProcessEntity(entity, gm);
                }

                return gm;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ImportWithNetDxfReflection failed: " + ex.Message);
                return null;
            }
        }

        private void ProcessEntity(object entity, GeometryModel gm)
        {
            if (entity == null) return;

            var entityType = entity.GetType();
            var typeName = entityType.Name;

            try
            {
                if (typeName == "Line")
                {
                    var startProp = entityType.GetProperty("StartPoint");
                    var endProp = entityType.GetProperty("EndPoint");
                    if (startProp != null && endProp != null)
                    {
                        var start = startProp.GetValue(entity);
                        var end = endProp.GetValue(entity);
                        if (start != null && end != null)
                        {
                            var x1 = GetCoordinate(start, "X");
                            var y1 = GetCoordinate(start, "Y");
                            var x2 = GetCoordinate(end, "X");
                            var y2 = GetCoordinate(end, "Y");
                            gm.Polylines.Add(new List<(double, double)> { (x1, y1), (x2, y2) });
                        }
                    }
                }
                else if (typeName == "LwPolyline" || typeName == "Polyline")
                {
                    var vertexesProp = entityType.GetProperty("Vertexes");
                    if (vertexesProp != null)
                    {
                        var vertexes = vertexesProp.GetValue(entity);
                        if (vertexes is IEnumerable enumerable)
                        {
                            var pts = new List<(double, double)>();
                            foreach (var vertex in enumerable)
                            {
                                if (vertex != null)
                                {
                                    var x = GetCoordinate(vertex, "X");
                                    var y = GetCoordinate(vertex, "Y");
                                    pts.Add((x, y));
                                }
                            }
                            if (pts.Count > 0) gm.Polylines.Add(pts);
                        }
                    }
                }
                else if (typeName == "Circle")
                {
                    var centerProp = entityType.GetProperty("Center");
                    var radiusProp = entityType.GetProperty("Radius");
                    if (centerProp != null && radiusProp != null)
                    {
                        var center = centerProp.GetValue(entity);
                        var radius = radiusProp.GetValue(entity);
                        if (center != null && radius != null)
                        {
                            var cx = GetCoordinate(center, "X");
                            var cy = GetCoordinate(center, "Y");
                            var r = Convert.ToDouble(radius);
                            gm.Polylines.Add(ApproximateCircle(cx, cy, r, 48));
                        }
                    }
                }
                else if (typeName == "Arc")
                {
                    var centerProp = entityType.GetProperty("Center");
                    var radiusProp = entityType.GetProperty("Radius");
                    var startAngleProp = entityType.GetProperty("StartAngle");
                    var endAngleProp = entityType.GetProperty("EndAngle");
                    if (centerProp != null && radiusProp != null && startAngleProp != null && endAngleProp != null)
                    {
                        var center = centerProp.GetValue(entity);
                        var radius = radiusProp.GetValue(entity);
                        var startAngle = startAngleProp.GetValue(entity);
                        var endAngle = endAngleProp.GetValue(entity);
                        if (center != null && radius != null && startAngle != null && endAngle != null)
                        {
                            var cx = GetCoordinate(center, "X");
                            var cy = GetCoordinate(center, "Y");
                            var r = Convert.ToDouble(radius);
                            var sa = Convert.ToDouble(startAngle);
                            var ea = Convert.ToDouble(endAngle);
                            gm.Polylines.Add(ApproximateArc(cx, cy, r, sa, ea, 36));
                        }
                    }
                }
                else if (typeName == "Spline")
                {
                    // Approximate spline with control points
                    var ctrlPtsProp = entityType.GetProperty("ControlPoints");
                    if (ctrlPtsProp != null)
                    {
                        var ctrlPts = ctrlPtsProp.GetValue(entity);
                        if (ctrlPts is IEnumerable enumerable)
                        {
                            var pts = new List<(double, double)>();
                            foreach (var pt in enumerable)
                            {
                                if (pt != null)
                                {
                                    var x = GetCoordinate(pt, "X");
                                    var y = GetCoordinate(pt, "Y");
                                    pts.Add((x, y));
                                }
                            }
                            if (pts.Count > 0) gm.Polylines.Add(pts);
                        }
                    }
                }
                else if (typeName == "Insert")
                {
                    // Handle block inserts - explode blocks by applying transform
                    var blockProp = entityType.GetProperty("Block");
                    var insertionProp = entityType.GetProperty("Position");
                    var scaleProp = entityType.GetProperty("Scale");
                    var rotationProp = entityType.GetProperty("Rotation");

                    if (blockProp != null && insertionProp != null)
                    {
                        var block = blockProp.GetValue(entity);
                        var insertion = insertionProp.GetValue(entity);
                        
                        var sx = 1.0;
                        var sy = 1.0;
                        var rotation = 0.0;

                        if (scaleProp != null)
                        {
                            var scale = scaleProp.GetValue(entity);
                            if (scale != null)
                            {
                                sx = GetCoordinate(scale, "X");
                                sy = GetCoordinate(scale, "Y");
                            }
                        }

                        if (rotationProp != null)
                        {
                            var rot = rotationProp.GetValue(entity);
                            if (rot != null) rotation = Convert.ToDouble(rot);
                        }

                        if (block != null && insertion != null)
                        {
                            var ix = GetCoordinate(insertion, "X");
                            var iy = GetCoordinate(insertion, "Y");

                            // Get entities from block
                            var entitiesProp = block.GetType().GetProperty("Entities");
                            if (entitiesProp != null)
                            {
                                var blockEntities = entitiesProp.GetValue(block);
                                if (blockEntities != null)
                                {
                                    var allProp = blockEntities.GetType().GetProperty("All");
                                    if (allProp != null)
                                    {
                                        var allEntities = allProp.GetValue(blockEntities);
                                        if (allEntities is IEnumerable enumerable)
                                        {
                                            foreach (var be in enumerable)
                                            {
                                                ProcessEntityWithTransform(be, gm, ix, iy, sx, sy, rotation);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ProcessEntity error for {typeName}: {ex.Message}");
            }
        }

        private void ProcessEntityWithTransform(object entity, GeometryModel gm, double ix, double iy, double sx, double sy, double rotation)
        {
            // Simple approach: process entity normally, then transform all points in the last added polyline
            int beforeCount = gm.Polylines.Count;
            ProcessEntity(entity, gm);
            
            // Transform newly added polylines
            for (int i = beforeCount; i < gm.Polylines.Count; i++)
            {
                var pl = gm.Polylines[i];
                var transformed = new List<(double, double)>();
                foreach (var (x, y) in pl)
                {
                    // Apply scale
                    var scaledX = x * sx;
                    var scaledY = y * sy;
                    
                    // Apply rotation
                    var rad = rotation * Math.PI / 180.0;
                    var rotatedX = scaledX * Math.Cos(rad) - scaledY * Math.Sin(rad);
                    var rotatedY = scaledX * Math.Sin(rad) + scaledY * Math.Cos(rad);
                    
                    // Apply translation
                    transformed.Add((rotatedX + ix, rotatedY + iy));
                }
                gm.Polylines[i] = transformed;
            }
        }

        private double GetCoordinate(object obj, string propName)
        {
            if (obj == null) return 0.0;
            var prop = obj.GetType().GetProperty(propName);
            if (prop == null) return 0.0;
            var value = prop.GetValue(obj);
            if (value == null) return 0.0;
            return Convert.ToDouble(value);
        }

        private List<(double X, double Y)> ApproximateCircle(double cx, double cy, double r, int segments)
        {
            var pts = new List<(double X, double Y)>();
            for (int i = 0; i <= segments; i++)
            {
                double theta = 2.0 * Math.PI * i / segments;
                pts.Add((cx + r * Math.Cos(theta), cy + r * Math.Sin(theta)));
            }
            return pts;
        }

        private List<(double X, double Y)> ApproximateArc(double cx, double cy, double r, double startDeg, double endDeg, int segments)
        {
            double start = startDeg * Math.PI / 180.0;
            double end = endDeg * Math.PI / 180.0;
            if (end < start) end += 2.0 * Math.PI;
            
            var pts = new List<(double X, double Y)>();
            for (int i = 0; i <= segments; i++)
            {
                double t = start + (end - start) * i / segments;
                pts.Add((cx + r * Math.Cos(t), cy + r * Math.Sin(t)));
            }
            return pts;
        }

        private GeometryModel ImportAsciiDxfFallback(string dxfPath)
        {
            var gm = new GeometryModel();
            
            try
            {
                var lines = File.ReadAllLines(dxfPath);
                bool inEntities = false;
                string? currentEntity = null;
                var currentPoints = new List<(double X, double Y)>();
                double x = 0, y = 0;
                double cx = 0, cy = 0, r = 0, startAngle = 0, endAngle = 0;
                int groupCode = -1;

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    
                    // Parse group code
                    if (int.TryParse(line, out var code))
                    {
                        groupCode = code;
                        
                        // Check for sections
                        if (groupCode == 0 && i + 1 < lines.Length)
                        {
                            var nextLine = lines[i + 1].Trim();
                            
                            if (nextLine == "SECTION" && i + 3 < lines.Length && lines[i + 3].Trim() == "ENTITIES")
                            {
                                inEntities = true;
                                continue;
                            }
                            else if (nextLine == "ENDSEC")
                            {
                                inEntities = false;
                                continue;
                            }
                            
                            if (inEntities)
                            {
                                // Save previous entity if any
                                if (currentEntity != null && currentPoints.Count > 0)
                                {
                                    gm.Polylines.Add(new List<(double, double)>(currentPoints));
                                    currentPoints.Clear();
                                }
                                
                                // Start new entity
                                if (nextLine == "LINE" || nextLine == "LWPOLYLINE" || nextLine == "POLYLINE")
                                {
                                    currentEntity = nextLine;
                                    currentPoints.Clear();
                                }
                                else if (nextLine == "CIRCLE")
                                {
                                    currentEntity = "CIRCLE";
                                    cx = cy = r = 0;
                                }
                                else if (nextLine == "ARC")
                                {
                                    currentEntity = "ARC";
                                    cx = cy = r = startAngle = endAngle = 0;
                                }
                                else
                                {
                                    currentEntity = null;
                                }
                            }
                        }
                        continue;
                    }
                    
                    if (!inEntities || currentEntity == null) continue;
                    
                    // Parse coordinate values
                    if (currentEntity == "LINE" || currentEntity == "LWPOLYLINE" || currentEntity == "POLYLINE")
                    {
                        if (groupCode == 10 && double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out x))
                        {
                            // X coordinate - wait for Y
                        }
                        else if (groupCode == 20 && double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                        {
                            currentPoints.Add((x, y));
                        }
                        else if (groupCode == 11 && double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out x))
                        {
                            // Second point X for LINE
                        }
                        else if (groupCode == 21 && double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                        {
                            // Second point Y for LINE
                            currentPoints.Add((x, y));
                        }
                    }
                    else if (currentEntity == "CIRCLE")
                    {
                        if (groupCode == 10 && double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out cx)) { }
                        else if (groupCode == 20 && double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out cy)) { }
                        else if (groupCode == 40 && double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out r))
                        {
                            gm.Polylines.Add(ApproximateCircle(cx, cy, r, 48));
                            currentEntity = null;
                        }
                    }
                    else if (currentEntity == "ARC")
                    {
                        if (groupCode == 10 && double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out cx)) { }
                        else if (groupCode == 20 && double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out cy)) { }
                        else if (groupCode == 40 && double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out r)) { }
                        else if (groupCode == 50 && double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out startAngle)) { }
                        else if (groupCode == 51 && double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out endAngle))
                        {
                            gm.Polylines.Add(ApproximateArc(cx, cy, r, startAngle, endAngle, 36));
                            currentEntity = null;
                        }
                    }
                }
                
                // Add last entity if any
                if (currentEntity != null && currentPoints.Count > 0)
                {
                    gm.Polylines.Add(currentPoints);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ASCII DXF fallback failed: " + ex.Message);
            }
            
            return gm;
        }
    }
}
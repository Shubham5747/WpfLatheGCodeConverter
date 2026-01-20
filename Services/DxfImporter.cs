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
                var doc = DxfDocument.Load(dxfPath);
                if (doc == null) return null;

                var gm = new GeometryModel();
                var entitiesObj = doc.Entities;
                if (entitiesObj == null) return gm;

                // Use reflection to iterate through all entity collections
                var entitiesType = entitiesObj.GetType();
                
                // Process LINE entities
                try
                {
                    var linesProp = entitiesType.GetProperty("Lines");
                    if (linesProp != null)
                    {
                        var linesEnum = linesProp.GetValue(entitiesObj) as IEnumerable;
                        if (linesEnum != null)
                        {
                            foreach (var lineObj in linesEnum)
                            {
                                ProcessEntityReflection(lineObj, gm);
                            }
                        }
                    }
                }
                catch { /* skip if Lines collection doesn't exist */ }

                // Process CIRCLE entities
                try
                {
                    var circlesProp = entitiesType.GetProperty("Circles");
                    if (circlesProp != null)
                    {
                        var circlesEnum = circlesProp.GetValue(entitiesObj) as IEnumerable;
                        if (circlesEnum != null)
                        {
                            foreach (var circleObj in circlesEnum)
                            {
                                ProcessEntityReflection(circleObj, gm);
                            }
                        }
                    }
                }
                catch { /* skip if Circles collection doesn't exist */ }

                // Process ARC entities
                try
                {
                    var arcsProp = entitiesType.GetProperty("Arcs");
                    if (arcsProp != null)
                    {
                        var arcsEnum = arcsProp.GetValue(entitiesObj) as IEnumerable;
                        if (arcsEnum != null)
                        {
                            foreach (var arcObj in arcsEnum)
                            {
                                ProcessEntityReflection(arcObj, gm);
                            }
                        }
                    }
                }
                catch { /* skip if Arcs collection doesn't exist */ }

                // Process SPLINE entities
                try
                {
                    var splinesProp = entitiesType.GetProperty("Splines");
                    if (splinesProp != null)
                    {
                        var splinesEnum = splinesProp.GetValue(entitiesObj) as IEnumerable;
                        if (splinesEnum != null)
                        {
                            foreach (var splineObj in splinesEnum)
                            {
                                ProcessEntityReflection(splineObj, gm);
                            }
                        }
                    }
                }
                catch { /* skip if Splines collection doesn't exist */ }

                // Process INSERT (block reference) entities
                try
                {
                    var insertsProp = entitiesType.GetProperty("Inserts");
                    if (insertsProp != null)
                    {
                        var insertsEnum = insertsProp.GetValue(entitiesObj) as IEnumerable;
                        if (insertsEnum != null)
                        {
                            foreach (var insertObj in insertsEnum)
                            {
                                ProcessEntityReflection(insertObj, gm);
                            }
                        }
                    }
                }
                catch { /* skip if Inserts collection doesn't exist */ }

                // Try various polyline collection names (LwPolylines, Polylines, etc.)
                foreach (var propName in new[] { "LwPolylines", "Polylines2D", "Polylines3D", "Polylines" })
                {
                    try
                    {
                        var polyProp = entitiesType.GetProperty(propName);
                        if (polyProp != null)
                        {
                            var polyEnum = polyProp.GetValue(entitiesObj) as IEnumerable;
                            if (polyEnum != null)
                            {
                                foreach (var polyObj in polyEnum)
                                {
                                    ProcessEntityReflection(polyObj, gm);
                                }
                            }
                        }
                    }
                    catch { /* skip if collection doesn't exist */ }
                }

                return gm;
            }
            catch
            {
                return null;
            }
        }

        private void ProcessEntityReflection(object entity, GeometryModel gm)
        {
            if (entity == null) return;
            var entityType = entity.GetType();
            var typeName = entityType.Name;

            try
            {
                if (typeName == "Line")
                {
                    // Extract start and end points using reflection
                    var startProp = entityType.GetProperty("StartPoint");
                    var endProp = entityType.GetProperty("EndPoint");
                    if (startProp != null && endProp != null)
                    {
                        var start = startProp.GetValue(entity);
                        var end = endProp.GetValue(entity);
                        if (start != null && end != null)
                        {
                            double x1 = GetCoordinate(start, "X");
                            double y1 = GetCoordinate(start, "Y");
                            double x2 = GetCoordinate(end, "X");
                            double y2 = GetCoordinate(end, "Y");
                            gm.Polylines.Add(new List<(double X, double Y)> { (x1, y1), (x2, y2) });
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
                            double cx = GetCoordinate(center, "X");
                            double cy = GetCoordinate(center, "Y");
                            double r = Convert.ToDouble(radius);
                            gm.Polylines.Add(ApproximateCircle(cx, cy, r, 64));
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
                            double cx = GetCoordinate(center, "X");
                            double cy = GetCoordinate(center, "Y");
                            double r = Convert.ToDouble(radius);
                            double sa = Convert.ToDouble(startAngle);
                            double ea = Convert.ToDouble(endAngle);
                            gm.Polylines.Add(ApproximateArc(cx, cy, r, sa, ea, 48));
                        }
                    }
                }
                else if (typeName == "Spline")
                {
                    var ctrlPointsProp = entityType.GetProperty("ControlPoints");
                    if (ctrlPointsProp != null)
                    {
                        var ctrlPoints = ctrlPointsProp.GetValue(entity) as IEnumerable;
                        if (ctrlPoints != null)
                        {
                            var polyline = new List<(double X, double Y)>();
                            foreach (var pt in ctrlPoints)
                            {
                                if (pt != null)
                                {
                                    double x = GetCoordinate(pt, "X");
                                    double y = GetCoordinate(pt, "Y");
                                    polyline.Add((x, y));
                                }
                            }
                            if (polyline.Count > 0)
                                gm.Polylines.Add(polyline);
                        }
                    }
                }
                else if (typeName.Contains("Polyline") || typeName == "LwPolyline")
                {
                    // Handle various polyline types
                    var vertexesProp = entityType.GetProperty("Vertexes");
                    var isClosedProp = entityType.GetProperty("IsClosed");
                    if (vertexesProp != null)
                    {
                        var vertexes = vertexesProp.GetValue(entity) as IEnumerable;
                        bool isClosed = false;
                        if (isClosedProp != null)
                        {
                            var closedVal = isClosedProp.GetValue(entity);
                            if (closedVal != null)
                                isClosed = Convert.ToBoolean(closedVal);
                        }

                        if (vertexes != null)
                        {
                            var polyline = new List<(double X, double Y)>();
                            foreach (var vertex in vertexes)
                            {
                                if (vertex != null)
                                {
                                    // Try Position property first
                                    var posProp = vertex.GetType().GetProperty("Position");
                                    if (posProp != null)
                                    {
                                        var pos = posProp.GetValue(vertex);
                                        if (pos != null)
                                        {
                                            double x = GetCoordinate(pos, "X");
                                            double y = GetCoordinate(pos, "Y");
                                            polyline.Add((x, y));
                                        }
                                    }
                                    else
                                    {
                                        // Try direct X, Y properties
                                        double x = GetCoordinate(vertex, "X");
                                        double y = GetCoordinate(vertex, "Y");
                                        polyline.Add((x, y));
                                    }
                                }
                            }
                            if (isClosed && polyline.Count > 0)
                            {
                                polyline.Add(polyline[0]);
                            }
                            if (polyline.Count > 0)
                                gm.Polylines.Add(polyline);
                        }
                    }
                }
                else if (typeName == "Insert")
                {
                    // Handle block INSERT entities
                    var blockProp = entityType.GetProperty("Block");
                    var posProp = entityType.GetProperty("Position");
                    var scaleProp = entityType.GetProperty("Scale");
                    var rotationProp = entityType.GetProperty("Rotation");

                    if (blockProp != null && posProp != null)
                    {
                        var block = blockProp.GetValue(entity);
                        var pos = posProp.GetValue(entity);
                        if (block != null && pos != null)
                        {
                            double offsetX = GetCoordinate(pos, "X");
                            double offsetY = GetCoordinate(pos, "Y");
                            double scaleX = 1.0, scaleY = 1.0, rotation = 0.0;

                            if (scaleProp != null)
                            {
                                var scale = scaleProp.GetValue(entity);
                                if (scale != null)
                                {
                                    scaleX = GetCoordinate(scale, "X");
                                    scaleY = GetCoordinate(scale, "Y");
                                }
                            }

                            if (rotationProp != null)
                            {
                                var rot = rotationProp.GetValue(entity);
                                if (rot != null)
                                    rotation = Convert.ToDouble(rot) * Math.PI / 180.0;
                            }

                            // Get block entities
                            var blockType = block.GetType();
                            var entitiesProp = blockType.GetProperty("Entities");
                            if (entitiesProp != null)
                            {
                                var blockEntities = entitiesProp.GetValue(block) as IEnumerable;
                                if (blockEntities != null)
                                {
                                    foreach (var blockEntity in blockEntities)
                                    {
                                        ProcessBlockEntity(blockEntity, gm, offsetX, offsetY, scaleX, scaleY, rotation);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Skip problematic entities
            }
        }

        private void ProcessBlockEntity(object entity, GeometryModel gm, double offsetX, double offsetY, double scaleX, double scaleY, double rotation)
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
                            double x1 = GetCoordinate(start, "X");
                            double y1 = GetCoordinate(start, "Y");
                            double x2 = GetCoordinate(end, "X");
                            double y2 = GetCoordinate(end, "Y");
                            var p1 = TransformPoint(x1, y1, offsetX, offsetY, scaleX, scaleY, rotation);
                            var p2 = TransformPoint(x2, y2, offsetX, offsetY, scaleX, scaleY, rotation);
                            gm.Polylines.Add(new List<(double X, double Y)> { p1, p2 });
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
                            double cx = GetCoordinate(center, "X");
                            double cy = GetCoordinate(center, "Y");
                            double r = Convert.ToDouble(radius);
                            var transformedCenter = TransformPoint(cx, cy, offsetX, offsetY, scaleX, scaleY, rotation);
                            double scaledRadius = r * Math.Max(scaleX, scaleY);
                            gm.Polylines.Add(ApproximateCircle(transformedCenter.X, transformedCenter.Y, scaledRadius, 64));
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
                            double cx = GetCoordinate(center, "X");
                            double cy = GetCoordinate(center, "Y");
                            double r = Convert.ToDouble(radius);
                            double sa = Convert.ToDouble(startAngle);
                            double ea = Convert.ToDouble(endAngle);
                            var transformedCenter = TransformPoint(cx, cy, offsetX, offsetY, scaleX, scaleY, rotation);
                            double scaledRadius = r * Math.Max(scaleX, scaleY);
                            double rotatedStartAngle = sa + (rotation * 180.0 / Math.PI);
                            double rotatedEndAngle = ea + (rotation * 180.0 / Math.PI);
                            gm.Polylines.Add(ApproximateArc(transformedCenter.X, transformedCenter.Y, scaledRadius, rotatedStartAngle, rotatedEndAngle, 48));
                        }
                    }
                }
                else if (typeName.Contains("Polyline") || typeName == "LwPolyline")
                {
                    var vertexesProp = entityType.GetProperty("Vertexes");
                    var isClosedProp = entityType.GetProperty("IsClosed");
                    if (vertexesProp != null)
                    {
                        var vertexes = vertexesProp.GetValue(entity) as IEnumerable;
                        bool isClosed = false;
                        if (isClosedProp != null)
                        {
                            var closedVal = isClosedProp.GetValue(entity);
                            if (closedVal != null)
                                isClosed = Convert.ToBoolean(closedVal);
                        }

                        if (vertexes != null)
                        {
                            var polyline = new List<(double X, double Y)>();
                            foreach (var vertex in vertexes)
                            {
                                if (vertex != null)
                                {
                                    var posProp = vertex.GetType().GetProperty("Position");
                                    if (posProp != null)
                                    {
                                        var pos = posProp.GetValue(vertex);
                                        if (pos != null)
                                        {
                                            double x = GetCoordinate(pos, "X");
                                            double y = GetCoordinate(pos, "Y");
                                            var transformed = TransformPoint(x, y, offsetX, offsetY, scaleX, scaleY, rotation);
                                            polyline.Add(transformed);
                                        }
                                    }
                                    else
                                    {
                                        double x = GetCoordinate(vertex, "X");
                                        double y = GetCoordinate(vertex, "Y");
                                        var transformed = TransformPoint(x, y, offsetX, offsetY, scaleX, scaleY, rotation);
                                        polyline.Add(transformed);
                                    }
                                }
                            }
                            if (isClosed && polyline.Count > 0)
                            {
                                polyline.Add(polyline[0]);
                            }
                            if (polyline.Count > 0)
                                gm.Polylines.Add(polyline);
                        }
                    }
                }
            }
            catch
            {
                // Skip problematic entities
            }
        }

        private double GetCoordinate(object point, string coordName)
        {
            if (point == null) return 0.0;
            var pointType = point.GetType();
            var prop = pointType.GetProperty(coordName);
            if (prop != null)
            {
                var val = prop.GetValue(point);
                if (val != null)
                    return Convert.ToDouble(val);
            }
            return 0.0;
        }

        private (double X, double Y) TransformPoint(double x, double y, double offsetX, double offsetY, double scaleX, double scaleY, double rotation)
        {
            // Apply scale
            double sx = x * scaleX;
            double sy = y * scaleY;

            // Apply rotation
            double cos = Math.Cos(rotation);
            double sin = Math.Sin(rotation);
            double rx = sx * cos - sy * sin;
            double ry = sx * sin + sy * cos;

            // Apply translation
            return (rx + offsetX, ry + offsetY);
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
            
            // Normalize angles
            while (end < start) end += 2.0 * Math.PI;
            
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
            var lines = File.ReadAllLines(dxfPath);
            
            int i = 0;
            while (i < lines.Length)
            {
                var line = lines[i].Trim();
                
                // Look for entity type (group code 0)
                if (line == "0")
                {
                    i++;
                    if (i >= lines.Length) break;
                    var entityType = lines[i].Trim();
                    
                    if (entityType == "LINE")
                    {
                        var lineData = ParseLineEntity(lines, ref i);
                        if (lineData != null)
                            gm.Polylines.Add(lineData);
                    }
                    else if (entityType == "LWPOLYLINE")
                    {
                        var polyData = ParseLwPolylineEntity(lines, ref i);
                        if (polyData != null && polyData.Count > 0)
                            gm.Polylines.Add(polyData);
                    }
                    else if (entityType == "CIRCLE")
                    {
                        var circleData = ParseCircleEntity(lines, ref i);
                        if (circleData != null && circleData.Count > 0)
                            gm.Polylines.Add(circleData);
                    }
                    else if (entityType == "ARC")
                    {
                        var arcData = ParseArcEntity(lines, ref i);
                        if (arcData != null && arcData.Count > 0)
                            gm.Polylines.Add(arcData);
                    }
                }
                i++;
            }
            
            return gm;
        }

        private List<(double X, double Y)>? ParseLineEntity(string[] lines, ref int index)
        {
            double x1 = 0, y1 = 0, x2 = 0, y2 = 0;
            bool hasX1 = false, hasY1 = false, hasX2 = false, hasY2 = false;
            
            while (index < lines.Length)
            {
                var code = lines[index].Trim();
                index++;
                if (index >= lines.Length) break;
                
                var value = lines[index].Trim();
                
                if (code == "0") { index--; break; } // next entity
                
                if (code == "10" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out x1)) hasX1 = true;
                else if (code == "20" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out y1)) hasY1 = true;
                else if (code == "11" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out x2)) hasX2 = true;
                else if (code == "21" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out y2)) hasY2 = true;
                
                index++;
            }
            
            if (hasX1 && hasY1 && hasX2 && hasY2)
                return new List<(double X, double Y)> { (x1, y1), (x2, y2) };
            return null;
        }

        private List<(double X, double Y)>? ParseLwPolylineEntity(string[] lines, ref int index)
        {
            var points = new List<(double X, double Y)>();
            double currentX = 0, currentY = 0;
            bool hasX = false, hasY = false;
            
            while (index < lines.Length)
            {
                var code = lines[index].Trim();
                index++;
                if (index >= lines.Length) break;
                
                var value = lines[index].Trim();
                
                if (code == "0") { index--; break; } // next entity
                
                if (code == "10")
                {
                    if (hasX && hasY)
                    {
                        points.Add((currentX, currentY));
                        hasX = false;
                        hasY = false;
                    }
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out currentX))
                        hasX = true;
                }
                else if (code == "20")
                {
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out currentY))
                        hasY = true;
                }
                
                index++;
            }
            
            // Add last point if complete
            if (hasX && hasY)
                points.Add((currentX, currentY));
            
            return points.Count > 0 ? points : null;
        }

        private List<(double X, double Y)>? ParseCircleEntity(string[] lines, ref int index)
        {
            double cx = 0, cy = 0, r = 0;
            bool hasCx = false, hasCy = false, hasR = false;
            
            while (index < lines.Length)
            {
                var code = lines[index].Trim();
                index++;
                if (index >= lines.Length) break;
                
                var value = lines[index].Trim();
                
                if (code == "0") { index--; break; } // next entity
                
                if (code == "10" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out cx)) hasCx = true;
                else if (code == "20" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out cy)) hasCy = true;
                else if (code == "40" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out r)) hasR = true;
                
                index++;
            }
            
            if (hasCx && hasCy && hasR)
                return ApproximateCircle(cx, cy, r, 64);
            return null;
        }

        private List<(double X, double Y)>? ParseArcEntity(string[] lines, ref int index)
        {
            double cx = 0, cy = 0, r = 0, startAngle = 0, endAngle = 0;
            bool hasCx = false, hasCy = false, hasR = false, hasStart = false, hasEnd = false;
            
            while (index < lines.Length)
            {
                var code = lines[index].Trim();
                index++;
                if (index >= lines.Length) break;
                
                var value = lines[index].Trim();
                
                if (code == "0") { index--; break; } // next entity
                
                if (code == "10" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out cx)) hasCx = true;
                else if (code == "20" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out cy)) hasCy = true;
                else if (code == "40" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out r)) hasR = true;
                else if (code == "50" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out startAngle)) hasStart = true;
                else if (code == "51" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out endAngle)) hasEnd = true;
                
                index++;
            }
            
            if (hasCx && hasCy && hasR && hasStart && hasEnd)
                return ApproximateArc(cx, cy, r, startAngle, endAngle, 48);
            return null;
        }
    }
}
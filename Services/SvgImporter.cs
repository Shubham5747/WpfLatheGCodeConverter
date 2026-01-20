using System.Collections.Generic;
using System.Xml.Linq;
using WpfLatheGCodeConverter.Models;

namespace WpfLatheGCodeConverter.Services
{
    public class SvgImporter
    {
        public GeometryModel Import(string svgPath)
        {
            var gm = new GeometryModel();
            
            if (string.IsNullOrWhiteSpace(svgPath))
                return gm;
            
            try
            {
                var doc = XDocument.Load(svgPath);
                if (doc.Root == null)
                    return gm;
                    
                XNamespace ns = doc.Root.Name.Namespace;
                
                foreach (var el in doc.Descendants(ns + "path"))
                {
                    var dAttr = el.Attribute("d");
                    if (dAttr == null) continue;
                    
                    var d = dAttr.Value;
                    if (string.IsNullOrWhiteSpace(d)) continue;
                    
                    var pts = SimplePathParse(d);
                    if (pts.Count > 0) gm.Polylines.Add(pts);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SVG import failed: " + ex.Message);
            }
            
            return gm;
        }

        private List<(double X, double Y)> SimplePathParse(string d)
        {
            var outPts = new List<(double, double)>();
            var tokens = d.Replace(",", " ").Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            int i = 0;
            char? cmd = null;
            while (i < tokens.Length)
            {
                var t = tokens[i];
                if (char.IsLetter(t[0])) { cmd = t[0]; i++; continue; }
                if (cmd == 'M' || cmd == 'L')
                {
                    if (i + 1 >= tokens.Length) break;
                    if (double.TryParse(tokens[i], out var x) && double.TryParse(tokens[i + 1], out var y))
                    {
                        outPts.Add((x, y));
                    }
                    i += 2;
                }
                else i++;
            }
            return outPts;
        }
    }
}
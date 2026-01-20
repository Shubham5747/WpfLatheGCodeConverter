using System.Globalization;
using System.Text;
using WpfLatheGCodeConverter.Models;

namespace WpfLatheGCodeConverter.Services
{
    public class GCodeGenerator
    {
        public string GenerateTurningGCode(GeometryModel geom, JobDefinition job, double defaultFeed, int defaultSpindle, double scale = 1.0)
        {
            var nfi = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("(Generated - Turning)");
            sb.AppendLine(job.Units == "mm" ? "G21" : "G20");
            sb.AppendLine("G90");
            foreach (var tool in job.Tools)
            {
                sb.AppendLine($"T{tool.TNumber:00}");
                sb.AppendLine("M6");
                sb.AppendLine($"S{tool.Spindle}");
                sb.AppendLine("M3");
                sb.AppendLine($"G0 Z{job.SafeZ.ToString(nfi)}");
                foreach (var pl in geom.Polylines)
                {
                    if (pl.Count == 0) continue;
                    var first = pl[0];
                    sb.AppendLine($"G0 X{(first.X * scale).ToString(nfi)} Z{(first.Y * scale).ToString(nfi)}");
                    foreach (var p in pl) sb.AppendLine($"G1 X{(p.X * scale).ToString(nfi)} Z{(p.Y * scale).ToString(nfi)} F{tool.Feed.ToString(nfi)}");
                    sb.AppendLine($"G0 Z{job.SafeZ.ToString(nfi)}");
                }
                sb.AppendLine("M5");
            }
            sb.AppendLine("M30");
            return sb.ToString();
        }
    }
}
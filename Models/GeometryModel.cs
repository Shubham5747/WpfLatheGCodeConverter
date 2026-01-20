using System.Collections.Generic;

namespace WpfLatheGCodeConverter.Models
{
    public class GeometryModel
    {
        public List<List<(double X, double Y)>> Polylines { get; } = new();
    }

    public class JobDefinition
    {
        public string Units { get; set; } = "mm";
        public double SafeZ { get; set; } = 5.0;
        public double DepthPerPass { get; set; } = 1.0;
        public List<Tool> Tools { get; set; } = new();
    }
}
namespace WpfLatheGCodeConverter.Models
{
    public class Tool
    {
        public int TNumber { get; set; } = 1;
        public string Name { get; set; } = "Unnamed";
        public double Diameter { get; set; } = 6.0;
        public double Feed { get; set; } = 0.2;
        public int Spindle { get; set; } = 1200;
        public string DisplayText => $"T{TNumber:00} {Name} Ø{Diameter}";
    }
}
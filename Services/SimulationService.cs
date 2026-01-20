using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WpfLatheGCodeConverter.Services
{
    public class SimulationService
    {
        private CancellationTokenSource? cts;
        public List<(double X, double Z)> ParseGCodeToPath(string gcode)
        {
            var path = new List<(double X, double Z)>();
            var lines = gcode.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            double curX = 0, curZ = 0;
            var rx = new Regex(@"X\s*([-+]?[0-9]*\.?[0-9]+)");
            var rz = new Regex(@"Z\s*([-+]?[0-9]*\.?[0-9]+)");
            foreach (var line in lines)
            {
                if (line.StartsWith("G0") || line.StartsWith("G1"))
                {
                    var mx = rx.Match(line);
                    var mz = rz.Match(line);
                    if (mx.Success) curX = double.Parse(mx.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    if (mz.Success) curZ = double.Parse(mz.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    path.Add((curX, curZ));
                }
            }
            return path;
        }

        public void StartSimulation(List<(double X, double Z)> path, double speedMultiplier, Action<(double X, double Z)> onPosition)
        {
            cts?.Cancel();
            cts = new CancellationTokenSource();
            var token = cts.Token;
            Task.Run(async () =>
            {
                foreach (var pt in path)
                {
                    if (token.IsCancellationRequested) break;
                    onPosition(pt);
                    await Task.Delay(TimeSpan.FromMilliseconds(200 / speedMultiplier), token);
                }
            }, token);
        }

        public void Pause() { cts?.Cancel(); cts = null; }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Shapes;
using System.Windows.Threading;
using WpfLatheGCodeConverter.Services;
using WpfLatheGCodeConverter.ViewModels;

namespace WpfLatheGCodeConverter
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel vm;
        private List<(double X1, double Y1, double X2, double Y2)>? lastSegments;

        public MainWindow()
        {
            InitializeComponent();

            // Use existing DataContext if set (e.g. from App.xaml), otherwise create it.
            if (DataContext is MainViewModel existingVm)
            {
                vm = existingVm;
            }
            else
            {
                vm = new MainViewModel();
                DataContext = vm;
            }

            // Subscribe to preview events once
            vm.PreviewRequest -= Vm_PreviewRequest;
            vm.PreviewRequest += Vm_PreviewRequest;

            // Re-render if canvas size changes (fit to new size)
            CanvasPreview.SizeChanged += (_, __) => {
                Dispatcher.BeginInvoke((Action)(() => RenderSegments(lastSegments)), DispatcherPriority.Render);
            };
        }

        private async void BtnTestEz_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "DXF files|*.dxf|All files|*.*"
                };
                if (ofd.ShowDialog() != true) return;

                var path = ofd.FileName;
                var wrapper = new EzDxfWrapper();

                // Run wrapper (synchronously)
                var gm = wrapper.ExtractGeometryViaEzDxf(path);

                // Build diagnostic message
                var sb = new StringBuilder();
                sb.AppendLine("File: " + path);
                sb.AppendLine("Wrapper returned null: " + (gm == null));
                sb.AppendLine("Wrapper LastJsonPath: " + (wrapper.LastJsonPath ?? "(null)"));
                sb.AppendLine("Wrapper LastLogPath: " + (wrapper.LastLogPath ?? "(null)"));

                int polyCount = gm?.Polylines?.Count ?? 0;
                sb.AppendLine("GeometryModel.Polylines.Count: " + polyCount);

                // If JSON exists, count "polyline" entries directly from it
                if (!string.IsNullOrEmpty(wrapper.LastJsonPath) && File.Exists(wrapper.LastJsonPath))
                {
                    try
                    {
                        var jsonText = File.ReadAllText(wrapper.LastJsonPath);
                        int directCount = 0;
                        // quick heuristic: count occurrences of '"type": "polyline"' (case-insensitive)
                        directCount = jsonText.Split(new[] { "\"type\"" }, StringSplitOptions.None)
                                              .Count(s => s.IndexOf("polyline", StringComparison.OrdinalIgnoreCase) >= 0);
                        sb.AppendLine("Direct JSON polyline (heuristic) count: " + directCount);
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("Failed reading JSON: " + ex.Message);
                    }
                }

                // If log exists, show first ~80 lines
                if (!string.IsNullOrEmpty(wrapper.LastLogPath) && File.Exists(wrapper.LastLogPath))
                {
                    try
                    {
                        var lines = File.ReadAllLines(wrapper.LastLogPath);
                        sb.AppendLine("--- Log (first 80 lines) ---");
                        for (int i = 0; i < Math.Min(80, lines.Length); i++)
                        {
                            sb.AppendLine(lines[i]);
                        }
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("Failed reading log: " + ex.Message);
                    }
                }

                MessageBox.Show(sb.ToString(), "EzDxf Test", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Test EzDxf failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void Vm_PreviewRequest(object? sender, PreviewRequestEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                lastSegments = e.Segments;
                RenderSegments(lastSegments);
            });
        }

        private void RenderSegments(List<(double X1, double Y1, double X2, double Y2)>? segs)
        {
            CanvasPreview.Children.Clear();
            if (segs == null || segs.Count == 0) return;

            // compute bounding box
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            foreach (var s in segs)
            {
                minX = Math.Min(minX, Math.Min(s.X1, s.X2));
                minY = Math.Min(minY, Math.Min(s.Y1, s.Y2));
                maxX = Math.Max(maxX, Math.Max(s.X1, s.X2));
                maxY = Math.Max(maxY, Math.Max(s.Y1, s.Y2));
            }

            double modelW = maxX - minX;
            double modelH = maxY - minY;
            if (modelW <= 0 || modelH <= 0) return;

            double canvasW = CanvasPreview.ActualWidth;
            double canvasH = CanvasPreview.ActualHeight;
            if (canvasW <= 0) canvasW = CanvasPreview.Width;
            if (canvasH <= 0) canvasH = CanvasPreview.Height;
            if (canvasW <= 0 || canvasH <= 0) return;

            const double marginFactor = 0.9;
            double scale = Math.Min(canvasW / modelW, canvasH / modelH) * marginFactor;

            double modelCx = (minX + maxX) / 2.0;
            double modelCy = (minY + maxY) / 2.0;
            double canvasCx = canvasW / 2.0;
            double canvasCy = canvasH / 2.0;

            foreach (var s in segs)
            {
                var line = new Line
                {
                    Stroke = System.Windows.Media.Brushes.Black,
                    StrokeThickness = 1.0,
                    X1 = (s.X1 - modelCx) * scale + canvasCx,
                    Y1 = (-(s.Y1 - modelCy)) * scale + canvasCy,
                    X2 = (s.X2 - modelCx) * scale + canvasCx,
                    Y2 = (-(s.Y2 - modelCy)) * scale + canvasCy
                };
                CanvasPreview.Children.Add(line);
            }
        }
    }
}
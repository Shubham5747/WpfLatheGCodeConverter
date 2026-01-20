using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using WpfLatheGCodeConverter.Models;
using WpfLatheGCodeConverter.Services;

namespace WpfLatheGCodeConverter.ViewModels
{
    public class PreviewRequestEventArgs : EventArgs
    {
        public PreviewRequestEventArgs(List<(double X1, double Y1, double X2, double Y2)> segments)
        {
            Segments = segments;
        }
        public List<(double X1, double Y1, double X2, double Y2)> Segments { get; }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        // UI collections / state
        public ObservableCollection<Tool> Tools { get; } = new();
        public Tool? SelectedTool { get; set; }

        public string Units { get; set; } = "mm";
        public double Scale { get; set; } = 1.0;
        public double DefaultFeed { get; set; } = 200;
        public int DefaultSpindle { get; set; } = 1200;
        public double SafeZ { get; set; } = 5.0;
        public double DepthPerPass { get; set; } = 1.0;

        public double SimulateSpeed { get; set; } = 1.0;
        public string LastGeneratedGCode { get; private set; } = string.Empty;

        // Commands
        public ICommand ImportFileCommand { get; }
        public ICommand ShowDiagnosticsCommand { get; }
        public ICommand AddToolCommand { get; }
        public ICommand EditToolCommand { get; }
        public ICommand RemoveToolCommand { get; }
        public ICommand GenerateGCodeCommand { get; }
        public ICommand SaveGCodeCommand { get; }
        public ICommand SimulatePlayCommand { get; }
        public ICommand SimulatePauseCommand { get; }

        // Events to communicate with the view
        public event EventHandler<PreviewRequestEventArgs>? PreviewRequest;
        public event EventHandler<(double X, double Z)>? SimulationPositionUpdated;

        // Services
        private readonly ImportService importService = new();
        private readonly GCodeGenerator gcodeGenerator = new();
        private readonly SimulationService simulationService = new();

        public MainViewModel()
        {
            // Commands
            ImportFileCommand = new RelayCommand(_ => ImportFile());
            ShowDiagnosticsCommand = new RelayCommand(_ => ShowDiagnostics(), _ => !string.IsNullOrEmpty(importService.LastDiagnosticsJsonPath) || !string.IsNullOrEmpty(importService.LastDiagnosticsLogPath));

            AddToolCommand = new RelayCommand(_ => AddTool());
            EditToolCommand = new RelayCommand(_ => EditTool(), _ => SelectedTool != null);
            RemoveToolCommand = new RelayCommand(_ => RemoveTool(), _ => SelectedTool != null);
            GenerateGCodeCommand = new RelayCommand(_ => GenerateGCode());
            SaveGCodeCommand = new RelayCommand(_ => SaveGCode(), _ => !string.IsNullOrEmpty(LastGeneratedGCode));

            SimulatePlayCommand = new RelayCommand(_ => StartSimulation(), _ => !string.IsNullOrEmpty(LastGeneratedGCode));
            SimulatePauseCommand = new RelayCommand(_ => PauseSimulation());

            // Seed a default tool
            Tools.Add(new Tool { TNumber = 1, Name = "DefaultTurningTool", Diameter = 6.0, Feed = 0.2, Spindle = 1200 });
        }

        private void ImportFile()
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Supported files|*.svg;*.dxf;*.png;*.jpg;*.jpeg;*.step;*.stp|All files|*.*"
            };
            if (ofd.ShowDialog() != true) return;

            try
            {
                var geom = importService.ImportFile(ofd.FileName);
                var segs = GeometryToSegments(geom);

                // DEBUG: show how many segments we received and diagnostics paths
                string diagJson = importService.LastDiagnosticsJsonPath ?? "(none)";
                string diagLog = importService.LastDiagnosticsLogPath ?? "(none)";
                MessageBox.Show($"Imported: {ofd.FileName}\nSegments: {segs.Count}\nDiag JSON: {diagJson}\nDiag Log: {diagLog}", "Import Debug");

                PreviewRequest?.Invoke(this, new PreviewRequestEventArgs(segs));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Import failed: " + ex.Message, "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            CommandManager.InvalidateRequerySuggested();
        }

        private void ShowDiagnostics()
        {
            var json = importService.LastDiagnosticsJsonPath;
            var log = importService.LastDiagnosticsLogPath;
            if (string.IsNullOrEmpty(json) && string.IsNullOrEmpty(log))
            {
                MessageBox.Show("No diagnostics available for the last import.");
                return;
            }

            var win = new DiagnosticsWindow(json, log);
            win.Owner = Application.Current.MainWindow;
            win.Show();
        }

        private List<(double X1, double Y1, double X2, double Y2)> GeometryToSegments(GeometryModel geom)
        {
            var outSegs = new List<(double, double, double, double)>();
            if (geom == null) return outSegs;
            foreach (var pl in geom.Polylines)
            {
                for (int i = 0; i + 1 < pl.Count; i++)
                {
                    outSegs.Add((pl[i].X, pl[i].Y, pl[i + 1].X, pl[i + 1].Y));
                }
            }
            return outSegs;
        }

        private void AddTool()
        {
            var t = new Tool { TNumber = Tools.Count + 1, Name = "T" + (Tools.Count + 1) };
            Tools.Add(t);
        }
        private void EditTool()
        {
            MessageBox.Show("Tool edit dialog not implemented in prototype.");
        }
        private void RemoveTool()
        {
            if (SelectedTool != null) Tools.Remove(SelectedTool);
        }

        private void GenerateGCode()
        {
            var job = new JobDefinition
            {
                Units = Units,
                SafeZ = SafeZ,
                DepthPerPass = DepthPerPass,
                Tools = new List<Tool>(Tools)
            };

            var geom = importService.LastGeometry ?? new GeometryModel();
            LastGeneratedGCode = gcodeGenerator.GenerateTurningGCode(geom, job, DefaultFeed, DefaultSpindle, Scale);
            MessageBox.Show("G-code generated (in-memory). Use Save G-code to write to file.");
            OnPropertyChanged(nameof(LastGeneratedGCode));
            CommandManager.InvalidateRequerySuggested();
        }

        private void SaveGCode()
        {
            var sfd = new Microsoft.Win32.SaveFileDialog { Filter = "G-code files|*.nc;*.gcode|All files|*.*", FileName = "output.gcode" };
            if (sfd.ShowDialog() != true) return;
            File.WriteAllText(sfd.FileName, LastGeneratedGCode);
            MessageBox.Show("Saved " + sfd.FileName);
        }

        private void StartSimulation()
        {
            if (string.IsNullOrEmpty(LastGeneratedGCode)) return;
            var path = simulationService.ParseGCodeToPath(LastGeneratedGCode);
            simulationService.StartSimulation(path, SimulateSpeed, pos =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    SimulationPositionUpdated?.Invoke(this, pos);
                });
            });
        }
        private void PauseSimulation()
        {
            simulationService.Pause();
        }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
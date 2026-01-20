using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace WpfLatheGCodeConverter
{
    public partial class DiagnosticsWindow : Window
    {
        private readonly string? jsonPath;
        private readonly string? logPath;

        public DiagnosticsWindow(string? jsonPath, string? logPath)
        {
            InitializeComponent();
            this.jsonPath = jsonPath;
            this.logPath = logPath;
            LoadContents();
        }

        private void LoadContents()
        {
            if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath))
            {
                try { TxtLog.Text = File.ReadAllText(logPath); }
                catch (Exception ex) { TxtLog.Text = "Failed to read log: " + ex.Message; }
            }
            else
            {
                TxtLog.Text = "Log not available.";
                BtnOpenLog.IsEnabled = false;
            }

            if (!string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath))
            {
                try { TxtJson.Text = File.ReadAllText(jsonPath); }
                catch (Exception ex) { TxtJson.Text = "Failed to read JSON: " + ex.Message; }
            }
            else
            {
                TxtJson.Text = "JSON not available.";
                BtnOpenJson.IsEnabled = false;
            }
        }

        private void BtnOpenLog_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
                }
                catch (Exception ex) { MessageBox.Show("Failed to open log: " + ex.Message); }
            }
        }

        private void BtnOpenJson_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(jsonPath) { UseShellExecute = true });
                }
                catch (Exception ex) { MessageBox.Show("Failed to open JSON: " + ex.Message); }
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadContents();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
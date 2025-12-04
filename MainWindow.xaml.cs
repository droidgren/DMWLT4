using DMWLT4.Properties;
using MWL4.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace MWL4
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
            AppendVersionToTitle();
        }

        protected override void OnClosed(EventArgs e)
        {
            Settings.Default.Save();
            base.OnClosed(e);
        }

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsDialog { Owner = this };
            dlg.ShowDialog();
        }

        private void OpenLogDirectoryMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"DMWLT4\Logs");

                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                Process.Start(new ProcessStartInfo(logDir) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to open log directory:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AppendVersionToTitle()
        {
            var asm = Assembly.GetExecutingAssembly();
            var ver = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? asm.GetName().Version?.ToString()
                   ?? "1.0";
            Title = $"{Title} v{ver}";
        }
    }
}
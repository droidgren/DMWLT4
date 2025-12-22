using DMWLT4.Properties;
using MWL4.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

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

            // Apply column settings on startup
            Loaded += (s, e) => UpdateColumnVisibility();
        }

        protected override void OnClosed(EventArgs e)
        {
            Settings.Default.Save();
            base.OnClosed(e);
        }

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsDialog { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                UpdateColumnVisibility();
            }
        }

        private void UpdateColumnVisibility()
        {
            var savedSetting = Settings.Default.VisibleColumns ?? string.Empty;
            var visibleSet = new HashSet<string>(savedSetting.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));

            foreach (var column in ResultsGrid.Columns)
            {
                string boundProperty = null;

                if (column is DataGridTextColumn textCol)
                {
                    if (textCol.Binding is Binding binding)
                        boundProperty = binding.Path.Path;
                }

                if (!string.IsNullOrEmpty(boundProperty))
                {
                    column.Visibility = visibleSet.Contains(boundProperty)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }
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

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var dg = (DataGrid)sender;
            if (DataContext is MainViewModel vm)
            {
                vm.SelectionChangedCommand.Execute(dg.SelectedItems?.Count ?? 0);
            }
        }
    }
}
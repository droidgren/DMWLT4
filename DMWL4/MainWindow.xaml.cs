using DMWLT4.Properties;
using MWL4.Infrastructure;
using MWL4.Models;
using MWL4.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace MWL4
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        // Column order as defined in the XAML, used by "Reset to default".
        private readonly List<DataGridColumn> _originalColumnOrder;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
            AppendVersionToTitle();

            _originalColumnOrder = ResultsGrid.Columns.ToList();

            // Put the cursor in the filter box when the filter bar is shown.
            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsFilterBarVisible) && _viewModel.IsFilterBarVisible)
                {
                    Dispatcher.BeginInvoke(new Action(() => FilterBox.Focus()), System.Windows.Threading.DispatcherPriority.Input);
                }
            };

            // Apply column settings on startup
            Loaded += (s, e) =>
            {
                UpdateColumnVisibility();
                ApplyColumnLayout(Settings.Default.ColumnLayout);
            };
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            Settings.Default.ColumnLayout = GetColumnLayout();
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            // The help window is not owned by this window, so close it explicitly.
            foreach (var help in Application.Current.Windows.OfType<HelpWindow>().ToList())
            {
                help.Close();
            }
            Settings.Default.Save();
            base.OnClosed(e);
        }

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsDialog { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                if (dlg.ColumnLayoutReset)
                {
                    ResetColumnLayout();
                }
                UpdateColumnVisibility();
                _viewModel.RefreshFilter();
                _viewModel.RefreshQueryKeys();
            }
        }

        private void TlsOptionsButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new TlsOptionsDialog(_viewModel.TlsIgnoreCertErrors, _viewModel.TlsClientCertThumbprint) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _viewModel.TlsIgnoreCertErrors = dlg.IgnoreCertErrors;
                _viewModel.TlsClientCertThumbprint = dlg.ClientCertThumbprint;
            }
        }

        private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Only react to double-clicks on a row, not on the header or scroll bars.
            var row = ItemsControl.ContainerFromElement(ResultsGrid, e.OriginalSource as DependencyObject) as DataGridRow;
            if (row?.Item is WorklistItem item)
            {
                new DatasetDetailsWindow(item) { Owner = this }.Show();
                e.Handled = true;
            }
        }

        private void UpdateColumnVisibility()
        {
            var savedSetting = Settings.Default.VisibleColumns ?? string.Empty;
            var visibleSet = new HashSet<string>(savedSetting.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));

            foreach (var column in ResultsGrid.Columns)
            {
                var boundProperty = ColumnLayout.GetBoundProperty(column);
                if (!string.IsNullOrEmpty(boundProperty))
                {
                    column.Visibility = visibleSet.Contains(boundProperty)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }
        }

        #region Column layout
        private string GetColumnLayout() => ColumnLayout.Save(ResultsGrid.Columns);

        private void ApplyColumnLayout(string layout) => ColumnLayout.Apply(ResultsGrid.Columns, _originalColumnOrder, layout);

        private void ResetColumnLayout()
        {
            ColumnLayout.Reset(_originalColumnOrder);
            Settings.Default.ColumnLayout = string.Empty;
        }
        #endregion

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

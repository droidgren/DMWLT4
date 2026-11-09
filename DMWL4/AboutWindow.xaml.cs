using MWL4.ViewModels;
using Serilog;
using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Navigation;

namespace MWL4
{
    public partial class AboutWindow : Window
    {
        private readonly CancellationTokenSource _closing = new CancellationTokenSource();

        public AboutWindow()
        {
            InitializeComponent();
            Loaded += async (s, e) =>
            {
                if (DataContext is AboutViewModel viewModel) await viewModel.CheckForUpdateAsync(_closing.Token);
            };
        }

        protected override void OnClosed(EventArgs e)
        {
            _closing.Cancel();
            base.OnClosed(e);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                // In .NET Framework 4.8, this opens the URL in the default browser.
                Process.Start(e.Uri.AbsoluteUri);
            }
            catch (Exception ex)
            {
                Log.ForContext<AboutWindow>().Warning(ex, "Could not open {url}", e.Uri);
                MessageBox.Show(this, "Could not open the browser:\n" + ex.Message, "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            e.Handled = true;
        }
    }
}

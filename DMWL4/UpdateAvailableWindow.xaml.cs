using MWL4.Infrastructure;
using Serilog;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace MWL4
{
    public partial class UpdateAvailableWindow : Window
    {
        public UpdateAvailableWindow(UpdateCheckResult update, string currentVersion)
        {
            InitializeComponent();
            VersionText.Text = $"Version {update.LatestVersion} is available. ({currentVersion} is installed)";
            ReleaseLink.NavigateUri = update.ReleaseUri;

            var notes = ReleaseNotesText.ToPlainText(update.ReleaseNotes);
            NotesBox.Text = notes;
            if (notes.Length == 0) NotesExpander.Visibility = Visibility.Collapsed;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void ReleaseLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                // On .NET Framework this opens the URL in the default browser.
                Process.Start(e.Uri.AbsoluteUri);
            }
            catch (Exception ex)
            {
                Log.ForContext<UpdateAvailableWindow>().Warning(ex, "Could not open {url}", e.Uri);
            }
            e.Handled = true;
        }
    }
}

using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace MWL4
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
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
            catch (Exception)
            {
                // Optionally handle/log errors. For simplicity, ignore.
            }
            e.Handled = true;
        }
    }
}
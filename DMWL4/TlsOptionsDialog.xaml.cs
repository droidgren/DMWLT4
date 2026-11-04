using MWL4.ViewModels;
using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Interop;

namespace MWL4
{
    /// <summary>
    /// TLS options: accept invalid server certificates and pick a client certificate from CurrentUser\My.
    /// Values are only returned to the caller on OK.
    /// </summary>
    public partial class TlsOptionsDialog : Window
    {
        public bool IgnoreCertErrors => IgnoreCertErrorsBox.IsChecked == true;
        public string ClientCertThumbprint { get; private set; }

        public TlsOptionsDialog(bool ignoreCertErrors, string clientCertThumbprint)
        {
            InitializeComponent();
            IgnoreCertErrorsBox.IsChecked = ignoreCertErrors;
            ClientCertThumbprint = clientCertThumbprint ?? string.Empty;
            UpdateCertificateText();
        }

        private void UpdateCertificateText()
        {
            if (string.IsNullOrWhiteSpace(ClientCertThumbprint))
            {
                CertificateText.Text = "None";
                return;
            }

            var cert = MainViewModel.FindCertificate(ClientCertThumbprint);
            CertificateText.Text = cert == null
                ? $"Not found in the certificate store (thumbprint {ClientCertThumbprint})"
                : Describe(cert);
        }

        private static string Describe(X509Certificate2 cert)
        {
            var name = cert.GetNameInfo(X509NameType.SimpleName, false);
            if (string.IsNullOrEmpty(name)) name = cert.Subject;
            return $"{name}\nValid until {cert.NotAfter:yyyy-MM-dd}, issued by {cert.GetNameInfo(X509NameType.SimpleName, true)}";
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            try
            {
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                var candidates = new X509Certificate2Collection(
                    store.Certificates.Cast<X509Certificate2>().Where(c => c.HasPrivateKey).ToArray());

                if (candidates.Count == 0)
                {
                    MessageBox.Show(this, "There are no certificates with a private key in your personal certificate store.",
                        "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var handle = new WindowInteropHelper(this).Handle;
                var selected = X509Certificate2UI.SelectFromCollection(candidates, "Client certificate",
                    "Select the certificate to authenticate this application to the server.",
                    X509SelectionFlag.SingleSelection, handle);

                if (selected.Count > 0)
                {
                    ClientCertThumbprint = selected[0].Thumbprint;
                    UpdateCertificateText();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open the certificate store:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                store.Close();
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ClientCertThumbprint = string.Empty;
            UpdateCertificateText();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}

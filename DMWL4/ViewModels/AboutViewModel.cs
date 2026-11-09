using MWL4.Infrastructure;
using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace MWL4.ViewModels
{
    public class AboutViewModel : INotifyPropertyChanged
    {
        private readonly UpdateChecker _updateChecker;

        /// <param name="updateChecker">Replaces the real check in tests.</param>
        public AboutViewModel(UpdateChecker updateChecker = null)
        {
            _updateChecker = updateChecker ?? new UpdateChecker(AppInfo.CurrentVersion);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private static Assembly Assembly => Assembly.GetExecutingAssembly();

        public string Version => "Version " + AppInfo.CurrentVersion;
        public string Company => Assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? string.Empty;
        public string Copyright => Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

        private string _updateStatus = "Checking for a new version...";
        public string UpdateStatus { get => _updateStatus; private set { _updateStatus = value; OnPropertyChanged(); } }

        private Uri _releaseUri;
        /// <summary>Where a new version can be found, or null if there is none.</summary>
        public Uri ReleaseUri { get => _releaseUri; private set { _releaseUri = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReleaseLinkText)); } }

        public string ReleaseLinkText => _releaseUri == null ? string.Empty : "Open the release page";

        /// <summary>Fills in the status line. Closing the window cancels the check, and a failed check only shows as text.</summary>
        public async Task CheckForUpdateAsync(CancellationToken token)
        {
            UpdateCheckResult result;
            try
            {
                result = await _updateChecker.CheckAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            switch (result.Outcome)
            {
                case UpdateCheckOutcome.UpToDate:
                    UpdateStatus = "You have the latest version.";
                    break;
                case UpdateCheckOutcome.NewVersion:
                    UpdateStatus = $"New version available: {result.LatestVersion}.";
                    ReleaseUri = result.ReleaseUri;
                    break;
                case UpdateCheckOutcome.NoRelease:
                    UpdateStatus = "No release has been published yet.";
                    break;
                default:
                    UpdateStatus = "Could not check for a new version.";
                    break;
            }
        }
    }
}

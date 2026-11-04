using DMWLT4.Properties;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using FellowOakDicom.Network.Tls;
using MWL4.Extensions;
using MWL4.Infrastructure;
using MWL4.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace MWL4.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ILogger _logger;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        #region Properties - Connection
        private string _callingAET;
        public string CallingAET
        {
            get => _callingAET;
            set { _callingAET = value; OnPropertyChanged(); Settings.Default.CallingAET = value; }
        }

        private string _serverAET;
        public string ServerAET
        {
            get => _serverAET;
            set { _serverAET = value; OnPropertyChanged(); Settings.Default.ServerAET = value; }
        }

        private string _serverHost;
        public string ServerHost
        {
            get => _serverHost;
            set { _serverHost = value; OnPropertyChanged(); Settings.Default.ServerHost = value; }
        }

        private int _serverPort;
        public int ServerPort
        {
            get => _serverPort;
            set { _serverPort = value; OnPropertyChanged(); Settings.Default.ServerPort = value; }
        }

        private bool _useTLS;
        public bool UseTLS
        {
            get => _useTLS;
            set { _useTLS = value; OnPropertyChanged(); Settings.Default.UseTLS = value; }
        }

        private bool _tlsIgnoreCertErrors;
        /// <summary>Accept self-signed or otherwise invalid server certificates.</summary>
        public bool TlsIgnoreCertErrors
        {
            get => _tlsIgnoreCertErrors;
            set { _tlsIgnoreCertErrors = value; OnPropertyChanged(); Settings.Default.TlsIgnoreCertErrors = value; }
        }

        private string _tlsClientCertThumbprint;
        /// <summary>Thumbprint of the client certificate in CurrentUser\My, or empty for none.</summary>
        public string TlsClientCertThumbprint
        {
            get => _tlsClientCertThumbprint;
            set { _tlsClientCertThumbprint = value ?? string.Empty; OnPropertyChanged(); Settings.Default.TlsClientCertThumbprint = _tlsClientCertThumbprint; }
        }
        #endregion

        #region Properties - Profiles
        public ObservableCollection<ConnectionProfile> Profiles { get; } = new ObservableCollection<ConnectionProfile>();

        private string _profileName = string.Empty;
        /// <summary>Text of the editable profile combo box: the name used by Save.</summary>
        public string ProfileName { get => _profileName; set { _profileName = value ?? string.Empty; OnPropertyChanged(); } }

        private ConnectionProfile _selectedProfile;
        /// <summary>Selecting a profile copies its values into the connection fields.</summary>
        public ConnectionProfile SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (ReferenceEquals(_selectedProfile, value)) return;
                _selectedProfile = value;
                OnPropertyChanged();
                if (value != null)
                {
                    ApplyProfile(value);
                }
            }
        }

        /// <summary>Asks the user a yes/no question. Replaceable for tests.</summary>
        public Func<string, bool> Confirm { get; set; } = message =>
            MessageBox.Show(message, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        #endregion

        #region Properties - Query
        private string _patientName;
        public string PatientName { get => _patientName; set { _patientName = value; OnPropertyChanged(); } }

        private string _patientID;
        public string PatientID { get => _patientID; set { _patientID = value; OnPropertyChanged(); } }

        private string _modality;
        public string Modality { get => _modality; set { _modality = value; OnPropertyChanged(); } }

        private string _stationAET;
        public string StationAET { get => _stationAET; set { _stationAET = value; OnPropertyChanged(); } }

        private string _stationName;
        public string StationName { get => _stationName; set { _stationName = value; OnPropertyChanged(); } }

        // Nullable so that a cleared DatePicker means "no limit" instead of silently keeping the old date.
        private DateTime? _startDate = DateTime.Today;
        public DateTime? StartDate { get => _startDate; set { _startDate = value; OnPropertyChanged(); } }

        private DateTime? _endDate = DateTime.Today;
        public DateTime? EndDate { get => _endDate; set { _endDate = value; OnPropertyChanged(); } }

        /// <summary>Extra query keys chosen in Settings -> Query, in the order of QueryKeyDefinition.All.</summary>
        public ObservableCollection<QueryKeyField> ExtraQueryFields { get; } = new ObservableCollection<QueryKeyField>();

        /// <summary>
        /// Rebuilds ExtraQueryFields from the ExtraQueryKeys setting. Values already typed in keys that remain are kept.
        /// </summary>
        public void RefreshQueryKeys()
        {
            var chosen = new HashSet<string>((Settings.Default.ExtraQueryKeys ?? string.Empty)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
            var previous = ExtraQueryFields.ToDictionary(f => f.Definition.Id, f => f.Value);

            ExtraQueryFields.Clear();
            foreach (var def in QueryKeyDefinition.All.Where(d => chosen.Contains(d.Id)))
            {
                ExtraQueryFields.Add(new QueryKeyField(def)
                {
                    Value = previous.TryGetValue(def.Id, out var v) ? v : string.Empty
                });
            }
        }
        #endregion

        #region Properties - Results / status
        private string _statusText = "Ready";
        public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }

        private string _queryStats = string.Empty;
        /// <summary>Statistics of the last query (time, responses, final status), shown at the right of the status bar.</summary>
        public string QueryStats { get => _queryStats; private set { _queryStats = value; OnPropertyChanged(); } }

        // Result of the last operation (echo/ping/query warnings/errors). Kept separately so that
        // UpdateStatusWithCounts (e.g. on selection change) does not erase it.
        private string _statusSuffix = string.Empty;

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (_isBusy == value) return;
                _isBusy = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private int _selectedCount;
        public int SelectedCount
        {
            get => _selectedCount;
            set
            {
                if (_selectedCount == value) return;
                _selectedCount = value;
                OnPropertyChanged();
                UpdateStatusWithCounts();
            }
        }

        public ObservableCollection<WorklistItem> WorklistResults { get; } = new ObservableCollection<WorklistItem>();

        /// <summary>The view the results grid shows. Used for the client-side filter.</summary>
        public ICollectionView ResultsView { get; }

        private string _filterText = string.Empty;
        /// <summary>Client-side filter: case-insensitive text match on the visible columns.</summary>
        public string FilterText
        {
            get => _filterText;
            set
            {
                _filterText = value ?? string.Empty;
                OnPropertyChanged();
                RefreshFilter();
            }
        }

        private bool _isFilterBarVisible;
        /// <summary>
        /// Shows or hides the filter bar (File -> View Filter Results). Hiding it clears the filter,
        /// so that no rows are hidden without a visible reason.
        /// </summary>
        public bool IsFilterBarVisible
        {
            get => _isFilterBarVisible;
            set
            {
                if (_isFilterBarVisible == value) return;
                _isFilterBarVisible = value;
                Settings.Default.ShowFilterBar = value;
                OnPropertyChanged();
                if (!value && !string.IsNullOrEmpty(FilterText))
                {
                    FilterText = string.Empty;
                }
            }
        }

        private List<PropertyInfo> _filterProperties = new List<PropertyInfo>();
        #endregion

        public ICommand DicomEchoCommand { get; }
        public ICommand QueryCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand AboutCommand { get; }
        public ICommand PingCommand { get; }
        public ICommand ClearResultsCommand { get; }
        public ICommand ClearFilterCommand { get; }
        public ICommand ToggleFilterBarCommand { get; }
        public ICommand HelpCommand { get; }
        public ICommand SelectionChangedCommand { get; }
        public ICommand SaveProfileCommand { get; }
        public ICommand DeleteProfileCommand { get; }

        // Set while a cancellable operation (Echo/Query) runs.
        private CancellationTokenSource _cts;

        public MainViewModel()
        {
            _logger = Log.ForContext<MainViewModel>();
            _logger.Information("MainViewModel initialized");

            ResultsView = CollectionViewSource.GetDefaultView(WorklistResults);

            LoadSettings();
            LoadProfiles();
            UpdateFilterProperties();
            RefreshQueryKeys();

            // Network commands are disabled while another network operation is running,
            // otherwise e.g. a double-clicked Query runs two C-FINDs into the same result list.
            DicomEchoCommand = new RelayCommand(async _ => await RunBusyAsync(DoEchoAsync), _ => !IsBusy);
            QueryCommand = new RelayCommand(async _ => await RunBusyAsync(DoQueryAsync), _ => !IsBusy);
            PingCommand = new RelayCommand(async _ => await RunBusyAsync(ct => DoPingAsync(), cancellable: false), _ => !IsBusy);
            CancelCommand = new RelayCommand(_ => CancelOperation(), _ => _cts != null && !_cts.IsCancellationRequested);
            AboutCommand = new RelayCommand(_ => ShowAbout());
            ExitCommand = new RelayCommand(_ => Application.Current?.Shutdown()); // settings are saved in MainWindow.OnClosed

            ClearResultsCommand = new RelayCommand(_ => ClearResults(), _ => !IsBusy);
            ClearFilterCommand = new RelayCommand(_ => FilterText = string.Empty);
            ToggleFilterBarCommand = new RelayCommand(_ => IsFilterBarVisible = !IsFilterBarVisible);
            HelpCommand = new RelayCommand(_ => ShowHelp());

            SaveProfileCommand = new RelayCommand(_ => SaveProfile());
            DeleteProfileCommand = new RelayCommand(_ => DeleteProfile(), _ => FindProfile(ProfileName) != null);

            SelectionChangedCommand = new RelayCommand(p =>
            {
                var count = 0;
                if (p is int i)
                {
                    count = i;
                }
                else if (p != null)
                {
                    var prop = p.GetType().GetProperty("Count");
                    if (prop != null)
                    {
                        var val = prop.GetValue(p, null);
                        if (val is int ci) count = ci;
                    }
                }
                SelectedCount = count;
            });
        }

        private async Task RunBusyAsync(Func<CancellationToken, Task> action, bool cancellable = true)
        {
            if (IsBusy) return;
            IsBusy = true;
            var cts = cancellable ? new CancellationTokenSource() : null;
            _cts = cts;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                await action(cts?.Token ?? CancellationToken.None);
            }
            finally
            {
                _cts = null;
                cts?.Dispose();
                IsBusy = false;
            }
        }

        private void CancelOperation()
        {
            var cts = _cts;
            if (cts == null || cts.IsCancellationRequested) return;
            _logger.Information("Cancel requested by user");
            StatusText = "Cancelling...";
            cts.Cancel();
            CommandManager.InvalidateRequerySuggested();
        }

        private void LoadSettings()
        {
            _callingAET = Settings.Default.CallingAET ?? string.Empty;
            _serverAET = Settings.Default.ServerAET ?? string.Empty;
            _serverHost = Settings.Default.ServerHost ?? string.Empty;
            _serverPort = Settings.Default.ServerPort;
            _useTLS = Settings.Default.UseTLS;
            _tlsIgnoreCertErrors = Settings.Default.TlsIgnoreCertErrors;
            _tlsClientCertThumbprint = Settings.Default.TlsClientCertThumbprint ?? string.Empty;
            _isFilterBarVisible = Settings.Default.ShowFilterBar;
        }

        /// <summary>Opens the help guide, or brings it to the front if it is already open.</summary>
        private void ShowHelp()
        {
            var existing = Application.Current?.Windows.OfType<HelpWindow>().FirstOrDefault();
            if (existing != null)
            {
                if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
                existing.Activate();
                return;
            }
            // Not owned by the main window, so it can be placed next to it instead of always on top.
            // MainWindow closes it on exit.
            new HelpWindow().Show();
        }

        private void ShowAbout()
        {
            var dlg = new AboutWindow
            {
                Owner = Application.Current?.MainWindow,
                DataContext = new AboutViewModel()
            };
            dlg.ShowDialog();
        }

        #region Profiles
        private void LoadProfiles()
        {
            Profiles.Clear();
            try
            {
                foreach (var p in ConnectionProfile.Deserialize(Settings.Default.ConnectionProfiles)
                                                   .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                                                   .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    Profiles.Add(p);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not read saved connection profiles, they are ignored");
            }
        }

        private void StoreProfiles()
        {
            Settings.Default.ConnectionProfiles = ConnectionProfile.Serialize(Profiles);
        }

        private ConnectionProfile FindProfile(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return Profiles.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase));
        }

        private void ApplyProfile(ConnectionProfile p)
        {
            CallingAET = p.CallingAET ?? string.Empty;
            ServerAET = p.ServerAET ?? string.Empty;
            ServerHost = p.ServerHost ?? string.Empty;
            ServerPort = p.ServerPort;
            UseTLS = p.UseTLS;
            TlsIgnoreCertErrors = p.TlsIgnoreCertErrors;
            TlsClientCertThumbprint = p.TlsClientCertThumbprint ?? string.Empty;
            _logger.Information("Loaded connection profile {name}", p.Name);
            SetStatus($"Profile '{p.Name}' loaded.");
        }

        private void SaveProfile()
        {
            var name = ProfileName?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                ShowMessage("Type a name for the profile in the Profile box first.", MessageBoxImage.Warning);
                return;
            }

            var profile = FindProfile(name);
            if (profile == null)
            {
                profile = new ConnectionProfile { Name = name };
                // Keep the list sorted by name.
                var index = 0;
                while (index < Profiles.Count && StringComparer.CurrentCultureIgnoreCase.Compare(Profiles[index].Name, name) < 0) index++;
                Profiles.Insert(index, profile);
            }

            profile.CallingAET = CallingAET;
            profile.ServerAET = ServerAET;
            profile.ServerHost = ServerHost;
            profile.ServerPort = ServerPort;
            profile.UseTLS = UseTLS;
            profile.TlsIgnoreCertErrors = TlsIgnoreCertErrors;
            profile.TlsClientCertThumbprint = TlsClientCertThumbprint;
            StoreProfiles();

            _selectedProfile = profile; // values are already current, so do not re-apply
            OnPropertyChanged(nameof(SelectedProfile));
            ProfileName = profile.Name;
            _logger.Information("Saved connection profile {name}", profile.Name);
            SetStatus($"Profile '{profile.Name}' saved.");
        }

        private void DeleteProfile()
        {
            var profile = FindProfile(ProfileName);
            if (profile == null) return;
            if (!Confirm($"Delete the connection profile '{profile.Name}'?")) return;

            Profiles.Remove(profile);
            StoreProfiles();
            _selectedProfile = null;
            OnPropertyChanged(nameof(SelectedProfile));
            ProfileName = string.Empty;
            _logger.Information("Deleted connection profile {name}", profile.Name);
            SetStatus($"Profile '{profile.Name}' deleted.");
        }
        #endregion

        /// <summary>
        /// Validates host (and optionally port) before a network operation. Shows a warning and returns false if invalid.
        /// </summary>
        private bool ValidateConnection(bool requirePort = true)
        {
            if (string.IsNullOrWhiteSpace(ServerHost))
            {
                ShowMessage("Called host is empty.", MessageBoxImage.Warning);
                return false;
            }

            if (requirePort && (ServerPort < 1 || ServerPort > 65535))
            {
                ShowMessage($"Port {ServerPort} is invalid. It must be between 1 and 65535.", MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Response (DIMSE) timeout and association timeout handling.
        /// fo-dicom's own RequestTimeout is not used: when it fires, fo-dicom drops the request but still waits
        /// until the server has sent all its responses, and it ignores cancellation from then on. Instead, a timer
        /// is started when the association is accepted and restarted on every response; when it fires, the
        /// association is aborted the same way as with the Cancel button.
        /// </summary>
        private sealed class TimeoutWatch : IDisposable
        {
            private readonly CancellationTokenSource _linked;
            private readonly Timer _timer;
            private readonly int _responseSeconds;
            private volatile string _reason;
            private volatile bool _disposed;

            public TimeoutWatch(IDicomClient client, CancellationToken userToken)
            {
                _linked = CancellationTokenSource.CreateLinkedTokenSource(userToken);
                _responseSeconds = Settings.Default.ResponseTimeoutSeconds;
                _timer = new Timer(_ => OnTimeout($"no response within {_responseSeconds} s"), null, Timeout.Infinite, Timeout.Infinite);

                client.AssociationAccepted += (s, e) => ResponseReceived();
                client.AssociationRequestTimedOut += (s, e) =>
                    OnTimeout($"association not accepted within {Settings.Default.AssociationTimeoutSeconds} s");
            }

            public CancellationToken Token => _linked.Token;
            public bool TimedOut => _reason != null;
            /// <summary>E.g. "no response within 30 s", or null if nothing timed out.</summary>
            public string Reason => _reason;

            /// <summary>Restarts the response timer. Call it for every response received.</summary>
            public void ResponseReceived()
            {
                if (_responseSeconds <= 0 || _disposed || _reason != null) return;
                try { _timer.Change(TimeSpan.FromSeconds(_responseSeconds), Timeout.InfiniteTimeSpan); }
                catch (ObjectDisposedException) { }
            }

            /// <summary>Stops the response timer, e.g. when the final response has arrived.</summary>
            public void Stop()
            {
                try { _timer.Change(Timeout.Infinite, Timeout.Infinite); }
                catch (ObjectDisposedException) { }
            }

            private void OnTimeout(string reason)
            {
                if (_reason != null || _disposed) return;
                _reason = reason;
                Log.ForContext<MainViewModel>().Warning("Timeout: {reason}, aborting association", reason);
                // Cancel from another thread: the events are raised from inside fo-dicom's own processing.
                Task.Run(() =>
                {
                    try { _linked.Cancel(); }
                    catch (ObjectDisposedException) { }
                });
            }

            public void Dispose()
            {
                _disposed = true;
                _timer.Dispose();
                _linked.Dispose();
            }
        }

        private async Task DoEchoAsync(CancellationToken token)
        {
            if (!ValidateConnection()) return;

            StatusText = "Performing C-ECHO...";
            _logger.Information("C-ECHO -> {host}:{port}", ServerHost, ServerPort);

            TimeoutWatch watch = null;
            try
            {
                var client = CreateClient();
                watch = new TimeoutWatch(client, token);

                var request = new DicomCEchoRequest();
                DicomStatus echoStatus = null;
                request.OnResponseReceived += (req, res) =>
                {
                    echoStatus = res.Status;
                    watch.Stop();
                };

                var sw = Stopwatch.StartNew();
                await client.AddRequestAsync(request);
                await client.SendAsync(watch.Token, DicomClientCancellationMode.ImmediatelyAbortAssociation);
                sw.Stop();

                // SendAsync completing without an exception does not guarantee that a successful
                // C-ECHO response was received, so check the actual response status.
                if (token.IsCancellationRequested)
                {
                    _logger.Information("C-ECHO cancelled");
                    SetStatus("Echo cancelled.");
                }
                else if (echoStatus == null)
                {
                    ReportEchoNoResponse(watch.TimedOut ? watch.Reason + " (timed out)" : "no response received");
                }
                else if (echoStatus != DicomStatus.Success)
                {
                    _logger.Warning("C-ECHO returned status {status}", echoStatus);
                    SetStatus($"Echo failed: {echoStatus}.");
                    ShowMessage($"C-ECHO to {ServerHost} returned status:\n{echoStatus}", MessageBoxImage.Warning);
                }
                else
                {
                    _logger.Information("C-ECHO success in {ms} ms", sw.ElapsedMilliseconds);
                    SetStatus($"Echo successful ({sw.ElapsedMilliseconds} ms).");
                    ShowMessage($"Connection to {ServerHost} successful.\nTime: {sw.ElapsedMilliseconds} ms", MessageBoxImage.Information);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _logger.Information("C-ECHO cancelled");
                SetStatus("Echo cancelled.");
            }
            catch (OperationCanceledException) when (watch?.TimedOut == true)
            {
                ReportEchoNoResponse(watch.Reason + " (timed out)");
            }
            catch (Exception ex)
            {
                HandleError("C-ECHO failed", ex);
            }
            finally
            {
                watch?.Dispose();
            }
        }

        private void ReportEchoNoResponse(string reason)
        {
            _logger.Warning("C-ECHO: {reason}", reason);
            SetStatus($"Echo failed: {reason}.");
            ShowMessage($"C-ECHO to {ServerHost} failed: {reason}.", MessageBoxImage.Warning);
        }

        private async Task DoPingAsync()
        {
            if (!ValidateConnection(requirePort: false)) return;

            StatusText = $"Pinging {ServerHost}...";
            try
            {
                using (var p = new Ping())
                {
                    var reply = await p.SendPingAsync(ServerHost, 4000);
                    if (reply.Status == IPStatus.Success)
                    {
                        SetStatus($"Ping successful ({reply.RoundtripTime} ms).");
                        ShowMessage($"Ping Success.\nTime: {reply.RoundtripTime}ms", MessageBoxImage.Information, "Ping Result");
                    }
                    else
                    {
                        SetStatus($"Ping failed: {reply.Status}.");
                        ShowMessage($"Ping Failed: {reply.Status}", MessageBoxImage.Warning, "Ping Result");
                    }
                }
            }
            catch (Exception ex)
            {
                HandleError("Ping error", ex);
            }
        }

        private async Task DoQueryAsync(CancellationToken token)
        {
            if (!ValidateConnection()) return;

            if (StartDate.HasValue && EndDate.HasValue && StartDate.Value.Date > EndDate.Value.Date)
            {
                ShowMessage("Start date is after end date.", MessageBoxImage.Warning);
                return;
            }

            foreach (var field in ExtraQueryFields)
            {
                if (field.Definition.Normalize(field.Value) == null)
                {
                    ShowMessage($"{field.Definition.Label}: '{field.Value.Trim()}' is not valid.\nExpected: {field.Definition.FormatText}.",
                        MessageBoxImage.Warning);
                    return;
                }
            }

            WorklistResults.Clear();
            SelectedCount = 0;
            _statusSuffix = string.Empty;
            StatusText = "Querying Worklist...";
            QueryStats = string.Empty;

            DicomStatus finalStatus = null;
            var pendingResponses = 0;
            var pendingWarnings = 0;
            var mappingErrors = 0;
            var sw = new Stopwatch();
            TimeoutWatch watch = null;

            try
            {
                var request = BuildCFindRequest();
                var client = CreateClient();
                watch = new TimeoutWatch(client, token);

                request.OnResponseReceived += (req, res) =>
                {
                    // Every match is sent with a Pending *state*, but the status code may be either
                    // FF00 (Pending) or FF01 (Pending - one or more optional keys not supported).
                    // DicomStatus == compares the code, so comparing against DicomStatus.Pending
                    // silently dropped all FF01 matches. Compare the state instead.
                    if (res.Status.State == DicomState.Pending)
                    {
                        watch.ResponseReceived();
                        pendingResponses++;
                        if (res.Status != DicomStatus.Pending)
                        {
                            pendingWarnings++;
                        }

                        if (!res.HasDataset)
                        {
                            _logger.Warning("Pending C-FIND response without dataset (status {status})", res.Status);
                            return;
                        }

                        try
                        {
                            var item = MapDatasetToWorklistItem(res.Dataset);
                            Application.Current?.Dispatcher?.Invoke(() => WorklistResults.Add(item));
                        }
                        catch (Exception ex)
                        {
                            mappingErrors++;
                            _logger.Error(ex, "Failed to map C-FIND response dataset to worklist item");
                        }
                    }
                    else
                    {
                        watch.Stop();
                        finalStatus = res.Status;
                    }
                };

                sw.Start();
                await client.AddRequestAsync(request);
                await client.SendAsync(watch.Token, DicomClientCancellationMode.ImmediatelyAbortAssociation);
                sw.Stop();

                var cancelled = token.IsCancellationRequested && finalStatus == null;
                var timedOut = watch.TimedOut;

                _logger.Information(
                    "Query completed. Results: {count}, pending responses: {pending} (of which FF01/warning: {warnings}), mapping errors: {errors}, final status: {status}, cancelled: {cancelled}, timed out: {timedOut}, {ms} ms",
                    WorklistResults.Count, pendingResponses, pendingWarnings, mappingErrors, finalStatus?.ToString() ?? "<none>", cancelled, timedOut, sw.ElapsedMilliseconds);

                var suffix = string.Empty;
                if (cancelled)
                {
                    suffix += $" Query cancelled - {WorklistResults.Count} item(s) received.";
                }
                else if (finalStatus == null && timedOut)
                {
                    suffix += $" Warning: {watch.Reason} (timed out) - results may be incomplete.";
                }
                else if (finalStatus == null)
                {
                    suffix += " Warning: no final response received from server (timeout or aborted association) - results may be incomplete.";
                }
                else if (finalStatus.State != DicomState.Success)
                {
                    suffix += $" Warning: server ended query with status {finalStatus} - results may be incomplete.";
                }

                if (mappingErrors > 0)
                {
                    suffix += $" {mappingErrors} response(s) could not be read, see log.";
                }

                QueryStats = FormatQueryStats(sw.ElapsedMilliseconds, pendingResponses, pendingWarnings, finalStatus,
                    cancelled ? "cancelled" : timedOut ? "timed out" : null);
                SetStatus(suffix.Trim());
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                sw.Stop();
                QueryStats = FormatQueryStats(sw.ElapsedMilliseconds, pendingResponses, pendingWarnings, finalStatus, "cancelled");
                SetStatus($"Query cancelled - {WorklistResults.Count} item(s) received.");
            }
            catch (OperationCanceledException) when (watch?.TimedOut == true)
            {
                sw.Stop();
                QueryStats = FormatQueryStats(sw.ElapsedMilliseconds, pendingResponses, pendingWarnings, finalStatus, "timed out");
                SetStatus($"Warning: {watch.Reason} (timed out) - results may be incomplete.");
            }
            catch (Exception ex)
            {
                sw.Stop();
                QueryStats = $"Last query: failed after {sw.ElapsedMilliseconds} ms";
                HandleError("Worklist query failed", ex);
            }
            finally
            {
                watch?.Dispose();
            }
        }

        /// <summary>E.g. "Last query: 245 ms · 3 responses (1 × FF01) · final: Success [0000]".</summary>
        private static string FormatQueryStats(long ms, int responses, int warnings, DicomStatus finalStatus, string noFinalReason)
        {
            var text = $"Last query: {ms} ms · {responses} response{(responses == 1 ? "" : "s")}";
            if (warnings > 0) text += $" ({warnings} × FF01)";
            text += finalStatus != null
                ? $" · final: {finalStatus.Description} [{finalStatus.Code:X4}]"
                : $" · final: none{(noFinalReason != null ? $" ({noFinalReason})" : "")}";
            return text;
        }

        private void ClearResults()
        {
            WorklistResults.Clear();
            SelectedCount = 0;
            QueryStats = string.Empty;
            SetStatus("Results cleared.");
        }

        /// <summary>Sets the result text of the last operation and refreshes the status bar.</summary>
        private void SetStatus(string suffix)
        {
            _statusSuffix = suffix ?? string.Empty;
            UpdateStatusWithCounts();
        }

        private void UpdateStatusWithCounts()
        {
            var text = $"Found {WorklistResults.Count} items.";
            if (!string.IsNullOrEmpty(FilterText))
            {
                text += $" {ResultsView.Cast<object>().Count()} shown.";
            }
            if (SelectedCount > 0)
            {
                text += $" {SelectedCount} selected.";
            }
            if (!string.IsNullOrEmpty(_statusSuffix))
            {
                text += " " + _statusSuffix;
            }
            StatusText = text;
        }

        #region Filter
        /// <summary>
        /// Re-reads the visible columns (they may have changed in Settings) and re-applies the filter.
        /// </summary>
        public void RefreshFilter()
        {
            UpdateFilterProperties();
            if (string.IsNullOrEmpty(FilterText))
            {
                ResultsView.Filter = null;
            }
            else
            {
                ResultsView.Filter = FilterItem;
            }
            UpdateStatusWithCounts();
        }

        private void UpdateFilterProperties()
        {
            var visible = new HashSet<string>((Settings.Default.VisibleColumns ?? string.Empty)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
            _filterProperties = typeof(WorklistItem).GetProperties()
                .Where(p => p.PropertyType == typeof(string) && visible.Contains(p.Name))
                .ToList();
        }

        private bool FilterItem(object obj)
        {
            if (!(obj is WorklistItem item)) return false;
            var filter = FilterText?.Trim();
            if (string.IsNullOrEmpty(filter)) return true;
            foreach (var prop in _filterProperties)
            {
                var value = prop.GetValue(item) as string;
                if (!string.IsNullOrEmpty(value) && value.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }
        #endregion

        private WorklistItem MapDatasetToWorklistItem(DicomDataset ds)
        {
            var spsSeq = DicomTag.ScheduledProcedureStepSequence;
            var protoSeq = DicomTag.ScheduledProtocolCodeSequence;
            var reqProcSeq = DicomTag.RequestedProcedureCodeSequence;

            return new WorklistItem
            {
                Dataset = ds,
                SpecificCharacterSet = ds.GetStringSafe(DicomTag.SpecificCharacterSet),

                PatientName = ds.GetStringSafe(DicomTag.PatientName),
                PatientID = ds.GetStringSafe(DicomTag.PatientID),
                AccessionNumber = ds.GetStringSafe(DicomTag.AccessionNumber),
                PatientSex = ds.GetStringSafe(DicomTag.PatientSex),
                PatientBirthDate = ds.GetStringSafe(DicomTag.PatientBirthDate),
                PatientAge = ds.GetStringSafe(DicomTag.PatientAge),
                PatientSize = ds.GetStringSafe(DicomTag.PatientSize),
                PatientWeight = ds.GetStringSafe(DicomTag.PatientWeight),
                MedicalAlerts = ds.GetStringSafe(DicomTag.MedicalAlerts),
                Allergies = ds.GetStringSafe(DicomTag.Allergies),

                StudyInstanceUID = ds.GetStringSafe(DicomTag.StudyInstanceUID),
                AdmissionID = ds.GetStringSafe(DicomTag.AdmissionID),
                CurrentPatientLocation = ds.GetStringSafe(DicomTag.CurrentPatientLocation),

                ReferringPhysicianName = ds.GetStringSafe(DicomTag.ReferringPhysicianName),
                RequestingPhysician = ds.GetStringSafe(DicomTag.RequestingPhysician),
                RequestedProcedureID = ds.GetStringSafe(DicomTag.RequestedProcedureID),
                RequestedProcedureDescription = ds.GetStringSafe(DicomTag.RequestedProcedureDescription),
                RequestedProcedurePriority = ds.GetStringSafe(DicomTag.RequestedProcedurePriority),
                ReasonForRequestedProcedure = ds.GetStringSafe(DicomTag.ReasonForTheRequestedProcedure),

                // For RequestedProcedureCodeSequence, we try to get the first item's Code Meaning or Value
                RequestedProcedureCodeSequence = ds.GetStringSafe(DicomTag.CodeMeaning, reqProcSeq),

                OrderEnteredBy = ds.GetStringSafe(DicomTag.OrderEnteredBy),
                OrderEntererLocation = ds.GetStringSafe(DicomTag.OrderEntererLocation),
                ImagingServiceRequestComments = ds.GetStringSafe(DicomTag.ImagingServiceRequestComments),

                ScheduledProcedureStepID = ds.GetStringSafe(DicomTag.ScheduledProcedureStepID, spsSeq),
                Modality = ds.GetStringSafe(DicomTag.Modality, spsSeq),
                ScheduledStationAET = ds.GetStringSafe(DicomTag.ScheduledStationAETitle, spsSeq),
                ScheduledStationName = ds.GetStringSafe(DicomTag.ScheduledStationName, spsSeq),
                ScheduledDate = ds.GetStringSafe(DicomTag.ScheduledProcedureStepStartDate, spsSeq),
                ScheduledTime = ds.GetStringSafe(DicomTag.ScheduledProcedureStepStartTime, spsSeq),
                ExamDescription = ds.GetStringSafe(DicomTag.ScheduledProcedureStepDescription, spsSeq),
                ScheduledProcedureStepStatus = ds.GetStringSafe(DicomTag.ScheduledProcedureStepStatus, spsSeq),
                ScheduledProcedureStepLocation = ds.GetStringSafe(DicomTag.ScheduledProcedureStepLocation, spsSeq),
                PreMedication = ds.GetStringSafe(DicomTag.PreMedication, spsSeq),
                ScheduledPerformingPhysicianName = ds.GetStringSafe(DicomTag.ScheduledPerformingPhysicianName, spsSeq),
                CommentsOnScheduledProcedureStep = ds.GetStringSafe(DicomTag.CommentsOnTheScheduledProcedureStep, spsSeq),
                ScheduledProtocolCodeValue = ds.GetNestedStringSafe(spsSeq, protoSeq, DicomTag.CodeValue),
                ScheduledProtocolCodeMeaning = ds.GetNestedStringSafe(spsSeq, protoSeq, DicomTag.CodeMeaning),
                ScheduledProtocolCodeScheme = ds.GetNestedStringSafe(spsSeq, protoSeq, DicomTag.CodingSchemeDesignator)
            };
        }

        /// <summary>
        /// Builds the patient name matching key. Appends a trailing '*' for "starts with" matching
        /// unless the user already supplied their own wildcards.
        /// </summary>
        private static string BuildPatientNameKey(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var trimmed = input.Trim();
            return trimmed.IndexOfAny(new[] { '*', '?' }) >= 0 ? trimmed : trimmed + "*";
        }

        /// <summary>
        /// Builds a DICOM DA range. Empty dates mean an open-ended range; both empty means any date.
        /// </summary>
        private static string BuildDateRange(DateTime? start, DateTime? end)
        {
            if (!start.HasValue && !end.HasValue) return string.Empty;
            var s = start.HasValue ? start.Value.ToString("yyyyMMdd") : string.Empty;
            var e = end.HasValue ? end.Value.ToString("yyyyMMdd") : string.Empty;
            return $"{s}-{e}";
        }

        /// <summary>
        /// Returns the Specific Character Set needed to send the given query values, or null if all are ASCII.
        /// Latin-1 (ISO_IR 100) covers e.g. Swedish å/ä/ö and is the most widely supported; anything outside
        /// Latin-1 falls back to UTF-8 (ISO_IR 192).
        /// </summary>
        private static string DetectCharacterSet(params string[] values)
        {
            var chars = values.Where(v => !string.IsNullOrEmpty(v)).SelectMany(v => v).ToArray();
            if (chars.All(c => c < 0x80)) return null;
            return chars.All(c => c <= 0xFF) ? "ISO_IR 100" : "ISO_IR 192";
        }

        /// <summary>
        /// The Specific Character Set to send: the configured one, or (Auto) the one detected from the query values.
        /// </summary>
        private static string ResolveCharacterSet(params string[] values)
        {
            var configured = Settings.Default.CharacterSet?.Trim();
            if (string.IsNullOrEmpty(configured) || string.Equals(configured, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                return DetectCharacterSet(values);
            }
            return configured;
        }

        private DicomCFindRequest BuildCFindRequest()
        {
            var patientName = BuildPatientNameKey(PatientName);
            var patientId = PatientID?.Trim() ?? string.Empty;
            // Modality is VR CS which only allows upper case characters.
            var modality = Modality?.Trim().ToUpperInvariant() ?? string.Empty;
            var stationAet = StationAET?.Trim() ?? string.Empty;
            var stationName = StationName?.Trim() ?? string.Empty;

            var ds = new DicomDataset();

            // Specific Character Set must be added before the text elements so that they are encoded with it.
            // Extra query keys from Settings -> Query (already validated in DoQueryAsync).
            var extraKeys = ExtraQueryFields
                .Select(f => (f.Definition, Value: f.Definition.Normalize(f.Value)))
                .Where(x => !string.IsNullOrEmpty(x.Value))
                .ToList();

            var charset = ResolveCharacterSet(new[] { patientName, patientId, stationName }
                .Concat(extraKeys.Select(x => x.Value)).ToArray());
            if (charset != null)
            {
                ds.Add(DicomTag.SpecificCharacterSet, charset);
                _logger.Debug("Using Specific Character Set {charset} (setting: {setting})", charset, Settings.Default.CharacterSet);
            }

            ds.Add(DicomTag.PatientName, patientName);
            ds.Add(DicomTag.PatientID, patientId);
            ds.Add(DicomTag.PatientSex, string.Empty);
            ds.Add(DicomTag.PatientBirthDate, string.Empty);
            ds.Add(DicomTag.PatientAge, string.Empty);
            ds.Add(DicomTag.PatientSize, string.Empty);
            ds.Add(DicomTag.PatientWeight, string.Empty);
            ds.Add(DicomTag.MedicalAlerts, string.Empty);
            ds.Add(DicomTag.Allergies, string.Empty);

            ds.Add(DicomTag.AccessionNumber, string.Empty);
            ds.Add(DicomTag.AdmissionID, string.Empty);
            ds.Add(DicomTag.CurrentPatientLocation, string.Empty);
            ds.Add(DicomTag.StudyInstanceUID, string.Empty);

            ds.Add(DicomTag.ReferringPhysicianName, string.Empty);
            ds.Add(DicomTag.RequestingPhysician, string.Empty);
            ds.Add(DicomTag.RequestedProcedureDescription, string.Empty);
            ds.Add(DicomTag.RequestedProcedureID, string.Empty);
            ds.Add(DicomTag.RequestedProcedurePriority, string.Empty);
            ds.Add(DicomTag.ReasonForTheRequestedProcedure, string.Empty);
            ds.Add(DicomTag.OrderEnteredBy, string.Empty);
            ds.Add(DicomTag.OrderEntererLocation, string.Empty);
            ds.Add(DicomTag.ImagingServiceRequestComments, string.Empty);

            // Add Requested Procedure Code Sequence (Return Key)
            var reqProcCodeItem = new DicomDataset
            {
                { DicomTag.CodeValue, string.Empty },
                { DicomTag.CodeMeaning, string.Empty },
                { DicomTag.CodingSchemeDesignator, string.Empty }
            };
            ds.Add(new DicomSequence(DicomTag.RequestedProcedureCodeSequence, reqProcCodeItem));

            // Sequence items inherit the Specific Character Set from the parent dataset when written.
            var sps = new DicomDataset();
            sps.Add(DicomTag.Modality, modality);
            sps.Add(DicomTag.ScheduledStationAETitle, stationAet);
            sps.Add(DicomTag.ScheduledStationName, stationName);
            sps.Add(DicomTag.ScheduledProcedureStepStartDate, BuildDateRange(StartDate, EndDate));
            sps.Add(DicomTag.ScheduledProcedureStepID, string.Empty);
            sps.Add(DicomTag.ScheduledProcedureStepStartTime, string.Empty);
            sps.Add(DicomTag.ScheduledProcedureStepDescription, string.Empty);
            sps.Add(DicomTag.ScheduledProcedureStepStatus, string.Empty);
            sps.Add(DicomTag.ScheduledProcedureStepLocation, string.Empty);
            sps.Add(DicomTag.PreMedication, string.Empty);
            sps.Add(DicomTag.ScheduledPerformingPhysicianName, string.Empty);
            sps.Add(DicomTag.CommentsOnTheScheduledProcedureStep, string.Empty);

            var protoItem = new DicomDataset
            {
                { DicomTag.CodeValue, string.Empty },
                { DicomTag.CodeMeaning, string.Empty },
                { DicomTag.CodingSchemeDesignator, string.Empty }
            };
            sps.Add(new DicomSequence(DicomTag.ScheduledProtocolCodeSequence, protoItem));

            // Fill in the extra matching keys. Most of them are already present as empty return keys.
            foreach (var (definition, value) in extraKeys)
            {
                var target = definition.Level == QueryKeyLevel.Sps ? sps : ds;
                target.AddOrUpdate(definition.Tag, value);
                _logger.Debug("Extra query key {key} {tag} = {value}", definition.Id, definition.TagText, value);
            }

            ds.Add(new DicomSequence(DicomTag.ScheduledProcedureStepSequence, sps));

            return new DicomCFindRequest(DicomQueryRetrieveLevel.NotApplicable) { Dataset = ds };
        }

        private IDicomClient CreateClient()
        {
            var client = UseTLS
                ? DicomClientFactory.Create(ServerHost, ServerPort, CreateTlsInitiator(), CallingAET, ServerAET)
                : DicomClientFactory.Create(ServerHost, ServerPort, false, CallingAET, ServerAET);

            client.ServiceOptions.LogDimseDatasets = Settings.Default.Client_LogDimseDatasets;
            client.ServiceOptions.LogDataPDUs = Settings.Default.Client_LogDataPDUs;

            var connectSeconds = Settings.Default.ConnectionTimeoutSeconds;
            var assocSeconds = Settings.Default.AssociationTimeoutSeconds;
            if (connectSeconds > 0) client.ClientOptions.ConnectionTimeoutInMs = connectSeconds * 1000;
            if (assocSeconds > 0) client.ClientOptions.AssociationRequestTimeoutInMs = assocSeconds * 1000;
            // The response timeout is handled by TimeoutWatch, see there why fo-dicom's RequestTimeout is not used.
            client.ServiceOptions.RequestTimeout = null;

            return client;
        }

        private ITlsInitiator CreateTlsInitiator()
        {
            var initiator = new DefaultTlsInitiator
            {
                IgnoreSslPolicyErrors = TlsIgnoreCertErrors
            };

            if (!string.IsNullOrWhiteSpace(TlsClientCertThumbprint))
            {
                var cert = FindCertificate(TlsClientCertThumbprint);
                if (cert == null)
                {
                    throw new InvalidOperationException(
                        $"The client certificate with thumbprint {TlsClientCertThumbprint} was not found in the current user's personal certificate store. Select another one in TLS options.");
                }
                initiator.Certificates = new X509CertificateCollection { cert };
                _logger.Information("Using TLS client certificate {subject} ({thumbprint})", cert.Subject, cert.Thumbprint);
            }

            return initiator;
        }

        /// <summary>Looks up a certificate by thumbprint in CurrentUser\My. Returns null if not found.</summary>
        public static X509Certificate2 FindCertificate(string thumbprint)
        {
            if (string.IsNullOrWhiteSpace(thumbprint)) return null;
            var clean = new string(thumbprint.Where(Uri.IsHexDigit).ToArray());
            var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            try
            {
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                var found = store.Certificates.Find(X509FindType.FindByThumbprint, clean, false);
                return found.Count > 0 ? found[0] : null;
            }
            finally
            {
                store.Close();
            }
        }

        private void HandleError(string context, Exception ex)
        {
            _logger.Error(ex, context);
            var message = GetUserMessage(ex);
            SetStatus($"{context}: {message}");
            ShowMessage($"{context}:\n{message}", MessageBoxImage.Error);
        }

        /// <summary>
        /// Returns the message of the exception that actually caused the error. fo-dicom wraps e.g. a refused
        /// connection in an AggregateException whose own message is only "One or more errors occurred".
        /// </summary>
        private static string GetUserMessage(Exception ex)
        {
            while (true)
            {
                if (ex is AggregateException agg)
                {
                    var flat = agg.Flatten();
                    if (flat.InnerExceptions.Count == 1)
                    {
                        ex = flat.InnerExceptions[0];
                        continue;
                    }
                    if (flat.InnerExceptions.Count > 1)
                    {
                        return string.Join("\n", flat.InnerExceptions.Select(GetUserMessage).Distinct());
                    }
                }
                return ex.Message;
            }
        }

        private void ShowMessage(string message, MessageBoxImage icon, string title = null)
        {
            if (title == null)
            {
                switch (icon)
                {
                    case MessageBoxImage.Error: title = "Error"; break;
                    case MessageBoxImage.Warning: title = "Warning"; break;
                    default: title = "Information"; break;
                }
            }
            MessageBox.Show(message, title, MessageBoxButton.OK, icon);
        }
    }
}

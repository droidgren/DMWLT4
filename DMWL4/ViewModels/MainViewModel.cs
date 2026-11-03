using DMWLT4.Properties;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using MWL4.Extensions;
using MWL4.Infrastructure;
using MWL4.Models;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Linq;

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
        #endregion

        private string _statusText = "Ready";
        public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }

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

        public ICommand DicomEchoCommand { get; }
        public ICommand QueryCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand AboutCommand { get; }
        public ICommand PingCommand { get; }
        public ICommand ClearResultsCommand { get; }
        public ICommand SelectionChangedCommand { get; }

        public MainViewModel()
        {
            _logger = Log.ForContext<MainViewModel>();
            _logger.Information("MainViewModel initialized");

            LoadSettings();

            // Network commands are disabled while another network operation is running,
            // otherwise e.g. a double-clicked Query runs two C-FINDs into the same result list.
            DicomEchoCommand = new RelayCommand(async _ => await RunBusyAsync(DoEchoAsync), _ => !IsBusy);
            QueryCommand = new RelayCommand(async _ => await RunBusyAsync(DoQueryAsync), _ => !IsBusy);
            PingCommand = new RelayCommand(async _ => await RunBusyAsync(DoPingAsync), _ => !IsBusy);
            AboutCommand = new RelayCommand(_ => ShowAbout());
            ExitCommand = new RelayCommand(_ =>
            {
                SaveSettings();
                Application.Current?.Shutdown();
            });

            ClearResultsCommand = new RelayCommand(_ => ClearResults(), _ => !IsBusy);

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

        private async Task RunBusyAsync(Func<Task> action)
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                await action();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void LoadSettings()
        {
            _callingAET = Settings.Default.CallingAET ?? string.Empty;
            _serverAET = Settings.Default.ServerAET ?? string.Empty;
            _serverHost = Settings.Default.ServerHost ?? string.Empty;
            _serverPort = Settings.Default.ServerPort;
            _useTLS = Settings.Default.UseTLS;
        }

        private void SaveSettings()
        {
            try
            {
                Settings.Default.Save();
                _logger.Debug("Settings saved successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save settings");
            }
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

        private async Task DoEchoAsync()
        {
            if (!ValidateConnection()) return;

            StatusText = "Performing C-ECHO...";
            _logger.Information("C-ECHO -> {host}:{port}", ServerHost, ServerPort);

            try
            {
                var client = CreateClient();
                var request = new DicomCEchoRequest();
                DicomStatus echoStatus = null;
                request.OnResponseReceived += (req, res) => echoStatus = res.Status;

                var sw = Stopwatch.StartNew();
                await client.AddRequestAsync(request);
                await client.SendAsync();
                sw.Stop();

                // SendAsync completing without an exception does not guarantee that a successful
                // C-ECHO response was received, so check the actual response status.
                if (echoStatus == null)
                {
                    _logger.Warning("C-ECHO: no response received");
                    SetStatus("Echo failed: no response received.");
                    ShowMessage($"No C-ECHO response received from {ServerHost}.", MessageBoxImage.Warning);
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
            catch (Exception ex)
            {
                HandleError("C-ECHO failed", ex);
            }
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

        private async Task DoQueryAsync()
        {
            if (!ValidateConnection()) return;

            if (StartDate.HasValue && EndDate.HasValue && StartDate.Value.Date > EndDate.Value.Date)
            {
                ShowMessage("Start date is after end date.", MessageBoxImage.Warning);
                return;
            }

            WorklistResults.Clear();
            SelectedCount = 0;
            _statusSuffix = string.Empty;
            StatusText = "Querying Worklist...";

            try
            {
                var request = BuildCFindRequest();
                var client = CreateClient();

                DicomStatus finalStatus = null;
                var pendingResponses = 0;
                var pendingWarnings = 0;
                var mappingErrors = 0;

                request.OnResponseReceived += (req, res) =>
                {
                    // Every match is sent with a Pending *state*, but the status code may be either
                    // FF00 (Pending) or FF01 (Pending - one or more optional keys not supported).
                    // DicomStatus == compares the code, so comparing against DicomStatus.Pending
                    // silently dropped all FF01 matches. Compare the state instead.
                    if (res.Status.State == DicomState.Pending)
                    {
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
                        finalStatus = res.Status;
                    }
                };

                await client.AddRequestAsync(request);
                await client.SendAsync();

                _logger.Information(
                    "Query completed. Results: {count}, pending responses: {pending} (of which FF01/warning: {warnings}), mapping errors: {errors}, final status: {status}",
                    WorklistResults.Count, pendingResponses, pendingWarnings, mappingErrors, finalStatus?.ToString() ?? "<none>");

                var suffix = string.Empty;
                if (finalStatus == null)
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

                SetStatus(suffix.Trim());
            }
            catch (Exception ex)
            {
                HandleError("Worklist query failed", ex);
            }
        }

        private void ClearResults()
        {
            WorklistResults.Clear();
            SelectedCount = 0;
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

        private WorklistItem MapDatasetToWorklistItem(DicomDataset ds)
        {
            var spsSeq = DicomTag.ScheduledProcedureStepSequence;
            var protoSeq = DicomTag.ScheduledProtocolCodeSequence;
            var reqProcSeq = DicomTag.RequestedProcedureCodeSequence;

            return new WorklistItem
            {
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
            var charset = DetectCharacterSet(patientName, patientId, stationName);
            if (charset != null)
            {
                ds.Add(DicomTag.SpecificCharacterSet, charset);
                _logger.Debug("Query contains non-ASCII characters, using Specific Character Set {charset}", charset);
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
            ds.Add(new DicomSequence(DicomTag.ScheduledProcedureStepSequence, sps));

            return new DicomCFindRequest(DicomQueryRetrieveLevel.NotApplicable) { Dataset = ds };
        }

        private IDicomClient CreateClient()
        {
            var client = DicomClientFactory.Create(ServerHost, ServerPort, UseTLS, CallingAET, ServerAET);
            client.ServiceOptions.LogDimseDatasets = Settings.Default.Client_LogDimseDatasets;
            client.ServiceOptions.LogDataPDUs = Settings.Default.Client_LogDataPDUs;
            return client;
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

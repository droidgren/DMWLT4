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

        private DateTime _startDate = DateTime.Today;
        public DateTime StartDate { get => _startDate; set { _startDate = value; OnPropertyChanged(); } }

        private DateTime _endDate = DateTime.Today;
        public DateTime EndDate { get => _endDate; set { _endDate = value; OnPropertyChanged(); } }
        #endregion

        private string _statusText = "Ready";
        public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }

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

            DicomEchoCommand = new RelayCommand(async _ => await DoEchoAsync());
            QueryCommand = new RelayCommand(async _ => await DoQueryAsync());
            PingCommand = new RelayCommand(async _ => await DoPingAsync());
            AboutCommand = new RelayCommand(_ => ShowAbout());
            ExitCommand = new RelayCommand(_ =>
            {
                SaveSettings();
                Application.Current?.Shutdown();
            });

            ClearResultsCommand = new RelayCommand(_ => ClearResults());

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

        private async Task DoEchoAsync()
        {
            if (string.IsNullOrWhiteSpace(ServerHost))
            {
                ShowMessage("Called host is empty.", MessageBoxImage.Warning);
                return;
            }

            StatusText = "Performing C-ECHO...";
            _logger.Information("C-ECHO -> {host}:{port}", ServerHost, ServerPort);

            try
            {
                var client = CreateClient();
                await client.AddRequestAsync(new DicomCEchoRequest());
                await client.SendAsync();

                _logger.Information("C-ECHO success");
                ShowMessage($"Connection to {ServerHost} successful.", MessageBoxImage.Information);
                StatusText = "Echo successful";
                UpdateStatusWithCounts();
            }
            catch (Exception ex)
            {
                HandleError("C-ECHO failed", ex);
            }
        }

        private async Task DoPingAsync()
        {
            if (string.IsNullOrWhiteSpace(ServerHost))
            {
                ShowMessage("Called host is empty.", MessageBoxImage.Warning);
                return;
            }

            StatusText = $"Pinging {ServerHost}...";
            try
            {
                using (var p = new Ping())
                {
                    var reply = await p.SendPingAsync(ServerHost, 4000);
                    if (reply.Status == IPStatus.Success)
                    {
                        ShowMessage($"Ping Success.\nTime: {reply.RoundtripTime}ms", MessageBoxImage.Information, "Ping Result");
                        StatusText = "Ping successful";
                        UpdateStatusWithCounts();
                    }
                    else
                    {
                        ShowMessage($"Ping Failed: {reply.Status}", MessageBoxImage.Warning, "Ping Result");
                        StatusText = "Ping failed";
                        UpdateStatusWithCounts();
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
            WorklistResults.Clear();
            SelectedCount = 0;
            StatusText = "Querying Worklist...";

            try
            {
                var request = BuildCFindRequest();
                var client = CreateClient();

                request.OnResponseReceived += (req, res) =>
                {
                    if (res.HasDataset && res.Status == DicomStatus.Pending)
                    {
                        var item = MapDatasetToWorklistItem(res.Dataset);
                        Application.Current?.Dispatcher?.Invoke(() => WorklistResults.Add(item));
                    }
                };

                await client.AddRequestAsync(request);
                await client.SendAsync();

                UpdateStatusWithCounts();
                _logger.Information("Query completed. Results: {count}", WorklistResults.Count);
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
            StatusText = "Cleared results.";
            UpdateStatusWithCounts();
        }

        private void UpdateStatusWithCounts()
        {
            var baseText = $"Found {WorklistResults.Count} items.";
            if (SelectedCount > 0)
            {
                StatusText = $"{baseText} {SelectedCount} selected.";
            }
            else
            {
                StatusText = baseText;
            }
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

                RequestingPhysician = ds.GetStringSafe(DicomTag.RequestingPhysician), // New
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
                ScheduledProtocolCodeValue = ds.GetNestedStringSafe(spsSeq, protoSeq, DicomTag.CodeValue),
                ScheduledProtocolCodeMeaning = ds.GetNestedStringSafe(spsSeq, protoSeq, DicomTag.CodeMeaning),
                ScheduledProtocolCodeScheme = ds.GetNestedStringSafe(spsSeq, protoSeq, DicomTag.CodingSchemeDesignator)
            };
        }

        private DicomCFindRequest BuildCFindRequest()
        {
            var ds = new DicomDataset
            {
                { DicomTag.PatientName, string.IsNullOrWhiteSpace(PatientName) ? string.Empty : PatientName.Trim() + "*" },
                { DicomTag.PatientID, PatientID?.Trim() ?? string.Empty },
                { DicomTag.PatientSex, string.Empty },
                { DicomTag.PatientBirthDate, string.Empty },
                { DicomTag.PatientAge, string.Empty },
                { DicomTag.PatientSize, string.Empty },
                { DicomTag.PatientWeight, string.Empty },
                { DicomTag.MedicalAlerts, string.Empty },
                { DicomTag.Allergies, string.Empty },

                { DicomTag.AccessionNumber, string.Empty },
                { DicomTag.AdmissionID, string.Empty },
                { DicomTag.CurrentPatientLocation, string.Empty },
                { DicomTag.StudyInstanceUID, string.Empty },

                { DicomTag.RequestingPhysician, string.Empty }, // New
                { DicomTag.RequestedProcedureDescription, string.Empty },
                { DicomTag.RequestedProcedureID, string.Empty },
                { DicomTag.RequestedProcedurePriority, string.Empty },
                { DicomTag.ReasonForTheRequestedProcedure, string.Empty },
                { DicomTag.OrderEnteredBy, string.Empty },
                { DicomTag.OrderEntererLocation, string.Empty },
                { DicomTag.ImagingServiceRequestComments, string.Empty }
            };

            // Add Requested Procedure Code Sequence (Return Key)
            var reqProcCodeItem = new DicomDataset
            {
                { DicomTag.CodeValue, string.Empty },
                { DicomTag.CodeMeaning, string.Empty },
                { DicomTag.CodingSchemeDesignator, string.Empty }
            };
            ds.Add(new DicomSequence(DicomTag.RequestedProcedureCodeSequence, reqProcCodeItem));

            var sps = new DicomDataset
            {
                { DicomTag.Modality, Modality?.Trim() ?? string.Empty },
                { DicomTag.ScheduledStationAETitle, StationAET?.Trim() ?? string.Empty },
                { DicomTag.ScheduledStationName, StationName?.Trim() ?? string.Empty },
                { DicomTag.ScheduledProcedureStepStartDate, $"{StartDate:yyyyMMdd}-{EndDate:yyyyMMdd}" },
                { DicomTag.ScheduledProcedureStepID, string.Empty },
                { DicomTag.ScheduledProcedureStepStartTime, string.Empty },
                { DicomTag.ScheduledProcedureStepDescription, string.Empty },
                { DicomTag.ScheduledProcedureStepStatus, string.Empty },
                { DicomTag.ScheduledProcedureStepLocation, string.Empty },
                { DicomTag.PreMedication, string.Empty },
                { DicomTag.ScheduledPerformingPhysicianName, string.Empty },
                { DicomTag.CommentsOnTheScheduledProcedureStep, string.Empty }
            };

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
            StatusText = $"{context}: {ex.Message}";
            ShowMessage($"{context}:\n{ex.Message}", MessageBoxImage.Error);
        }

        private void ShowMessage(string message, MessageBoxImage icon, string title = "Information")
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, icon);
        }
    }
}
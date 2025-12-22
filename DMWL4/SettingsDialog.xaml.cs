using DMWLT4.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace MWL4
{
    public partial class SettingsDialog : Window
    {
        public class ColumnSetting
        {
            public bool IsVisible { get; set; }
            public string PropertyName { get; set; } // Display Name (with spaces)
            public string BoundProperty { get; set; } // Actual C# Property
            public string TagNumber { get; set; }
        }

        private List<ColumnSetting> _columns;

        public SettingsDialog()
        {
            InitializeComponent();
            LoadColumns();
        }

        private void LoadColumns()
        {
            var savedSetting = Settings.Default.VisibleColumns ?? string.Empty;
            var visibleSet = new HashSet<string>(savedSetting.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));

            // Map: Display Name (Spaces) -> (C# Property, DICOM Tag)
            var definitions = new List<(string Display, string Prop, string Tag)>
            {
                ("Patient Name", "PatientName", "(0010,0010)"),
                ("Patient ID", "PatientID", "(0010,0020)"),
                ("Patient Birth Date", "PatientBirthDate", "(0010,0030)"),
                ("Patient Sex", "PatientSex", "(0010,0040)"),
                ("Patient Age", "PatientAge", "(0010,1010)"),
                ("Patient Size", "PatientSize", "(0010,1020)"),
                ("Patient Weight", "PatientWeight", "(0010,1030)"),
                ("Contrast Allergies", "Allergies", "(0010,2110)"),
                ("Medical Alerts", "MedicalAlerts", "(0010,2000)"),

                ("Accession Number", "AccessionNumber", "(0008,0050)"),
                ("Requesting Physician", "RequestingPhysician", "(0032,1032)"), // New
                ("Requested Procedure Description", "RequestedProcedureDescription", "(0032,1060)"),
                ("Requested Procedure Code Sequence", "RequestedProcedureCodeSequence", "(0032,1064)"),
                ("Requested Procedure ID", "RequestedProcedureID", "(0040,1001)"),
                ("Requested Procedure Priority", "RequestedProcedurePriority", "(0040,1003)"),
                ("Reason for the Requested Procedure", "ReasonForRequestedProcedure", "(0040,2001)"),
                ("Order Entered By", "OrderEnteredBy", "(0040,2008)"),
                ("Order Enterer Location", "OrderEntererLocation", "(0040,2009)"),
                ("Imaging Service Request Comments", "ImagingServiceRequestComments", "(0040,2400)"),

                ("Modality", "Modality", "(0008,0060)"),
                ("Scheduled Station AE Title", "ScheduledStationAET", "(0040,0001)"),
                ("SPS Start Date", "ScheduledDate", "(0040,0002)"),
                ("SPS Start Time", "ScheduledTime", "(0040,0003)"),
                ("Scheduled Performing Physician Name", "ScheduledPerformingPhysicianName", "(0040,0006)"),
                ("Scheduled Procedure Step Description", "ExamDescription", "(0040,0007)"),
                ("Scheduled Procedure Step ID", "ScheduledProcedureStepID", "(0040,0009)"),
                ("Scheduled Station Name", "ScheduledStationName", "(0040,0010)"),
                ("Scheduled Procedure Step Location", "ScheduledProcedureStepLocation", "(0040,0011)"),
                ("Pre-Medication", "PreMedication", "(0040,0012)"),
                ("SPS Status", "ScheduledProcedureStepStatus", "(0040,0020)"),

                ("Protocol Code Value", "ScheduledProtocolCodeValue", "(0008,0100)"),
                ("Protocol Code Meaning", "ScheduledProtocolCodeMeaning", "(0008,0104)"),
                ("Protocol Code Scheme", "ScheduledProtocolCodeScheme", "(0008,0102)"),

                ("Study Instance UID", "StudyInstanceUID", "(0020,000D)"),
                ("Admission ID", "AdmissionID", "(0038,0010)"),
                ("Current Patient Location", "CurrentPatientLocation", "(0038,0300)")
            };

            _columns = new List<ColumnSetting>();

            foreach (var def in definitions)
            {
                _columns.Add(new ColumnSetting
                {
                    PropertyName = def.Display,
                    BoundProperty = def.Prop,
                    TagNumber = def.Tag,
                    IsVisible = visibleSet.Contains(def.Prop)
                });
            }

            // Sort alphabetically by the Display Name
            _columns.Sort((a, b) => string.Compare(a.PropertyName, b.PropertyName, StringComparison.Ordinal));

            ColumnsGrid.ItemsSource = _columns;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var visibleNames = _columns
                .Where(c => c.IsVisible)
                .Select(c => c.BoundProperty)
                .ToArray();

            Settings.Default.VisibleColumns = string.Join(";", visibleNames);
            Settings.Default.Save();

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
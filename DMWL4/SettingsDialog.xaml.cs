using DMWLT4.Properties;
using MWL4.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

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

        public class QueryKeySetting
        {
            public bool IsSelected { get; set; }
            public QueryKeyDefinition Definition { get; set; }
            public string Id => Definition.Id;
            public string Label => Definition.Label;
            public string TagText => Definition.TagText;
            public string FormatText => Definition.FormatText;
        }

        private List<ColumnSetting> _columns;
        private List<QueryKeySetting> _queryKeys;

        /// <summary>True if "Reset to default" was used. The caller then restores the original column order and widths.</summary>
        public bool ColumnLayoutReset { get; private set; }

        public SettingsDialog()
        {
            InitializeComponent();
            LoadColumns();
            LoadQueryKeys();

            // All options are copied into the dialog and only written back on OK,
            // so that Cancel really discards the changes.
            LogDimseDatasets.IsChecked = Settings.Default.Client_LogDimseDatasets;
            LogDataPDUs.IsChecked = Settings.Default.Client_LogDataPDUs;

            ConnectionTimeoutBox.Text = Settings.Default.ConnectionTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            AssociationTimeoutBox.Text = Settings.Default.AssociationTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            ResponseTimeoutBox.Text = Settings.Default.ResponseTimeoutSeconds.ToString(CultureInfo.InvariantCulture);

            CharacterSetBox.SelectedValue = Settings.Default.CharacterSet;
            if (CharacterSetBox.SelectedIndex < 0) CharacterSetBox.SelectedIndex = 0;
        }

        private static HashSet<string> ParseColumns(string setting) =>
            new HashSet<string>((setting ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));

        /// <summary>The default VisibleColumns value, taken from the setting's [DefaultSettingValue].</summary>
        public static string DefaultVisibleColumns =>
            Settings.Default.Properties["VisibleColumns"]?.DefaultValue as string ?? string.Empty;

        private void LoadColumns()
        {
            var visibleSet = ParseColumns(Settings.Default.VisibleColumns);

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

                ("Specific Character Set", "SpecificCharacterSet", "(0008,0005)"),
                ("Accession Number", "AccessionNumber", "(0008,0050)"),
                ("Referring Physician", "ReferringPhysicianName", "(0008,0090)"),
                ("Requesting Physician", "RequestingPhysician", "(0032,1032)"),
                ("Requested Procedure Description", "RequestedProcedureDescription", "(0032,1060)"),
                ("Requested Procedure Code Sequence", "RequestedProcedureCodeSequence", "(0032,1064)"),
                ("Requested Procedure ID", "RequestedProcedureID", "(0040,1001)"),
                ("Requested Procedure Priority", "RequestedProcedurePriority", "(0040,1003)"),
                ("Reason for the Requested Procedure", "ReasonForRequestedProcedure", "(0040,1002)"),
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
                ("Comments on the Scheduled Procedure Step", "CommentsOnScheduledProcedureStep", "(0040,0400)"),

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

        private void LoadQueryKeys()
        {
            var chosen = ParseColumns(Settings.Default.ExtraQueryKeys);
            _queryKeys = QueryKeyDefinition.All
                .Select(d => new QueryKeySetting { Definition = d, IsSelected = chosen.Contains(d.Id) })
                .ToList();
            QueryKeysGrid.ItemsSource = _queryKeys;
        }

        private void ClearQueryKeysButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var key in _queryKeys) key.IsSelected = false;
            QueryKeysGrid.CommitEdit();
            QueryKeysGrid.Items.Refresh();
        }

        private void ResetColumnsButton_Click(object sender, RoutedEventArgs e)
        {
            var defaults = ParseColumns(DefaultVisibleColumns);
            foreach (var column in _columns)
            {
                column.IsVisible = defaults.Contains(column.BoundProperty);
            }
            ColumnsGrid.CommitEdit();
            ColumnsGrid.Items.Refresh();
            ColumnLayoutReset = true;
        }

        /// <summary>Parses a timeout box. Shows a warning and returns false if the value is outside min..3600.</summary>
        private bool TryReadSeconds(TextBox box, string name, int min, out int value)
        {
            if (int.TryParse(box.Text?.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out value) && value >= min && value <= 3600)
            {
                return true;
            }
            // Show the tab with the invalid value first.
            for (DependencyObject d = box; d != null; d = LogicalTreeHelper.GetParent(d))
            {
                if (d is TabItem tab)
                {
                    tab.IsSelected = true;
                    break;
                }
            }
            MessageBox.Show(this, $"{name} must be a whole number of seconds between {min} and 3600.", "Warning",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            box.Focus();
            box.SelectAll();
            return false;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadSeconds(ConnectionTimeoutBox, "Connection timeout", 1, out var connectSeconds)) return;
            if (!TryReadSeconds(AssociationTimeoutBox, "Association timeout", 1, out var assocSeconds)) return;
            if (!TryReadSeconds(ResponseTimeoutBox, "Response timeout", 0, out var responseSeconds)) return;

            var visibleNames = _columns
                .Where(c => c.IsVisible)
                .Select(c => c.BoundProperty)
                .ToArray();

            Settings.Default.VisibleColumns = string.Join(";", visibleNames);
            Settings.Default.Client_LogDimseDatasets = LogDimseDatasets.IsChecked == true;
            Settings.Default.Client_LogDataPDUs = LogDataPDUs.IsChecked == true;
            Settings.Default.ConnectionTimeoutSeconds = connectSeconds;
            Settings.Default.AssociationTimeoutSeconds = assocSeconds;
            Settings.Default.ResponseTimeoutSeconds = responseSeconds;
            Settings.Default.CharacterSet = CharacterSetBox.SelectedValue as string ?? "Auto";
            Settings.Default.ExtraQueryKeys = string.Join(";", _queryKeys.Where(k => k.IsSelected).Select(k => k.Id));
            if (ColumnLayoutReset)
            {
                Settings.Default.ColumnLayout = string.Empty;
            }
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

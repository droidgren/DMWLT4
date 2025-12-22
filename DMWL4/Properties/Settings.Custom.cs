using System.Configuration;

namespace DMWLT4.Properties
{
    [SettingsProvider(typeof(MWL4.Properties.CustomFileSettingsProvider))]
    internal sealed partial class Settings
    {
        // Default stores the list of columns visible by default
        [UserScopedSetting]
        [DefaultSettingValue("PatientName;PatientID;AccessionNumber;RequestedProcedureID;RequestedProcedureDescription;ExamDescription;ScheduledTime;ScheduledDate;ScheduledProcedureStepID;ScheduledProcedureStepStatus;Modality;ScheduledStationAET;ScheduledStationName;ScheduledProtocolCodeValue;ScheduledProtocolCodeMeaning;StudyInstanceUID")]
        public string VisibleColumns
        {
            get => (string)this["VisibleColumns"];
            set => this["VisibleColumns"] = value;
        }
    }
}
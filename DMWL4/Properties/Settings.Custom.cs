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

        /// <summary>Column order and width of the results table: "Prop=displayIndex,width;..." (empty width = Auto).</summary>
        [UserScopedSetting]
        [DefaultSettingValue("")]
        public string ColumnLayout
        {
            get => (string)this["ColumnLayout"];
            set => this["ColumnLayout"] = value;
        }

        /// <summary>Whether the filter bar above the results table is shown (File -> View Filter Results). Hidden by default.</summary>
        [UserScopedSetting]
        [DefaultSettingValue("False")]
        public bool ShowFilterBar
        {
            get => (bool)this["ShowFilterBar"];
            set => this["ShowFilterBar"] = value;
        }

        /// <summary>TCP connection timeout in seconds.</summary>
        [UserScopedSetting]
        [DefaultSettingValue("10")]
        public int ConnectionTimeoutSeconds
        {
            get => (int)this["ConnectionTimeoutSeconds"];
            set => this["ConnectionTimeoutSeconds"] = value;
        }

        /// <summary>Timeout in seconds for the association request (A-ASSOCIATE-AC/RJ).</summary>
        [UserScopedSetting]
        [DefaultSettingValue("10")]
        public int AssociationTimeoutSeconds
        {
            get => (int)this["AssociationTimeoutSeconds"];
            set => this["AssociationTimeoutSeconds"] = value;
        }

        /// <summary>Timeout in seconds for DIMSE responses. 0 = no limit.</summary>
        [UserScopedSetting]
        [DefaultSettingValue("30")]
        public int ResponseTimeoutSeconds
        {
            get => (int)this["ResponseTimeoutSeconds"];
            set => this["ResponseTimeoutSeconds"] = value;
        }

        /// <summary>Extra query keys shown in the Query panel: QueryKeyDefinition Ids, ';'-separated.</summary>
        [UserScopedSetting]
        [DefaultSettingValue("")]
        public string ExtraQueryKeys
        {
            get => (string)this["ExtraQueryKeys"];
            set => this["ExtraQueryKeys"] = value;
        }

        /// <summary>Specific Character Set for queries: "Auto", "ISO_IR 100" or "ISO_IR 192".</summary>
        [UserScopedSetting]
        [DefaultSettingValue("Auto")]
        public string CharacterSet
        {
            get => (string)this["CharacterSet"];
            set => this["CharacterSet"] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("False")]
        public bool TlsIgnoreCertErrors
        {
            get => (bool)this["TlsIgnoreCertErrors"];
            set => this["TlsIgnoreCertErrors"] = value;
        }

        /// <summary>Thumbprint of the client certificate in CurrentUser\My. Empty = no client certificate.</summary>
        [UserScopedSetting]
        [DefaultSettingValue("")]
        public string TlsClientCertThumbprint
        {
            get => (string)this["TlsClientCertThumbprint"];
            set => this["TlsClientCertThumbprint"] = value;
        }

        /// <summary>Saved connection profiles, serialized as XML.</summary>
        [UserScopedSetting]
        [DefaultSettingValue("")]
        public string ConnectionProfiles
        {
            get => (string)this["ConnectionProfiles"];
            set => this["ConnectionProfiles"] = value;
        }
    }
}

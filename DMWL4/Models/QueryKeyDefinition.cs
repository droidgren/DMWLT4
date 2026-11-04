using FellowOakDicom;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MWL4.Models
{
    public enum QueryKeyLevel
    {
        /// <summary>Top level of the C-FIND identifier.</summary>
        Top,
        /// <summary>Inside the Scheduled Procedure Step Sequence (0040,0100).</summary>
        Sps
    }

    public enum QueryKeyKind
    {
        Text,
        /// <summary>Code string (CS): sent in upper case.</summary>
        Code,
        /// <summary>DA: a date or a date range.</summary>
        Date,
        /// <summary>TM: a time or a time range.</summary>
        Time,
        /// <summary>UI: exact match only (no wildcards).</summary>
        Uid
    }

    /// <summary>
    /// An optional matching key that can be added to the Query panel in Settings -> Query.
    /// </summary>
    public class QueryKeyDefinition
    {
        public string Id { get; }
        public string Label { get; }
        public DicomTag Tag { get; }
        public QueryKeyLevel Level { get; }
        public QueryKeyKind Kind { get; }

        public QueryKeyDefinition(string id, string label, DicomTag tag, QueryKeyLevel level, QueryKeyKind kind = QueryKeyKind.Text)
        {
            Id = id;
            Label = label;
            Tag = tag;
            Level = level;
            Kind = kind;
        }

        public string TagText => $"({Tag.Group:X4},{Tag.Element:X4})" + (Level == QueryKeyLevel.Sps ? " in SPS" : string.Empty);

        public string FormatText
        {
            get
            {
                switch (Kind)
                {
                    case QueryKeyKind.Code: return "Code (sent in upper case)";
                    case QueryKeyKind.Date: return "yyyyMMdd or yyyyMMdd-yyyyMMdd";
                    case QueryKeyKind.Time: return "HHmm[ss] or HHmm-HHmm";
                    case QueryKeyKind.Uid: return "UID, exact match";
                    default: return "Text as typed, * and ? allowed";
                }
            }
        }

        public string ToolTip => $"{TagText} {Tag.DictionaryEntry?.Name}. {FormatText}.";

        private static readonly Regex DateRegex = new Regex(@"^(\d{8}|\d{8}-|-\d{8}|\d{8}-\d{8})$");
        private const string TimePart = @"\d{2}(\d{2}(\d{2}(\.\d{1,6})?)?)?";
        private static readonly Regex TimeRegex = new Regex($"^({TimePart}|{TimePart}-|-{TimePart}|{TimePart}-{TimePart})$");
        // One or more UIDs (backslash-separated list matching is allowed for UI keys).
        private static readonly Regex UidRegex = new Regex(@"^[0-9.]{1,64}(\\[0-9.]{1,64})*$");

        /// <summary>Returns the value to send (trimmed, upper case for codes), or null if it is invalid for the key's kind.</summary>
        public string Normalize(string value)
        {
            var v = value?.Trim() ?? string.Empty;
            if (v.Length == 0) return v;
            switch (Kind)
            {
                case QueryKeyKind.Code: return v.ToUpperInvariant();
                case QueryKeyKind.Date: return DateRegex.IsMatch(v) ? v : null;
                case QueryKeyKind.Time: return TimeRegex.IsMatch(v) ? v : null;
                case QueryKeyKind.Uid: return UidRegex.IsMatch(v) ? v : null;
                default: return v;
            }
        }

        /// <summary>All keys that can be added, in the order they are shown.</summary>
        public static IReadOnlyList<QueryKeyDefinition> All { get; } = new List<QueryKeyDefinition>
        {
            new QueryKeyDefinition("AccessionNumber", "Accession Number", DicomTag.AccessionNumber, QueryKeyLevel.Top),
            new QueryKeyDefinition("ReferringPhysicianName", "Referring Physician", DicomTag.ReferringPhysicianName, QueryKeyLevel.Top),
            new QueryKeyDefinition("RequestingPhysician", "Requesting Physician", DicomTag.RequestingPhysician, QueryKeyLevel.Top),
            new QueryKeyDefinition("RequestedProcedureID", "Requested Proc. ID", DicomTag.RequestedProcedureID, QueryKeyLevel.Top),
            new QueryKeyDefinition("RequestedProcedurePriority", "Priority", DicomTag.RequestedProcedurePriority, QueryKeyLevel.Top, QueryKeyKind.Code),
            new QueryKeyDefinition("AdmissionID", "Admission ID", DicomTag.AdmissionID, QueryKeyLevel.Top),
            new QueryKeyDefinition("CurrentPatientLocation", "Patient Location", DicomTag.CurrentPatientLocation, QueryKeyLevel.Top),
            new QueryKeyDefinition("StudyInstanceUID", "Study Instance UID", DicomTag.StudyInstanceUID, QueryKeyLevel.Top, QueryKeyKind.Uid),
            new QueryKeyDefinition("PatientBirthDate", "Birth Date", DicomTag.PatientBirthDate, QueryKeyLevel.Top, QueryKeyKind.Date),
            new QueryKeyDefinition("PatientSex", "Patient Sex", DicomTag.PatientSex, QueryKeyLevel.Top, QueryKeyKind.Code),
            new QueryKeyDefinition("ScheduledProcedureStepID", "SPS ID", DicomTag.ScheduledProcedureStepID, QueryKeyLevel.Sps),
            new QueryKeyDefinition("ScheduledProcedureStepStatus", "SPS Status", DicomTag.ScheduledProcedureStepStatus, QueryKeyLevel.Sps, QueryKeyKind.Code),
            new QueryKeyDefinition("ScheduledProcedureStepDescription", "SPS Description", DicomTag.ScheduledProcedureStepDescription, QueryKeyLevel.Sps),
            new QueryKeyDefinition("ScheduledProcedureStepLocation", "SPS Location", DicomTag.ScheduledProcedureStepLocation, QueryKeyLevel.Sps),
            new QueryKeyDefinition("ScheduledPerformingPhysicianName", "Performing Physician", DicomTag.ScheduledPerformingPhysicianName, QueryKeyLevel.Sps),
            new QueryKeyDefinition("ScheduledProcedureStepStartTime", "SPS Start Time", DicomTag.ScheduledProcedureStepStartTime, QueryKeyLevel.Sps, QueryKeyKind.Time),
        };

        public static QueryKeyDefinition Find(string id) => All.FirstOrDefault(d => d.Id == id);
    }
}

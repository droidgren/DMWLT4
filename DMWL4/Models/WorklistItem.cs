namespace MWL4.Models
{
    public class WorklistItem
    {
        public string PatientName { get; set; }
        public string PatientID { get; set; }
        public string PatientSex { get; set; }
        public string PatientBirthDate { get; set; }
        public string PatientAge { get; set; }
        public string PatientSize { get; set; }
        public string PatientWeight { get; set; }
        public string MedicalAlerts { get; set; }
        public string Allergies { get; set; } // Contrast Allergies

        public string AccessionNumber { get; set; }
        public string AdmissionID { get; set; }
        public string CurrentPatientLocation { get; set; }
        public string StudyInstanceUID { get; set; }

        public string RequestingPhysician { get; set; } // New (0032,1032)
        public string RequestedProcedureID { get; set; }
        public string RequestedProcedureDescription { get; set; }
        public string RequestedProcedureCodeSequence { get; set; }
        public string RequestedProcedurePriority { get; set; }
        public string ReasonForRequestedProcedure { get; set; }

        public string OrderEnteredBy { get; set; }
        public string OrderEntererLocation { get; set; }
        public string ImagingServiceRequestComments { get; set; }

        public string ScheduledProcedureStepID { get; set; }
        public string Modality { get; set; }
        public string ScheduledDate { get; set; }
        public string ScheduledTime { get; set; }
        public string ExamDescription { get; set; }
        public string ScheduledStationAET { get; set; }
        public string ScheduledStationName { get; set; }
        public string ScheduledProcedureStepStatus { get; set; }
        public string ScheduledProcedureStepLocation { get; set; }
        public string PreMedication { get; set; }
        public string ScheduledPerformingPhysicianName { get; set; }
        public string ScheduledProtocolCodeValue { get; set; }
        public string ScheduledProtocolCodeMeaning { get; set; }
        public string ScheduledProtocolCodeScheme { get; set; }
    }
}
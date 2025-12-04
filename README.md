# DICOM Modality Worklist Tester

![Version](https://img.shields.io/badge/version-1.1-blue)
![License](https://img.shields.io/badge/license-MIT-green)
![Platform](https://img.shields.io/badge/platform-Windows-lightgrey)
![.NET Framework](https://img.shields.io/badge/.NET-4.8-purple)

**DICOM Modality Worklist Tester** is a lightweight, standalone utility designed to simulate a Modality (SCU) for testing and verifying DICOM Modality Worklist (MWL) services on a PACS or RIS (SCP).

Built on the robust **Fellow Oak DICOM (fo-dicom)** library, this tool provides a reliable way to query worklists without the need for complex installation or heavy runtimes.

![Application Screenshot](/Screenshots/print_screen.jpg)

## 🚀 Features

* **Zero Dependencies:** Built on .NET 4.8 (included natively in Windows 10 and 11). No external runtime installation is required.
* **Portable:** Single executable with a small file size.
* **Connectivity Tools:** Integrated network **Ping** and DICOM Verification (**C-ECHO**) to troubleshoot connection issues before querying.
* **Secure:** Full support for **TLS** (Transport Layer Security) for testing secure DICOM connections.
* **Deep Logging:** Extensive logging settings to analyze communication traffic and debug DICOM associations.
* **Open Source:** Free to use, modify, and distribute under the MIT License.

## 📋 Supported DICOM Tags

The application handles specific DICOM tags for querying and retrieving worklist items.

### Query Keys
These tags can be used as matching keys when sending a C-FIND request to the Worklist SCP.

| Tag Name | Tag ID | Description |
| :--- | :--- | :--- |
| `PatientName` | (0010,0010) | Patient's Name |
| `PatientID` | (0010,0020) | Patient ID |
| `AccessionNumber` | (0008,0050) | Accession Number |
| `StudyInstanceUID` | (0020,000D) | Study Instance UID |
| `RequestedProcedureID` | (0040,1001) | Requested Procedure ID |
| `RequestedProcedureDescription` | (0032,1060) | Requested Procedure Description |
| `ScheduledProcedureStepID` | (0040,0009) | Scheduled Procedure Step ID |
| `Modality` | (0008,0060) | Modality |
| `ScheduledDate` | (0040,0002) | Scheduled Procedure Step Start Date |
| `ScheduledTime` | (0040,0003) | Scheduled Procedure Step Start Time |
| `ExamDescription` | (0032,1030) | Study Description / Exam Description |
| `ScheduledStationAET` | (0040,0001) | Scheduled Station AE Title |
| `ScheduledStationName` | (0040,0010) | Scheduled Station Name |
| `ScheduledProcedureStepStatus` | (0040,0020) | Scheduled Procedure Step Status |
| `ScheduledProtocolCodeValue` | (0040,0008) | > (0008,0100) Code Value |
| `ScheduledProtocolCodeMeaning` | (0040,0008) | > (0008,0104) Code Meaning |

### Log-Only Tags
These tags are retrieved in the response and are visible in the application logs for validation purposes, but are not used as primary query filters in the UI.

| Tag Name | Tag ID | Description |
| :--- | :--- | :--- |
| `PatientSex` | (0010,0040) | Patient's Sex |
| `PatientBirthDate` | (0010,0030) | Patient's Birth Date |
| `AdmissionID` | (0038,0010) | Admission ID |
| `CurrentPatientLocation` | (0038,0300) | Current Patient Location |
| `RequestedProcedurePriority` | (0040,1003) | Requested Procedure Priority |
| `ReasonForRequestedProcedure` | (0040,1002) | Reason for Requested Procedure |
| `ScheduledPerformingPhysicianName`| (0040,0006) | Scheduled Performing Physician's Name |
| `ScheduledProtocolCodeScheme` | (0040,0008) | > (0008,0102) Coding Scheme Designator |

## 🛠️ Requirements

* **OS:** Windows 10 or Windows 11.
* **Framework:** .NET Framework 4.8 (Pre-installed on supported OS).

## 📦 Installation

This is a portable application.

1.  Download the latest release.
2.  Extract the files to a folder of your choice.
3.  Run `DMWL4.exe`.

## ⚙️ Usage

1.  **Configure Connection:** Enter the IP, Port, and AE Title of the target Worklist SCP (RIS/PACS).
2.  **Set Local AE:** Configure your local Calling AE Title.
3.  **Test Connection:** Use the **Ping** or **DICOM Echo** buttons to verify network visibility.
4.  **Query:** Enter search criteria (e.g., Modality, Date, or Patient Name) and execute the query.
5.  **Review:** Check the results list or view the detailed Logs for specific tag values.

## 🐛 Reporting Bugs & Feature Requests

If you encounter any bugs or have an idea for a new feature, please open an issue in the [Issues](../../issues) section of this repository.

* **Bugs:** Please provide steps to reproduce the issue and attach relevant logs if possible.
* **Features:** Describe the desired functionality and how it would improve the tool.

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

* Powered by [Fellow Oak DICOM (fo-dicom)](https://github.com/fo-dicom/fo-dicom).
* Original idea by [Sonny Pate](https://github.com/sonnypate/MWL-Tester) project which is built on .NET 8.

---

> **Disclaimer:** This software is intended for testing and simulation purposes only. It is not certified for clinical use or primary diagnosis.

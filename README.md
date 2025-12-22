# <img src="icon.jfif" width="48" height="48" alt="App Icon" style="vertical-align: middle;"> DICOM Modality Worklist Tester

![Version](https://img.shields.io/badge/version-1.1-blue)
![License](https://img.shields.io/badge/license-MIT-green)
![Platform](https://img.shields.io/badge/platform-Windows-lightgrey)
![.NET Framework](https://img.shields.io/badge/.NET-4.8-purple)

**DICOM Modality Worklist Tester** is a lightweight, standalone utility designed to simulate a Modality (SCU) for testing and verifying DICOM Modality Worklist (MWL) services on a PACS or RIS (SCP).

Built on the robust **Fellow Oak DICOM (fo-dicom)** library, this tool provides a reliable way to query worklists without the need for complex installation or heavy runtimes.

## Table of Contents
* [Features](#-features)
* [Screenshots](#screenshots)
* [Supported DICOM Tags](#-supported-dicom-tags)
* [Requirements](#️-requirements)
* [Installation](#-installation)
* [Usage](#️-usage)
* [Reporting Bugs & Feature Requests](#-reporting-bugs--feature-requests)
* [License](#-license)
* [Acknowledgments](#-acknowledgments)

## 🚀 Features

* **Zero Dependencies:** Built on .NET 4.8 (included natively in Windows 10 and 11). No external runtime installation is required.
* **Flexible Deployment:** Available as a portable executable or MSI installer.
* **Customizable Results:** Select exactly which DICOM tags to display in the results table via the Settings menu. Your preferences are saved between sessions.
* **Connectivity Tools:** Integrated network **Ping** and DICOM Verification (**C-ECHO**) to troubleshoot connection issues before querying.
* **Secure:** Full support for **TLS** (Transport Layer Security) for testing secure DICOM connections.
* **Deep Logging:** Extensive logging settings to analyze communication traffic and debug DICOM associations.
* **Open Source:** Free to use, modify, and distribute under the MIT License.

## Screenshots
![Application Screenshot](/screenshots/main.jpg)
![Application Settings](/screenshots/settings.jpg)

## 📋 Supported DICOM Tags

The application handles the following DICOM tags. You can configure which of these tags are visible in the result grid via the **File -> Settings** menu.

| Tag Name | Tag ID | Description |
| :--- | :--- | :--- |
| `PatientName` | (0010,0010) | Patient's Name |
| `PatientID` | (0010,0020) | Patient ID |
| `PatientBirthDate` | (0010,0030) | Patient's Birth Date |
| `PatientSex` | (0010,0040) | Patient's Sex |
| `PatientAge` | (0010,1010) | Patient's Age |
| `PatientSize` | (0010,1020) | Patient's Size |
| `PatientWeight` | (0010,1030) | Patient's Weight |
| `MedicalAlerts` | (0010,2000) | Medical Alerts |
| `Allergies` | (0010,2110) | Contrast Allergies |
| `AccessionNumber` | (0008,0050) | Accession Number |
| `StudyInstanceUID` | (0020,000D) | Study Instance UID |
| `Modality` | (0008,0060) | Modality |
| `RequestingPhysician` | (0032,1032) | Requesting Physician |
| `RequestedProcedureDescription` | (0032,1060) | Requested Procedure Description |
| `RequestedProcedureCodeSequence` | (0032,1064) | Requested Procedure Code Sequence |
| `ExamDescription` | (0032,1030) | Study Description / Exam Description |
| `AdmissionID` | (0038,0010) | Admission ID |
| `CurrentPatientLocation` | (0038,0300) | Current Patient Location |
| `ScheduledStationAET` | (0040,0001) | Scheduled Station AE Title |
| `ScheduledDate` | (0040,0002) | Scheduled Procedure Step Start Date |
| `ScheduledTime` | (0040,0003) | Scheduled Procedure Step Start Time |
| `ScheduledPerformingPhysicianName`| (0040,0006) | Scheduled Performing Physician's Name |
| `ScheduledProcedureStepDescription` | (0040,0007) | Scheduled Procedure Step Description |
| `ScheduledProtocolCodeValue` | (0040,0008) | > (0008,0100) Code Value |
| `ScheduledProtocolCodeMeaning` | (0040,0008) | > (0008,0104) Code Meaning |
| `ScheduledProtocolCodeScheme` | (0040,0008) | > (0008,0102) Coding Scheme Designator |
| `ScheduledProcedureStepID` | (0040,0009) | Scheduled Procedure Step ID |
| `ScheduledStationName` | (0040,0010) | Scheduled Station Name |
| `ScheduledProcedureStepLocation` | (0040,0011) | Scheduled Procedure Step Location |
| `PreMedication` | (0040,0012) | Pre-Medication |
| `ScheduledProcedureStepStatus` | (0040,0020) | Scheduled Procedure Step Status |
| `RequestedProcedureID` | (0040,1001) | Requested Procedure ID |
| `ReasonForRequestedProcedure` | (0040,1002) | Reason for Requested Procedure |
| `RequestedProcedurePriority` | (0040,1003) | Requested Procedure Priority |
| `OrderEnteredBy` | (0040,2008) | Order Entered By |
| `OrderEntererLocation` | (0040,2009) | Order Enterer Location |
| `ImagingServiceRequestComments` | (0040,2400) | Imaging Service Request Comments |

## 🛠️ Requirements

* **OS:** Windows 10 or Windows 11.
* **Framework:** .NET Framework 4.8 (Pre-installed on supported OS).

## 📦 Installation

You can choose between the Windows Installer or the Portable version.

### Option 1: Portable 
1.  Download the **ZIP file** from the [latest release](../../releases/latest).
2.  Extract the files to a folder of your choice.
3.  Run `DMWLT4.exe`.

### Option 2: Installer 
1.  Download the **MSI Installer** from the [latest release](../../releases/latest).
2.  Double-click the file to install the application.
3.  Launch **DICOM Modality Worklist Tester** from your Start Menu.

### ⚠️ Microsoft SmartScreen Warning
Because this is an open-source tool and is not digitally signed with a paid certificate, you may see a **"Windows protected your PC"** warning (Microsoft Defender SmartScreen) when you try to run the application for the first time.

To proceed:
1.  Click **More info**.
2.  Click **Run anyway**.

This is a standard warning for unsigned software and does not indicate a security threat.

## ⚙️ Usage

1.  **Configure Connection:** Enter the IP, Port, and AE Title of the target Worklist SCP (RIS/PACS).
2.  **Set Local AE:** Configure your local Calling AE Title.
3.  **Test Connection:** Use the **Ping** or **DICOM Echo** buttons to verify network visibility.
4.  **Query:** Enter search criteria (e.g., Modality, Date, or Patient Name) and execute the query.
5.  **Review:** Check the results list. You can customize visible columns via **File -> Settings**.

## 🐛 Reporting Bugs & Feature Requests

If you encounter any bugs or have an idea for a new feature, please open an issue in the [Issues](../../issues) section of this repository.

* **Bugs:** Please provide steps to reproduce the issue and attach relevant logs if possible.
* **Features:** Describe the desired functionality and how it would improve the tool.

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

* Powered by [Fellow Oak DICOM (fo-dicom)](https://github.com/fo-dicom/fo-dicom).
* Original GUI idea by [Sonny Pate](https://github.com/sonnypate/MWL-Tester/tree/main/MWL-Tester) project which is built on .NET 8.

---

> **Disclaimer:** This software is intended for testing and simulation purposes only. It is not certified for clinical use or primary diagnosis.

// Test harness for DMWLT4: runs a mock Modality Worklist SCP on localhost and drives the app's
// real MainViewModel / SettingsDialog. Message boxes are captured and auto-dismissed.
using FellowOakDicom;
using FellowOakDicom.Network;
using Microsoft.Extensions.Logging;
using MWL4;
using MWL4.Infrastructure;
using MWL4.ViewModels;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

static class Scp
{
    public static readonly object Lock = new object();
    public static DicomStatus EchoStatus = DicomStatus.Success;
    public static DicomStatus FinalFindStatus = DicomStatus.Success;
    public static int ResponseDelayMs = 0;
    public static int EchoDelayMs = 0;
    public static readonly List<DicomDataset> FindRequests = new List<DicomDataset>();
    public static int EchoCount;
    public static string Today = DateTime.Today.ToString("yyyyMMdd");

    public static List<(DicomDataset Item, DicomStatus Status)> Items()
    {
        DicomDataset Make(string name, string id, string modality, string comments, bool latin1)
        {
            var ds = new DicomDataset();
            if (latin1) ds.Add(DicomTag.SpecificCharacterSet, "ISO_IR 100");
            ds.Add(DicomTag.PatientName, name);
            ds.Add(DicomTag.PatientID, id);
            ds.Add(DicomTag.AccessionNumber, "ACC" + id);
            var sps = new DicomDataset
            {
                { DicomTag.Modality, modality },
                { DicomTag.ScheduledProcedureStepStartDate, Today },
                { DicomTag.ScheduledProcedureStepStartTime, "100000" },
                { DicomTag.ScheduledProcedureStepID, "SPS" + id },
                { DicomTag.CommentsOnTheScheduledProcedureStep, comments },
                { DicomTag.ScheduledProcedureStepStatus, id == "3" ? "ARRIVED" : "SCHEDULED" },
            };
            ds.Add(new DicomSequence(DicomTag.ScheduledProcedureStepSequence, sps));
            return ds;
        }
        return new List<(DicomDataset, DicomStatus)>
        {
            (Make("Åström^Anna", "1", "CT", "Bring contrast", true), DicomStatus.Pending),
            // Sent with FF01 (Pending - optional keys not supported), which used to be dropped.
            (Make("Doe^John", "2", "MR", "", false), DicomStatus.QueryRetrieveOptionalKeysNotSupported),
            (Make("Smith^Bob", "3", "CT", "", false), DicomStatus.Pending),
        };
    }

    static string Str(DicomDataset ds, DicomTag tag) => ds.TryGetString(tag, out var s) ? s ?? "" : "";

    public static bool Matches(DicomDataset req, DicomDataset item)
    {
        var name = Str(req, DicomTag.PatientName);
        if (name != "")
        {
            var rx = "^" + Regex.Escape(name).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            if (!Regex.IsMatch(Str(item, DicomTag.PatientName), rx)) return false;
        }
        var rs = req.GetSequence(DicomTag.ScheduledProcedureStepSequence).Items[0];
        var isps = item.GetSequence(DicomTag.ScheduledProcedureStepSequence).Items[0];
        var mod = Str(rs, DicomTag.Modality);
        if (mod != "" && mod != Str(isps, DicomTag.Modality)) return false;
        var acc = Str(req, DicomTag.AccessionNumber);
        if (acc != "" && acc != Str(item, DicomTag.AccessionNumber)) return false;
        var spsStatus = Str(rs, DicomTag.ScheduledProcedureStepStatus);
        if (spsStatus != "" && spsStatus != Str(isps, DicomTag.ScheduledProcedureStepStatus)) return false;
        var range = Str(rs, DicomTag.ScheduledProcedureStepStartDate);
        if (range != "")
        {
            var d = Str(isps, DicomTag.ScheduledProcedureStepStartDate);
            var parts = range.Contains("-") ? range.Split('-') : new[] { range, range };
            if (parts[0] != "" && string.CompareOrdinal(d, parts[0]) < 0) return false;
            if (parts[1] != "" && string.CompareOrdinal(d, parts[1]) > 0) return false;
        }
        return true;
    }
}

public class MockScp : DicomService, IDicomServiceProvider, IDicomCFindProvider, IDicomCEchoProvider
{
    public MockScp(INetworkStream stream, Encoding fallbackEncoding, ILogger log, DicomServiceDependencies deps)
        : base(stream, fallbackEncoding, log, deps) { }

    public Task OnReceiveAssociationRequestAsync(DicomAssociation association)
    {
        foreach (var pc in association.PresentationContexts)
            pc.AcceptTransferSyntaxes(DicomTransferSyntax.ExplicitVRLittleEndian, DicomTransferSyntax.ImplicitVRLittleEndian);
        return SendAssociationAcceptAsync(association);
    }
    public Task OnReceiveAssociationReleaseRequestAsync() => SendAssociationReleaseResponseAsync();
    public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason) { }
    public void OnConnectionClosed(Exception exception) { }

    public async Task<DicomCEchoResponse> OnCEchoRequestAsync(DicomCEchoRequest request)
    {
        Interlocked.Increment(ref Scp.EchoCount);
        if (Scp.EchoDelayMs > 0) await Task.Delay(Scp.EchoDelayMs);
        return new DicomCEchoResponse(request, Scp.EchoStatus);
    }

    public async IAsyncEnumerable<DicomCFindResponse> OnCFindRequestAsync(DicomCFindRequest request)
    {
        lock (Scp.Lock) Scp.FindRequests.Add(request.Dataset);
        foreach (var (item, status) in Scp.Items())
        {
            if (!Scp.Matches(request.Dataset, item)) continue;
            if (Scp.ResponseDelayMs > 0) await Task.Delay(Scp.ResponseDelayMs);
            yield return new DicomCFindResponse(request, status) { Dataset = item };
        }
        yield return new DicomCFindResponse(request, Scp.FinalFindStatus);
    }
}

static class MessageBoxes
{
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint GetDlgItemText(IntPtr h, int id, StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);

    static readonly List<string> _seen = new List<string>();
    static readonly HashSet<IntPtr> _handled = new HashSet<IntPtr>();

    public static void Start()
    {
        var pid = (uint)Process.GetCurrentProcess().Id;
        var t = new Thread(() =>
        {
            while (true)
            {
                EnumWindows((h, l) =>
                {
                    GetWindowThreadProcessId(h, out var p);
                    if (p != pid || !IsWindowVisible(h)) return true;
                    var cls = new StringBuilder(64); GetClassName(h, cls, 64);
                    if (cls.ToString() != "#32770") return true;
                    lock (_seen)
                    {
                        if (_handled.Add(h))
                        {
                            var title = new StringBuilder(256); GetWindowText(h, title, 256);
                            var text = new StringBuilder(2048); GetDlgItemText(h, 0xFFFF, text, 2048);
                            _seen.Add($"[{title}] {text.ToString().Replace("\n", " | ")}");
                        }
                    }
                    // Click the dialog's (only) button. Its ID varies (WPF's OK-only box uses IDCANCEL).
                    var btn = FindWindowEx(h, IntPtr.Zero, "Button", null);
                    if (btn != IntPtr.Zero) PostMessage(btn, 0x00F5 /*BM_CLICK*/, IntPtr.Zero, IntPtr.Zero);
                    else PostMessage(h, 0x0010 /*WM_CLOSE*/, IntPtr.Zero, IntPtr.Zero);
                    return true;
                }, IntPtr.Zero);
                Thread.Sleep(100);
            }
        }) { IsBackground = true };
        t.Start();
    }

    public static List<string> Take()
    {
        lock (_seen) { var r = _seen.ToList(); _seen.Clear(); _handled.Clear(); return r; }
    }
}

static class Program
{
    const int Port = 41112;
    const int ClosedPort = 41113;
    const int TlsPort = 41114;
    static int _pass, _fail;

    static void Check(string name, bool ok, string detail = "")
    {
        if (ok) _pass++; else _fail++;
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {name}{(detail != "" ? "  -> " + detail : "")}");
    }

    static async Task WaitIdle(MainViewModel vm)
    {
        var sw = Stopwatch.StartNew();
        while (vm.IsBusy && sw.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(50);
        await Task.Delay(300); // let message boxes be captured
    }

    static async Task<List<string>> Run(MainViewModel vm, System.Windows.Input.ICommand cmd, object arg = null)
    {
        MessageBoxes.Take();
        cmd.Execute(arg);
        await WaitIdle(vm);
        return MessageBoxes.Take();
    }

    static DicomDataset LastFind() { lock (Scp.Lock) return Scp.FindRequests.LastOrDefault(); }
    static int FindCount() { lock (Scp.Lock) return Scp.FindRequests.Count; }
    static string SpsStr(DicomDataset ds, DicomTag tag) =>
        ds.GetSequence(DicomTag.ScheduledProcedureStepSequence).Items[0].TryGetString(tag, out var s) ? s ?? "" : "";
    static string Str(DicomDataset ds, DicomTag tag) => ds.TryGetString(tag, out var s) ? s ?? "" : "";

    static ApplicationSettingsBase _settings;

    static async Task RunTests()
    {
        var settingsType = typeof(MainViewModel).Assembly.GetType("DMWLT4.Properties.Settings");
        var settings = (ApplicationSettingsBase)settingsType.GetProperty("Default", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null);
        _settings = settings;
        // Run with known values instead of whatever the user has configured. Settings are only changed in memory.
        settings["CharacterSet"] = "Auto";
        settings["ConnectionTimeoutSeconds"] = 10;
        settings["AssociationTimeoutSeconds"] = 10;
        settings["ResponseTimeoutSeconds"] = 30;
        settings["VisibleColumns"] = SettingsDialog.DefaultVisibleColumns;
        settings["TlsIgnoreCertErrors"] = false;
        settings["TlsClientCertThumbprint"] = "";
        settings["ShowFilterBar"] = true;
        settings["ExtraQueryKeys"] = "";

        var vm = new MainViewModel
        {
            ServerHost = "127.0.0.1",
            ServerPort = Port,
            ServerAET = "MOCKSCP",
            CallingAET = "TESTSCU",
            UseTLS = false,
            TlsIgnoreCertErrors = false,
            TlsClientCertThumbprint = "",
            StartDate = null,
            EndDate = null,
        };

        Console.WriteLine("\n1. Busy guard (double Query)");
        Scp.ResponseDelayMs = 400;
        var before = FindCount();
        MessageBoxes.Take();
        vm.QueryCommand.Execute(null);
        var busyDisables = !vm.QueryCommand.CanExecute(null) && !vm.DicomEchoCommand.CanExecute(null)
                           && !vm.PingCommand.CanExecute(null) && !vm.ClearResultsCommand.CanExecute(null);
        vm.QueryCommand.Execute(null); // second click / Enter
        await WaitIdle(vm);
        Scp.ResponseDelayMs = 0;
        Check("Query/Echo/Ping/Clear disabled while busy", busyDisables);
        Check("Only one C-FIND sent", FindCount() - before == 1, $"{FindCount() - before} sent");
        Check("No duplicate rows (3 items)", vm.WorklistResults.Count == 3, $"{vm.WorklistResults.Count} rows");
        Check("Commands enabled again afterwards", vm.QueryCommand.CanExecute(null));

        Console.WriteLine("\n2. FF01 pending responses and mapping");
        Check("Item sent with FF01 is kept (Doe^John)", vm.WorklistResults.Any(r => r.PatientName == "Doe^John"));
        var a = vm.WorklistResults.FirstOrDefault(r => r.PatientID == "1");
        Check("SPS Comments column mapped", a?.CommentsOnScheduledProcedureStep == "Bring contrast", a?.CommentsOnScheduledProcedureStep);
        Check("Status bar shows count", vm.StatusText.StartsWith("Found 3 items."), vm.StatusText);

        Console.WriteLine("\n3. Non-ASCII patient name (Åström)");
        vm.PatientName = "Åström";
        await Run(vm, vm.QueryCommand);
        var req = LastFind();
        Check("Request has Specific Character Set ISO_IR 100", Str(req, DicomTag.SpecificCharacterSet) == "ISO_IR 100", Str(req, DicomTag.SpecificCharacterSet));
        Check("Server received name 'Åström*'", Str(req, DicomTag.PatientName) == "Åström*", Str(req, DicomTag.PatientName));
        Check("Query matches Åström^Anna", vm.WorklistResults.Count == 1 && vm.WorklistResults[0].PatientName == "Åström^Anna",
              string.Join(",", vm.WorklistResults.Select(r => r.PatientName)));
        vm.PatientName = "Smith";
        await Run(vm, vm.QueryCommand);
        Check("ASCII query sends no Specific Character Set", !LastFind().Contains(DicomTag.SpecificCharacterSet));

        Console.WriteLine("\n4. Patient name wildcard");
        vm.PatientName = "Doe*";
        await Run(vm, vm.QueryCommand);
        Check("'Doe*' is sent as 'Doe*' (not 'Doe**')", Str(LastFind(), DicomTag.PatientName) == "Doe*", Str(LastFind(), DicomTag.PatientName));
        vm.PatientName = "Do?^John";
        await Run(vm, vm.QueryCommand);
        Check("'Do?^John' is sent unchanged", Str(LastFind(), DicomTag.PatientName) == "Do?^John", Str(LastFind(), DicomTag.PatientName));
        vm.PatientName = "";

        Console.WriteLine("\n5. Lowercase modality");
        vm.Modality = "ct";
        await Run(vm, vm.QueryCommand);
        Check("'ct' sent as 'CT'", SpsStr(LastFind(), DicomTag.Modality) == "CT", SpsStr(LastFind(), DicomTag.Modality));
        Check("Returns the 2 CT items", vm.WorklistResults.Count == 2, $"{vm.WorklistResults.Count} rows");
        vm.Modality = "";

        Console.WriteLine("\n6. Date range");
        var today = DateTime.Today;
        vm.StartDate = today.AddDays(1); vm.EndDate = today;
        before = FindCount();
        var msgs = await Run(vm, vm.QueryCommand);
        Check("Start > end gives a warning", msgs.Any(m => m.Contains("Start date is after end date")), string.Join("; ", msgs));
        Check("Start > end sends no query", FindCount() == before);
        vm.StartDate = null; vm.EndDate = today;
        await Run(vm, vm.QueryCommand);
        Check("Cleared start date -> open range '-yyyyMMdd'", SpsStr(LastFind(), DicomTag.ScheduledProcedureStepStartDate) == "-" + Scp.Today,
              SpsStr(LastFind(), DicomTag.ScheduledProcedureStepStartDate));
        vm.StartDate = today; vm.EndDate = null;
        await Run(vm, vm.QueryCommand);
        Check("Cleared end date -> open range 'yyyyMMdd-'", SpsStr(LastFind(), DicomTag.ScheduledProcedureStepStartDate) == Scp.Today + "-",
              SpsStr(LastFind(), DicomTag.ScheduledProcedureStepStartDate));
        Check("Open range returns today's items", vm.WorklistResults.Count == 3, $"{vm.WorklistResults.Count} rows");
        vm.StartDate = null; vm.EndDate = null;
        await Run(vm, vm.QueryCommand);
        Check("Both cleared -> any date (empty)", SpsStr(LastFind(), DicomTag.ScheduledProcedureStepStartDate) == "",
              "'" + SpsStr(LastFind(), DicomTag.ScheduledProcedureStepStartDate) + "'");
        vm.StartDate = today.AddDays(1); vm.EndDate = today.AddDays(1);
        await Run(vm, vm.QueryCommand);
        Check("Tomorrow-only range returns nothing", vm.WorklistResults.Count == 0, $"{vm.WorklistResults.Count} rows");
        vm.StartDate = null; vm.EndDate = null;

        Console.WriteLine("\n7. C-ECHO");
        var echoBefore = Scp.EchoCount;
        msgs = await Run(vm, vm.DicomEchoCommand);
        Check("Echo to running SCP succeeds", Scp.EchoCount == echoBefore + 1 && vm.StatusText.Contains("Echo successful ("), vm.StatusText);
        Check("Echo shows round-trip time", msgs.Any(m => m.Contains("successful") && m.Contains("Time:") && m.Contains("ms")), string.Join("; ", msgs));
        Check("Success box is still titled 'Information'", msgs.Any(m => m.StartsWith("[Information] Connection to")), string.Join("; ", msgs));
        await Run(vm, vm.SelectionChangedCommand, 2);
        Check("Echo result stays after selecting rows", vm.StatusText.Contains("Echo successful") && vm.StatusText.Contains("2 selected"), vm.StatusText);
        Scp.EchoStatus = DicomStatus.ProcessingFailure;
        msgs = await Run(vm, vm.DicomEchoCommand);
        Scp.EchoStatus = DicomStatus.Success;
        Check("Echo with failure status reports failure", vm.StatusText.Contains("Echo failed") && !vm.StatusText.Contains("successful"), vm.StatusText);
        vm.ServerPort = ClosedPort;
        msgs = await Run(vm, vm.DicomEchoCommand);
        vm.ServerPort = Port;
        Check("Echo to stopped server reports failure", vm.StatusText.Contains("C-ECHO failed") && msgs.Any(m => m.Contains("C-ECHO failed")), vm.StatusText);
        Check("Stopped-server error shows the real cause, not the AggregateException text",
              !vm.StatusText.Contains("Ett eller flera fel") && !vm.StatusText.Contains("One or more errors"), vm.StatusText);
        Check("Error box is titled 'Error'", msgs.Any(m => m.StartsWith("[Error] C-ECHO failed")), string.Join("; ", msgs));

        Console.WriteLine("\n8. Status bar keeps query warning after selection");
        Scp.FinalFindStatus = DicomStatus.ProcessingFailure;
        await Run(vm, vm.QueryCommand);
        Scp.FinalFindStatus = DicomStatus.Success;
        var hadWarning = vm.StatusText.Contains("results may be incomplete");
        Check("Non-success final status gives a warning", hadWarning, vm.StatusText);
        await Run(vm, vm.SelectionChangedCommand, 1);
        Check("Warning still shown after selecting a row", vm.StatusText.Contains("results may be incomplete") && vm.StatusText.Contains("1 selected"), vm.StatusText);
        await Run(vm, vm.ClearResultsCommand);
        Check("'Results cleared.' stays visible", vm.StatusText.Contains("Results cleared."), vm.StatusText);

        Console.WriteLine("\n9. Ping");
        msgs = await Run(vm, vm.PingCommand);
        Check("Ping 127.0.0.1 succeeds and stays in status", vm.StatusText.Contains("Ping successful"), vm.StatusText);

        Console.WriteLine("\n10. Host/port validation");
        vm.ServerHost = "";
        before = FindCount();
        msgs = await Run(vm, vm.QueryCommand);
        Check("Empty host is rejected before connecting", FindCount() == before && msgs.Any(m => m.Contains("Called host is empty")), string.Join("; ", msgs));
        Check("Warning box is titled 'Warning'", msgs.Any(m => m.StartsWith("[Warning] Called host is empty")), string.Join("; ", msgs));
        vm.ServerHost = "127.0.0.1";
        vm.ServerPort = 70000;
        msgs = await Run(vm, vm.QueryCommand);
        Check("Port 70000 is rejected", FindCount() == before && msgs.Any(m => m.Contains("Port 70000 is invalid")), string.Join("; ", msgs));
        vm.ServerPort = Port;

        Console.WriteLine("\n11. Settings dialog Cancel discards logging changes");
        var dimseBefore = (bool)settings["Client_LogDimseDatasets"];
        var pduBefore = (bool)settings["Client_LogDataPDUs"];
        bool toggledInDialog = false;
        var dlg = new SettingsDialog();
        dlg.Loaded += (s, e) =>
        {
            var cb1 = (CheckBox)dlg.FindName("LogDimseDatasets");
            var cb2 = (CheckBox)dlg.FindName("LogDataPDUs");
            cb1.IsChecked = !dimseBefore;
            cb2.IsChecked = !pduBefore;
            // With the old TwoWay binding the setting would already have changed at this point.
            toggledInDialog = (bool)settings["Client_LogDimseDatasets"] == dimseBefore;
            var cancel = FindButton(dlg, "Cancel");
            dlg.Dispatcher.BeginInvoke(new Action(() => cancel.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent))));
        };
        var result = dlg.ShowDialog();
        Check("Toggling a checkbox does not change the setting immediately", toggledInDialog);
        Check("Dialog closed via Cancel", result == false);
        Check("Logging settings unchanged after Cancel",
              (bool)settings["Client_LogDimseDatasets"] == dimseBefore && (bool)settings["Client_LogDataPDUs"] == pduBefore);
        // Reopening shows the stored values again
        var dlg2 = new SettingsDialog();
        Check("Reopened dialog shows original values",
              ((CheckBox)dlg2.FindName("LogDimseDatasets")).IsChecked == dimseBefore && ((CheckBox)dlg2.FindName("LogDataPDUs")).IsChecked == pduBefore);
        dlg2.Close();

        await RunFeatureTests(vm, settings);
    }

    static int ViewCount(MainViewModel vm) => vm.ResultsView.Cast<object>().Count();

    static async Task RunFeatureTests(MainViewModel vm, ApplicationSettingsBase settings)
    {
        Console.WriteLine("\n12. Cancel");
        Scp.ResponseDelayMs = 1000;
        MessageBoxes.Take();
        vm.QueryCommand.Execute(null);
        await Task.Delay(300);
        var cancelEnabledWhileBusy = vm.CancelCommand.CanExecute(null);
        var sw = Stopwatch.StartNew();
        vm.CancelCommand.Execute(null);
        while (vm.IsBusy && sw.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(20);
        var cancelMs = sw.ElapsedMilliseconds;
        await Task.Delay(300);
        var msgs = MessageBoxes.Take();
        Scp.ResponseDelayMs = 0;
        Check("Cancel is enabled while a query runs", cancelEnabledWhileBusy);
        Check("Query stops soon after Cancel", cancelMs < 1500, $"{cancelMs} ms");
        Check("Status says the query was cancelled", vm.StatusText.Contains("Query cancelled"), vm.StatusText);
        Check("No error box after Cancel", msgs.Count == 0, string.Join("; ", msgs));
        Check("Statistics show 'cancelled'", vm.QueryStats.Contains("cancelled"), vm.QueryStats);
        Check("Cancel is disabled when idle", !vm.CancelCommand.CanExecute(null));
        Check("Query enabled again after Cancel", vm.QueryCommand.CanExecute(null));

        Console.WriteLine("\n13. Response timeout");
        settings["ResponseTimeoutSeconds"] = 1;
        Scp.ResponseDelayMs = 3000;
        sw.Restart();
        await Run(vm, vm.QueryCommand);
        var timeoutMs = sw.ElapsedMilliseconds;
        Scp.ResponseDelayMs = 0;
        Check("Timed-out query warns 'timed out'", vm.StatusText.Contains("no response within 1 s (timed out)"), vm.StatusText);
        Check("Timed-out query returns before the server answers (< 3 s)", timeoutMs < 3000, $"{timeoutMs} ms");
        Check("Statistics show 'timed out'", vm.QueryStats.Contains("timed out"), vm.QueryStats);
        Scp.ResponseDelayMs = 700; // responses keep coming within the timeout -> no timeout
        await Run(vm, vm.QueryCommand);
        Scp.ResponseDelayMs = 0;
        Check("Timer restarts on each response (3 × 0.7 s with 1 s timeout)", vm.WorklistResults.Count == 3 && !vm.StatusText.Contains("timed out"), vm.StatusText);
        Scp.EchoDelayMs = 3000;
        sw.Restart();
        msgs = await Run(vm, vm.DicomEchoCommand);
        var echoTimeoutMs = sw.ElapsedMilliseconds;
        Scp.EchoDelayMs = 0;
        Check("Timed-out echo reports failure", vm.StatusText.Contains("Echo failed: no response within 1 s (timed out)")
              && msgs.Any(m => m.StartsWith("[Warning]") && m.Contains("timed out")), vm.StatusText + " " + string.Join("; ", msgs));
        Check("Timed-out echo returns before the server answers (< 3 s)", echoTimeoutMs < 3000, $"{echoTimeoutMs} ms");
        settings["ResponseTimeoutSeconds"] = 30;

        Console.WriteLine("\n14. Query statistics");
        await Run(vm, vm.QueryCommand);
        Check("Statistics show 3 responses", vm.QueryStats.Contains("3 responses"), vm.QueryStats);
        Check("Statistics show 1 FF01 response", vm.QueryStats.Contains("(1 × FF01)"), vm.QueryStats);
        Check("Statistics show final status code [0000]", vm.QueryStats.Contains("[0000]"), vm.QueryStats);
        Check("Statistics show elapsed time", Regex.IsMatch(vm.QueryStats, @"Last query: \d+ ms"), vm.QueryStats);

        Console.WriteLine("\n15. Character set option");
        var astrom = vm.WorklistResults.FirstOrDefault(r => r.PatientID == "1");
        Check("Response Specific Character Set is shown (ISO_IR 100)", astrom?.SpecificCharacterSet == "ISO_IR 100", astrom?.SpecificCharacterSet);
        var smith = vm.WorklistResults.FirstOrDefault(r => r.PatientID == "3");
        Check("Response without (0008,0005) gives empty Character Set", smith != null && smith.SpecificCharacterSet == "", smith?.SpecificCharacterSet);
        settings["CharacterSet"] = "ISO_IR 192";
        vm.PatientName = "Smith";
        await Run(vm, vm.QueryCommand);
        Check("Fixed ISO_IR 192 is sent for an ASCII query", Str(LastFind(), DicomTag.SpecificCharacterSet) == "ISO_IR 192", Str(LastFind(), DicomTag.SpecificCharacterSet));
        Check("Query with ISO_IR 192 still matches", vm.WorklistResults.Count == 1, $"{vm.WorklistResults.Count} rows");
        settings["CharacterSet"] = "ISO_IR 100";
        vm.PatientName = "Åström";
        await Run(vm, vm.QueryCommand);
        Check("Fixed ISO_IR 100 is sent", Str(LastFind(), DicomTag.SpecificCharacterSet) == "ISO_IR 100");
        Check("Åström matches with ISO_IR 100", vm.WorklistResults.Count == 1, $"{vm.WorklistResults.Count} rows");
        settings["CharacterSet"] = "Auto";
        vm.PatientName = "Smith";
        await Run(vm, vm.QueryCommand);
        Check("Auto sends no character set for ASCII", !LastFind().Contains(DicomTag.SpecificCharacterSet));
        vm.PatientName = "";

        Console.WriteLine("\n16. Row details (dataset tree)");
        await Run(vm, vm.QueryCommand);
        var item = vm.WorklistResults.FirstOrDefault(r => r.PatientID == "1");
        Check("Worklist item keeps the returned dataset", item?.Dataset != null);
        var nodes = DatasetNode.Build(item?.Dataset);
        var spsNode = nodes.FirstOrDefault(n => n.Text.StartsWith("(0040,0100) SQ"));
        Check("Tree has the SPS sequence node", spsNode != null, string.Join(" | ", nodes.Select(n => n.Text)));
        Check("Sequence node shows item count", spsNode?.Text.Contains("[1 item(s)]") == true, spsNode?.Text);
        var item1 = spsNode?.Children.FirstOrDefault();
        Check("Sequence has 'Item 1' child", item1?.Text == "Item 1", item1?.Text);
        Check("Item contains Modality = CT", item1?.Children.Any(c => c.Text == "(0008,0060) CS  Modality = CT") == true,
              string.Join(" | ", item1?.Children.Select(c => c.Text) ?? Enumerable.Empty<string>()));
        Check("Patient name element is decoded (Åström^Anna)", nodes.Any(n => n.Text == "(0010,0010) PN  Patient's Name = Åström^Anna"),
              nodes.FirstOrDefault(n => n.Text.StartsWith("(0010,0010)"))?.Text);
        var text = DatasetNode.ToText(nodes);
        Check("Text dump indents sequence items", text.Contains("\n  Item 1") && text.Contains("\n    (0008,0060)"));
        var priv = new DicomDataset();
        priv.Add(new DicomLongString(new DicomTag(0x0011, 0x0010), "ACME"));
        priv.Add(new DicomUnknown(new DicomTag(0x0011, 0x1001), new byte[] { 1, 2, 3, 4 }));
        priv.Add(new DicomOtherByte(DicomTag.PixelData, new byte[] { 1, 2 }));
        var privNodes = DatasetNode.Build(priv);
        Check("Private/unknown and binary tags are listed", privNodes.Count == 3 && privNodes.Any(n => n.Text.Contains("(0011,1001) UN") && n.Text.Contains("<4 bytes>"))
              && privNodes.Any(n => n.Text.Contains("(7FE0,0010) OB") && n.Text.Contains("<2 bytes>")),
              string.Join(" | ", privNodes.Select(n => n.Text)));
        Check("Details window opens for a row", TryOpenDetails(item));

        Console.WriteLine("\n17. Grid filter");
        vm.RefreshFilter();
        Check("No filter shows all 3 rows", ViewCount(vm) == 3, $"{ViewCount(vm)} rows");
        vm.FilterText = "smith";
        Check("Filter 'smith' shows 1 row (case insensitive)", ViewCount(vm) == 1, $"{ViewCount(vm)} rows");
        Check("Status shows '1 shown.'", vm.StatusText.Contains("Found 3 items. 1 shown."), vm.StatusText);
        vm.FilterText = "contrast"; // only in SPS Comments, which is hidden by default
        Check("Filter ignores hidden columns", ViewCount(vm) == 0, $"{ViewCount(vm)} rows");
        settings["VisibleColumns"] = SettingsDialog.DefaultVisibleColumns + ";CommentsOnScheduledProcedureStep";
        vm.RefreshFilter();
        Check("Filter matches after the column is made visible", ViewCount(vm) == 1, $"{ViewCount(vm)} rows");
        settings["VisibleColumns"] = SettingsDialog.DefaultVisibleColumns;
        vm.ClearFilterCommand.Execute(null);
        Check("Clearing the filter shows all rows", ViewCount(vm) == 3 && vm.FilterText == "" && !vm.StatusText.Contains("shown"), vm.StatusText);
        vm.FilterText = "MR";
        await Run(vm, vm.QueryCommand);
        Check("Filter applies to rows of a new query", ViewCount(vm) == 1 && vm.WorklistResults.Count == 3, $"{ViewCount(vm)} of {vm.WorklistResults.Count}");
        vm.FilterText = "";

        Console.WriteLine("\n18. Connection profiles");
        var profilesBefore = (string)settings["ConnectionProfiles"];
        vm.Confirm = _ => true;
        const string name = "__MwlTest profile";
        vm.ProfileName = name;
        vm.SaveProfileCommand.Execute(null);
        var countAfterSave = vm.Profiles.Count;
        var saved = vm.Profiles.FirstOrDefault(p => p.Name == name);
        Check("Profile saved", saved != null && saved.ServerHost == "127.0.0.1" && saved.ServerPort == Port && saved.ServerAET == "MOCKSCP");
        Check("Profile is stored in the setting", ((string)settings["ConnectionProfiles"]).Contains(name));
        var roundTrip = MWL4.Models.ConnectionProfile.Deserialize((string)settings["ConnectionProfiles"]).FirstOrDefault(p => p.Name == name);
        Check("Profile setting round-trips", roundTrip != null && roundTrip.ServerHost == "127.0.0.1" && roundTrip.ServerPort == Port && roundTrip.CallingAET == "TESTSCU");
        vm.ProfileName = name.ToUpperInvariant();
        vm.SaveProfileCommand.Execute(null);
        Check("Saving the same name again overwrites (no duplicate)", vm.Profiles.Count == countAfterSave, $"{vm.Profiles.Count} profiles");
        vm.ServerHost = "other.example";
        vm.ServerPort = 1234;
        vm.SelectedProfile = null;
        vm.SelectedProfile = vm.Profiles.First(p => p.Name == name);
        Check("Selecting the profile restores host and port", vm.ServerHost == "127.0.0.1" && vm.ServerPort == Port, $"{vm.ServerHost}:{vm.ServerPort}");
        Check("Selecting a profile updates the stored setting too", (string)settings["ServerHost"] == "127.0.0.1");
        vm.ProfileName = name;
        Check("Delete is enabled for an existing name", vm.DeleteProfileCommand.CanExecute(null));
        vm.DeleteProfileCommand.Execute(null);
        Check("Profile deleted", vm.Profiles.All(p => p.Name != name) && !((string)settings["ConnectionProfiles"]).Contains(name));
        Check("Delete is disabled for an unknown name", !vm.DeleteProfileCommand.CanExecute(null));
        Check("Broken profile XML is ignored", SafeDeserialize("<not xml"));
        settings["ConnectionProfiles"] = profilesBefore;

        Console.WriteLine("\n19. TLS");
        await RunTlsTests(vm);

        Console.WriteLine("\n20. Settings dialog: Reset to default and timeouts");
        settings["VisibleColumns"] = "PatientName";
        var visibleBefore = (string)settings["VisibleColumns"];
        HashSet<string> checkedAfterReset = null;
        bool resetFlag = false;
        var dlg = new SettingsDialog();
        dlg.Loaded += (s, e) =>
        {
            var grid = (DataGrid)dlg.FindName("ColumnsGrid");
            var cols = (List<SettingsDialog.ColumnSetting>)grid.ItemsSource;
            foreach (var c in cols) c.IsVisible = false;
            FindButton(dlg, "Reset to default").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            checkedAfterReset = new HashSet<string>(cols.Where(c => c.IsVisible).Select(c => c.BoundProperty));
            resetFlag = dlg.ColumnLayoutReset;
            var cancel = FindButton(dlg, "Cancel");
            dlg.Dispatcher.BeginInvoke(new Action(() => cancel.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent))));
        };
        dlg.ShowDialog();
        var defaults = new HashSet<string>(SettingsDialog.DefaultVisibleColumns.Split(';'));
        Check("Reset checks exactly the default columns", checkedAfterReset != null && checkedAfterReset.SetEquals(defaults),
              checkedAfterReset == null ? "null" : string.Join(";", checkedAfterReset));
        Check("Reset sets ColumnLayoutReset", resetFlag);
        Check("Cancel after Reset keeps the stored columns", (string)settings["VisibleColumns"] == visibleBefore, (string)settings["VisibleColumns"]);
        settings["VisibleColumns"] = SettingsDialog.DefaultVisibleColumns;

        var dlg3 = new SettingsDialog();
        Check("Dialog shows the timeout settings", ((TextBox)dlg3.FindName("ResponseTimeoutBox")).Text == "30"
              && ((TextBox)dlg3.FindName("ConnectionTimeoutBox")).Text == "10");
        Check("Dialog shows the character set setting", (string)((ComboBox)dlg3.FindName("CharacterSetBox")).SelectedValue == "Auto");
        ((TextBox)dlg3.FindName("ResponseTimeoutBox")).Text = "abc";
        MessageBoxes.Take();
        var okClicked = false;
        dlg3.Loaded += (s, e) =>
        {
            FindButton(dlg3, "OK").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); // invalid -> warning, stays open
            okClicked = true;
            var cancel = FindButton(dlg3, "Cancel");
            dlg3.Dispatcher.BeginInvoke(new Action(() => cancel.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent))));
        };
        var res3 = dlg3.ShowDialog();
        await Task.Delay(300);
        msgs = MessageBoxes.Take();
        Check("Invalid timeout is rejected on OK", okClicked && res3 == false && msgs.Any(m => m.Contains("Response timeout must be")), string.Join("; ", msgs));
        Check("Invalid timeout is not stored", (int)settings["ResponseTimeoutSeconds"] == 30);

        Console.WriteLine("\n21. Column layout");
        var dg = new DataGrid();
        foreach (var p in new[] { "A", "B", "C", "D" })
            dg.Columns.Add(new DataGridTextColumn { Binding = new System.Windows.Data.Binding(p), Width = DataGridLength.Auto });
        var original = dg.Columns.ToList();
        var host = new Window { Content = dg, Width = 400, Height = 200, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -2000 };
        host.Show();
        dg.Columns[2].DisplayIndex = 0;          // C first
        dg.Columns[1].Width = new DataGridLength(123); // B resized
        var layout = ColumnLayout.Save(dg.Columns);
        Check("Layout is saved with order and width", layout == "A=1,;B=2,123;C=0,;D=3,", layout);
        ColumnLayout.Reset(original);
        Check("Reset restores original order and Auto width",
              dg.Columns.Select(c => c.DisplayIndex).SequenceEqual(new[] { 0, 1, 2, 3 }) && dg.Columns.All(c => c.Width.IsAuto));
        ColumnLayout.Apply(dg.Columns, original, layout);
        Check("Apply restores order", dg.Columns[2].DisplayIndex == 0 && dg.Columns[0].DisplayIndex == 1 && dg.Columns[3].DisplayIndex == 3,
              string.Join(",", dg.Columns.Select(c => c.DisplayIndex)));
        Check("Apply restores width", dg.Columns[1].Width.IsAbsolute && dg.Columns[1].Width.Value == 123);
        ColumnLayout.Reset(original);
        ColumnLayout.Apply(dg.Columns, original, "D=0,;B=1,;Removed=2,50"); // A, C missing (e.g. new columns)
        Check("Columns missing from the layout go last in XAML order",
              string.Join("", dg.Columns.OrderBy(c => c.DisplayIndex).Select(c => ColumnLayout.GetBoundProperty(c))) == "DBAC",
              string.Join("", dg.Columns.OrderBy(c => c.DisplayIndex).Select(c => ColumnLayout.GetBoundProperty(c))));
        ColumnLayout.Apply(dg.Columns, original, "garbage;;x=y,z");
        Check("Garbage layout is ignored", true);
        host.Close();

        Console.WriteLine("\n22. View Filter Results (filter bar toggle)");
        await Run(vm, vm.QueryCommand);
        Check("Filter bar is hidden by default (setting default False)",
              (string)settings.Properties["ShowFilterBar"].DefaultValue == "False");
        settings["ShowFilterBar"] = false;
        Check("A new window starts with the filter bar hidden when the setting is off", !new MainViewModel().IsFilterBarVisible);
        vm.IsFilterBarVisible = false;
        vm.IsFilterBarVisible = true;
        Check("View Filter Results shows the filter bar and stores it", vm.IsFilterBarVisible && (bool)settings["ShowFilterBar"]);
        vm.FilterText = "smith";
        vm.IsFilterBarVisible = false;
        Check("Hiding the filter bar clears the filter", vm.FilterText == "" && ViewCount(vm) == 3, $"'{vm.FilterText}', {ViewCount(vm)} rows");
        Check("Hidden state is stored in the setting", (bool)settings["ShowFilterBar"] == false);
        vm.ToggleFilterBarCommand.Execute(null);
        Check("Toggle command shows the filter bar again", vm.IsFilterBarVisible && (bool)settings["ShowFilterBar"]);
        vm.ToggleFilterBarCommand.Execute(null);
        Check("Toggle command hides it", !vm.IsFilterBarVisible);
        vm.IsFilterBarVisible = true;

        Console.WriteLine("\n23. Help");
        vm.HelpCommand.Execute(null);
        vm.HelpCommand.Execute(null);
        await Task.Delay(300);
        var helpWindows = Application.Current.Windows.OfType<HelpWindow>().ToList();
        Check("Help opens one window (second F1 activates it)", helpWindows.Count == 1, $"{helpWindows.Count} windows");
        var help = helpWindows.FirstOrDefault();
        if (help != null)
        {
            help.Left = -3000;
            var titles = help.Topics.Select(t => t.Title).ToList();
            Check("Help has all topics", titles.SequenceEqual(new[] { "Getting started", "Connection", "Connection profiles",
                  "TLS (secure connections)", "Querying the worklist", "Working with results", "Settings", "Troubleshooting", "Keyboard shortcuts" }),
                  string.Join(", ", titles));
            var sv = FindVisualChild<System.Windows.Controls.ScrollViewer>((DependencyObject)help.FindName("Viewer"));
            var offsets = new List<double>();
            foreach (var t in titles.Take(5))
            {
                help.ShowTopic(t);
                help.UpdateLayout();
                offsets.Add(sv?.VerticalOffset ?? -1);
            }
            Check("Selecting topics scrolls down through the guide",
                  offsets[0] == 0 && offsets.Zip(offsets.Skip(1), (x, y) => y > x).All(b => b),
                  string.Join(", ", offsets.Select(o => o.ToString("0"))));
            help.ShowTopic("Getting started");
            help.UpdateLayout();
            Check("Going back to the first topic scrolls to the top", sv?.VerticalOffset == 0, sv?.VerticalOffset.ToString());
            help.Close();
        }

        await RunQueryKeyTests(vm, settings);
    }

    static QueryKeyField Field(MainViewModel vm, string id) => vm.ExtraQueryFields.FirstOrDefault(f => f.Definition.Id == id);

    static async Task RunQueryKeyTests(MainViewModel vm, ApplicationSettingsBase settings)
    {
        Console.WriteLine("\n24. Extra query keys");
        Check("No extra query fields by default", vm.ExtraQueryFields.Count == 0 && (string)settings.Properties["ExtraQueryKeys"].DefaultValue == "");
        Check("16 extra query keys are available", MWL4.Models.QueryKeyDefinition.All.Count == 16);
        settings["ExtraQueryKeys"] = "ScheduledProcedureStepStatus;AccessionNumber;ReferringPhysicianName;Unknown";
        vm.RefreshQueryKeys();
        Check("Fields are created from the setting in definition order (unknown ids ignored)",
              string.Join(",", vm.ExtraQueryFields.Select(f => f.Definition.Id)) == "AccessionNumber,ReferringPhysicianName,ScheduledProcedureStepStatus",
              string.Join(",", vm.ExtraQueryFields.Select(f => f.Definition.Id)));

        vm.StartDate = null; vm.EndDate = null;
        await Run(vm, vm.QueryCommand);
        var req = LastFind();
        Check("Empty extra keys are sent as empty return keys", Str(req, DicomTag.AccessionNumber) == "" && req.Contains(DicomTag.ReferringPhysicianName));
        Check("No extra key -> all 3 rows", vm.WorklistResults.Count == 3, $"{vm.WorklistResults.Count} rows");

        Field(vm, "AccessionNumber").Value = " ACC2 ";
        await Run(vm, vm.QueryCommand);
        Check("Accession Number is sent trimmed", Str(LastFind(), DicomTag.AccessionNumber) == "ACC2", Str(LastFind(), DicomTag.AccessionNumber));
        Check("Accession Number matches only Doe^John", vm.WorklistResults.Count == 1 && vm.WorklistResults[0].PatientName == "Doe^John",
              string.Join(",", vm.WorklistResults.Select(r => r.PatientName)));
        Field(vm, "AccessionNumber").Value = "";

        Field(vm, "ScheduledProcedureStepStatus").Value = "arrived";
        await Run(vm, vm.QueryCommand);
        Check("SPS Status is sent upper case inside the SPS item", SpsStr(LastFind(), DicomTag.ScheduledProcedureStepStatus) == "ARRIVED",
              SpsStr(LastFind(), DicomTag.ScheduledProcedureStepStatus));
        Check("SPS Status is not sent at top level", !LastFind().Contains(DicomTag.ScheduledProcedureStepStatus));
        Check("SPS Status ARRIVED matches only Smith^Bob", vm.WorklistResults.Count == 1 && vm.WorklistResults[0].PatientName == "Smith^Bob",
              string.Join(",", vm.WorklistResults.Select(r => r.PatientName)));
        Field(vm, "ScheduledProcedureStepStatus").Value = "";

        Field(vm, "ReferringPhysicianName").Value = "Åström^Erik";
        await Run(vm, vm.QueryCommand);
        Check("Non-ASCII extra key sets Specific Character Set ISO_IR 100", Str(LastFind(), DicomTag.SpecificCharacterSet) == "ISO_IR 100",
              Str(LastFind(), DicomTag.SpecificCharacterSet));
        Check("Referring Physician sent unchanged (no wildcard added)", Str(LastFind(), DicomTag.ReferringPhysicianName) == "Åström^Erik",
              Str(LastFind(), DicomTag.ReferringPhysicianName));

        settings["ExtraQueryKeys"] = "ReferringPhysicianName;PatientBirthDate;ScheduledProcedureStepStartTime;StudyInstanceUID";
        vm.RefreshQueryKeys();
        Check("Typed values are kept when the key list changes", Field(vm, "ReferringPhysicianName")?.Value == "Åström^Erik");
        Check("Removed keys disappear", Field(vm, "AccessionNumber") == null && vm.ExtraQueryFields.Count == 4);
        Field(vm, "ReferringPhysicianName").Value = "";

        var before = FindCount();
        Field(vm, "PatientBirthDate").Value = "2026";
        var msgs = await Run(vm, vm.QueryCommand);
        Check("Invalid birth date gives a warning and sends no query",
              FindCount() == before && msgs.Any(m => m.StartsWith("[Warning] Birth Date: '2026' is not valid")), string.Join("; ", msgs));
        Field(vm, "PatientBirthDate").Value = "19800101-19901231";
        await Run(vm, vm.QueryCommand);
        Check("Birth date range is sent", Str(LastFind(), DicomTag.PatientBirthDate) == "19800101-19901231", Str(LastFind(), DicomTag.PatientBirthDate));
        Field(vm, "PatientBirthDate").Value = "";

        Field(vm, "ScheduledProcedureStepStartTime").Value = "0800-1200";
        await Run(vm, vm.QueryCommand);
        Check("Time range is sent in the SPS item", SpsStr(LastFind(), DicomTag.ScheduledProcedureStepStartTime) == "0800-1200");
        before = FindCount();
        Field(vm, "ScheduledProcedureStepStartTime").Value = "8 am";
        msgs = await Run(vm, vm.QueryCommand);
        Check("Invalid time is rejected", FindCount() == before && msgs.Any(m => m.Contains("SPS Start Time")), string.Join("; ", msgs));
        Field(vm, "ScheduledProcedureStepStartTime").Value = "";

        before = FindCount();
        Field(vm, "StudyInstanceUID").Value = "1.2.3*";
        msgs = await Run(vm, vm.QueryCommand);
        Check("Wildcard in a UID key is rejected", FindCount() == before && msgs.Any(m => m.Contains("Study Instance UID")), string.Join("; ", msgs));
        Field(vm, "StudyInstanceUID").Value = "1.2.840.10008.1";
        await Run(vm, vm.QueryCommand);
        Check("Valid UID is sent", Str(LastFind(), DicomTag.StudyInstanceUID) == "1.2.840.10008.1");
        Field(vm, "StudyInstanceUID").Value = "";

        Console.WriteLine("\n25. Settings dialog: query keys tab");
        settings["ExtraQueryKeys"] = "AccessionNumber;PatientSex";
        var keysBefore = (string)settings["ExtraQueryKeys"];
        List<SettingsDialog.QueryKeySetting> listed = null;
        List<string> checkedAfterClear = null;
        var dlg = new SettingsDialog();
        dlg.Loaded += (s, e) =>
        {
            var grid = (DataGrid)dlg.FindName("QueryKeysGrid");
            listed = ((List<SettingsDialog.QueryKeySetting>)grid.ItemsSource).ToList();
            FindButton(dlg, "Clear all").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            checkedAfterClear = listed.Where(k => k.IsSelected).Select(k => k.Id).ToList();
            listed.First(k => k.Id == "AdmissionID").IsSelected = true;
            var cancel = FindButton(dlg, "Cancel");
            dlg.Dispatcher.BeginInvoke(new Action(() => cancel.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent))));
        };
        dlg.ShowDialog();
        Check("Query tab lists all 16 keys", listed?.Count == 16, listed?.Count.ToString());
        Check("Clear all unchecks every key", checkedAfterClear != null && checkedAfterClear.Count == 0);
        Check("Cancel keeps the stored query keys", (string)settings["ExtraQueryKeys"] == keysBefore, (string)settings["ExtraQueryKeys"]);
        var dlg4 = new SettingsDialog();
        var listed4 = (List<SettingsDialog.QueryKeySetting>)((DataGrid)dlg4.FindName("QueryKeysGrid")).ItemsSource;
        Check("Dialog checks the stored keys", string.Join(",", listed4.Where(k => k.IsSelected).Select(k => k.Id)) == "AccessionNumber,PatientSex",
              string.Join(",", listed4.Where(k => k.IsSelected).Select(k => k.Id)));
        dlg4.Close();

        settings["ExtraQueryKeys"] = "";
        vm.RefreshQueryKeys();
        Check("Empty setting removes all extra fields", vm.ExtraQueryFields.Count == 0);
    }

    static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    static bool SafeDeserialize(string xml)
    {
        try { MWL4.Models.ConnectionProfile.Deserialize(xml); return false; }
        catch (InvalidOperationException) { }
        // The view model must survive it: construct one with the broken setting.
        var before = (string)_settings["ConnectionProfiles"];
        _settings["ConnectionProfiles"] = xml;
        try { return new MainViewModel().Profiles.Count == 0; }
        finally { _settings["ConnectionProfiles"] = before; }
    }

    static bool TryOpenDetails(MWL4.Models.WorklistItem item)
    {
        var w = new DatasetDetailsWindow(item) { ShowInTaskbar = false, Left = -2000 };
        w.Show();
        var tree = (TreeView)w.FindName("DatasetTree");
        var ok = tree.Items.Count > 0 && w.Title.Contains("Åström^Anna");
        w.Close();
        return ok;
    }

    static X509Certificate2 CreateSelfSignedCert()
    {
        using (var rsa = RSA.Create(2048))
        {
            var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using (var cert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1)))
            {
                // SChannel cannot use the ephemeral key of a freshly created certificate; re-import it from a PFX.
                return new X509Certificate2(cert.Export(X509ContentType.Pfx, "x"), "x", X509KeyStorageFlags.Exportable);
            }
        }
    }

    static async Task RunTlsTests(MainViewModel vm)
    {
        X509Certificate2 cert;
        try { cert = CreateSelfSignedCert(); }
        catch (Exception ex) { Console.WriteLine("  SKIP  could not create a self-signed certificate: " + ex.Message); return; }

        using (DicomServerFactory.Create<MockScp>(TlsPort, new FellowOakDicom.Network.Tls.DefaultTlsAcceptor(cert)))
        {
            vm.ServerPort = TlsPort;
            vm.UseTLS = true;
            try
            {
                vm.TlsIgnoreCertErrors = false;
                var echoBefore = Scp.EchoCount;
                var msgs = await Run(vm, vm.DicomEchoCommand);
                Check("TLS echo to self-signed server fails when certificate errors are not accepted",
                      Scp.EchoCount == echoBefore && vm.StatusText.Contains("C-ECHO failed"), vm.StatusText);

                vm.TlsIgnoreCertErrors = true;
                msgs = await Run(vm, vm.DicomEchoCommand);
                Check("TLS echo succeeds when certificate errors are accepted",
                      Scp.EchoCount == echoBefore + 1 && vm.StatusText.Contains("Echo successful"), vm.StatusText + " " + string.Join("; ", msgs));

                await Run(vm, vm.QueryCommand);
                Check("TLS query returns the worklist", vm.WorklistResults.Count == 3, $"{vm.WorklistResults.Count} rows, {vm.StatusText}");

                vm.TlsClientCertThumbprint = "0011223344556677889900112233445566778899";
                msgs = await Run(vm, vm.DicomEchoCommand);
                Check("Missing client certificate gives a clear error",
                      msgs.Any(m => m.Contains("client certificate") && m.Contains("was not found")), string.Join("; ", msgs));
            }
            finally
            {
                vm.TlsClientCertThumbprint = "";
                vm.TlsIgnoreCertErrors = false;
                vm.UseTLS = false;
                vm.ServerPort = Port;
            }
        }
    }

    // ---------------------------------------------------------------- version check

    sealed class StubHandler : HttpMessageHandler
    {
        readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _answer;
        public HttpRequestMessage LastRequest;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) => _answer = answer;

        public static StubHandler Answering(HttpStatusCode status, string body) =>
            new StubHandler((r, t) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body ?? string.Empty) }));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            LastRequest = request;
            return _answer(request, token);
        }
    }

    static string ReleaseJson(string tag, string url = "https://github.com/droidgren/DMWLT4/releases/tag/x", string jsonBody = null) =>
        "{\"id\":1,\"tag_name\":\"" + tag + "\",\"html_url\":\"" + url + "\",\"draft\":false,\"assets\":[{\"name\":\"a\"}]"
        + (jsonBody == null ? string.Empty : ",\"body\":\"" + jsonBody + "\"") + "}";

    static async Task TestUpdateChecker()
    {
        Console.WriteLine("\nVersion check");

        Task<UpdateCheckResult> Check110(HttpMessageHandler handler, TimeSpan? timeout = null, CancellationToken token = default(CancellationToken)) =>
            new UpdateChecker("1.1.0", handler: handler, timeout: timeout).CheckAsync(token);
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> hang = async (r, t) => { await Task.Delay(Timeout.Infinite, t); return null; };

        foreach (var newer in new[] { "1.2.0", "v1.2.0", "V1.2.0", "1.4", "2", "1.1.1", "1.2.0-rc1" })
        {
            var result = await Check110(StubHandler.Answering(HttpStatusCode.OK, ReleaseJson(newer)));
            Check($"Tag {newer} is newer than 1.1.0", result.Outcome == UpdateCheckOutcome.NewVersion && !result.LatestVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase), result.Outcome.ToString());
        }
        foreach (var same in new[] { "1.1.0", "1.1", "v1.1", "1.0.9", "1.0", "0.9.9" })
        {
            var result = await Check110(StubHandler.Answering(HttpStatusCode.OK, ReleaseJson(same)));
            Check($"Tag {same} is not newer than 1.1.0", result.Outcome == UpdateCheckOutcome.UpToDate, result.Outcome.ToString());
        }

        var found = await Check110(StubHandler.Answering(HttpStatusCode.OK, ReleaseJson("v1.2.0", "https://github.com/droidgren/DMWLT4/releases/tag/v1.2.0")));
        Check("New version: names the version without the v", found.LatestVersion == "1.2.0", found.LatestVersion);
        Check("New version: links to the release page", found.ReleaseUri?.AbsoluteUri == "https://github.com/droidgren/DMWLT4/releases/tag/v1.2.0", found.ReleaseUri?.ToString());

        var odd = await Check110(StubHandler.Answering(HttpStatusCode.OK, ReleaseJson("1.2.0", "file:///C:/Windows/System32/calc.exe")));
        Check("A link outside GitHub is not trusted", odd.ReleaseUri?.AbsoluteUri == AppInfo.ProjectUrl + "/releases/latest", odd.ReleaseUri?.ToString());
        var lookalike = await Check110(StubHandler.Answering(HttpStatusCode.OK, ReleaseJson("1.2.0", "https://github.com.example.org/x")));
        Check("A lookalike host is not trusted", lookalike.ReleaseUri?.AbsoluteUri == AppInfo.ProjectUrl + "/releases/latest", lookalike.ReleaseUri?.ToString());

        Check("404: nothing published", (await Check110(StubHandler.Answering(HttpStatusCode.NotFound, "{}"))).Outcome == UpdateCheckOutcome.NoRelease);
        Check("500: failed", (await Check110(StubHandler.Answering(HttpStatusCode.InternalServerError, "oops"))).Outcome == UpdateCheckOutcome.Failed);
        Check("403 rate limit: failed", (await Check110(StubHandler.Answering(HttpStatusCode.Forbidden, "{}"))).Outcome == UpdateCheckOutcome.Failed);
        Check("Malformed JSON: failed", (await Check110(StubHandler.Answering(HttpStatusCode.OK, "<html>proxy login</html>"))).Outcome == UpdateCheckOutcome.Failed);
        Check("Empty answer: failed", (await Check110(StubHandler.Answering(HttpStatusCode.OK, string.Empty))).Outcome == UpdateCheckOutcome.Failed);
        Check("No tag: failed", (await Check110(StubHandler.Answering(HttpStatusCode.OK, "{}"))).Outcome == UpdateCheckOutcome.Failed);
        Check("A tag that is no version: failed", (await Check110(StubHandler.Answering(HttpStatusCode.OK, ReleaseJson("latest")))).Outcome == UpdateCheckOutcome.Failed);

        var unreachable = new StubHandler((r, t) => { throw new HttpRequestException("No route to host"); });
        Check("No network: failed, no exception", (await Check110(unreachable)).Outcome == UpdateCheckOutcome.Failed);

        var watch = Stopwatch.StartNew();
        var timedOut = await Check110(new StubHandler(hang), TimeSpan.FromMilliseconds(100));
        Check("Timeout: failed, no exception", timedOut.Outcome == UpdateCheckOutcome.Failed && watch.Elapsed < TimeSpan.FromSeconds(5), watch.Elapsed.ToString());

        var headers = StubHandler.Answering(HttpStatusCode.OK, ReleaseJson("1.1.0"));
        await Check110(headers);
        Check("Asks for the latest release of this project", headers.LastRequest.RequestUri.AbsoluteUri == "https://api.github.com/repos/droidgren/DMWLT4/releases/latest", headers.LastRequest.RequestUri.ToString());
        Check("Sends the User-Agent GitHub requires, and nothing about the user", headers.LastRequest.Headers.UserAgent.ToString() == "DMWLT4/1.1.0", headers.LastRequest.Headers.UserAgent.ToString());

        // The About window's status line
        async Task<AboutViewModel> About(HttpMessageHandler handler)
        {
            var vm = new AboutViewModel(new UpdateChecker("1.1.0", handler: handler));
            await vm.CheckForUpdateAsync(CancellationToken.None);
            return vm;
        }
        var latest = await About(StubHandler.Answering(HttpStatusCode.OK, ReleaseJson("1.1.0")));
        Check("About: latest version", latest.UpdateStatus == "You have the latest version." && latest.ReleaseUri == null, latest.UpdateStatus);
        var update = await About(StubHandler.Answering(HttpStatusCode.OK, ReleaseJson("1.2.0")));
        Check("About: new version with a link", update.UpdateStatus == "New version available: 1.2.0." && update.ReleaseUri != null && update.ReleaseLinkText.Length > 0, update.UpdateStatus);
        var broken = await About(StubHandler.Answering(HttpStatusCode.InternalServerError, "oops"));
        Check("About: failure is text, not an exception", broken.UpdateStatus == "Could not check for a new version." && broken.ReleaseUri == null, broken.UpdateStatus);

        // The release notes
        var withNotes = await Check110(StubHandler.Answering(HttpStatusCode.OK, ReleaseJson("1.2.0", jsonBody: "## New\\r\\n* First \\u00c5")));
        Check("Notes come with a new version, line breaks included", withNotes.ReleaseNotes == "## New\r\n* First \u00c5", withNotes.ReleaseNotes);
        var markdown = "# Release Notes\r\n\r\n> **Test only.**\r\n\r\n## New Features\n* **Bold** and `code`\n- Dash\n\n## Downloads\n* DMWLT4.msi\n\n**Full Changelog**: x";
        var plain = ReleaseNotesText.ToPlainText(markdown).Replace("\r\n", "\n");
        Check("Notes: Markdown becomes plain text, Downloads left out", plain == "Release Notes\n\nTest only.\n\nNew Features\n\u2022 Bold and code\n\u2022 Dash", plain);
        Check("Notes: nothing gives nothing", ReleaseNotesText.ToPlainText(null).Length == 0 && ReleaseNotesText.ToPlainText("  \n ").Length == 0);

        // The check at startup
        Task<UpdateCheckResult> AtStartup(HttpMessageHandler handler, bool enabled = true, TimeSpan? timeout = null, CancellationToken token = default(CancellationToken)) =>
            StartupUpdateCheck.FindNewVersionAsync(enabled, new UpdateChecker("1.1.0", handler: handler, timeout: timeout), token);

        var startupFound = await AtStartup(StubHandler.Answering(HttpStatusCode.OK, ReleaseJson("1.2.0", jsonBody: "notes")));
        Check("Startup: a new version is reported, with notes", startupFound?.LatestVersion == "1.2.0" && startupFound.ReleaseNotes == "notes", startupFound?.LatestVersion);
        Check("Startup: silent when up to date", await AtStartup(StubHandler.Answering(HttpStatusCode.OK, ReleaseJson("1.1.0"))) == null);
        Check("Startup: silent on 500", await AtStartup(StubHandler.Answering(HttpStatusCode.InternalServerError, "oops")) == null);
        Check("Startup: silent with no network", await AtStartup(new StubHandler((r, t) => { throw new HttpRequestException("No route to host"); })) == null);
        Check("Startup: silent on an unexpected exception", await AtStartup(new StubHandler((r, t) => { throw new NotSupportedException("Odd proxy"); })) == null);
        Check("Startup: silent on timeout", await AtStartup(new StubHandler(hang), timeout: TimeSpan.FromMilliseconds(100)) == null);

        using (var cts = new CancellationTokenSource())
        {
            var pending = AtStartup(new StubHandler(hang), timeout: TimeSpan.FromSeconds(30), token: cts.Token);
            cts.Cancel();
            Check("Startup: closing the program ends the check quietly", await pending == null);
        }

        var turnedOff = StubHandler.Answering(HttpStatusCode.OK, ReleaseJson("1.2.0"));
        Check("Startup: turned off, no window", await AtStartup(turnedOff, enabled: false) == null);
        Check("Startup: turned off, nothing is sent", turnedOff.LastRequest == null);

        // Read from the attribute, because Settings.Default reflects the real user.config.
        var defaultValue = _settings.GetType().GetProperty("ShowUpdateWindowAtStartup").GetCustomAttribute<DefaultSettingValueAttribute>()?.Value;
        Check("Startup: the window is on by default", defaultValue == "True", defaultValue);

        // Only changed in memory, and the dialog is never confirmed, so user.config is not touched.
        foreach (var value in new[] { true, false })
        {
            _settings["ShowUpdateWindowAtStartup"] = value;
            var box = (CheckBox)new SettingsDialog().FindName("ShowUpdateWindowAtStartup");
            Check($"Settings: the Updates checkbox shows {value}", box.IsChecked == value);
        }
    }

    static Button FindButton(DependencyObject root, string content)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Button b && (b.Content as string) == content) return b;
            var found = FindButton(child, content);
            if (found != null) return found;
        }
        return null;
    }

    [STAThread]
    static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        new DicomSetupBuilder().Build();
        MessageBoxes.Start();

        using (var server = DicomServerFactory.Create<MockScp>(Port))
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Startup += async (s, e) =>
            {
                try { await RunTests(); await TestUpdateChecker(); }
                catch (Exception ex) { _fail++; Console.WriteLine("  FAIL  harness exception: " + ex); }
                app.Shutdown();
            };
            app.Run();
        }

        Console.WriteLine($"\n{_pass} passed, {_fail} failed");
        return _fail == 0 ? 0 : 1;
    }
}

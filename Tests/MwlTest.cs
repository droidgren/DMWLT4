// Test harness for DMWLT4: runs a mock Modality Worklist SCP on localhost and drives the app's
// real MainViewModel / SettingsDialog. Message boxes are captured and auto-dismissed.
using FellowOakDicom;
using FellowOakDicom.Network;
using Microsoft.Extensions.Logging;
using MWL4;
using MWL4.ViewModels;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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

    public Task<DicomCEchoResponse> OnCEchoRequestAsync(DicomCEchoRequest request)
    {
        Interlocked.Increment(ref Scp.EchoCount);
        return Task.FromResult(new DicomCEchoResponse(request, Scp.EchoStatus));
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

    static async Task RunTests()
    {
        var vm = new MainViewModel
        {
            ServerHost = "127.0.0.1",
            ServerPort = Port,
            ServerAET = "MOCKSCP",
            CallingAET = "TESTSCU",
            UseTLS = false,
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
        var settingsType = typeof(MainViewModel).Assembly.GetType("DMWLT4.Properties.Settings");
        var settings = (ApplicationSettingsBase)settingsType.GetProperty("Default", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null);
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
                try { await RunTests(); }
                catch (Exception ex) { _fail++; Console.WriteLine("  FAIL  harness exception: " + ex); }
                app.Shutdown();
            };
            app.Run();
        }

        Console.WriteLine($"\n{_pass} passed, {_fail} failed");
        return _fail == 0 ? 0 : 1;
    }
}

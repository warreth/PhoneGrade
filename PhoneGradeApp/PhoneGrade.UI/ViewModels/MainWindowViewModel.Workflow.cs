using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using PhoneGrade.Core;
using PhoneGrade.Core.Apple;
using PhoneGrade.Core.Diagnostics;
using PhoneGrade.UI.Services;
using Avalonia.Threading;
using ReactiveUI;

namespace PhoneGrade.UI.ViewModels;

public partial class MainWindowViewModel
{

    public string SelectedGradeDisplay => Services.GradeWording.Grade(DeviceData.Quality);

    public string SelectedInvoiceMethodDisplay => Services.GradeWording.InvoiceMethod(DeviceData.PayMethod);

    /// <summary>True once real device data has been read: drives the summary grid.</summary>
    public bool HasDevice => DeviceData is { Model: not "NOMODEL", Identifier: not "NOID" };

    // Kiosk state-driven workflow properties
    public AppWorkflowState WorkflowState
    {
        get => _workflowState;
        set
        {
            this.RaiseAndSetIfChanged(ref _workflowState, value);
            this.RaisePropertyChanged(nameof(IsIdleState));
            this.RaisePropertyChanged(nameof(IsActiveState));
            this.RaisePropertyChanged(nameof(IsSummaryState));
        }
    }

    public bool IsIdleState => WorkflowState == AppWorkflowState.Idle;
    public bool IsActiveState => WorkflowState == AppWorkflowState.Active;
    public bool IsSummaryState => WorkflowState == AppWorkflowState.Summary;

    // Popups kept minimal: only quality & payment, and only when no default is set.
    public bool IsQualityPopupVisible { get => _isQualityPopupVisible; set => this.RaiseAndSetIfChanged(ref _isQualityPopupVisible, value); }

    public bool IsPaymentPopupVisible { get => _isPaymentPopupVisible; set => this.RaiseAndSetIfChanged(ref _isPaymentPopupVisible, value); }

    public ReactiveCommand<Unit, Unit> StartCommand { get; }
    public ReactiveCommand<Unit, Unit> RetestCommand { get; }
    public ReactiveCommand<string, Unit> SetQualityCommand { get; }
    public ReactiveCommand<string, Unit> SetPaymentMethodCommand { get; }
    public ReactiveCommand<InteractiveTestResult, Unit> DismissInteractiveTestCommand { get; }
    public ReactiveCommand<Unit, Unit> RestoreDismissedTestsCommand { get; }

    public ReactiveCommand<Unit, Unit> FinishInspectionCommand { get; }

    public void ApplyInteractiveResults(InteractiveTestSuiteResult suite)
    {
        DeviceData.InteractiveTests = suite;
        var newIssues = DeviceData.MergeInteractiveResults(Issues.ToList());
        Issues.Clear();
        foreach (var iss in newIssues)
        {
            Issues.Add(iss);
        }
        HasIssues = Issues.Count > 0;

        int passed = suite.Tests.Count(t => t.Status == TestStatus.Passed);
        int failed = suite.Tests.Count(t => t.Status == TestStatus.Failed);
        int skipped = suite.Tests.Count(t => t.Status == TestStatus.Skipped);

        InteractiveTestSummary = skipped == 0
            ? string.Format(LocalizationManager.GetString("Session_TestsPlain"), passed, failed, suite.Platform)
            : string.Format(LocalizationManager.GetString("Session_TestsSkipped"), passed, failed, skipped, suite.Platform);

        if (failed > 0)
        {
            InteractiveSessionStatus = skipped > 0
                ? string.Format(LocalizationManager.GetString("Session_FailedSkipped"), failed, skipped)
                : string.Format(LocalizationManager.GetString("Session_Failed"), failed);
        }
        else if (skipped > 0)
        {
            InteractiveSessionStatus = string.Format(
                LocalizationManager.GetString("Session_SkippedOnly"), skipped);
        }
        else
        {
            InteractiveSessionStatus = LocalizationManager.GetString(
                suite.AllPassed ? "Session_AllPassed" : "Session_Done");
        }

        RefreshInteractiveTestLists();
    }

    /// <summary>
    /// Fills the two lists in the inspection report.
    ///
    /// They are refilled here and at the end of the flow, because the run can
    /// finish either way round: results that arrive after the report was built
    /// would otherwise sit unseen until the operator paid for nothing.
    /// </summary>
    private void RefreshInteractiveTestLists()
    {
        FailedInteractiveTests.Clear();
        SkippedInteractiveTests.Clear();
        DismissedInteractiveTests.Clear();

        foreach (var test in DeviceData.InteractiveTests?.Tests ?? Enumerable.Empty<InteractiveTestResult>())
        {
            // Counted, not dropped: the report says how much it is leaving out.
            if (test.Excluded)
            {
                DismissedInteractiveTests.Add(test);
            }
            else if (test.Status == TestStatus.Failed)
            {
                FailedInteractiveTests.Add(test);
            }
            else if (test.Status == TestStatus.Skipped)
            {
                SkippedInteractiveTests.Add(test);
            }
        }

        RaiseReportChanged();
    }

    /// <summary>
    /// Takes one row out of the inspection report.
    ///
    /// The decision is written onto the row, not kept in the list, because both
    /// lists are emptied and refilled every time the report refreshes: a
    /// dismissal held only by a list would be back on the next refresh. A new
    /// run rebuilds the rows from what the phone sent, so a fresh measurement
    /// shows itself again.
    /// </summary>
    private void DismissInteractiveTest(InteractiveTestResult? test)
    {
        if (test is null || test.Excluded) return;

        test.Excluded = true;
        FailedInteractiveTests.Remove(test);
        SkippedInteractiveTests.Remove(test);
        DismissedInteractiveTests.Add(test);

        RaiseReportChanged();
    }

    /// <summary>Puts every row back in the report, so a dismissal is never final.</summary>
    private void RestoreDismissedTests()
    {
        if (DismissedInteractiveTests.Count == 0) return;

        foreach (var test in DismissedInteractiveTests)
        {
            test.Excluded = false;
        }

        RefreshInteractiveTestLists();
    }

    /// <summary>
    /// Raises everything that moves when a row enters or leaves the report.
    ///
    /// The three are one piece of state: what is listed, what is hidden, and
    /// whether the green line is allowed to stand. Raising them together is
    /// what keeps a view from showing a figure for rows it no longer believes
    /// are hidden.
    /// </summary>
    private void RaiseReportChanged()
    {
        this.RaisePropertyChanged(nameof(NoInteractiveTestProblems));
        this.RaisePropertyChanged(nameof(HasDismissedInteractiveTests));
        this.RaisePropertyChanged(nameof(DismissedInteractiveTestCount));
    }

    private void ResetToIdle()
    {
        string? lastUdid = SelectedDevice.Key;
        if (_flowCts != null)
        {
            _flowCts.Cancel();
            _flowCts.Dispose();
            _flowCts = null;
        }

        // Preserve session if unhooked mid-test
        if (WorkflowState == AppWorkflowState.Active && !string.IsNullOrWhiteSpace(lastUdid) && DeviceData != null)
        {
            DeviceSessionManager.PreserveDisconnectedSession(lastUdid, DeviceData, Progress);
            Status = LocalizationManager.GetString("Status_Disconnected");
        }
        else
        {
            Status = LocalizationManager.GetString("Status_ConnectUsb");
        }

        WorkflowState = AppWorkflowState.Idle;
        Busy = false;
        IsQualityPopupVisible = false;
        IsPaymentPopupVisible = false;
    }

    /// <summary>Resets the current device session and retests it.</summary>
    private async Task RetestCurrentDeviceAsync()
    {
        string? udid = SelectedDevice.Key;
        if (!string.IsNullOrWhiteSpace(udid))
        {
            DeviceSessionManager.ResetDevice(udid);
            await RunFlowAsync();
        }
    }

    /// <summary>The whole pipeline for one device.</summary>
    private async Task RunFlowAsync()
    {
        // Every line of the flow touches bound state, so a caller off the UI
        // thread is moved over before anything runs. The gate below guards
        // itself too, because it is public and has other callers.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(RunFlowAsync);
            return;
        }

        string? udid = SelectedDevice.Key;
        if (udid is not { Length: > 0 })
        {
            Status = LocalizationManager.GetString("Status_NoDeviceSelected");
            return;
        }

        // Licensing gate: block at the limit before any device work starts.
        if (!await PassScanGateAsync())
        {
            Status = LocalizationManager.GetString("Status_TrialLimit");
            return;
        }

        Busy = true;
        WorkflowState = AppWorkflowState.Active;
        DeviceSessionManager.MarkStarted(udid);
        _flowCts = new CancellationTokenSource();
        Issues.Clear();
        ComponentChecks.Clear();
        HasIssues = false;
        Progress = 5;
        try
        {
            // 1. Trust / connectivity
            Status = LocalizationManager.GetString("Status_Checking");
            var state = await DeviceService.GetConnectionStateAsync(udid);
            if (state == DeviceService.ConnectionState.Unauthorized)
            {
                Status = LocalizationManager.GetString("Status_Unauthorized");
                WorkflowState = AppWorkflowState.Idle; // Fallback to Idle
                return;
            }
            if (state == DeviceService.ConnectionState.NotTrusted)
            {
                Status = LocalizationManager.GetString("Status_NotTrusted");
                WorkflowState = AppWorkflowState.Idle; // Fallback to Idle
                return;
            }
            if (state == DeviceService.ConnectionState.PermissionDenied)
            {
                Status = LocalizationManager.GetString("Status_PermissionDenied");
                return;
            }
            if (state == DeviceService.ConnectionState.DriverMissing)
            {
                Status = LocalizationManager.GetString("Status_DriverMissing");
                return;
            }
            if (state == DeviceService.ConnectionState.DaemonStopped)
            {
                Status = LocalizationManager.GetString(OperatingSystem.IsWindows()
                    ? "Status_DaemonStoppedWin"
                    : "Status_DaemonStoppedUnix");
                return;
            }
            if (state != DeviceService.ConnectionState.Connected)
            {
                Status = LocalizationManager.GetString("Status_Unreachable");
                return;
            }
            Progress = 20;

            // 2. Activation bypass (optional)
            if (AutoActivate && state == DeviceService.ConnectionState.NotActivated ||
                AutoActivate && await NeedsActivationAsync(udid))
            {
                Status = LocalizationManager.GetString("Status_Activating");
                string result = await ActivationService.SkipActivationAsync(udid);
                Status = result;
            }
            Progress = 40;

            // 3. Read device data
            Status = LocalizationManager.GetString("Status_ReadingData");
            DeviceData = await DeviceService.GetDeviceDataAsync(udid);
            ComponentChecks.Clear();
            foreach (var check in DeviceData.ComponentChecks)
                ComponentChecks.Add(check);
            Progress = 60;

            // 4. Battery health: keep actual percentage in UI, label service adds [X] when printing if < 85%

            // 5. Diagnostics (panic logs & sensors): strictly asynchronous on background thread (non-blocking)
            if (RunDiagnostics)
            {
                // Read on the UI thread and carried into the background: the pass
                // must not read a setting while the operator is changing it.
                bool scanCrashLogs = ScanCrashLogs;
                CrashLogRange crashLogRange = CrashLogRangeItem.Range;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var diagIssues = await DiagnosticService.DiagnoseAsync(udid, scanCrashLogs, crashLogRange);
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            foreach (var issue in diagIssues)
                                Issues.Add(issue);
                            HasIssues = Issues.Count > 0;
                        });
                    }
                    catch { }
                });
            }
            Progress = 75;

            // Wait for user to explicitly click 'Afronden'
            Status = LocalizationManager.GetString("Status_SpecsRead");
        }
        catch (Exception ex)
        {
            Status = string.Format(LocalizationManager.GetString("Status_Error"), ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task ContinueAfterQualityAsync(string quality)
    {
        string penalised = GradePolicy.ApplyMissingApiPenalty(quality, DeviceData.ComponentChecks);
        if (penalised != quality)
        {
            quality = penalised;
            SystemEventLogger.Warning(LogSource.Desktop, "Prevented Grade 'A' selection due to missing mandatory browser APIs.", DeviceData.Identifier);
            Status = LocalizationManager.GetString("Status_GradeDowngraded");
        }
        
        SelectedGrade = quality;
        IsQualityPopupVisible = false;

        if (DefaultPaymentMethod == PaymentMethods.NeverAsk)
        {
            // A shop that does not record an invoice method is not stopped for one.
            // The device keeps no method, the label leaves the field off and the
            // report says nothing about it, which is what "not asked" means.
            await ContinueAfterPaymentAsync("");
        }
        else if (DefaultPaymentMethod is { Length: > 0 })
        {
            await ContinueAfterPaymentAsync(DefaultPaymentMethod);
        }
        else
        {
            Progress = 90;
            Status = LocalizationManager.GetString("Status_ChoosePayment");
            IsPaymentPopupVisible = true;
        }
    }

    private Task ContinueAfterPaymentAsync(string method)
    {
        SelectedInvoiceMethod = method;
        IsQualityPopupVisible = false;
        IsPaymentPopupVisible = false;
        Progress = 100;
        Status = LocalizationManager.GetString("Status_TestsComplete");
        // Populate defect inspection report (only non-OEM/mismatch components and failed tests)
        DefectiveComponents.Clear();
        foreach (var c in DeviceData.ComponentChecks)
        {
            if (c.Status == ComponentStatusType.Mismatch || c.Status == ComponentStatusType.Untrusted)
            {
                DefectiveComponents.Add(c);
            }
        }

        RefreshInteractiveTestLists();

        WorkflowState = AppWorkflowState.Summary;

        string? udid = SelectedDevice.Key;
        if (!string.IsNullOrWhiteSpace(udid))
        {
            DeviceSessionManager.MarkCompleted(udid, DeviceData);
        }

        if (OpenEditorBeforePrint)
        {
            DataEditorRequested?.Invoke(DeviceData);
            return Task.CompletedTask;
        }

        if (!ShowSummaryScreenAfterTesting)
        {
            FinishInspectionAndShow();
        }

        return Task.CompletedTask;
    }

    /// <summary>Generate + open the label; single click path for the user.</summary>
    private async Task FinishInspectionAsync()
    {
        // Check if PWA test is required and not completed
        if (RequirePwaTest && DeviceData.InteractiveTests == null)
        {
            Status = LocalizationManager.GetString("Status_PwaRequired");
            return;
        }

        // Enforce Grading Penalty: Prevent auto 'A' grade if missing mandatory APIs
        string targetQuality = GradePolicy.ApplyMissingApiPenalty(DefaultQuality, DeviceData.ComponentChecks);
        if (targetQuality != DefaultQuality)
        {
            SystemEventLogger.Warning(LogSource.Desktop, "Downgraded automatic grade from A to B due to missing mandatory browser APIs.", DeviceData.Identifier);
        }

        // 6. Quality + payment: defaults from settings, else popup
        if (!string.IsNullOrEmpty(targetQuality))
            await ContinueAfterQualityAsync(targetQuality);
        else
        {
            IsQualityPopupVisible = true;
            Status = LocalizationManager.GetString("Status_ChooseQuality");
        }
    }

    /// <summary>
    /// Records the inspection and shows the report.
    ///
    /// It used to write the label and open whatever application the system had
    /// registered for a .dymo file, which meant the flow ended in either the DYMO
    /// editor or an error dialog depending on what was installed on the operator's
    /// machine. It now records the inspection and puts the operator in front of the
    /// export panel, which is the same screen the summary screen's button opens and
    /// which works the same whether or not DYMO software is present.
    /// </summary>
    private void FinishInspectionAndShow()
    {
        try
        {
            Status = LocalizationManager.GetString("Status_LabelGenerating");
            AuditLogService.ExportAuditLog(DeviceData);
            Progress = 100;

            // A shop may switch the web test off, and then the report reaches this
            // screen without one. Saying so beats a blank row, which reads as data
            // that failed to load.
            if (DeviceData.InteractiveTests is null)
            {
                InteractiveTestSummary = LocalizationManager.GetString("Summary_TestsNotRun");
                InteractiveSessionStatus = LocalizationManager.GetString("Summary_TestsNotRun");
            }

            WorkflowState = AppWorkflowState.Summary;

            if (!ShowSummaryScreenAfterTesting)
            {
                // With no report screen there is nowhere for the operator to read
                // what was found, so the export panel opens in its place rather
                // than the label being written to a file nobody is shown.
                ExportViewModel?.Open();
            }
        }
        catch (Exception ex)
        {
            Status = string.Format(LocalizationManager.GetString("Status_LabelFailed"), ex.Message);
        }
    }

}

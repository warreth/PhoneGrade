using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using PhoneGrade.Core;
using ReactiveUI;

namespace PhoneGrade.UI.ViewModels;

/// <summary>
/// Consolidated summary view showing device specs, component checks, and interactive test results.
/// Displayed after testing completes when ShowSummaryScreenAfterTesting is enabled.
/// </summary>
public class SummaryViewModel : ReactiveObject
{
    private DeviceData _deviceData = new();
    public DeviceData DeviceData
    {
        get => _deviceData;
        set
        {
            this.RaiseAndSetIfChanged(ref _deviceData, value);
            this.RaisePropertyChanged(nameof(Model));
            this.RaisePropertyChanged(nameof(Identifier));
            this.RaisePropertyChanged(nameof(BatteryHealthDisplay));
            this.RaisePropertyChanged(nameof(ColorDisplay));
            this.RaisePropertyChanged(nameof(QualityDisplay));
            this.RaisePropertyChanged(nameof(OverallStatus));
            this.RaisePropertyChanged(nameof(HasIssues));
            UpdateComponentChecks();
            UpdateInteractiveTests();
        }
    }

    public ObservableCollection<ComponentStatus> ComponentChecks { get; } = new();
    public ObservableCollection<InteractiveTestResult> InteractiveTests { get; } = new();

    public string Model => _deviceData.Model != "NOMODEL" ? _deviceData.Model : "Onbekend model";
    public string Identifier => _deviceData.Identifier != "NOID" ? _deviceData.Identifier : "Geen ID";
    
    public string BatteryHealthDisplay => _deviceData.BatteryHealth != "NOBATT" 
        ? _deviceData.BatteryHealth 
        : "Niet beschikbaar";
    
    public string ColorDisplay => _deviceData.Color != "NOCOLOR" 
        ? _deviceData.Color 
        : "Onbekend";
    
    public string QualityDisplay => _deviceData.Quality != "NOQUALITY" 
        ? _deviceData.Quality 
        : "Niet ingesteld";

    /// <summary>Overall pass/fail status based on component checks and interactive tests.</summary>
    public string OverallStatus
    {
        get
        {
            var componentIssues = _deviceData.ComponentChecks
                .Count(c => c.Status == ComponentStatusType.Mismatch);
            
            var testFailures = _deviceData.InteractiveTests?.Tests
                .Count(t => t.Status == TestStatus.Failed) ?? 0;

            if (componentIssues == 0 && testFailures == 0)
                return "[PASS] Alle controles geslaagd";
            
            if (testFailures > 0 && componentIssues > 0)
                return $"[FAIL] {testFailures} test(s) en {componentIssues} component(en) gefaald";
            
            if (testFailures > 0)
                return $"[FAIL] {testFailures} interactieve test(s) gefaald";
            
            return $"[FAIL] {componentIssues} component(en) niet origineel";
        }
    }

    /// <summary>True if any component or test has failed.</summary>
    public bool HasIssues
    {
        get
        {
            var componentIssues = _deviceData.ComponentChecks
                .Any(c => c.Status == ComponentStatusType.Mismatch);
            
            var testFailures = _deviceData.InteractiveTests?.Tests
                .Any(t => t.Status == TestStatus.Failed) ?? false;

            return componentIssues || testFailures;
        }
    }

    public ReactiveCommand<Unit, Unit> GenerateLabelCommand { get; }
    public ReactiveCommand<Unit, Unit> RetestCommand { get; }

    public SummaryViewModel()
    {
        GenerateLabelCommand = ReactiveCommand.Create(GenerateLabel);
        RetestCommand = ReactiveCommand.Create(Retest);
    }

    private void UpdateComponentChecks()
    {
        ComponentChecks.Clear();
        // Only show defective or replaced (non-OEM) components in the inspection report
        foreach (var check in _deviceData.ComponentChecks)
        {
            if (check.Status == ComponentStatusType.Mismatch || check.Status == ComponentStatusType.Untrusted)
            {
                ComponentChecks.Add(check);
            }
        }
    }

    private void UpdateInteractiveTests()
    {
        InteractiveTests.Clear();
        // Only show failed interactive tests in the inspection report
        if (_deviceData.InteractiveTests?.Tests != null)
        {
            foreach (var test in _deviceData.InteractiveTests.Tests)
            {
                if (test.Status == TestStatus.Failed)
                {
                    InteractiveTests.Add(test);
                }
            }
        }
    }

    private void GenerateLabel()
    {
        // Stub: actual implementation will trigger label generation via MainWindowViewModel
        // ponytail: integrate with LabelService, add when UI wiring complete
    }

    private void Retest()
    {
        // Stub: actual implementation will reset flow and restart testing
        // ponytail: connect to MainWindowViewModel.StartFlowCommand, add when UI wiring complete
    }
}
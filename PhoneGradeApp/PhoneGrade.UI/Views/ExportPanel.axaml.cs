using Avalonia.Controls;
using Avalonia.VisualTree;
using PhoneGrade.UI.ViewModels;

namespace PhoneGrade.UI.Views;

/// <summary>
/// The export and print panel.
///
/// A user control rather than a window, because it is drawn on top of the report
/// screen inside the main window. The operator came from there and goes back to
/// it, and a second window would leave two windows on screen with no visual order
/// between them.
///
/// Its one job beyond drawing the list is to tell its view model how much room the
/// label sheet has. Only a laid out panel knows that, and the panel draws the sheet
/// scaled to fit, so the line saying the sheet is not at full size has to be asked
/// for from here rather than guessed at.
/// </summary>
public partial class ExportPanel : UserControl
{
    private const string SheetColumnName = "PreviewColumn";

    public ExportPanel()
    {
        InitializeComponent();
        LayoutUpdated += ReportAvailableWidth;
    }

    /// <summary>Whether the sheet was already reported scaled down, so it is said once.</summary>
    private bool _reportedScaled;

    /// <summary>
    /// Tells the panel how wide the preview column ended up, which is what decides
    /// whether the sheet is drawn at its real size.
    /// </summary>
    private void ReportAvailableWidth(object? sender, EventArgs e)
    {
        if (DataContext is not ExportViewModel panel) return;

        double available = PreviewColumn()?.Bounds.Width ?? 0;
        if (available <= 0) return;

        panel.MeasureSheet(available - SheetGutter);

        // Said once per state rather than on every layout pass: the line appears
        // when it becomes true and disappears when it stops being true, and a
        // layout pass that fires sixty times a second must not write to a
        // property sixty times a second.
        if (panel.SheetOverflows == _reportedScaled) return;
        _reportedScaled = panel.SheetOverflows;
    }

    /// <summary>
    /// The panel's own padding either side of the sheet. Taken off the column's
    /// width so the sheet is measured against the space it actually has rather
    /// than against the space its container has.
    /// </summary>
    private const double SheetGutter = 24;

    private Control? PreviewColumn() =>
        this.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(border => border.Classes.Contains("previewColumn"));
}
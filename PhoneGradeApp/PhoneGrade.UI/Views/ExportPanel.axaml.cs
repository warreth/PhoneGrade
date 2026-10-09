using Avalonia.Controls;

namespace PhoneGrade.UI.Views;

/// <summary>
/// The finish panel.
///
/// A user control rather than a window, because it is drawn on top of the report
/// screen inside the main window. The operator came from there and goes back to
/// it, and a second window would leave two windows on screen with no visual order
/// between them.
///
/// It has no layout work of its own any more: the label sheet is
/// <see cref="LabelView"/>, which draws the same plate the PDF is written from and
/// scales itself to whatever column it is given.
/// </summary>
public partial class ExportPanel : UserControl
{
    public ExportPanel()
    {
        InitializeComponent();
    }
}

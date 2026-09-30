using Avalonia.Controls;

namespace PhoneGrade.UI.Views;

/// <summary>
/// The first-run introduction screen. It carries no state of its own: every
/// binding points at the main view model, which is where the decision to show
/// this screen at all (the IntroSeen setting) and the licensing view model live.
/// </summary>
public partial class IntroView : UserControl
{
    public IntroView()
    {
        InitializeComponent();
    }
}

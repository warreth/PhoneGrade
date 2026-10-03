using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PhoneGrade.UI.Views;

public partial class AdbTutorialView : UserControl
{
    public AdbTutorialView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
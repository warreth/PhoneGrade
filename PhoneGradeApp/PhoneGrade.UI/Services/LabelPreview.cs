using PhoneGrade.Core;
using PhoneGrade.UI.ViewModels;

namespace PhoneGrade.UI.Services;

/// <summary>
/// Builds the label plate both previews draw and the writers fill.
/// </summary>
/// <remarks>
/// The one place the app turns the current device and the label settings into a
/// plate. The finish panel and the label settings both come through here, so the
/// label a shop configures and the label it prints cannot be built by two different
/// readings of the same settings.
/// </remarks>
public static class LabelPreview
{
    /// <summary>The values the label will carry, with the panel's own rules applied.</summary>
    public static LabelFields Fields(MainWindowViewModel main) => LabelFields.From(
        main.DeviceData,
        main.Enable85PercentChecker,
        content: main.LabelContent,
        colour: ColorWording.OnLabel,
        batteryThreshold: main.LabelBatteryThreshold);

    /// <summary>The drawing plan for one arrangement of the current device.</summary>
    public static LabelPlate Plate(
        MainWindowViewModel main, LabelLayout layout, LabelBarcodeMode mode,
        LabelCodeSymbology symbology, LabelVariant variant) =>
        LabelPlate.Build(
            Fields(main), layout, mode, symbology, variant, main.LabelCyclesThreshold);
}

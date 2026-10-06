using PhoneGrade.Core;

namespace PhoneGrade.UI.Services;

/// <summary>
/// The operator-facing wording for the two fields the operator picks from a
/// fixed set: the grade and the invoice method.
///
/// This lives here rather than on the model because the wording is a property of
/// the language, and the model sits below the language layer. The grade used to be
/// formatted on the model, which is why an English operator saw "KLASSE A" and
/// "Niet opgegeven" on a screen that was otherwise in English.
///
/// The grade is shown as the bare letter, A, B or C, because that is what the
/// label, the report, the CSV and the JSON all carry. A second spelling on screen
/// would be a third thing to keep in step with the printed output.
/// </summary>
public static class GradeWording
{
    /// <summary>The grade, or the not-graded wording when nothing has been picked.</summary>
    public static string Grade(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == DevicePlaceholders.Grade)
            return LocalizationManager.GetString("Value_NotGraded");

        // An unrecognised value is passed through rather than dressed up, so it stays
        // recognisable on screen instead of reading as though it were a grade.
        return raw;
    }

    /// <summary>The invoice method in the wording it is offered in.</summary>
    public static string InvoiceMethod(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == DevicePlaceholders.PayMethod)
            return LocalizationManager.GetString("Value_NotSet");

        return raw switch
        {
            "Marge" => LocalizationManager.GetString("Payment_Marge"),
            "BTW" => LocalizationManager.GetString("Payment_BTW"),
            _ => raw,
        };
    }
}

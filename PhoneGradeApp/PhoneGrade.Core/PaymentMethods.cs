namespace PhoneGrade.Core;

/// <summary>
/// The values the invoice method setting can hold.
/// </summary>
/// <remarks>
/// The two methods are the shop's own wording ("Marge" and "BTW" in Dutch) and are
/// stored as they always have been. The two extra values are about asking rather
/// than about invoicing: empty means the app asks at every inspection, and
/// <see cref="NeverAsk"/> means the shop does not record a method and does not want
/// to be stopped for one. Neither ever reaches a label: a label shows a method or it
/// shows nothing.
/// </remarks>
public static class PaymentMethods
{
    /// <summary>Ask at every inspection, and store whatever the operator answers.</summary>
    public const string Ask = "";

    /// <summary>Do not ask and do not record a method.</summary>
    public const string NeverAsk = "NONE";
}

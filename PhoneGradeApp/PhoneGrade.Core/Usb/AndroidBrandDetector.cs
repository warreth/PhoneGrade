using System.Text.RegularExpressions;

namespace PhoneGrade.Core.Usb;

/// <summary>
/// Works out which phone brand a USB device belongs to, or answers nothing.
///
/// The vendor ID comes first because the operating system reports it for every
/// device and it is the one thing all three platforms agree on. It is not
/// enough on its own: phone makers only started taking vendor IDs of their own
/// recently, and until then they shipped under somebody else's. So a device the
/// ID table does not know is asked again in words, because the description the
/// OS attaches to the event usually carries the brand spelled out.
///
/// Answering nothing is the important half of the job. The guide this feeds is
/// for Android phones, so an iPad, a keyboard or a cable that reports nothing
/// recognisable has to come back empty rather than be guessed at.
/// </summary>
public static class AndroidBrandDetector
{
    /// <summary>
    /// Words that identify a phone maker, longest and most specific first.
    /// Each maps to the canonical name the manufacturer step tables use, so a
    /// device found by its description lands on the same guide as the same
    /// device found by its vendor ID.
    /// </summary>
    private static readonly (string Word, string Brand)[] SpokenBrands =
    {
        ("samsung", "Samsung"),
        ("pixel", "Google"),
        ("google", "Google"),
        ("xiaomi", "Xiaomi"),
        ("redmi", "Redmi"),
        ("poco", "Poco"),
        ("oneplus", "OnePlus"),
        ("realme", "Realme"),
        ("motorola", "Motorola"),
        ("moto", "Motorola"),
        ("huawei", "Huawei"),
        ("honor", "Honor"),
        ("oppo", "Oppo"),
        ("vivo", "Vivo"),
        ("xperia", "Sony"),
        ("sony", "Sony"),
        ("htc", "HTC"),
        ("zte", "ZTE"),
        ("tecno", "Tecno"),
        ("infinix", "Infinix"),
        ("meizu", "Meizu"),
        ("hisense", "Hisense"),
        ("alcatel", "Alcatel"),
        ("nokia", "Nokia"),
        ("coolpad", "Coolpad"),
        ("fairphone", "Fairphone"),
        ("archos", "Archos"),
        ("sharp", "Sharp"),
        ("nvidia", "Nvidia"),
        ("lg", "LG"),
        ("nothing", "Nothing"),
    };

    /// <summary>
    /// The brand behind a connected device, or an empty string when it is not a
    /// phone this tool knows how to talk about.
    /// </summary>
    public static string Detect(UsbDeviceInfo device)
    {
        if (VendorIdDictionary.IsKnownNonAndroidVendor(device.VendorId)) return "";

        if (VendorIdDictionary.TryGetManufacturer(device.VendorId, out string fromId))
            return fromId;

        return FromDescription(device.Description);
    }

    /// <summary>The brand named in the description, or an empty string.</summary>
    public static string FromDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return "";

        // An Apple device says so, and none of the words below may be reached
        // through it: "iPad" next to a word that happens to be in the list is
        // exactly the mix-up this whole check exists to prevent.
        if (Regex.IsMatch(description, @"\b(apple|iphone|ipad|ipod)\b", RegexOptions.IgnoreCase))
            return "";

        foreach ((string word, string brand) in SpokenBrands)
        {
            if (Regex.IsMatch(description, $@"\b{word}\b", RegexOptions.IgnoreCase))
                return brand;
        }

        return "";
    }
}

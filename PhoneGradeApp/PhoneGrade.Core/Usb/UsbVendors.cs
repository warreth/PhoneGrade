using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PhoneGrade.Core.Usb;

/// <summary>
/// Who is on the other end of a USB cable, in one table.
///
/// The vendor ID decides first, because the operating system reports it for
/// every device and all three platforms agree on it. A description is the
/// second question: phone makers shipped under somebody else's ID for years,
/// and the words the OS attaches to the event often carry the brand the ID
/// does not.
///
/// Both halves used to live in their own table, and the two disagreed about
/// Oppo, Vivo, Sony and Qualcomm: the same cable was a phone to the how-to and
/// a stranger to the diagnostics. They are one table here, and the tests hold
/// the halves together.
///
/// The table is an allow list of phones. Answering nothing is the important
/// half of the job: the guide this feeds is for Android phones, so an iPad, a
/// keyboard or a hub that reports nothing recognisable has to come back empty
/// rather than be guessed at.
/// </summary>
public static class UsbVendors
{
    /// <summary>Apple's vendor ID: iPhone, iPad and iPod all share it.</summary>
    public const ushort Apple = 0x05AC;

    /// <summary>
    /// One phone maker: the USB IDs it hands out and the words its devices are
    /// described with.
    ///
    /// A maker that has no ID of its own yet is a row with no IDs and only the
    /// words, which is how the table takes on a new brand without inventing an
    /// ID for it. The spelling of <see cref="Manufacturer"/> is the canonical
    /// one the brand step tables in the how-to are keyed on, so a device found
    /// by ID and the same device found by description land on the same guide.
    /// </summary>
    public sealed record Vendor(ushort[] Ids, string Manufacturer, string[] SpokenWords);

    /// <summary>
    /// The makers the app recognises, in match order: the words are tried top
    /// to bottom, so a more specific word has to come before a shorter one that
    /// contains it.
    /// </summary>
    public static readonly IReadOnlyList<Vendor> Known =
    [
        new([0x04E8], "Samsung", ["samsung"]),
        new([0x18D1], "Google", ["pixel", "google"]),
        new([0x2717], "Xiaomi", ["xiaomi"]),
        new([0x2970], "Redmi", ["redmi"]),
        new([0x2D2E], "Poco", ["poco"]),
        new([0x2A70], "OnePlus", ["oneplus"]),
        new([0x303A], "Realme", ["realme"]),
        new([0x22B8], "Motorola", ["motorola", "moto"]),
        new([0x12D1], "Huawei", ["huawei"]),
        new([0x339B], "Honor", ["honor"]),
        // OPPO and Vivo each appear under two IDs: the phone route and the
        // diagnostics route learned a different one, and both are on real cables.
        new([0x2E3C, 0x22D9], "Oppo", ["oppo"]),
        new([0x2856, 0x29A9], "Vivo", ["vivo"]),
        new([0x0FCE], "Sony", ["xperia", "sony"]),
        new([0x0BB4], "HTC", ["htc"]),
        new([0x19D2], "ZTE", ["zte"]),
        new([0x2931], "Tecno", ["tecno"]),
        new([0x2B0C], "Infinix", ["infinix"]),
        new([0x2A45], "Meizu", ["meizu"]),
        new([0x109B], "Hisense", ["hisense"]),
        new([0x1949], "Alcatel", ["alcatel"]),
        new([], "Nokia", ["nokia"]),
        new([], "Coolpad", ["coolpad"]),
        new([], "Fairphone", ["fairphone"]),
        new([0x0E79], "Archos", ["archos"]),
        new([0x1EBF, 0x04DD], "Sharp", ["sharp"]),
        new([0x0955], "Nvidia", ["nvidia"]),
        new([0x1004], "LG", ["lg"]),
        new([0x2A96], "Nothing", ["nothing"]),
        // Qualcomm hands out the ID phones fall back to in EDL mode; the rest
        // are components and contract makers that ship inside some phone.
        new([0x05C6], "Qualcomm", []),
        new([0x0B05], "Asus", []),
        new([0x0489], "Foxconn", []),
    ];

    /// <summary>
    /// Vendor IDs belonging to companies that do not build Android phones.
    ///
    /// The table above is an allow list, so this is not what decides the normal
    /// case. It exists because a vendor ID alone cannot say "phone": Apple hands
    /// out one ID for every iPhone, iPad and iPod, and an iPad on a cable is the
    /// one device this guide must never open for. Naming them here keeps that
    /// rule readable and testable instead of leaving it implied by an absent row.
    /// </summary>
    public static readonly IReadOnlyCollection<ushort> NonAndroidVendors =
    [
        Apple,   // iPhone, iPad, iPod
        0x045E,  // Microsoft: keyboards, mice, game pads
        0x046D,  // Logitech
        0x8086,  // Intel: Bluetooth and wireless radios behind a USB bridge
        0x0BDA,  // Realtek card readers
        0x0A5C,  // Broadcom
        0x17EF,  // Lenovo: laptop keyboards, docks
        0x413C,  // Dell
        0x03F0,  // HP
        0x0525,  // NetChip: the gadget driver emulators and dev boards use
    ];

    private static readonly Dictionary<ushort, string> ManufacturerById = BuildManufacturerLookup();
    private static readonly (string Word, string Brand)[] SpokenBrands = BuildSpokenBrands();

    /// <summary>Tries to get the manufacturer name for a vendor ID.</summary>
    public static bool TryGetManufacturer(ushort vendorId, out string manufacturer)
    {
        if (ManufacturerById.TryGetValue(vendorId, out string? found) && found is not null)
        {
            manufacturer = found;
            return true;
        }

        manufacturer = "";
        return false;
    }

    /// <summary>True for a vendor ID that belongs to a known phone maker.</summary>
    public static bool IsAndroidVendor(ushort vendorId) => ManufacturerById.ContainsKey(vendorId);

    /// <summary>True for a vendor that has never shipped an Android phone.</summary>
    public static bool IsKnownNonAndroidVendor(ushort vendorId) => NonAndroidVendors.Contains(vendorId);

    /// <summary>
    /// The brand behind a connected device, or an empty string when it is not a
    /// phone this tool knows how to talk about.
    /// </summary>
    public static string BrandFor(UsbDeviceInfo device)
    {
        if (IsKnownNonAndroidVendor(device.VendorId)) return "";

        if (TryGetManufacturer(device.VendorId, out string fromId))
            return fromId;

        return BrandFromDescription(device.Description);
    }

    /// <summary>The brand named in the description, or an empty string.</summary>
    public static string BrandFromDescription(string? description)
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

    private static Dictionary<ushort, string> BuildManufacturerLookup()
    {
        var lookup = new Dictionary<ushort, string>();
        foreach (Vendor vendor in Known)
        {
            foreach (ushort id in vendor.Ids)
                lookup[id] = vendor.Manufacturer;
        }

        return lookup;
    }

    private static (string Word, string Brand)[] BuildSpokenBrands()
    {
        var words = new List<(string, string)>();
        foreach (Vendor vendor in Known)
        {
            foreach (string word in vendor.SpokenWords)
                words.Add((word, vendor.Manufacturer));
        }

        return words.ToArray();
    }
}

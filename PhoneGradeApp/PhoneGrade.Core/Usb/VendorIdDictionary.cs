using System.Collections.Generic;

namespace PhoneGrade.Core.Usb;

/// <summary>
/// Maps USB Vendor IDs (VIDs) to Android manufacturer names.
/// Used to identify Android devices from USB events and show manufacturer-specific guides.
/// </summary>
public static class VendorIdDictionary
{
    /// <summary>
    /// Mapping of USB Vendor IDs to manufacturer names.
    /// Values are the canonical manufacturer names used for localization keys.
    /// </summary>
    public static readonly IReadOnlyDictionary<ushort, string> AndroidVendors = new Dictionary<ushort, string>
    {
        { 0x04E8, "Samsung" },        // Samsung Electronics
        { 0x18D1, "Google" },         // Google (Pixel, Nexus)
        { 0x2717, "Xiaomi" },         // Xiaomi
        { 0x22B8, "Motorola" },       // Motorola Mobility
        { 0x0FCE, "Sony" },           // Sony Mobile
        { 0x12D1, "Huawei" },         // Huawei
        { 0x2A70, "OnePlus" },        // OnePlus
        { 0x0BB4, "HTC" },            // HTC
        { 0x1004, "LG" },             // LG Electronics
        { 0x05C6, "Qualcomm" },       // Qualcomm (often used for EDL mode, but also some devices)
        { 0x2970, "Redmi" },          // Redmi (Xiaomi sub-brand)
        { 0x2D2E, "Poco" },           // POCO (Xiaomi sub-brand)
        { 0x303A, "Realme" },         // Realme
        { 0x2E3C, "Oppo" },           // Oppo
        { 0x2856, "Vivo" },           // Vivo
        { 0x1EBF, "Sharp" },          // Sharp
        { 0x04DD, "Sharp" },          // Sharp (alt)
        { 0x0955, "Nvidia" },         // Nvidia (Shield)
        { 0x0E79, "Archos" },         // Archos
        { 0x1949, "Alcatel" },        // Alcatel (TCL)
        { 0x2931, "Tecno" },          // Tecno
        { 0x2B0C, "Infinix" },        // Infinix
        { 0x0B05, "Asus" },           // Asus
        { 0x0489, "Foxconn" },        // Foxconn (manufactures for many brands)
        { 0x2A96, "Nothing" },        // Nothing Technology
        { 0x339B, "Honor" },          // Honor Device Co. (their own ID since the split from Huawei)
        { 0x19D2, "ZTE" },            // ZTE
        { 0x2A45, "Meizu" },          // Meizu
        { 0x109B, "Hisense" },        // Hisense
    };

    /// <summary>
    /// Vendor IDs belonging to companies that do not build Android phones.
    ///
    /// The table above is an allow list, so this is not what decides the normal
    /// case. It exists because a vendor ID alone cannot say "phone": Apple hands
    /// out one ID for every iPhone, iPad and iPod, and an iPad on a cable is the
    /// one device this guide must never open for. Naming them here keeps that
    /// rule readable and testable instead of leaving it implied by an absent row.
    /// </summary>
    public static readonly IReadOnlyCollection<ushort> NonAndroidVendors = new ushort[]
    {
        0x05AC, // Apple: iPhone, iPad, iPod
        0x045E, // Microsoft: keyboards, mice, game pads
        0x046D, // Logitech
        0x8086, // Intel: Bluetooth and wireless radios behind a USB bridge
        0x0BDA, // Realtek card readers
        0x0A5C, // Broadcom
        0x17EF, // Lenovo: laptop keyboards, docks
        0x413C, // Dell
        0x03F0, // HP
        0x0525, // NetChip: the gadget driver emulators and dev boards use
    };

    /// <summary>True for a vendor that has never shipped an Android phone.</summary>
    public static bool IsKnownNonAndroidVendor(ushort vendorId) => NonAndroidVendors.Contains(vendorId);

    /// <summary>
    /// Tries to get the manufacturer name for a given vendor ID.
    /// </summary>
    /// <param name="vendorId">The USB Vendor ID (VID).</param>
    /// <param name="manufacturer">The manufacturer name if found.</param>
    /// <returns>True if the VID is a known Android manufacturer.</returns>
    public static bool TryGetManufacturer(ushort vendorId, out string manufacturer)
    {
        if (AndroidVendors.TryGetValue(vendorId, out string? found) && found is not null)
        {
            manufacturer = found;
            return true;
        }

        manufacturer = "";
        return false;
    }

    /// <summary>
    /// Checks if a vendor ID belongs to a known Android manufacturer.
    /// </summary>
    public static bool IsAndroidVendor(ushort vendorId) => AndroidVendors.ContainsKey(vendorId);
}
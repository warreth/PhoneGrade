using PhoneGrade.Core.Usb;
using Xunit;

namespace Tests;

// ============ Which PnP node has the phone's name in it ============
//
// Windows gives one phone a row per interface it exposes, and only one of
// those rows carries the name an operator would recognise. The composite
// interface is called "USB Composite Device" on every phone ever made; the
// portable device node underneath it is the one the OS fills in from the phone
// itself. Choosing between them decides what the how-to calls the phone, and
// it is the whole of what the WMI lookup does besides asking the question.

public class PnpNamePickerTests
{
    [Fact]
    public void ThePortableDeviceNode_WinsOverEveryInterface()
    {
        // The portable node has to win on where it sits in the tree, not on how
        // long its name is: the interface here is given the longer of the two so
        // that a longest-name rule would take it.
        var nodes = new[]
        {
            ("USB Composite Device", @"USB\VID_04E8&PID_6860&REV_0400"),
            ("SAMSUNG Galaxy A55 Ultra", @"USB\VID_04E8&PID_6860&0001"),
            ("Galaxy A55", @"SWD\WPDBUSENUM\_??_USBSTOR#Disk&Ven_Samsung&Prod_SSD"),
        };

        Assert.Equal("Galaxy A55", PnpNamePicker.Pick(nodes));
    }

    [Fact]
    public void WithoutAPortableNode_TheNameThatReadsAsAPhoneIsTaken()
    {
        // The portable node is often a beat behind the interface, so this is
        // the answer the how-to opens with. A name that reads as a phone wins
        // over one that reads as a port, and either beats nothing.
        var nodes = new[]
        {
            ("USB Composite Device", @"USB\VID_04E8&PID_6860&REV_0400"),
            ("SAMSUNG Galaxy A55", @"USB\VID_04E8&PID_6860&0001"),
        };

        Assert.Equal("SAMSUNG Galaxy A55", PnpNamePicker.Pick(nodes));
    }

    [Fact]
    public void WhenNothingButHardwareIsNamed_TheAnswerIsEmpty()
    {
        // An empty string is the honest answer here: the caller falls back to
        // the brand rather than heading the how-to with "USB Composite Device",
        // and it is what tells the caller to read again in a moment.
        var nodes = new[]
        {
            ("USB Composite Device", @"USB\VID_04E8&PID_6860&REV_0400"),
            ("", @"USB\VID_04E8&PID_6860&0001"),
        };

        Assert.Equal("", PnpNamePicker.Pick(nodes));
        Assert.Equal("", PnpNamePicker.Pick(Array.Empty<(string, string)>()));
    }
}

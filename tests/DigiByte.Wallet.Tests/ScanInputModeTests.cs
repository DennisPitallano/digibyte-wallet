using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Which way in the scan panel opens first (specs/030-desktop-paste-scan, FR-001 to FR-004): paste on a mouse or
/// trackpad device or one with no camera, the camera on a touch device with a camera, and always the person's own
/// last choice on this device once they have made one.
/// </summary>
public class ScanInputModeTests
{
    private static DeviceFacts Facts(bool finePointer, bool? hasCamera) =>
        new(finePointer, hasCamera, CanCaptureScreen: false, CanReadClipboardText: false, CanReadClipboardImages: false);

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(true, null)]
    public void A_mouse_or_trackpad_opens_paste_first(bool finePointer, bool? hasCamera)
    {
        Assert.Equal(ScanEntry.Paste, ScanInputMode.Decide(Facts(finePointer, hasCamera), null));
    }

    [Fact]
    public void A_touch_device_without_a_camera_opens_paste_first()
    {
        Assert.Equal(ScanEntry.Paste, ScanInputMode.Decide(Facts(false, false), null));
    }

    [Fact]
    public void A_touch_device_with_a_camera_opens_the_camera_first()
    {
        Assert.Equal(ScanEntry.Camera, ScanInputMode.Decide(Facts(false, true), null));
    }

    [Fact]
    public void A_touch_device_whose_camera_is_unknown_keeps_todays_camera_first()
    {
        Assert.Equal(ScanEntry.Camera, ScanInputMode.Decide(Facts(false, null), null));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, null)]
    public void A_remembered_camera_choice_wins(bool finePointer, bool? hasCamera)
    {
        Assert.Equal(ScanEntry.Camera, ScanInputMode.Decide(Facts(finePointer, hasCamera), ScanEntry.Camera));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, null)]
    [InlineData(true, true)]
    public void A_remembered_paste_choice_wins(bool finePointer, bool? hasCamera)
    {
        Assert.Equal(ScanEntry.Paste, ScanInputMode.Decide(Facts(finePointer, hasCamera), ScanEntry.Paste));
    }

    [Theory]
    [InlineData("paste", ScanEntry.Paste)]
    [InlineData("camera", ScanEntry.Camera)]
    public void Stored_choices_are_read_back(string stored, ScanEntry expected)
    {
        Assert.Equal(expected, ScanInputMode.ParseRemembered(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("PASTE")]
    [InlineData("Camera")]
    [InlineData("x")]
    [InlineData("screen")]
    public void Anything_else_stored_means_no_choice(string? stored)
    {
        Assert.Null(ScanInputMode.ParseRemembered(stored));
    }

    [Theory]
    [InlineData(ScanEntry.Paste)]
    [InlineData(ScanEntry.Camera)]
    public void A_choice_survives_being_stored(ScanEntry mode)
    {
        Assert.Equal(mode, ScanInputMode.ParseRemembered(ScanInputMode.ToStored(mode)));
    }

    [Fact]
    public void Stored_values_are_the_documented_words()
    {
        Assert.Equal("paste", ScanInputMode.ToStored(ScanEntry.Paste));
        Assert.Equal("camera", ScanInputMode.ToStored(ScanEntry.Camera));
    }
}

using Microsoft.Win32;
using PcControl.Windows;

namespace PcControl.Tests.Windows;

/// <summary>Uses its own throwaway registry keys, never the real Run key.</summary>
public sealed class StartupServiceTests : IDisposable
{
    private const string Root = @"Software\PcControlTests\Startup";
    private readonly string _run = Root + @"\Run";
    private readonly string _approval = Root + @"\Approved";
    private readonly StartupService _service;

    public StartupServiceTests()
    {
        _service = new StartupService("PcControlTest", _run, _approval);
    }

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\PcControlTests", throwOnMissingSubKey: false);

    [Fact]
    public void ByDefault_ItIsOff()
    {
        Assert.False(_service.IsEnabled);
    }

    [Fact]
    public void TurningItOn_WritesTheQuotedPath_AndTurningItOffRemovesIt()
    {
        _service.SetEnabled(true, @"C:\Program Files\PC Control\PcControl.exe");

        Assert.True(_service.IsEnabled);
        using (var key = Registry.CurrentUser.OpenSubKey(_run))
        {
            Assert.Equal(@"""C:\Program Files\PC Control\PcControl.exe""", key!.GetValue("PcControlTest"));
        }

        _service.SetEnabled(false);
        Assert.False(_service.IsEnabled);
    }

    [Fact]
    public void WindowsDisablingIt_IsRespected_AndTurningItOnAgainClearsThat()
    {
        _service.SetEnabled(true, @"C:\x\PcControl.exe");
        using (var approval = Registry.CurrentUser.CreateSubKey(_approval))
        {
            approval.SetValue("PcControlTest", new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        }

        Assert.False(_service.IsEnabled);

        _service.SetEnabled(true, @"C:\x\PcControl.exe");
        Assert.True(_service.IsEnabled);
    }

    [Fact]
    public void TurningOffWhenAlreadyOff_DoesNothingBad()
    {
        _service.SetEnabled(false);
        Assert.False(_service.IsEnabled);
    }
}

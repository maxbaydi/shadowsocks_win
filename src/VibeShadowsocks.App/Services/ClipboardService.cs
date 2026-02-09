using Windows.ApplicationModel.DataTransfer;

namespace VibeShadowsocks.App.Services;

public sealed class ClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        var dataPackage = new DataPackage();
        dataPackage.SetText(text);
        Clipboard.SetContent(dataPackage);
    }
}

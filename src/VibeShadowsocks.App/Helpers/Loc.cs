using Microsoft.Windows.ApplicationModel.Resources;

namespace VibeShadowsocks.App.Helpers;

public static class Loc
{
    private static readonly ResourceLoader Loader = new();

    public static string Get(string key)
    {
        try
        {
            return Loader.GetString(key);
        }
        catch
        {
            return key;
        }
    }

    public static string Format(string key, params object[] args) =>
        string.Format(Get(key), args);
}

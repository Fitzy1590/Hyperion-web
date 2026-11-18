using Microsoft.Web.WebView2.Core;

namespace Hyperion.Services;

public static class BrowserEnvironmentFactory
{
    static readonly string[] GoogleVars = ["GOOGLE_API_KEY", "GOOGLE_DEFAULT_CLIENT_ID", "GOOGLE_DEFAULT_CLIENT_SECRET"];

    public static string UserDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hyperion", "WebView2");

    /// <summary>
    /// Chromium reads the Google API variables from its process environment, so they must be set before the browser
    /// process starts. Values come from the user's environment; nothing is stored in the repository.
    /// NOTE: unverified that the Edge-based WebView2 runtime honours them for Google account sync.
    /// </summary>
    public static Task<CoreWebView2Environment> CreateAsync()
    {
        foreach (var name in GoogleVars)
        {
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 }) continue;
            if (Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User) is { Length: > 0 } v)
                Environment.SetEnvironmentVariable(name, v);
        }
        var options = new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true };
        return CoreWebView2Environment.CreateAsync(null, UserDataFolder, options);
    }

    /// <summary>Loads an unpacked extension folder (Manifest V3). Chrome Web Store installs are not exposed by WebView2.</summary>
    public static Task<CoreWebView2BrowserExtension> AddExtensionAsync(CoreWebView2 core, string unpackedFolder) =>
        core.Profile.AddBrowserExtensionAsync(unpackedFolder);
}

using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Riven.Configuration;

/// <summary>
/// Riven plugin configuration.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the Riven API host or IP address.
    /// </summary>
    public string Host { get; set; } = "192.168.1.158";

    /// <summary>
    /// Gets or sets the Riven API port.
    /// </summary>
    public int Port { get; set; } = 8080;

    /// <summary>
    /// Gets or sets a value indicating whether HTTPS should be used.
    /// </summary>
    public bool UseHttps { get; set; }

    /// <summary>
    /// Gets or sets the Riven API key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the web action script should be available.
    /// </summary>
    public bool EnableWebActions { get; set; } = true;

    /// <summary>
    /// Gets the configured Riven base URL.
    /// </summary>
    public string GetBaseUrl()
    {
        var scheme = UseHttps ? "https" : "http";
        var host = Host.Trim().TrimEnd('/');
        return $"{scheme}://{host}:{Port}";
    }
}

using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Riven.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public string Host { get; set; } = "192.168.1.158";
    public int Port { get; set; } = 8080;
    public bool UseHttps { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public bool EnableWebActions { get; set; } = true;

    public string? DefaultQualityOverride { get; set; }

    public string? DefaultProfileOverride { get; set; }

    public bool RefreshLibraryOnComplete { get; set; }

    public string GetBaseUrl()
    {
        var scheme = UseHttps ? "https" : "http";
        var host = Host.Trim().TrimEnd('/');
        return $"{scheme}://{host}:{Port}";
    }
}
using System.Text.RegularExpressions;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Riven;

/// <summary>
/// Fallback file-based script injection for installs where response middleware is bypassed.
/// </summary>
public sealed class JavaScriptInjectionService : IHostedService
{
    private const string StartComment = "<!-- BEGIN Riven Plugin -->";
    private const string EndComment = "<!-- END Riven Plugin -->";
    private readonly IApplicationPaths _appPaths;
    private readonly ILogger<JavaScriptInjectionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JavaScriptInjectionService"/> class.
    /// </summary>
    public JavaScriptInjectionService(ILogger<JavaScriptInjectionService> logger, IApplicationPaths appPaths)
    {
        _logger = logger;
        _appPaths = appPaths;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            try
            {
                Thread.Sleep(2000);
                CleanupOldInjection();

                if (Plugin.Instance?.Configuration.EnableWebActions == true)
                {
                    InjectScript();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Riven fallback file injection failed; middleware injection may still work.");
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void CleanupOldInjection()
    {
        var indexPath = GetIndexPath();
        if (!File.Exists(indexPath))
        {
            return;
        }

        var content = File.ReadAllText(indexPath);
        var cleanupRegex = new Regex($"{Regex.Escape(StartComment)}[\\s\\S]*?{Regex.Escape(EndComment)}\\s*", RegexOptions.Multiline);
        if (!cleanupRegex.IsMatch(content))
        {
            return;
        }

        File.WriteAllText(indexPath, cleanupRegex.Replace(content, string.Empty));
    }

    private void InjectScript()
    {
        var indexPath = GetIndexPath();
        if (!File.Exists(indexPath))
        {
            return;
        }

        var content = File.ReadAllText(indexPath);
        if (content.Contains(StartComment, StringComparison.Ordinal) || content.Contains("/Riven/Web/riven.js", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var injection = $"{StartComment}\n<script defer src=\"/Riven/Web/riven.js\"></script>\n{EndComment}\n";
        if (!content.Contains("</body>", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        File.WriteAllText(indexPath, Regex.Replace(content, "</body>", injection + "</body>", RegexOptions.IgnoreCase));
    }

    private string GetIndexPath()
    {
        return Path.Combine(_appPaths.WebPath, "index.html");
    }
}

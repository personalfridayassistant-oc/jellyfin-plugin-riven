using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Riven;

/// <summary>
/// Injects the Riven web script into Jellyfin Web responses.
/// </summary>
public sealed class ScriptInjectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ScriptInjectionMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptInjectionMiddleware"/> class.
    /// </summary>
    public ScriptInjectionMiddleware(RequestDelegate next, ILogger<ScriptInjectionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Processes the request.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (!IsIndexHtmlRequest(path) || Plugin.Instance?.Configuration.EnableWebActions != true)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        context.Request.Headers.Remove("Accept-Encoding");
        var originalBody = context.Response.Body;

        try
        {
            using var memory = new MemoryStream();
            context.Response.Body = memory;

            await _next(context).ConfigureAwait(false);

            if (context.Response.StatusCode != StatusCodes.Status200OK
                || !string.IsNullOrEmpty(context.Response.Headers.ContentEncoding.ToString())
                || !(context.Response.ContentType ?? string.Empty).StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            {
                await WriteOriginalAsync(memory, originalBody).ConfigureAwait(false);
                return;
            }

            memory.Position = 0;
            string html;
            using (var reader = new StreamReader(memory, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
            {
                html = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(html) || html.Contains("/Riven/Web/riven.js", StringComparison.OrdinalIgnoreCase))
            {
                await WriteOriginalAsync(memory, originalBody).ConfigureAwait(false);
                return;
            }

            var modified = InjectScript(html, ResolveBasePath(context, path));
            if (modified == html)
            {
                await WriteOriginalAsync(memory, originalBody).ConfigureAwait(false);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(modified);
            context.Response.Headers.Remove("Content-Length");
            context.Response.ContentLength = bytes.Length;
            await originalBody.WriteAsync(bytes).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Riven script injection failed; passing through original response.");
            if (context.Response.Body is MemoryStream memory)
            {
                await WriteOriginalAsync(memory, originalBody).ConfigureAwait(false);
            }
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static bool IsIndexHtmlRequest(string path)
    {
        return path.Equals("/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/index.html", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/web", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/web/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/web/index.html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveBasePath(HttpContext context, string path)
    {
        var basePath = context.Request.PathBase.Value?.TrimEnd('/') ?? string.Empty;
        if (!string.IsNullOrEmpty(basePath))
        {
            return basePath;
        }

        var webIndex = path.IndexOf("/web/", StringComparison.OrdinalIgnoreCase);
        if (webIndex > 0)
        {
            return path[..webIndex];
        }

        if (path.EndsWith("/web", StringComparison.OrdinalIgnoreCase) && path.Length > 4)
        {
            return path[..^4];
        }

        return string.Empty;
    }

    private static string InjectScript(string html, string basePath)
    {
        var bodyClose = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (bodyClose < 0)
        {
            return html;
        }

        var safeBasePath = System.Net.WebUtility.HtmlEncode(basePath);
        var script = $"<script defer src=\"{safeBasePath}/Riven/Web/riven.js\"></script>";
        return html.Insert(bodyClose, script + "\n");
    }

    private static async Task WriteOriginalAsync(MemoryStream memory, Stream originalBody)
    {
        if (memory.Length == 0)
        {
            return;
        }

        memory.Position = 0;
        await memory.CopyToAsync(originalBody).ConfigureAwait(false);
    }
}

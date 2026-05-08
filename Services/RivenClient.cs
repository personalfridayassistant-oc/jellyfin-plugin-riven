using System.Net.Http.Json;
using System.Text.Json;
using Jellyfin.Plugin.Riven.Configuration;
using Jellyfin.Plugin.Riven.Models;

namespace Jellyfin.Plugin.Riven.Services;

/// <summary>
/// Client for Riven API calls used by the plugin.
/// </summary>
public sealed class RivenClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="RivenClient"/> class.
    /// </summary>
    public RivenClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Resolves a Riven item id from external metadata ids.
    /// </summary>
    public async Task<RivenItem?> ResolveItemAsync(RivenLookup lookup, CancellationToken cancellationToken)
    {
        var config = GetConfig();
        EnsureConfigured(config);

        var mediaType = lookup.MediaType;
        var searches = new[] { lookup.TmdbId, lookup.TvdbId, lookup.ImdbId, lookup.Title }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var search in searches)
        {
            var uri = BuildUri(config, "/api/v1/items", new Dictionary<string, string?>
            {
                ["type"] = mediaType,
                ["search"] = search,
                ["extended"] = "true",
                ["api_key"] = config.ApiKey
            });

            var response = await _httpClient.GetFromJsonAsync<RivenSearchResponse>(uri, JsonOptions, cancellationToken).ConfigureAwait(false);
            var match = response?.Items.FirstOrDefault(item => Matches(item, lookup));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>
    /// Retries a Riven item.
    /// </summary>
    public async Task<string> RetryAsync(string rivenItemId, CancellationToken cancellationToken)
    {
        var config = GetConfig();
        EnsureConfigured(config);
        var uri = BuildUri(config, "/api/v1/items/retry", new Dictionary<string, string?> { ["api_key"] = config.ApiKey });
        var response = await _httpClient.PostAsJsonAsync(uri, new { ids = new[] { rivenItemId } }, JsonOptions, cancellationToken).ConfigureAwait(false);
        return await ReadRivenMessageAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a Riven item and then retries it.
    /// </summary>
    public async Task<string> DeleteAndRetryAsync(string rivenItemId, CancellationToken cancellationToken)
    {
        var config = GetConfig();
        EnsureConfigured(config);
        var removeUri = BuildUri(config, "/api/v1/items/remove", new Dictionary<string, string?> { ["api_key"] = config.ApiKey });
        var removeRequest = new HttpRequestMessage(HttpMethod.Delete, removeUri)
        {
            Content = JsonContent.Create(new { ids = new[] { rivenItemId } }, options: JsonOptions)
        };

        using var removeResponse = await _httpClient.SendAsync(removeRequest, cancellationToken).ConfigureAwait(false);
        var removeMessage = await ReadRivenMessageAsync(removeResponse, cancellationToken).ConfigureAwait(false);
        var retryMessage = await RetryAsync(rivenItemId, cancellationToken).ConfigureAwait(false);
        return $"{removeMessage}; {retryMessage}";
    }

    /// <summary>
    /// Submits a magnet link for an existing movie item.
    /// </summary>
    public async Task<string> SubmitMovieMagnetAsync(string rivenItemId, string magnet, CancellationToken cancellationToken)
    {
        var config = GetConfig();
        EnsureConfigured(config);
        if (string.IsNullOrWhiteSpace(magnet) || !magnet.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A valid magnet link is required.");
        }

        var startUri = BuildUri(config, "/api/v1/scrape/start_session", new Dictionary<string, string?>
        {
            ["magnet"] = magnet,
            ["item_id"] = rivenItemId,
            ["media_type"] = "movie",
            ["api_key"] = config.ApiKey
        });

        using var startResponse = await _httpClient.PostAsync(startUri, null, cancellationToken).ConfigureAwait(false);
        using var startDoc = await ReadJsonDocumentAsync(startResponse, cancellationToken).ConfigureAwait(false);
        var root = startDoc.RootElement;
        var sessionId = root.GetProperty("session_id").GetString();
        var files = root.GetProperty("containers").GetProperty("files");
        var firstFile = files.EnumerateArray().FirstOrDefault();

        if (string.IsNullOrWhiteSpace(sessionId) || firstFile.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException("Riven started a session but did not return a selectable file.");
        }

        var fileData = new Dictionary<string, object?>
        {
            ["file_id"] = firstFile.GetProperty("file_id").GetInt32(),
            ["filename"] = firstFile.GetProperty("filename").GetString(),
            ["filesize"] = firstFile.GetProperty("filesize").GetInt64(),
            ["download_url"] = firstFile.TryGetProperty("download_url", out var downloadUrl) ? downloadUrl.GetString() : null
        };

        var selectUri = BuildUri(config, $"/api/v1/scrape/select_files/{Uri.EscapeDataString(sessionId)}", new Dictionary<string, string?> { ["api_key"] = config.ApiKey });
        using var selectResponse = await _httpClient.PostAsJsonAsync(selectUri, new Dictionary<string, object?> { [fileData["file_id"]!.ToString()!] = fileData }, JsonOptions, cancellationToken).ConfigureAwait(false);
        await ReadRivenMessageAsync(selectResponse, cancellationToken).ConfigureAwait(false);

        var updateUri = BuildUri(config, $"/api/v1/scrape/update_attributes/{Uri.EscapeDataString(sessionId)}", new Dictionary<string, string?> { ["api_key"] = config.ApiKey });
        using var updateResponse = await _httpClient.PostAsJsonAsync(updateUri, fileData, JsonOptions, cancellationToken).ConfigureAwait(false);
        await ReadRivenMessageAsync(updateResponse, cancellationToken).ConfigureAwait(false);

        var completeUri = BuildUri(config, $"/api/v1/scrape/complete_session/{Uri.EscapeDataString(sessionId)}", new Dictionary<string, string?> { ["api_key"] = config.ApiKey });
        using var completeResponse = await _httpClient.PostAsync(completeUri, null, cancellationToken).ConfigureAwait(false);
        return await ReadRivenMessageAsync(completeResponse, cancellationToken).ConfigureAwait(false);
    }

    private static PluginConfiguration GetConfig()
    {
        return Plugin.Instance?.Configuration ?? throw new InvalidOperationException("Riven plugin is not initialized.");
    }

    private static void EnsureConfigured(PluginConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.Host) || config.Port <= 0 || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new InvalidOperationException("Configure the Riven host, port, and API key before using Riven actions.");
        }
    }

    private static Uri BuildUri(PluginConfiguration config, string path, IReadOnlyDictionary<string, string?> query)
    {
        var builder = new UriBuilder(config.GetBaseUrl()) { Path = path.TrimStart('/') };
        var pairs = query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}");
        builder.Query = string.Join('&', pairs);
        return builder.Uri;
    }

    private static bool Matches(RivenItem item, RivenLookup lookup)
    {
        if (!string.IsNullOrWhiteSpace(lookup.TmdbId) && string.Equals(item.TmdbId, lookup.TmdbId, StringComparison.OrdinalIgnoreCase))
        {
            return MatchesNumbers(item, lookup);
        }

        if (!string.IsNullOrWhiteSpace(lookup.TvdbId) && string.Equals(item.TvdbId, lookup.TvdbId, StringComparison.OrdinalIgnoreCase))
        {
            return MatchesNumbers(item, lookup);
        }

        if (!string.IsNullOrWhiteSpace(lookup.ImdbId) && string.Equals(item.ImdbId, lookup.ImdbId, StringComparison.OrdinalIgnoreCase))
        {
            return MatchesNumbers(item, lookup);
        }

        return MatchesNumbers(item, lookup)
            && !string.IsNullOrWhiteSpace(lookup.Title)
            && string.Equals(item.Title ?? item.ParentTitle, lookup.Title, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesNumbers(RivenItem item, RivenLookup lookup)
    {
        return (!lookup.SeasonNumber.HasValue || item.SeasonNumber == lookup.SeasonNumber)
            && (!lookup.EpisodeNumber.HasValue || item.EpisodeNumber == lookup.EpisodeNumber);
    }

    private static async Task<string> ReadRivenMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Riven returned {(int)response.StatusCode}: {text}");
        }

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        return document.RootElement.TryGetProperty("message", out var message) ? message.GetString() ?? text : text;
    }

    private static async Task<JsonDocument> ReadJsonDocumentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Riven returned {(int)response.StatusCode}: {text}");
        }

        return JsonDocument.Parse(text);
    }
}

/// <summary>
/// Metadata used to resolve a Riven item from a Jellyfin item.
/// </summary>
public sealed class RivenLookup
{
    /// <summary>
    /// Gets or sets the Riven media type.
    /// </summary>
    public string MediaType { get; set; } = "movie";

    /// <summary>
    /// Gets or sets the display title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the IMDb id.
    /// </summary>
    public string? ImdbId { get; set; }

    /// <summary>
    /// Gets or sets the TMDB id.
    /// </summary>
    public string? TmdbId { get; set; }

    /// <summary>
    /// Gets or sets the TVDB id.
    /// </summary>
    public string? TvdbId { get; set; }

    /// <summary>
    /// Gets or sets the season number.
    /// </summary>
    public int? SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the episode number.
    /// </summary>
    public int? EpisodeNumber { get; set; }
}

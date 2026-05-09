using System.Net.Http.Json;
using System.Text.Json;
using Jellyfin.Plugin.Riven.Configuration;
using Jellyfin.Plugin.Riven.Models;

namespace Jellyfin.Plugin.Riven.Services;

public sealed class RivenClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public RivenClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<RivenItem?> ResolveItemAsync(RivenLookup lookup, CancellationToken cancellationToken)
    {
        var config = GetConfig();
        EnsureConfigured(config);

        var mediaType = lookup.MediaType;
        var searches = new[] { lookup.TmdbId, lookup.TvdbId, lookup.ImdbId, lookup.Title }
            .Where(v => !string.IsNullOrWhiteSpace(v))
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

    public async Task<string> RetryAsync(string rivenItemId, string? qualityOverride, string? profileOverride, CancellationToken cancellationToken)
    {
        var config = GetConfig();
        EnsureConfigured(config);

        var query = new Dictionary<string, string?> { ["api_key"] = config.ApiKey };
        if (!string.IsNullOrWhiteSpace(qualityOverride))
        {
            query["quality"] = qualityOverride;
        }

        if (!string.IsNullOrWhiteSpace(profileOverride))
        {
            query["profile"] = profileOverride;
        }

        var uri = BuildUri(config, "/api/v1/items/retry", query);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);

        if (!string.IsNullOrWhiteSpace(qualityOverride) || !string.IsNullOrWhiteSpace(profileOverride))
        {
            var body = new Dictionary<string, object?> { ["ids"] = new[] { rivenItemId } };
            if (!string.IsNullOrWhiteSpace(qualityOverride))
            {
                body["quality"] = qualityOverride;
            }

            if (!string.IsNullOrWhiteSpace(profileOverride))
            {
                body["profile"] = profileOverride;
            }

            request.Content = JsonContent.Create(body, options: JsonOptions);
        }
        else
        {
            request.Content = JsonContent.Create(new { ids = new[] { rivenItemId } }, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadRivenMessageAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> DeleteAndReAddAsync(RivenItem item, string mediaType, CancellationToken cancellationToken)
    {
        var config = GetConfig();
        EnsureConfigured(config);

        var removeUri = BuildUri(config, "/api/v1/items/remove", new Dictionary<string, string?> { ["api_key"] = config.ApiKey });
        var removeRequest = new HttpRequestMessage(HttpMethod.Delete, removeUri)
        {
            Content = JsonContent.Create(new { ids = new[] { item.Id } }, options: JsonOptions)
        };

        using var removeResponse = await _httpClient.SendAsync(removeRequest, cancellationToken).ConfigureAwait(false);
        var removeMessage = await ReadRivenMessageAsync(removeResponse, cancellationToken).ConfigureAwait(false);

        var addMessage = await AddAsync(item, mediaType, cancellationToken).ConfigureAwait(false);
        return $"{removeMessage}; {addMessage}";
    }

    public async Task<string> AddAsync(RivenItem item, string mediaType, CancellationToken cancellationToken)
    {
        var config = GetConfig();
        EnsureConfigured(config);

        var tmdbId = item.TmdbId ?? item.ParentIds?.TmdbId;
        var tvdbId = item.TvdbId ?? item.ParentIds?.TvdbId;
        if (string.IsNullOrWhiteSpace(tmdbId) && string.IsNullOrWhiteSpace(tvdbId))
        {
            throw new InvalidOperationException("Riven re-add requires a TMDB or TVDB id, but this item does not have one.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["media_type"] = mediaType
        };

        if (!string.IsNullOrWhiteSpace(tmdbId))
        {
            payload["tmdb_ids"] = new[] { tmdbId };
        }

        if (!string.IsNullOrWhiteSpace(tvdbId))
        {
            payload["tvdb_ids"] = new[] { tvdbId };
        }

        var addUri = BuildUri(config, "/api/v1/items/add", new Dictionary<string, string?> { ["api_key"] = config.ApiKey });
        using var addResponse = await _httpClient.PostAsJsonAsync(addUri, payload, JsonOptions, cancellationToken).ConfigureAwait(false);
        return await ReadRivenMessageAsync(addResponse, cancellationToken).ConfigureAwait(false);
    }

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

        if (!root.TryGetProperty("session_id", out _) || !root.TryGetProperty("containers", out _))
        {
            throw new InvalidOperationException("Riven did not return a valid session for magnet submission.");
        }

        var sessionId = root.GetProperty("session_id").GetString()!;
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
            ["download_url"] = firstFile.TryGetProperty("download_url", out var du) ? du.GetString() : null
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

    public async Task<RivenScrapeStartResponse> StartMagnetSessionAsync(string rivenItemId, string magnet, string mediaType, CancellationToken cancellationToken)
    {
        var config = GetConfig();
        EnsureConfigured(config);
        if (string.IsNullOrWhiteSpace(magnet) || !magnet.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A valid magnet link is required.");
        }

        var uri = BuildUri(config, "/api/v1/scrape/start_session", new Dictionary<string, string?>
        {
            ["magnet"] = magnet,
            ["item_id"] = rivenItemId,
            ["media_type"] = mediaType,
            ["api_key"] = config.ApiKey
        });

        using var response = await _httpClient.PostAsync(uri, null, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Riven returned {(int)response.StatusCode}: {text}");
        }

        return JsonSerializer.Deserialize<RivenScrapeStartResponse>(text, JsonOptions)
            ?? throw new InvalidOperationException("Riven returned an empty session response.");
    }

    public async Task<string> SelectFileAsync(string sessionId, RivenFileOption file, CancellationToken cancellationToken)
    {
        var config = GetConfig();
        var uri = BuildUri(config, $"/api/v1/scrape/select_files/{Uri.EscapeDataString(sessionId)}", new Dictionary<string, string?> { ["api_key"] = config.ApiKey });
        var fileData = new Dictionary<string, object?>
        {
            ["file_id"] = file.FileId,
            ["filename"] = file.Filename,
            ["filesize"] = file.Filesize
        };
        using var selectResponse = await _httpClient.PostAsJsonAsync(uri, new Dictionary<string, object?> { [file.FileId.ToString()] = fileData }, JsonOptions, cancellationToken).ConfigureAwait(false);
        var message = await ReadRivenMessageAsync(selectResponse, cancellationToken).ConfigureAwait(false);

        var updateUri = BuildUri(config, $"/api/v1/scrape/update_attributes/{Uri.EscapeDataString(sessionId)}", new Dictionary<string, string?> { ["api_key"] = config.ApiKey });
        using var updateResponse = await _httpClient.PostAsJsonAsync(updateUri, fileData, JsonOptions, cancellationToken).ConfigureAwait(false);
        await ReadRivenMessageAsync(updateResponse, cancellationToken).ConfigureAwait(false);
        return message;
    }

    public async Task<string> CompleteSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        var config = GetConfig();
        var uri = BuildUri(config, $"/api/v1/scrape/complete_session/{Uri.EscapeDataString(sessionId)}", new Dictionary<string, string?> { ["api_key"] = config.ApiKey });
        using var response = await _httpClient.PostAsync(uri, null, cancellationToken).ConfigureAwait(false);
        return await ReadRivenMessageAsync(response, cancellationToken).ConfigureAwait(false);
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
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}");
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
        return document.RootElement.TryGetProperty("message", out var msg) ? msg.GetString() ?? text : text;
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

public sealed class RivenLookup
{
    public string MediaType { get; set; } = "movie";
    public string? Title { get; set; }
    public string? ImdbId { get; set; }
    public string? TmdbId { get; set; }
    public string? TvdbId { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
}

using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Riven.Models;

/// <summary>
/// Request body for actions that target a Jellyfin item.
/// </summary>
public sealed class RivenItemActionRequest
{
    /// <summary>
    /// Gets or sets the Jellyfin item id.
    /// </summary>
    public Guid ItemId { get; set; }
}

/// <summary>
/// Request body for a manual magnet submission.
/// </summary>
public sealed class ManualMagnetRequest
{
    /// <summary>
    /// Gets or sets the Jellyfin item id.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the magnet link.
    /// </summary>
    public string Magnet { get; set; } = string.Empty;
}

/// <summary>
/// Response returned by plugin action endpoints.
/// </summary>
public sealed class RivenActionResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether the action succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resolved Riven item id.
    /// </summary>
    public string? RivenItemId { get; set; }
}

/// <summary>
/// Riven item search response.
/// </summary>
public sealed class RivenSearchResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether the request succeeded.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the returned items.
    /// </summary>
    [JsonPropertyName("items")]
    public List<RivenItem> Items { get; set; } = [];
}

/// <summary>
/// Riven item representation.
/// </summary>
public sealed class RivenItem
{
    /// <summary>
    /// Gets or sets the Riven item id.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item title.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the item type.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets the parent title.
    /// </summary>
    [JsonPropertyName("parent_title")]
    public string? ParentTitle { get; set; }

    /// <summary>
    /// Gets or sets the season number.
    /// </summary>
    [JsonPropertyName("season_number")]
    public int? SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the episode number.
    /// </summary>
    [JsonPropertyName("episode_number")]
    public int? EpisodeNumber { get; set; }

    /// <summary>
    /// Gets or sets the IMDb id.
    /// </summary>
    [JsonPropertyName("imdb_id")]
    public string? ImdbId { get; set; }

    /// <summary>
    /// Gets or sets the TVDB id.
    /// </summary>
    [JsonPropertyName("tvdb_id")]
    public string? TvdbId { get; set; }

    /// <summary>
    /// Gets or sets the TMDB id.
    /// </summary>
    [JsonPropertyName("tmdb_id")]
    public string? TmdbId { get; set; }
}

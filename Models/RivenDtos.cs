using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Riven.Models;

public sealed class RivenItemActionRequest
{
    public Guid ItemId { get; set; }
    public string? QualityOverride { get; set; }
    public string? ProfileOverride { get; set; }
}

public sealed class ManualMagnetRequest
{
    public Guid ItemId { get; set; }
    public string Magnet { get; set; } = string.Empty;
}

public sealed class RivenActionResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? RivenItemId { get; set; }
}

public sealed class RivenSearchResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("items")]
    public List<RivenItem> Items { get; set; } = [];
}

public sealed class RivenItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("parent_title")]
    public string? ParentTitle { get; set; }

    [JsonPropertyName("season_number")]
    public int? SeasonNumber { get; set; }

    [JsonPropertyName("episode_number")]
    public int? EpisodeNumber { get; set; }

    [JsonPropertyName("imdb_id")]
    public string? ImdbId { get; set; }

    [JsonPropertyName("tvdb_id")]
    public string? TvdbId { get; set; }

    [JsonPropertyName("tmdb_id")]
    public string? TmdbId { get; set; }

    [JsonPropertyName("parent_ids")]
    public RivenProviderIds? ParentIds { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("media_metadata")]
    public RivenMediaMetadata? MediaMetadata { get; set; }

    [JsonPropertyName("filesystem_entry")]
    public RivenFileSystemEntry? FilesystemEntry { get; set; }
}

public sealed class RivenProviderIds
{
    [JsonPropertyName("imdb_id")]
    public string? ImdbId { get; set; }

    [JsonPropertyName("tvdb_id")]
    public string? TvdbId { get; set; }

    [JsonPropertyName("tmdb_id")]
    public string? TmdbId { get; set; }
}

public sealed class RivenMediaMetadata
{
    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("video")]
    public RivenVideoTrack? Video { get; set; }

    [JsonPropertyName("audio_tracks")]
    public List<RivenAudioTrack> AudioTracks { get; set; } = [];

    [JsonPropertyName("quality_source")]
    public string? QualitySource { get; set; }
}

public sealed class RivenVideoTrack
{
    [JsonPropertyName("codec")]
    public string? Codec { get; set; }

    [JsonPropertyName("resolution_width")]
    public int? ResolutionWidth { get; set; }

    [JsonPropertyName("resolution_height")]
    public int? ResolutionHeight { get; set; }

    [JsonPropertyName("hdr_type")]
    public string? HdrType { get; set; }
}

public sealed class RivenAudioTrack
{
    [JsonPropertyName("codec")]
    public string? Codec { get; set; }

    [JsonPropertyName("channels")]
    public string? Channels { get; set; }
}

public sealed class RivenFileSystemEntry
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("file_size")]
    public long FileSize { get; set; }

    [JsonPropertyName("original_filename")]
    public string? OriginalFilename { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }
}

public sealed class RivenScrapeStartResponse
{
    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("containers")]
    public RivenContainers? Containers { get; set; }

    [JsonPropertyName("files")]
    public List<RivenFileOption> Files { get; set; } = [];

    [JsonPropertyName("selected_files")]
    public List<RivenFileOption> SelectedFiles { get; set; } = [];
}

public sealed class RivenContainers
{
    [JsonPropertyName("files")]
    public List<RivenFileOption> Files { get; set; } = [];
}

public sealed class RivenFileOption
{
    [JsonPropertyName("file_id")]
    public int FileId { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("filesize")]
    public long Filesize { get; set; }

    [JsonPropertyName("quality")]
    public string? Quality { get; set; }

    [JsonPropertyName("codec")]
    public string? Codec { get; set; }

    [JsonPropertyName("resolution")]
    public string? Resolution { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("seeders")]
    public int? Seeders { get; set; }

    [JsonPropertyName("leechers")]
    public int? Leechers { get; set; }
}

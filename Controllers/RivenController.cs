using System.Globalization;
using System.Net.Mime;
using System.Reflection;
using System.Text.Json;
using Jellyfin.Plugin.Riven.Models;
using Jellyfin.Plugin.Riven.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Riven.Controllers;

[ApiController]
[Route("Riven")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class RivenController : ControllerBase
{
    private static readonly HttpClient SharedHttpClient = new();
    private readonly ILibraryManager _libraryManager;
    private readonly RivenClient _rivenClient;

    public RivenController(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
        _rivenClient = new RivenClient(SharedHttpClient);
    }

    [HttpGet("Web/riven.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    public ActionResult GetWebScript()
    {
        if (Plugin.Instance?.Configuration.EnableWebActions != true)
        {
            return NotFound();
        }

        var assembly = Assembly.GetExecutingAssembly();
        const string ResourceName = "Jellyfin.Plugin.Riven.Web.riven.js";
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return NotFound();
        }

        using var reader = new StreamReader(stream);
        return Content(reader.ReadToEnd(), "application/javascript");
    }

    [HttpPost("Retry")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<RivenActionResponse>> Retry([FromBody] RivenItemActionRequest request, CancellationToken cancellationToken)
    {
        return await ExecuteResolvedActionAsync(request.ItemId, async item =>
        {
            var config = Plugin.Instance?.Configuration;
            var qualityOverride = request.QualityOverride ?? config?.DefaultQualityOverride;
            var profileOverride = request.ProfileOverride ?? config?.DefaultProfileOverride;

            var message = await _rivenClient.RetryAsync(item.Id, qualityOverride, profileOverride, cancellationToken).ConfigureAwait(false);

            if (config?.RefreshLibraryOnComplete == true)
            {
                await TriggerLibraryRefreshAsync(cancellationToken).ConfigureAwait(false);
            }

            return new RivenActionResponse { Success = true, Message = message, RivenItemId = item.Id };
        }, cancellationToken).ConfigureAwait(false);
    }

    [HttpPost("DeleteAndRetry")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<RivenActionResponse>> DeleteAndRetry([FromBody] RivenItemActionRequest request, CancellationToken cancellationToken)
    {
        var jellyfinItem = _libraryManager.GetItemById(request.ItemId);
        if (jellyfinItem is not Movie && jellyfinItem is not Episode)
        {
            return BadRequest(new RivenActionResponse { Success = false, Message = "Delete and retry is only available for movies and individual episodes." });
        }

        return await ExecuteResolvedActionAsync(request.ItemId, async item =>
        {
            var config = Plugin.Instance?.Configuration;
            var qualityOverride = request.QualityOverride ?? config?.DefaultQualityOverride;
            var profileOverride = request.ProfileOverride ?? config?.DefaultProfileOverride;

            var message = await _rivenClient.DeleteAndRetryAsync(item.Id, qualityOverride, profileOverride, cancellationToken).ConfigureAwait(false);

            if (config?.RefreshLibraryOnComplete == true)
            {
                await TriggerLibraryRefreshAsync(cancellationToken).ConfigureAwait(false);
            }

            return new RivenActionResponse { Success = true, Message = message, RivenItemId = item.Id };
        }, cancellationToken).ConfigureAwait(false);
    }

    [HttpPost("SubmitMagnet")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<RivenScrapeStartResponse>> SubmitMagnet([FromBody] ManualMagnetRequest request, CancellationToken cancellationToken)
    {
        var jellyfinItem = _libraryManager.GetItemById(request.ItemId);
        if (jellyfinItem is not Movie)
        {
            return BadRequest(new RivenActionResponse { Success = false, Message = "Manual magnet submission currently supports movies." });
        }

        return await ExecuteResolvedActionAsync(request.ItemId, async item =>
        {
            var result = await _rivenClient.StartMagnetSessionAsync(item.Id, request.Magnet, "movie", cancellationToken).ConfigureAwait(false);
            if (result.SessionId is null || (result.Containers?.Files.Count ?? 0) == 0)
            {
                throw new InvalidOperationException("Riven did not return any available streams for the provided magnet link.");
            }

            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    [HttpPost("SubmitTvMagnet")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<RivenScrapeStartResponse>> SubmitTvMagnet([FromBody] ManualMagnetRequest request, CancellationToken cancellationToken)
    {
        var jellyfinItem = _libraryManager.GetItemById(request.ItemId);
        if (jellyfinItem is not Series)
        {
            return BadRequest(new RivenActionResponse { Success = false, Message = "TV magnet submission is only available on the main series page, not seasons or episodes." });
        }

        return await ExecuteResolvedActionAsync(request.ItemId, async item =>
        {
            var result = await _rivenClient.StartMagnetSessionAsync(item.Id, request.Magnet, "tv", cancellationToken).ConfigureAwait(false);

            if (result.SessionId is null || (result.Containers?.Files.Count ?? 0) == 0)
            {
                throw new InvalidOperationException("Riven did not return any available streams for the provided magnet link.");
            }

            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    [HttpPost("SelectStream")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<RivenActionResponse>> SelectStream([FromBody] SelectStreamRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.FileId))
        {
            return BadRequest(new RivenActionResponse { Success = false, Message = "Session ID and File ID are required." });
        }

        try
        {
            var file = new RivenFileOption
            {
                FileId = int.Parse(request.FileId, CultureInfo.InvariantCulture),
                Filename = request.Filename,
                Filesize = request.Filesize
            };
            await _rivenClient.SelectFileAsync(request.SessionId, file, cancellationToken).ConfigureAwait(false);
            await _rivenClient.CompleteSessionAsync(request.SessionId, cancellationToken).ConfigureAwait(false);

            if (Plugin.Instance?.Configuration.RefreshLibraryOnComplete == true)
            {
                await TriggerLibraryRefreshAsync(cancellationToken).ConfigureAwait(false);
            }

            return Ok(new RivenActionResponse { Success = true, Message = "Stream selected and session completed.", RivenItemId = request.SessionId });
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or JsonException)
        {
            return BadRequest(new RivenActionResponse { Success = false, Message = ex.Message });
        }
    }

    [HttpPost("RefreshLibrary")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<RivenActionResponse>> RefreshLibrary(CancellationToken cancellationToken)
    {
        await TriggerLibraryRefreshAsync(cancellationToken).ConfigureAwait(false);
        return Ok(new RivenActionResponse { Success = true, Message = "Jellyfin library refresh has been triggered." });
    }

    private async Task TriggerLibraryRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _libraryManager.ValidateMediaLibrary(new Progress<double>(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Riven] Library refresh failed: {ex.Message}");
        }
    }

    private async Task<ActionResult<TResponse>> ExecuteResolvedActionAsync<TResponse>(
        Guid jellyfinItemId,
        Func<RivenItem, Task<TResponse>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            var lookup = BuildLookup(jellyfinItemId);
            var rivenItem = await _rivenClient.ResolveItemAsync(lookup, cancellationToken).ConfigureAwait(false);
            if (rivenItem is null)
            {
                return NotFound(new RivenActionResponse { Success = false, Message = "No matching Riven item was found for this Jellyfin item." });
            }

            return Ok(await action(rivenItem).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException)
        {
            return BadRequest(new RivenActionResponse { Success = false, Message = ex.Message });
        }
    }

    private RivenLookup BuildLookup(Guid jellyfinItemId)
    {
        var item = _libraryManager.GetItemById(jellyfinItemId) ?? throw new InvalidOperationException("Jellyfin item was not found.");
        var lookup = new RivenLookup
        {
            Title = item.Name,
            ImdbId = GetProviderId(item, "Imdb"),
            TmdbId = GetProviderId(item, "Tmdb"),
            TvdbId = GetProviderId(item, "Tvdb")
        };

        switch (item)
        {
            case Movie:
                lookup.MediaType = "movie";
                break;
            case Series:
                lookup.MediaType = "show";
                break;
            case Season season:
                lookup.MediaType = "season";
                lookup.SeasonNumber = season.IndexNumber;
                ApplySeriesProviderIds(season.Series, lookup);
                break;
            case Episode episode:
                lookup.MediaType = "episode";
                lookup.SeasonNumber = episode.ParentIndexNumber;
                lookup.EpisodeNumber = episode.IndexNumber;
                ApplySeriesProviderIds(episode.Series, lookup);
                break;
            default:
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Riven actions do not support Jellyfin item type {0}.", item.GetType().Name));
        }

        return lookup;
    }

    private static void ApplySeriesProviderIds(Series? series, RivenLookup lookup)
    {
        if (series is null)
        {
            return;
        }

        lookup.Title = series.Name;
        lookup.ImdbId ??= GetProviderId(series, "Imdb");
        lookup.TmdbId ??= GetProviderId(series, "Tmdb");
        lookup.TvdbId ??= GetProviderId(series, "Tvdb");
    }

    private static string? GetProviderId(BaseItem item, string provider)
    {
        return item.ProviderIds.TryGetValue(provider, out var value) ? value : null;
    }
}

public sealed class SelectStreamRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string FileId { get; set; } = string.Empty;
    public string? Filename { get; set; }
    public long Filesize { get; set; }
    public Guid ItemId { get; set; }
}

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

/// <summary>
/// API endpoints used by the Riven web actions.
/// </summary>
[ApiController]
[Route("Riven")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class RivenController : ControllerBase
{
    private static readonly HttpClient SharedHttpClient = new();
    private readonly ILibraryManager _libraryManager;
    private readonly RivenClient _rivenClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="RivenController"/> class.
    /// </summary>
    public RivenController(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
        _rivenClient = new RivenClient(SharedHttpClient);
    }

    /// <summary>
    /// Serves the optional web action script.
    /// </summary>
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

    /// <summary>
    /// Retries the matching Riven item.
    /// </summary>
    [HttpPost("Retry")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<RivenActionResponse>> Retry([FromBody] RivenItemActionRequest request, CancellationToken cancellationToken)
    {
        return await ExecuteResolvedActionAsync(request.ItemId, async item =>
        {
            var message = await _rivenClient.RetryAsync(item.Id, cancellationToken).ConfigureAwait(false);
            return new RivenActionResponse { Success = true, Message = message, RivenItemId = item.Id };
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a movie or episode in Riven and triggers a retry so it can be reacquired.
    /// </summary>
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
            var message = await _rivenClient.DeleteAndRetryAsync(item.Id, cancellationToken).ConfigureAwait(false);
            return new RivenActionResponse { Success = true, Message = message, RivenItemId = item.Id };
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Submits a magnet link for a movie item through Riven's manual scrape flow.
    /// </summary>
    [HttpPost("SubmitMagnet")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<RivenActionResponse>> SubmitMagnet([FromBody] ManualMagnetRequest request, CancellationToken cancellationToken)
    {
        var jellyfinItem = _libraryManager.GetItemById(request.ItemId);
        if (jellyfinItem is not Movie)
        {
            return BadRequest(new RivenActionResponse { Success = false, Message = "Manual magnet submission currently supports movies. TV episode mapping can be added next." });
        }

        return await ExecuteResolvedActionAsync(request.ItemId, async item =>
        {
            var message = await _rivenClient.SubmitMovieMagnetAsync(item.Id, request.Magnet, cancellationToken).ConfigureAwait(false);
            return new RivenActionResponse { Success = true, Message = message, RivenItemId = item.Id };
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ActionResult<RivenActionResponse>> ExecuteResolvedActionAsync(
        Guid jellyfinItemId,
        Func<RivenItem, Task<RivenActionResponse>> action,
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

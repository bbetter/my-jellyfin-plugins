using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.IO;
using System.Net.Mime;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace JellyShare;

[ApiController]
[Route("share")]
public class ShareController : ControllerBase
{
    private readonly ShareTokenService _tokenService;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<ShareController> _logger;

    private const string FolderNotShareableMessage =
        "Folders and albums cannot be shared directly. Share individual tracks or episodes.";

    public ShareController(
        ShareTokenService tokenService,
        ILibraryManager libraryManager,
        ILogger<ShareController> logger)
    {
        _tokenService = tokenService;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public record CreateShareRequest(
        [Required] Guid ItemId,
        [Range(1, 10080)] int ExpirationMinutes = 60);

    public record ExtendShareRequest(
        [Range(1, 10080)] int AdditionalMinutes = 60);

    public record CreateShareResponse(
        [property: JsonPropertyName("token")]    string Token,
        [property: JsonPropertyName("expiresAt")] DateTime ExpiresAt,
        [property: JsonPropertyName("shareUrl")] string ShareUrl);

    public record ShareLinkInfo(
        [property: JsonPropertyName("token")]    string Token,
        [property: JsonPropertyName("itemId")]   Guid ItemId,
        [property: JsonPropertyName("itemName")] string ItemName,
        [property: JsonPropertyName("expiresAt")] DateTime ExpiresAt,
        [property: JsonPropertyName("shareUrl")] string ShareUrl);

    /// <summary>Creates a temporary share link for a media item.</summary>
    [HttpPost("create")]
    [Authorize]
    [ProducesResponseType(typeof(CreateShareResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult CreateShare([FromBody] CreateShareRequest request)
    {
        var item = _libraryManager.GetItemById(request.ItemId);
        if (item is null)
            return NotFound("Item not found.");

        if (item.IsFolder)
            return BadRequest(FolderNotShareableMessage);

        var token = _tokenService.Create(request.ItemId, request.ExpirationMinutes);
        var expiresAt = DateTime.UtcNow.AddMinutes(request.ExpirationMinutes);

        // Respect X-Forwarded-Proto/Host set by reverse proxies (Caddy terminates TLS)
        var scheme = (string?)Request.Headers["X-Forwarded-Proto"] ?? Request.Scheme;
        var host   = (string?)Request.Headers["X-Forwarded-Host"]  ?? Request.Host.ToString();
        var shareUrl = $"{scheme}://{host}/share/{Uri.EscapeDataString(token)}";

        // Never log the token or the share URL (both grant access); log the item only.
        _logger.LogInformation("Share link created for item {ItemId}", request.ItemId);

        return Ok(new CreateShareResponse(token, expiresAt, shareUrl));
    }

    /// <summary>Lists all active share links.</summary>
    [HttpGet("list")]
    [Authorize]
    [ProducesResponseType(typeof(System.Collections.Generic.List<ShareLinkInfo>), StatusCodes.Status200OK)]
    public IActionResult ListShares()
    {
        var scheme = (string?)Request.Headers["X-Forwarded-Proto"] ?? Request.Scheme;
        var host   = (string?)Request.Headers["X-Forwarded-Host"]  ?? Request.Host.ToString();

        var result = _tokenService.GetAll().Select(kvp =>
        {
            var item = _libraryManager.GetItemById(kvp.Value.ItemId);
            return new ShareLinkInfo(
                Token:     kvp.Key,
                ItemId:    kvp.Value.ItemId,
                ItemName:  item?.Name ?? "Unknown",
                ExpiresAt: kvp.Value.ExpiresAt,
                ShareUrl:  $"{scheme}://{host}/share/{Uri.EscapeDataString(kvp.Key)}"
            );
        }).ToList();

        return Ok(result);
    }

    /// <summary>Revokes a share link.</summary>
    [HttpDelete("{token}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult RevokeShare([FromRoute] string token)
    {
        if (_tokenService.Revoke(token))
            return NoContent();
        return NotFound("Token not found.");
    }

    /// <summary>Extends a share link's expiration.</summary>
    [HttpPost("{token}/extend")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult ExtendShare([FromRoute] string token, [FromBody] ExtendShareRequest request)
    {
        if (_tokenService.Extend(token, request.AdditionalMinutes))
            return NoContent();
        return NotFound("Token not found.");
    }

    /// <summary>Streams a shared media item using its token.</summary>
    [HttpGet("{token}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetSharedMedia([FromRoute] string token)
    {
        var shareToken = _tokenService.Validate(token);
        if (shareToken is null)
        {
            // Token deliberately not logged: it is a bearer credential.
            _logger.LogWarning("Invalid or expired share token requested");
            return NotFound("Share link is invalid or has expired.");
        }

        var item = _libraryManager.GetItemById(shareToken.ItemId);
        if (item is null)
            return NotFound("Media item not found.");

        if (item.IsFolder)
            return BadRequest(FolderNotShareableMessage);

        if (string.IsNullOrEmpty(item.Path) || !System.IO.File.Exists(item.Path))
        {
            _logger.LogWarning("Media file not found on disk: {Path}", item.Path);
            return NotFound("Media file not found on disk.");
        }

        var contentType = GetContentType(item.Path);

        // No fileDownloadName → Content-Disposition: inline → browser streams/plays instead of downloading
        return PhysicalFile(item.Path, contentType, enableRangeProcessing: true);
    }

    private static string GetContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp4" or ".m4v" => "video/mp4",
            ".mkv"           => "video/x-matroska",
            ".avi"           => "video/x-msvideo",
            ".mov"           => "video/quicktime",
            ".webm"          => "video/webm",
            ".mp3"           => "audio/mpeg",
            ".flac"          => "audio/flac",
            ".aac"           => "audio/aac",
            ".ogg"           => "audio/ogg",
            ".wav"           => "audio/wav",
            ".m4a" or ".m4b" => "audio/mp4",
            ".opus"          => "audio/ogg",
            ".wma"           => "audio/x-ms-wma",
            ".ts"            => "video/mp2t",
            _                => MediaTypeNames.Application.Octet
        };
}

using System.ComponentModel.DataAnnotations;
using EvidenceChain.Api.Auth;
using EvidenceChain.Api.Notifications;
using EvidenceChain.Api.RateLimiting;
using EvidenceChain.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EvidenceChain.Api.Controllers;

/// <summary>
/// The signed-in user's own notifications about custody transfers, for every role. Whose they are comes from the token
/// alone: no action takes a user id, and another user's notification id answers 404, as an unknown one does.
/// The two POSTs take no Idempotency-Key, unlike every other POST here: marking read is idempotent by nature (it only
/// ever sets an unset ReadAtUtc), and it is a per-user change on the Reads budget, not a custody write on Writes.
/// </summary>
[ApiController]
[Authorize]
[EnableRateLimiting(RateLimitPolicies.Reads)]
[Route("api/v1/notifications")]
public sealed class NotificationsController(INotifications notifications) : ControllerBase
{
    /// <summary>The caller's notifications, newest first, one page at a time, with their unread count.</summary>
    /// <param name="cursor">The nextCursor of the previous page.</param>
    /// <param name="limit">Items per page, 1 to 50.</param>
    /// <param name="cancellationToken">Aborted when the client cancels.</param>
    [HttpGet]
    [ProducesResponseType<NotificationPageResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<NotificationPageResponse>> List(
        [FromQuery] NotificationCursor? cursor, [FromQuery, Range(1, 50)] int limit = 20, CancellationToken cancellationToken = default)
    {
        var page = await notifications.PageAsync(User.ToActor(), cursor?.BeforeId, limit, cancellationToken);
        return Ok(new NotificationPageResponse(page.Items, page.NextBeforeId is { } id ? NotificationCursor.After(id).ToString() : null, page.UnreadCount));
    }

    /// <summary>How many of the caller's notifications are unread; what the header polls.</summary>
    /// <param name="cancellationToken">Aborted when the client cancels.</param>
    [HttpGet("unread-count")]
    [ProducesResponseType<UnreadCountResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<UnreadCountResponse>> UnreadCount(CancellationToken cancellationToken) =>
        Ok(new UnreadCountResponse(await notifications.UnreadCountAsync(User.ToActor(), cancellationToken)));

    /// <summary>Marks one notification read. Reading it again is fine.</summary>
    /// <param name="id">The notification id.</param>
    /// <param name="cancellationToken">Aborted when the client cancels.</param>
    [HttpPost("{id:long}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> MarkRead(long id, CancellationToken cancellationToken) =>
        await notifications.MarkReadAsync(User.ToActor(), id, cancellationToken)
            ? NoContent()
            : Problem(statusCode: StatusCodes.Status404NotFound, detail: $"You have no notification with the id {id}.");

    /// <summary>Marks read the caller's notifications up to the newest they have seen; any newer stay unread.</summary>
    // No param tags: the XML-comment generator would describe the body with the cancellation token's text.
    [HttpPost("read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> MarkReadUpTo(MarkReadUpToBody body, CancellationToken cancellationToken)
    {
        await notifications.MarkReadUpToAsync(User.ToActor(), body.UpToId, cancellationToken);
        return NoContent();
    }
}

/// <summary>A page of notifications. nextCursor is null on the last page.</summary>
public sealed record NotificationPageResponse(IReadOnlyList<NotificationItem> Items, string? NextCursor, int UnreadCount);

/// <summary>The caller's unread count.</summary>
public sealed record UnreadCountResponse(int UnreadCount);

public sealed class MarkReadUpToBody
{
    /// <summary>The newest notification id the caller has seen.</summary>
    [Range(1, long.MaxValue)]
    public long UpToId { get; init; }
}

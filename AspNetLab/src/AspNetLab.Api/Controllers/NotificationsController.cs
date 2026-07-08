using AspNetLab.Api.Filters;
using AspNetLab.Api.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace AspNetLab.Api.Controllers;

[ApiController]
[Route("notifications")]
public sealed class NotificationsController(
    NotificationQueue queue,
    NotificationStore store) : ControllerBase
{
    /// <summary>
    /// 202 Accepted, а не 200: работа принята, но ещё не сделана — честная семантика
    /// для асинхронной обработки. Location указывает, где следить за статусом.
    /// </summary>
    [HttpPost]
    [ValidateRecipient]
    public async Task<IActionResult> Enqueue(NotificationRequest request, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        store.Set(new NotificationState(id, request, NotificationStatus.Queued));
        await queue.EnqueueAsync(new NotificationWorkItem(id, request), ct);

        return AcceptedAtAction(nameof(GetStatus), new { id }, new { id });
    }

    [HttpGet("{id:guid}")]
    public IActionResult GetStatus(Guid id) =>
        store.TryGet(id, out var state) ? Ok(state) : NotFound();
}

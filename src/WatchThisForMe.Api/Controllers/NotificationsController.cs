using Microsoft.AspNetCore.Mvc;
using WatchThisForMe.Core.Notifications;

namespace WatchThisForMe.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public class NotificationsController(INotificationRepository repository) : ControllerBase
{
    // Newest first, across all watches. Real delivery (email) comes with the Azure deployment;
    // until then this is where notices are read.
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WatchNotification>>> List([FromQuery] int max = 50, CancellationToken cancellationToken = default) =>
        Ok(await repository.ListRecentAsync(Math.Clamp(max, 1, 200), cancellationToken));
}

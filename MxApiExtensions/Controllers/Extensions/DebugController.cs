using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Extensions;
using MxApiExtensions.Classes.LibMatrix;
using MxApiExtensions.Services;

namespace MxApiExtensions.Controllers.Extensions;

[ApiController]
[Route("/")]
public class DebugController(ILogger<ProxyConfigurationController> logger, MxApiExtensionsConfiguration config, UserContextService userContextService)
    : ControllerBase {
    private readonly ILogger _logger = logger;

    private static ConcurrentDictionary<string, RoomInfoEntry> _roomInfoCache = new();

    [HttpGet("debug")]
    public async Task<object?> GetDebug() {
#if !DEBUG
        var user = await userContextService.GetCurrentUserContext();
        var mxid = user.Homeserver.UserId;
        if(!config.Admins.Contains(mxid)) {
            _logger.LogWarning("Got debug request for {user}, but they are not an admin", mxid);
            Response.StatusCode = StatusCodes.Status403Forbidden;
            Response.ContentType = "application/json";

            await Response.WriteAsJsonAsync(new {
                ErrorCode = "M_FORBIDDEN",
                Error = "You are not an admin"
            });
            await Response.CompleteAsync();
            return null;
        }
        _logger.LogInformation("Got debug request for {user}", mxid);
#endif

        return new {
            syncControllerTasks = SyncController.TrackedTasks.Select(t => new {
                t?.Id,
                t?.IsCompleted,
                t?.IsCompletedSuccessfully,
                t?.IsCanceled,
                t?.IsFaulted,
                Status = t?.Status.GetDisplayName()
            }),
            UserContextService.UserContextStore
        };
    }
}

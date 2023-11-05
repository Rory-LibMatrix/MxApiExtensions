using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using MxApiExtensions.Classes.LibMatrix;
using MxApiExtensions.Services;

namespace MxApiExtensions.Controllers.Extensions;

[ApiController]
[Route("/")]
public class DebugController : ControllerBase {
    private readonly ILogger _logger;
    private readonly MxApiExtensionsConfiguration _config;
    private readonly AuthenticationService _authenticationService;

    private static ConcurrentDictionary<string, RoomInfoEntry> _roomInfoCache = new();

    public DebugController(ILogger<ProxyConfigurationController> logger, MxApiExtensionsConfiguration config, AuthenticationService authenticationService,
        AuthenticatedHomeserverProviderService authenticatedHomeserverProviderService) {
        _logger = logger;
        _config = config;
        _authenticationService = authenticationService;
    }

    [HttpGet("debug")]
    public async Task<object?> GetDebug() {
        var mxid = await _authenticationService.GetMxidFromToken();
        if(!_config.Admins.Contains(mxid)) {
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
        return new {
            SyncStates = SyncController._syncStates
        };
    }
}

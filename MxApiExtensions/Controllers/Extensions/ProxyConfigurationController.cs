using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using MxApiExtensions.Classes.LibMatrix;
using MxApiExtensions.Services;

namespace MxApiExtensions.Controllers.Extensions;

[ApiController]
[Route("/_matrix/client/unstable/gay.rory.mxapiextensions")]
public class ProxyConfigurationController : ControllerBase {
    private readonly ILogger _logger;
    private readonly MxApiExtensionsConfiguration _config;
    private readonly AuthenticationService _authenticationService;

    private static ConcurrentDictionary<string, RoomInfoEntry> _roomInfoCache = new();

    public ProxyConfigurationController(ILogger<ProxyConfigurationController> logger, MxApiExtensionsConfiguration config, AuthenticationService authenticationService,
        AuthenticatedHomeserverProviderService authenticatedHomeserverProviderService) {
        _logger = logger;
        _config = config;
        _authenticationService = authenticationService;
    }

    [HttpGet("proxy_config")]
    public async Task<MxApiExtensionsConfiguration> GetConfig() {
        var mxid = await _authenticationService.GetMxidFromToken();
        if(!_config.Admins.Contains(mxid)) {
            _logger.LogWarning("Got proxy config request for {user}, but they are not an admin", mxid);
            Response.StatusCode = StatusCodes.Status403Forbidden;
            Response.ContentType = "application/json";

            await Response.WriteAsJsonAsync(new {
                ErrorCode = "M_FORBIDDEN",
                Error = "You are not an admin"
            });
            await Response.CompleteAsync();
            return null;
        }

        _logger.LogInformation("Got proxy config request for {user}", mxid);
        return _config;
    }
}

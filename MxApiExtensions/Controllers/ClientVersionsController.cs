using System.Net.Http.Headers;
using LibMatrix.Responses;
using Microsoft.AspNetCore.Mvc;
using MxApiExtensions.Services;

namespace MxApiExtensions.Controllers;

[ApiController]
[Route("/")]
public class ClientVersionsController : ControllerBase {
    private readonly ILogger _logger;
    private readonly AuthenticatedHomeserverProviderService _authenticatedHomeserverProviderService;
    private static Dictionary<string, string> _tokenMap = new();

    public ClientVersionsController(ILogger<ClientVersionsController> logger, MxApiExtensionsConfiguration config, AuthenticationService authenticationService, AuthenticatedHomeserverProviderService authenticatedHomeserverProviderService) {
        _logger = logger;
        _authenticatedHomeserverProviderService = authenticatedHomeserverProviderService;
    }

    [HttpGet("/_matrix/client/versions")]
    public async Task<ClientVersionsResponse> Proxy([FromQuery] string? access_token, string? _) {
        var clientVersions = new ClientVersionsResponse() {
            Versions = new() {
                "r0.0.1",
                "r0.1.0",
                "r0.2.0",
                "r0.3.0",
                "r0.4.0",
                "r0.5.0",
                "r0.6.0",
                "r0.6.1",
                "v1.1",
                "v1.2",
                "v1.3",
                "v1.4",
                "v1.5",
                "v1.6"
            },
            UnstableFeatures = new()
        };
        try {
            var hs = await _authenticatedHomeserverProviderService.GetHomeserver();
            clientVersions = await hs.GetClientVersions();

            _logger.LogInformation("Fetching client versions for {}: {}{}", hs.WhoAmI.UserId, Request.Path, Request.QueryString);
        }
        catch { }

        clientVersions.UnstableFeatures.Add("gay.rory.mxapiextensions.v0", true);
        return clientVersions;
    }
}

using System.Net.Http.Headers;
using ArcaneLibs.Extensions;
using LibMatrix;
using LibMatrix.Extensions;
using LibMatrix.Responses;
using LibMatrix.Services;
using Microsoft.AspNetCore.Mvc;
using MxApiExtensions.Classes.LibMatrix;
using MxApiExtensions.Services;

namespace MxApiExtensions.Controllers;

[ApiController]
[Route("/")]
public class LoginController(ILogger<LoginController> logger, HomeserverProviderService hsProvider, HomeserverResolverService hsResolver, AuthenticationService auth,
        MxApiExtensionsConfiguration conf)
    : ControllerBase {
    private readonly ILogger _logger = logger;
    private readonly HomeserverProviderService _hsProvider = hsProvider;
    private readonly MxApiExtensionsConfiguration _conf = conf;

    [HttpPost("/_matrix/client/{_}/login")]
    public async Task Proxy([FromBody] LoginRequest request, string _) {
        string hsCanonical = null;
        if (Request.Headers.Keys.Any(x => x.ToUpper() == "MXAE_UPSTREAM")) {
            hsCanonical = Request.Headers.GetByCaseInsensitiveKey("MXAE_UPSTREAM")[0]!;
            _logger.LogInformation("Found upstream: {}", hsCanonical);
        }
        else {
            if (!request.Identifier.User.Contains("#")) {
                Response.StatusCode = (int)StatusCodes.Status403Forbidden;
                Response.ContentType = "application/json";
                await Response.StartAsync();
                await Response.WriteAsync(new MxApiMatrixException {
                    ErrorCode = "M_FORBIDDEN",
                    Error = "[MxApiExtensions] Invalid username, must be of the form @user#domain:" + Request.Host.Value
                }.GetAsJson() ?? "");
                await Response.CompleteAsync();
            }

            hsCanonical = request.Identifier.User.Split('#')[1].Split(':')[0];
            request.Identifier.User = request.Identifier.User.Split(':')[0].Replace('#', ':');
            if (!request.Identifier.User.StartsWith('@')) request.Identifier.User = '@' + request.Identifier.User;
        }

        var hs = await hsResolver.ResolveHomeserverFromWellKnown(hsCanonical);
        //var hs = await _hsProvider.Login(hsCanonical, mxid, request.Password);
        var hsClient = new MatrixHttpClient { BaseAddress = new Uri(hs.Client) };
        //hsClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", hsClient.DefaultRequestHeaders.Authorization!.Parameter);
        if (!string.IsNullOrWhiteSpace(request.InitialDeviceDisplayName))
            request.InitialDeviceDisplayName += $" (via MxApiExtensions at {Request.Host.Value})";
        var resp = await hsClient.PostAsJsonAsync("/_matrix/client/r0/login", request);
        var loginResp = await resp.Content.ReadAsStringAsync();
        Response.StatusCode = (int)resp.StatusCode;
        Response.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
        await Response.StartAsync();
        await Response.WriteAsync(loginResp);
        await Response.CompleteAsync();
        var token = (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        await auth.SaveMxidForToken(token, request.Identifier.User);
    }

    [HttpGet("/_matrix/client/{_}/login")]
    public async Task<object> Proxy(string? _) {
        return new {
            flows = new[] {
                new {
                    type = "m.login.password"
                }
            }
        };
    }
}
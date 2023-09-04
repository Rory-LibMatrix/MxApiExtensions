using System.Net.Http.Headers;
using LibMatrix;
using LibMatrix.Extensions;
using LibMatrix.Responses;
using LibMatrix.Services;
using Microsoft.AspNetCore.Mvc;
using MxApiExtensions.Services;

namespace MxApiExtensions.Controllers;

[ApiController]
[Route("/")]
public class LoginController : ControllerBase {
    private readonly ILogger _logger;
    private readonly HomeserverProviderService _hsProvider;
    private readonly HomeserverResolverService _hsResolver;
    private readonly AuthenticationService _auth;
    private readonly MxApiExtensionsConfiguration _conf;

    public LoginController(ILogger<LoginController> logger, HomeserverProviderService hsProvider, HomeserverResolverService hsResolver, AuthenticationService auth, MxApiExtensionsConfiguration conf) {
        _logger = logger;
        _hsProvider = hsProvider;
        _hsResolver = hsResolver;
        _auth = auth;
        _conf = conf;
    }

    [HttpPost("/_matrix/client/{_}/login")]
    public async Task Proxy([FromBody] LoginRequest request, string _) {
        if (!request.Identifier.User.Contains("#")) {
            Response.StatusCode = (int)StatusCodes.Status403Forbidden;
            Response.ContentType = "application/json";
            await Response.StartAsync();
            await Response.WriteAsync(new MxApiMatrixException() {
                ErrorCode = "M_FORBIDDEN",
                Error = "[MxApiExtensions] Invalid username, must be of the form @user#domain:" + Request.Host.Value
            }.GetAsJson() ?? "");
            await Response.CompleteAsync();
        }
        var hsCanonical = request.Identifier.User.Split('#')[1].Split(':')[0];
        request.Identifier.User = request.Identifier.User.Split(':')[0].Replace('#', ':');
        if(!request.Identifier.User.StartsWith('@')) request.Identifier.User = '@' + request.Identifier.User;
        var hs = await _hsResolver.ResolveHomeserverFromWellKnown(hsCanonical);
        //var hs = await _hsProvider.Login(hsCanonical, mxid, request.Password);
        var hsClient = new MatrixHttpClient { BaseAddress = new Uri(hs) };
        //hsClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", hsClient.DefaultRequestHeaders.Authorization!.Parameter);
        var resp = await hsClient.PostAsJsonAsync("/_matrix/client/r0/login", request);
        var loginResp = await resp.Content.ReadAsStringAsync();
        Response.StatusCode = (int)resp.StatusCode;
        Response.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
        await Response.StartAsync();
        await Response.WriteAsync(loginResp);
        await Response.CompleteAsync();
        var token = (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        await _auth.SaveMxidForToken(token, request.Identifier.User);
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

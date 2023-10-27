using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using MxApiExtensions.Classes.LibMatrix;
using MxApiExtensions.Services;

namespace MxApiExtensions.Controllers;

[ApiController]
[Route("/{*_}")]
public class GenericController : ControllerBase {
    private readonly ILogger<GenericController> _logger;
    private readonly MxApiExtensionsConfiguration _config;
    private readonly AuthenticationService _authenticationService;
    private readonly AuthenticatedHomeserverProviderService _authenticatedHomeserverProviderService;
    private static Dictionary<string, string> _tokenMap = new();

    public GenericController(ILogger<GenericController> logger, MxApiExtensionsConfiguration config, AuthenticationService authenticationService,
        AuthenticatedHomeserverProviderService authenticatedHomeserverProviderService) {
        _logger = logger;
        _config = config;
        _authenticationService = authenticationService;
        _authenticatedHomeserverProviderService = authenticatedHomeserverProviderService;
    }

    [HttpGet]
    public async Task Proxy([FromQuery] string? access_token, string? _) {
        try {
            access_token ??= _authenticationService.GetToken(fail: false);
            var mxid = await _authenticationService.GetMxidFromToken(fail: false);
            var hs = await _authenticatedHomeserverProviderService.GetHomeserver();

            _logger.LogInformation("Proxying request for {}: {}{}", mxid, Request.Path, Request.QueryString);

            //remove access_token from query string
            Request.QueryString = new QueryString(
                Request.QueryString.Value?.Replace("&access_token", "access_token")
                    .Replace($"access_token={access_token}", "")
            );

            var resp = await hs.ClientHttpClient.GetAsync($"{Request.Path}{Request.QueryString}");

            if (resp.Content is null) {
                throw new MxApiMatrixException {
                    ErrorCode = "M_UNKNOWN",
                    Error = "No content in response"
                };
            }

            Response.StatusCode = (int)resp.StatusCode;
            Response.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
            await Response.StartAsync();
            await using var stream = await resp.Content.ReadAsStreamAsync();
            await stream.CopyToAsync(Response.Body);
            await Response.Body.FlushAsync();
            await Response.CompleteAsync();
        }
        catch (MxApiMatrixException e) {
            _logger.LogError(e, "Matrix error");
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            Response.ContentType = "application/json";

            await Response.WriteAsync(e.GetAsJson());
            await Response.CompleteAsync();
        }
        catch (Exception e) {
            _logger.LogError(e, "Unhandled error");
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            Response.ContentType = "text/plain";

            await Response.WriteAsync(e.ToString());
            await Response.CompleteAsync();
        }
    }

    [HttpPost]
    public async Task ProxyPost([FromQuery] string? access_token, string _) {
        try {
            access_token ??= _authenticationService.GetToken(fail: false);
            var mxid = await _authenticationService.GetMxidFromToken(fail: false);
            var hs = await _authenticatedHomeserverProviderService.GetHomeserver();

            _logger.LogInformation("Proxying request for {}: {}{}", mxid, Request.Path, Request.QueryString);

            using var hc = new HttpClient();
            hc.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access_token);
            hc.Timeout = TimeSpan.FromMinutes(10);
            //remove access_token from query string
            Request.QueryString = new QueryString(
                Request.QueryString.Value
                    .Replace("&access_token", "access_token")
                    .Replace($"access_token={access_token}", "")
            );

            var resp = await hs.ClientHttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{Request.Path}{Request.QueryString}") {
                Method = HttpMethod.Post,
                Content = new StreamContent(Request.Body)
            });

            if (resp.Content is null) {
                throw new MxApiMatrixException {
                    ErrorCode = "M_UNKNOWN",
                    Error = "No content in response"
                };
            }

            Response.StatusCode = (int)resp.StatusCode;
            Response.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
            await Response.StartAsync();
            await using var stream = await resp.Content.ReadAsStreamAsync();
            await stream.CopyToAsync(Response.Body);
            await Response.Body.FlushAsync();
            await Response.CompleteAsync();
        }
        catch (MxApiMatrixException e) {
            _logger.LogError(e, "Matrix error");
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            Response.ContentType = "application/json";

            await Response.WriteAsJsonAsync(e.GetAsJson());
            await Response.CompleteAsync();
        }
        catch (Exception e) {
            _logger.LogError(e, "Unhandled error");
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            Response.ContentType = "text/plain";

            await Response.WriteAsync(e.ToString());
            await Response.CompleteAsync();
        }
    }

    [HttpPut]
    public async Task ProxyPut([FromQuery] string? access_token, string _) {
        try {
            access_token ??= _authenticationService.GetToken(fail: false);
            var mxid = await _authenticationService.GetMxidFromToken(fail: false);
            var hs = await _authenticatedHomeserverProviderService.GetHomeserver();

            _logger.LogInformation("Proxying request for {}: {}{}", mxid, Request.Path, Request.QueryString);

            using var hc = new HttpClient();
            hc.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access_token);
            hc.Timeout = TimeSpan.FromMinutes(10);
            //remove access_token from query string
            Request.QueryString = new QueryString(
                Request.QueryString.Value
                    .Replace("&access_token", "access_token")
                    .Replace($"access_token={access_token}", "")
            );

            var resp = await hs.ClientHttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Put, $"{Request.Path}{Request.QueryString}") {
                Method = HttpMethod.Put,
                Content = new StreamContent(Request.Body)
            });

            if (resp.Content is null) {
                throw new MxApiMatrixException {
                    ErrorCode = "M_UNKNOWN",
                    Error = "No content in response"
                };
            }

            Response.StatusCode = (int)resp.StatusCode;
            Response.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
            await Response.StartAsync();
            await using var stream = await resp.Content.ReadAsStreamAsync();
            await stream.CopyToAsync(Response.Body);
            await Response.Body.FlushAsync();
            await Response.CompleteAsync();
        }
        catch (MxApiMatrixException e) {
            _logger.LogError(e, "Matrix error");
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            Response.ContentType = "application/json";

            await Response.WriteAsJsonAsync(e.GetAsJson());
            await Response.CompleteAsync();
        }
        catch (Exception e) {
            _logger.LogError(e, "Unhandled error");
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            Response.ContentType = "text/plain";

            await Response.WriteAsync(e.ToString());
            await Response.CompleteAsync();
        }
    }
}

using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;

namespace MxApiExtensions.Controllers;

[ApiController]
[Route("/")]
public class GenericController : ControllerBase {
    private readonly ILogger<GenericController> _logger;
    private readonly CacheConfiguration _config;
    private readonly Auth _auth;
    private static Dictionary<string, string> _tokenMap = new();

    public GenericController(ILogger<GenericController> logger, CacheConfiguration config, Auth auth) {
        _logger = logger;
        _config = config;
        _auth = auth;
    }

    [HttpGet("{*_}")]
    public async Task Proxy([FromQuery] string? access_token, string _) {
        try {
            access_token ??= _auth.GetToken(fail: false);
            var mxid = _auth.GetUserId(fail: false);

            _logger.LogInformation($"Proxying request for {mxid}: {Request.Path}{Request.QueryString}");

            using var hc = new HttpClient();
            hc.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access_token);
            hc.Timeout = TimeSpan.FromMinutes(10);
            //remove access_token from query string
            Request.QueryString = new QueryString(
                Request.QueryString.Value
                    .Replace("&access_token", "access_token")
                    .Replace($"access_token={access_token}", "")
            );

            var resp = await hc.GetAsync($"{_config.Homeserver}{Request.Path}{Request.QueryString}");

            if (resp.Content is null) {
                throw new MatrixException {
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
        catch (MatrixException e) {
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

    [HttpPost("{*_}")]
    public async Task ProxyPost([FromQuery] string? access_token, string _) {
        try {
            access_token ??= _auth.GetToken(fail: false);
            var mxid = _auth.GetUserId(fail: false);

            _logger.LogInformation($"Proxying request for {mxid}: {Request.Path}{Request.QueryString}");

            using var hc = new HttpClient();
            hc.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access_token);
            hc.Timeout = TimeSpan.FromMinutes(10);
            //remove access_token from query string
            Request.QueryString = new QueryString(
                Request.QueryString.Value
                    .Replace("&access_token", "access_token")
                    .Replace($"access_token={access_token}", "")
            );

            var resp = await hc.SendAsync(new() {
                Method = HttpMethod.Post,
                RequestUri = new Uri($"{_config.Homeserver}{Request.Path}{Request.QueryString}"),
                Content = new StreamContent(Request.Body),
            });

            if (resp.Content is null) {
                throw new MatrixException {
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
        catch (MatrixException e) {
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

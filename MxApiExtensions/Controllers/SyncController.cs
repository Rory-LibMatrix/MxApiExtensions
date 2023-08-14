using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;

namespace MxApiExtensions.Controllers;

[ApiController]
[Route("/")]
public class SyncController : ControllerBase {
    private readonly ILogger<SyncController> _logger;
    private readonly CacheConfiguration _config;
    private readonly Auth _auth;

    public SyncController(ILogger<SyncController> logger, CacheConfiguration config, Auth auth) {
        _logger = logger;
        _config = config;
        _auth = auth;
    }

    [HttpGet("/_matrix/client/v3/sync")]
    public async Task Sync([FromQuery] string? since, [FromQuery] string? access_token) {
        try {
            access_token ??= _auth.GetToken();
            var mxid = _auth.GetUserId();
            var cacheFile = GetFilePath(mxid, since);

            if (!await TrySendCached(cacheFile)) {
                using var hc = new HttpClient();
                hc.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access_token);
                hc.Timeout = TimeSpan.FromMinutes(10);
                //remove access_token from query string
                Request.QueryString = new QueryString(
                    Request.QueryString.Value
                        .Replace("&access_token", "access_token")
                        .Replace($"access_token={access_token}", "")
                );

                var resp = hc.GetAsync($"{_config.Homeserver}{Request.Path}{Request.QueryString}").Result;
                // var resp = await hs._httpClient.GetAsync($"/_matrix/client/v3/sync?since={since}");

                if (resp.Content is null) {
                    throw new MatrixException() {
                        ErrorCode = "M_UNKNOWN",
                        Error = "No content in response"
                    };
                }

                Response.StatusCode = (int)resp.StatusCode;
                Response.ContentType = "application/json";
                await Response.StartAsync();
                await using var stream = await resp.Content.ReadAsStreamAsync();
                await using var target = System.IO.File.OpenWrite(cacheFile);
                byte[] buffer = new byte[1];

                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
                    await Response.Body.WriteAsync(buffer.AsMemory(0, bytesRead));
                    target.Write(buffer, 0, bytesRead);
                }

                await target.FlushAsync();
                await Response.CompleteAsync();
            }
        }
        catch (MatrixException e) {
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            Response.ContentType = "application/json";

            await Response.WriteAsJsonAsync(e.GetAsJson());
            await Response.CompleteAsync();
        }
        catch (Exception e) {
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            Response.ContentType = "text/plain";

            await Response.WriteAsync(e.ToString());
            await Response.CompleteAsync();
        }
    }

    private async Task<bool> TrySendCached(string cacheFile) {
        if (System.IO.File.Exists(cacheFile)) {
            Response.StatusCode = 200;
            Response.ContentType = "application/json";
            await Response.StartAsync();
            await using var stream = System.IO.File.OpenRead(cacheFile);
            await stream.CopyToAsync(Response.Body);
            await Response.CompleteAsync();
            return true;
        }

        return false;
    }

#region Cache management

    public string GetFilePath(string mxid, string since) {
        var cacheDir = Path.Join("cache", mxid);
        Directory.CreateDirectory(cacheDir);
        var cacheFile = Path.Join(cacheDir, $"sync-{since}.json");
        if (!Path.GetFullPath(cacheFile).StartsWith(Path.GetFullPath(cacheDir))) {
            throw new MatrixException() {
                ErrorCode = "M_UNKNOWN",
                Error = "[Rory&::MxSyncCache] Cache file path is not in cache directory"
            };
        }

        return cacheFile;
    }

#endregion
}

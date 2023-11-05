using System.Net.Http.Headers;
using LibMatrix.Homeservers;
using LibMatrix.Services;
using Microsoft.AspNetCore.Mvc;
using MxApiExtensions.Classes.LibMatrix;
using MxApiExtensions.Services;

namespace MxApiExtensions.Controllers;

[ApiController]
[Route("/")]
public class MediaProxyController(ILogger<GenericController> logger, MxApiExtensionsConfiguration config, AuthenticationService authenticationService,
        AuthenticatedHomeserverProviderService authenticatedHomeserverProviderService, HomeserverProviderService hsProvider)
    : ControllerBase {
    private class MediaCacheEntry {
        public DateTime LastRequested { get; set; } = DateTime.Now;
        public byte[] Data { get; set; }
        public string ContentType { get; set; }
        public long Size => Data.LongCount();
    }

    private static Dictionary<string, MediaCacheEntry> _mediaCache = new();
    private static SemaphoreSlim _semaphore = new(1, 1);
    
    [HttpGet("/_matrix/media/{_}/download/{serverName}/{mediaId}")]
    public async Task Proxy(string? _, string serverName, string mediaId) {
        try {
            logger.LogInformation("Proxying media: {}{}", serverName, mediaId);

            await _semaphore.WaitAsync();
            MediaCacheEntry entry;
            if (!_mediaCache.ContainsKey($"{serverName}/{mediaId}")) {
                _mediaCache.Add($"{serverName}/{mediaId}", entry = new());
                List<RemoteHomeserver> FeasibleHomeservers = new();
                {
                    var a = await authenticatedHomeserverProviderService.TryGetRemoteHomeserver();
                    if(a is not null)
                        FeasibleHomeservers.Add(a);
                }
                
                FeasibleHomeservers.Add(await hsProvider.GetRemoteHomeserver(serverName));
                
                foreach (var homeserver in FeasibleHomeservers) {
                    var resp = await homeserver.ClientHttpClient.GetAsync($"{Request.Path}");
                    if(!resp.IsSuccessStatusCode) continue;
                    entry.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
                    entry.Data = await resp.Content.ReadAsByteArrayAsync();
                    break;
                }
            }
            else entry = _mediaCache[$"{serverName}/{mediaId}"];
            _semaphore.Release();
            
            Response.StatusCode = 200;
            Response.ContentType = entry.ContentType;
            await Response.StartAsync();
            await Response.Body.WriteAsync(entry.Data, 0, entry.Data.Length);
            await Response.Body.FlushAsync();
            await Response.CompleteAsync();
        }
        catch (MxApiMatrixException e) {
            logger.LogError(e, "Matrix error");
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            Response.ContentType = "application/json";

            await Response.WriteAsync(e.GetAsJson());
            await Response.CompleteAsync();
        }
        catch (Exception e) {
            logger.LogError(e, "Unhandled error");
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            Response.ContentType = "text/plain";

            await Response.WriteAsync(e.ToString());
            await Response.CompleteAsync();
        }
    }
}

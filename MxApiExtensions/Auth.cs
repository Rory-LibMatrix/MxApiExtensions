using System.Net.Http.Headers;
using System.Text.Json;
using MatrixRoomUtils.Core.Extensions;

namespace MxApiExtensions;

public class Auth {
    private readonly ILogger<Auth> _logger;
    private readonly CacheConfiguration _config;
    private readonly HttpRequest _request;

    private static Dictionary<string, string> _tokenMap = new();

    public Auth(ILogger<Auth> logger, CacheConfiguration config, IHttpContextAccessor request) {
        _logger = logger;
        _config = config;
        _request = request.HttpContext.Request;
    }

    internal string? GetToken(bool fail = true) {
        string? token;
        if (_request.Headers.TryGetValue("Authorization", out var tokens)) {
            token = tokens.FirstOrDefault()?[7..];
        }
        else {
            token = _request.Query["access_token"];
        }

        if (token == null && fail) {
            throw new MatrixException() {
                ErrorCode = "M_MISSING_TOKEN",
                Error = "Missing access token"
            };
        }

        return token;
    }

    public string GetUserId(bool fail = true) {
        var token = GetToken(fail);
        if (token == null) {
            if(fail) {
                throw new MatrixException() {
                    ErrorCode = "M_MISSING_TOKEN",
                    Error = "Missing access token"
                };
            }
            return "@anonymous:*";
        }
        try {
            return _tokenMap.GetOrCreate(token, GetMxidFromToken);
        }
        catch {
            return GetUserId();
        }
    }

    private string GetMxidFromToken(string token) {
        using var hc = new HttpClient();
        hc.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = hc.GetAsync($"{_config.Homeserver}/_matrix/client/v3/account/whoami").Result;
        if (!resp.IsSuccessStatusCode) {
            throw new MatrixException() {
                ErrorCode = "M_UNKNOWN",
                Error = "[Rory&::MxSyncCache] Whoami request failed"
            };
        }

        if (resp.Content is null) {
            throw new MatrixException() {
                ErrorCode = "M_UNKNOWN",
                Error = "No content in response"
            };
        }

        var json = JsonDocument.Parse(resp.Content.ReadAsStream()).RootElement;
        var mxid = json.GetProperty("user_id").GetString()!;
        _logger.LogInformation($"Got mxid {mxid} from token {token}");
        return mxid;
    }

}

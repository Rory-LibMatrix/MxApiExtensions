using ArcaneLibs.Extensions;
using LibMatrix;
using LibMatrix.Services;
using MxApiExtensions.Classes.LibMatrix;

namespace MxApiExtensions.Services;

public class AuthenticationService(ILogger<AuthenticationService> logger, MxApiExtensionsConfiguration config, IHttpContextAccessor request, HomeserverProviderService homeserverProviderService) {
    private readonly HttpRequest _request = request.HttpContext!.Request;

    private static Dictionary<string, string> _tokenMap = new();

    internal string? GetToken(bool fail = true) {
        string? token;
        if (_request.Headers.TryGetValue("Authorization", out var tokens)) {
            token = tokens.FirstOrDefault()?[7..];
        }
        else {
            token = _request.Query["access_token"];
        }

        if (string.IsNullOrWhiteSpace(token) && fail) {
            throw new MxApiMatrixException {
                ErrorCode = "M_MISSING_TOKEN",
                Error = "Missing access token"
            };
        }

        return token;
    }

    public async Task<string> GetMxidFromToken(string? token = null, bool fail = true) {
        token ??= GetToken(fail);
        if (string.IsNullOrWhiteSpace(token)) {
            if (fail) {
                throw new MxApiMatrixException {
                    ErrorCode = "M_MISSING_TOKEN",
                    Error = "Missing access token"
                };
            }

            return "@anonymous:*";
        }

        if(_tokenMap is not { Count: >0 } && File.Exists("token_map")) {
            _tokenMap = (await File.ReadAllLinesAsync("token_map"))
                .Select(l => l.Split('\t'))
                .ToDictionary(l => l[0], l => l[1]);
            
            //THIS IS BROKEN, DO NOT USE!
            // foreach (var (mapToken, mapUser) in _tokenMap) {
            //     try {
            //         var hs = await homeserverProviderService.GetAuthenticatedWithToken(mapUser.Split(':', 2)[1], mapToken);
            //     }
            //     catch (MatrixException e) {
            //         if (e is { ErrorCode: "M_UNKNOWN_TOKEN" }) _tokenMap[mapToken] = "";
            //     }
            //     catch {
            //         // ignored
            //     }
            // }
            // _tokenMap.RemoveAll((x, y) => string.IsNullOrWhiteSpace(y));
            // await File.WriteAllTextAsync("token_map", _tokenMap.Aggregate("", (x, y) => $"{y.Key}\t{y.Value}\n"));
        }
        

        if (_tokenMap.TryGetValue(token, out var mxid)) return mxid;

        var lookupTasks = new Dictionary<string, Task<string?>>();
        foreach (var homeserver in config.AuthHomeservers) {
            try {
                lookupTasks.Add(homeserver, GetMxidFromToken(token, homeserver));
                await lookupTasks[homeserver].WaitAsync(TimeSpan.FromMilliseconds(500));
                if (lookupTasks[homeserver].IsCompletedSuccessfully && !string.IsNullOrWhiteSpace(lookupTasks[homeserver].Result)) break;
            }
            catch {}
        }
        await Task.WhenAll(lookupTasks.Values);

        mxid = lookupTasks.Values.FirstOrDefault(x => x.Result != null)?.Result;
        if(mxid is null) {
            throw new MxApiMatrixException {
                ErrorCode = "M_UNKNOWN_TOKEN",
                Error = "Token not found on any configured homeservers: " + string.Join(", ", config.AuthHomeservers)
            };
        }

        // using var hc = new HttpClient();
        // hc.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // var resp = hc.GetAsync($"{_config.Homeserver}/_matrix/client/v3/account/whoami").Result;
        // if (!resp.IsSuccessStatusCode) {
        //     throw new MatrixException {
        //         ErrorCode = "M_UNKNOWN",
        //         Error = "[Rory&::MxSyncCache] Whoami request failed"
        //     };
        // }
        //
        // if (resp.Content is null) {
        //     throw new MatrixException {
        //         ErrorCode = "M_UNKNOWN",
        //         Error = "No content in response"
        //     };
        // }
        //
        // var json = (await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync())).RootElement;
        // var mxid = json.GetProperty("user_id").GetString()!;
        logger.LogInformation("Got mxid {} from token {}", mxid, token);
        await SaveMxidForToken(token, mxid);
        return mxid;
    }

    private async Task<string?> GetMxidFromToken(string token, string hsDomain) {
        logger.LogInformation("Looking up mxid for token {} on {}", token, hsDomain);
        var hs = await homeserverProviderService.GetAuthenticatedWithToken(hsDomain, token);
        try {
            var res = hs.WhoAmI.UserId;
            logger.LogInformation("Got mxid {} for token {} on {}", res, token, hsDomain);
            return res;
        }
        catch (MxApiMatrixException e) {
            if (e.ErrorCode == "M_UNKNOWN_TOKEN") {
                return null;
            }

            throw;
        }
    }

    public async Task SaveMxidForToken(string token, string mxid) {
        _tokenMap.Add(token, mxid);
        await File.AppendAllLinesAsync("token_map", new[] { $"{token}\t{mxid}" });
    }
}

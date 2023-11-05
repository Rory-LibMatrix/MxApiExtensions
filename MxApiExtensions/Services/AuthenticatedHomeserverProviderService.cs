using ArcaneLibs.Extensions;
using LibMatrix.Homeservers;
using LibMatrix.Services;
using MxApiExtensions.Classes.LibMatrix;

namespace MxApiExtensions.Services;

public class AuthenticatedHomeserverProviderService(AuthenticationService authenticationService, HomeserverProviderService homeserverProviderService, IHttpContextAccessor request) {
    public async Task<RemoteHomeserver?> TryGetRemoteHomeserver() {
        try {
            return await GetRemoteHomeserver();
        }
        catch {
            return null;
        }
    }
    
    public async Task<RemoteHomeserver> GetRemoteHomeserver() {
        try {
            return await GetHomeserver();
        }
        catch (MxApiMatrixException e) {
            if (e is not { ErrorCode: "M_MISSING_TOKEN" }) throw;
            if (!request.HttpContext!.Request.Headers.Keys.Any(x=>x.ToUpper() == "MXAE_UPSTREAM"))
                throw new MxApiMatrixException() {
                    ErrorCode = "MXAE_MISSING_UPSTREAM",
                    Error = "[MxApiExtensions] Missing MXAE_UPSTREAM header for unauthenticated request, this should be a server_name!"
                };
            return await homeserverProviderService.GetRemoteHomeserver(request.HttpContext.Request.Headers.GetByCaseInsensitiveKey("MXAE_UPSTREAM")[0]);
        }
    }

    public async Task<AuthenticatedHomeserverGeneric> GetHomeserver() {
        var token = authenticationService.GetToken();
        if (token == null) {
            throw new MxApiMatrixException {
                ErrorCode = "M_MISSING_TOKEN",
                Error = "Missing access token"
            };
        }

        var mxid = await authenticationService.GetMxidFromToken(token);
        if (mxid == "@anonymous:*") {
            throw new MxApiMatrixException {
                ErrorCode = "M_MISSING_TOKEN",
                Error = "Missing access token"
            };
        }

        var hsCanonical = string.Join(":", mxid.Split(':').Skip(1));
        return await homeserverProviderService.GetAuthenticatedWithToken(hsCanonical, token);
    }
}

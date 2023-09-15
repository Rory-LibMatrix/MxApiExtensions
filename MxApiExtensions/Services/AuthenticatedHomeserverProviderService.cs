using LibMatrix.Homeservers;
using LibMatrix.Services;
using MxApiExtensions.Classes.LibMatrix;

namespace MxApiExtensions.Services;

public class AuthenticatedHomeserverProviderService {
    private readonly AuthenticationService _authenticationService;
    private readonly HomeserverProviderService _homeserverProviderService;

    public AuthenticatedHomeserverProviderService(AuthenticationService authenticationService, HomeserverProviderService homeserverProviderService) {
        _authenticationService = authenticationService;
        _homeserverProviderService = homeserverProviderService;
    }

    public async Task<AuthenticatedHomeserverGeneric> GetHomeserver() {
        var token = _authenticationService.GetToken();
        if (token == null) {
            throw new MxApiMatrixException {
                ErrorCode = "M_MISSING_TOKEN",
                Error = "Missing access token"
            };
        }

        var mxid = await _authenticationService.GetMxidFromToken(token);
        if (mxid == "@anonymous:*") {
            throw new MxApiMatrixException {
                ErrorCode = "M_MISSING_TOKEN",
                Error = "Missing access token"
            };
        }

        var hsCanonical = string.Join(":", mxid.Split(':').Skip(1));
        return await _homeserverProviderService.GetAuthenticatedWithToken(hsCanonical, token);
    }
}

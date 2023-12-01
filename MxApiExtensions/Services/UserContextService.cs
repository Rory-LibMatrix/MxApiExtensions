using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using ArcaneLibs.Extensions;
using LibMatrix;
using LibMatrix.Homeservers;
using MxApiExtensions.Classes;

namespace MxApiExtensions.Services;

public class UserContextService(MxApiExtensionsConfiguration config, AuthenticatedHomeserverProviderService hsProvider) {
    internal static ConcurrentDictionary<string, UserContext> UserContextStore { get; set; } = new();
    public readonly int SessionCount = UserContextStore.Count;

    public class UserContext {
        public SyncState? SyncState { get; set; }
        [JsonIgnore]
        public AuthenticatedHomeserverGeneric Homeserver { get; set; }
        public MxApiExtensionsUserConfiguration UserConfiguration { get; set; }
    }

    private readonly SemaphoreSlim _getUserContextSemaphore = new SemaphoreSlim(1, 1);
    public async Task<UserContext> GetCurrentUserContext() {
        var hs = await hsProvider.GetHomeserver();
        // await _getUserContextSemaphore.WaitAsync();
        var ucs = await UserContextStore.GetOrCreateAsync($"{hs.WhoAmI.UserId}/{hs.WhoAmI.DeviceId}/{hs.ServerName}:{hs.AccessToken}", async x => {
            var userContext = new UserContext() {
                Homeserver = hs
            };
            try {
                userContext.UserConfiguration = await hs.GetAccountDataAsync<MxApiExtensionsUserConfiguration>(MxApiExtensionsUserConfiguration.EventId);
            }
            catch (MatrixException e) {
                if (e is not { ErrorCode: "M_NOT_FOUND" }) throw;
                userContext.UserConfiguration = config.DefaultUserConfiguration;
            }

            await hs.SetAccountDataAsync(MxApiExtensionsUserConfiguration.EventId, userContext.UserConfiguration);

            return userContext;
        }, _getUserContextSemaphore);
        // _getUserContextSemaphore.Release();
        return ucs;
    }
}

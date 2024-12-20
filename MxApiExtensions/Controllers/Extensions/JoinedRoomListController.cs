using System.Collections.Concurrent;
using System.Net.Http.Headers;
using ArcaneLibs.Extensions;
using LibMatrix.EventTypes.Spec.State;
using LibMatrix.Homeservers;
using LibMatrix.RoomTypes;
using Microsoft.AspNetCore.Mvc;
using MxApiExtensions.Classes.LibMatrix;
using MxApiExtensions.Services;

namespace MxApiExtensions.Controllers.Extensions;

[ApiController]
[Route("/_matrix/client/unstable/gay.rory.mxapiextensions")]
public class JoinedRoomListController : ControllerBase {
    private static ILogger _logger;
    private static MxApiExtensionsConfiguration _config;
    private readonly AuthenticationService _authenticationService;
    private readonly AuthenticatedHomeserverProviderService _authenticatedHomeserverProviderService;

    private static ConcurrentDictionary<string, RoomInfoEntry> _roomInfoCache = new();

    public JoinedRoomListController(ILogger<JoinedRoomListController> logger, MxApiExtensionsConfiguration config, AuthenticationService authenticationService,
        AuthenticatedHomeserverProviderService authenticatedHomeserverProviderService) {
        _logger = logger;
        _config = config;
        _authenticationService = authenticationService;
        _authenticatedHomeserverProviderService = authenticatedHomeserverProviderService;
    }

    [HttpGet("joined_rooms_with_info")]
    public async IAsyncEnumerable<RoomInfoEntry> GetJoinedRooms([FromQuery] string? access_token) {
        List<GenericRoom> rooms = new();
        AuthenticatedHomeserverGeneric? hs = null;
        try {
            hs = await _authenticatedHomeserverProviderService.GetHomeserver();
            _logger.LogInformation("Got room list with info request for {user} ({hs})", hs.UserId, hs.ServerName);
            rooms = await hs.GetJoinedRooms();
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

            await Response.WriteAsJsonAsync(e.ToString());
            await Response.CompleteAsync();
        }

        if (hs is not null) {
            Response.ContentType = "application/json";
            Response.Headers.Append("Cache-Control", "public, max-age=60");
            Response.Headers.Append("Expires", DateTime.Now.AddMinutes(1).ToString("R"));
            Response.Headers.Append("Last-Modified", DateTime.Now.ToString("R"));
            Response.Headers.Append("X-Matrix-Server", hs.ServerName);
            Response.Headers.Append("X-Matrix-User", hs.UserId);
            // await Response.StartAsync();

            var cachedRooms = _roomInfoCache
                .Where(cr => rooms.Any(r => r.RoomId == cr.Key) && cr.Value.ExpiresAt > DateTime.Now)
                .ToList();
            rooms.RemoveAll(r => cachedRooms.Any(cr => cr.Key == r.RoomId));

            foreach (var room in cachedRooms) {
                yield return room.Value;
                _logger.LogInformation("Sent cached room info for {room} for {user} ({hs})", room.Key, hs.UserId, hs.ServerName);
            }

            var tasks = rooms.Select(r => GetRoomInfo(hs, r.RoomId)).ToAsyncEnumerable();

            await foreach (var result in tasks) {
                yield return result;
                _logger.LogInformation("Sent room info for {room} for {user} ({hs})", result.RoomId, hs.UserId, hs.ServerName);
            }
        }
    }

    private async Task<RoomInfoEntry> GetRoomInfo(AuthenticatedHomeserverGeneric hs, string roomId) {
        _logger.LogInformation("Getting room info for {room} for {user} ({hs})", roomId, hs.UserId, hs.ServerName);
        var room = hs.GetRoom(roomId);
        var state = room.GetFullStateAsync();
        var result = new RoomInfoEntry {
            RoomId = roomId,
            RoomState = new(),
            MemberCounts = new(),
            StateCount = 0,
            ExpiresAt = DateTime.Now.AddMinutes(5)
        };

        await foreach (var @event in state) {
            // result.ExpiresAt = result.ExpiresAt.AddMilliseconds(100);
            result.StateCount++;
            if (@event.Type != "m.room.member") result.RoomState.Add(@event);
            else {
                if (!result.MemberCounts.ContainsKey((@event.TypedContent as RoomMemberEventContent)?.Membership)) result.MemberCounts.Add((@event.TypedContent as RoomMemberEventContent)?.Membership, 0);
                result.MemberCounts[(@event.TypedContent as RoomMemberEventContent)?.Membership]++;
            }
        }

        result.ExpiresAt = result.ExpiresAt.AddMilliseconds(100 * result.StateCount);

        _logger.LogInformation("Got room info for {room} for {user} ({hs})", roomId, hs.UserId, hs.ServerName);
        while (!_roomInfoCache.TryAdd(roomId, result)) {
            _logger.LogWarning("Failed to add room info for {room} to cache, retrying...", roomId);
            await Task.Delay(100);
            if (_roomInfoCache.ContainsKey(roomId)) break;
        }

        return result;
    }

    [HttpGet("joined_rooms_with_info_cache")]
    public async Task<object> GetRoomInfoCache() {
        var mxid = await _authenticationService.GetMxidFromToken();
        if (!_config.Admins.Contains(mxid)) {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            Response.ContentType = "application/json";

            await Response.WriteAsJsonAsync(new {
                ErrorCode = "M_FORBIDDEN",
                Error = "You are not an admin"
            });
            await Response.CompleteAsync();
            return null;
        }

        return _roomInfoCache.Select(x => new {
            x.Key,
            x.Value.ExpiresAt,
            ExpiresIn = x.Value.ExpiresAt - DateTime.Now,
            x.Value.MemberCounts,
            x.Value.StateCount
        }).OrderByDescending(x => x.ExpiresAt);
    }
}

using LibMatrix;
using LibMatrix.Services;
using Microsoft.AspNetCore.Mvc;
using MxApiExtensions.Services;

namespace MxApiExtensions.Controllers.Client.Room;

[ApiController]
[Route("/")]
public class RoomController(ILogger<LoginController> logger, HomeserverResolverService hsResolver, AuthenticationService auth, MxApiExtensionsConfiguration conf,
        AuthenticatedHomeserverProviderService hsProvider)
    : ControllerBase {
    [HttpGet("/_matrix/client/{_}/rooms/{roomId}/members_by_homeserver")]
    public async Task<Dictionary<string, List<string>>> GetRoomMembersByHomeserver(string _, [FromRoute] string roomId, [FromQuery] bool joinedOnly = true) {
        var hs = await hsProvider.GetHomeserver();
        var room = hs.GetRoom(roomId);
        return await room.GetMembersByHomeserverAsync(joinedOnly);
    }

    /// <summary>
    /// Fetches up to <paramref name="limit"/> timeline events
    /// </summary>
    /// <param name="_"></param>
    /// <param name="roomId"></param>
    /// <param name="from"></param>
    /// <param name="limit"></param>
    /// <param name="dir"></param>
    /// <param name="filter"></param>
    /// <param name="includeState"></param>
    /// <param name="fixForward">Reverse load all messages and reverse on API side, fixes history starting at join event</param>
    /// <returns></returns>
    [HttpGet("/_matrix/client/{_}/rooms/{roomId}/mass_messages")]
    public async IAsyncEnumerable<StateEventResponse> RedactUser(string _, [FromRoute] string roomId, [FromQuery(Name = "from")] string from = "",
        [FromQuery(Name = "limit")] int limit = 100, [FromQuery(Name = "dir")] string dir = "b", [FromQuery(Name = "filter")] string filter = "",
        [FromQuery(Name = "include_state")] bool includeState = true, [FromQuery(Name = "fix_forward")] bool fixForward = false) {
        var hs = await hsProvider.GetHomeserver();
        var room = hs.GetRoom(roomId);
        var msgs = room.GetManyMessagesAsync(from: from, limit: limit, dir: dir, filter: filter, includeState: includeState, fixForward: fixForward);
        await foreach (var resp in msgs) {
            Console.WriteLine($"GetMany messages returned {resp.Chunk.Count} timeline events and {resp.State.Count} state events, end={resp.End}");
            foreach (var timelineEvent in resp.Chunk) {
                yield return timelineEvent;
            }

            if (includeState)
                foreach (var timelineEvent in resp.State) {
                    yield return timelineEvent;
                }
        }
    }
}

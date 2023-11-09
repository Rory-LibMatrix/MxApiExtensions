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
}
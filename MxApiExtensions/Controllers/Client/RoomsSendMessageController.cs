using System.Buffers.Text;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArcaneLibs.Extensions;
using LibMatrix;
using LibMatrix.EventTypes.Spec;
using LibMatrix.Extensions;
using LibMatrix.Helpers;
using LibMatrix.Homeservers;
using LibMatrix.Responses;
using LibMatrix.Services;
using Microsoft.AspNetCore.Mvc;
using MxApiExtensions.Classes;
using MxApiExtensions.Classes.LibMatrix;
using MxApiExtensions.Services;

namespace MxApiExtensions.Controllers;

[ApiController]
[Route("/")]
public class RoomsSendMessageController(ILogger<LoginController> logger, HomeserverResolverService hsResolver, AuthenticationService auth, MxApiExtensionsConfiguration conf,
        AuthenticatedHomeserverProviderService hsProvider)
    : ControllerBase {
    [HttpPut("/_matrix/client/{_}/rooms/{roomId}/send/m.room.message/{txnId}")]
    public async Task Proxy([FromBody] JsonObject request, [FromRoute] string roomId, [FromRoute] string txnId, string _) {
        var hs = await hsProvider.GetHomeserver();

        var msg = request.Deserialize<RoomMessageEventContent>();
        if (msg is not null && msg.Body.StartsWith("mxae!")) {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            handleMxaeCommand(hs, roomId, msg);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            await Response.WriteAsJsonAsync(new EventIdResponse() {
                EventId = "$" + string.Join("", Random.Shared.GetItems("abcdefghijklmnopqrstuvwxyzABCDEFGHIJLKMNOPQRSTUVWXYZ0123456789".ToCharArray(), 100))
            });
            await Response.CompleteAsync();
        }
        else {
            try {
                var resp = await hs.ClientHttpClient.PutAsJsonAsync($"{Request.Path}{Request.QueryString}", request);
                var loginResp = await resp.Content.ReadAsStringAsync();
                Response.StatusCode = (int)resp.StatusCode;
                Response.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
                await Response.StartAsync();
                await Response.WriteAsync(loginResp);
                await Response.CompleteAsync();
            }
            catch (MatrixException e) {
                await Response.StartAsync();
                await Response.WriteAsync(e.GetAsJson());
                await Response.CompleteAsync();
            }
        }
    }

    private async Task handleMxaeCommand(AuthenticatedHomeserverGeneric hs, string roomId, RoomMessageEventContent msg) {
        var syncState = SyncController._syncStates.GetValueOrDefault(hs.AccessToken);
        if (syncState is null) return;
        syncState.SendEphemeralTimelineEventInRoom(roomId, new() {
            Sender = "@mxae:" + Request.Host.Value,
            Type = "m.room.message",
            TypedContent = MessageFormatter.FormatSuccess("Thinking..."),
            OriginServerTs = (ulong)new DateTimeOffset(DateTime.UtcNow.ToUniversalTime()).ToUnixTimeMilliseconds(),
            Unsigned = new() {
                Age = 1
            },
            RoomId = roomId,
            EventId = "$" + string.Join("", Random.Shared.GetItems("abcdefghijklmnopqrstuvwxyzABCDEFGHIJLKMNOPQRSTUVWXYZ0123456789".ToCharArray(), 100))
        });
    }
}
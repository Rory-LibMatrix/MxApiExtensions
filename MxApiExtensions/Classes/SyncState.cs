using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using LibMatrix;
using LibMatrix.EventTypes.Spec.Ephemeral;
using LibMatrix.EventTypes.Spec.State;
using LibMatrix.Helpers;
using LibMatrix.Homeservers;
using LibMatrix.Responses;
using Microsoft.OpenApi.Extensions;

namespace MxApiExtensions.Classes;

public class SyncState {
    private Task<HttpResponseMessage>? _nextSyncResponse;
    public string? NextBatch { get; set; }
    public ConcurrentQueue<SyncResponse> SyncQueue { get; set; } = new();

    [JsonIgnore]
    public Task<HttpResponseMessage>? NextSyncResponse {
        get => _nextSyncResponse;
        set {
            _nextSyncResponse = value;
            NextSyncResponseStartedAt = DateTime.Now;
        }
    }

    public DateTime NextSyncResponseStartedAt { get; set; } = DateTime.Now;

    [JsonIgnore]
    public AuthenticatedHomeserverGeneric Homeserver { get; set; }

    #region Debug stuff

    public object NextSyncResponseTaskInfo => new {
        NextSyncResponse?.Id,
        NextSyncResponse?.IsCompleted,
        NextSyncResponse?.IsCompletedSuccessfully,
        NextSyncResponse?.IsCanceled,
        NextSyncResponse?.IsFaulted,
        Status = NextSyncResponse?.Status.GetDisplayName()
    };

    #endregion

    public SyncResponse SendEphemeralTimelineEventInRoom(string roomId, StateEventResponse @event, SyncResponse? existingResponse = null) {
        if (existingResponse is null)
            SyncQueue.Enqueue(existingResponse = new());
        existingResponse.Rooms ??= new();
        existingResponse.Rooms.Join ??= new();
        existingResponse.Rooms.Join.TryAdd(roomId, new());
        existingResponse.Rooms.Join[roomId].Timeline ??= new();
        existingResponse.Rooms.Join[roomId].Timeline.Events ??= new();
        existingResponse.Rooms.Join[roomId].Timeline.Events.Add(@event);
        return existingResponse;
    }

    public SyncResponse SendStatusMessage(string text, SyncResponse? existingResponse = null) {
        if (existingResponse is null)
            SyncQueue.Enqueue(existingResponse = new());
        existingResponse.Presence ??= new();
        // existingResponse.Presence.Events ??= new();
        existingResponse.Presence.Events.RemoveAll(x => x.Sender == Homeserver.WhoAmI.UserId);
        existingResponse.Presence.Events.Add(new StateEventResponse {
            TypedContent = new PresenceEventContent {
                Presence = "online",
                StatusMessage = text,
                LastActiveAgo = 15,
                CurrentlyActive = true
            },
            Type = "m.presence",
            StateKey = "",
            Sender = Homeserver.WhoAmI.UserId,
            OriginServerTs = 0,
            RoomId = null, //TODO: implement
            EventId = null
        });
        return existingResponse;
    }
}

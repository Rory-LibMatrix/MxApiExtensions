using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using LibMatrix;
using LibMatrix.EventTypes.Spec.State;
using LibMatrix.Helpers;
using LibMatrix.Homeservers;
using LibMatrix.Responses;
using Microsoft.OpenApi.Extensions;

namespace MxApiExtensions.Classes;

public class SyncState {
    private Task? _nextSyncResponse;
    public string? NextBatch { get; set; }
    public ConcurrentQueue<SyncResponse> SyncQueue { get; set; } = new();
    public bool IsInitialSync { get; set; }

    [JsonIgnore]
    public Task? NextSyncResponse {
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

    public void SendEphemeralTimelineEventInRoom(string roomId, StateEventResponse @event) {
        SyncQueue.Enqueue(new() {
            NextBatch = NextBatch ?? "null",
            Rooms = new() {
                Join = new() {
                    {
                        roomId,
                        new() {
                            Timeline = new() {
                                Events = new() {
                                    @event
                                }
                            }
                        }
                    }
                }
            }
        });
    }

    public void SendStatusMessage(string text) {
        SyncQueue.Enqueue(new() {
            NextBatch = NextBatch ?? "null",
            Presence = new() {
                Events = new() {
                    new StateEventResponse {
                        TypedContent = new PresenceEventContent {
                            DisplayName = "MxApiExtensions",
                            Presence = "online",
                            StatusMessage = text,
                            // AvatarUrl = (await syncState.Homeserver.GetProfile(syncState.Homeserver.WhoAmI.UserId)).AvatarUrl
                            AvatarUrl = "",
                            LastActiveAgo = 15,
                            CurrentlyActive = true
                        },
                        Type = "m.presence",
                        StateKey = Homeserver.WhoAmI.UserId,
                        Sender = Homeserver.WhoAmI.UserId,
                        EventId = Guid.NewGuid().ToString(),
                        OriginServerTs = 0
                    }
                }
            }
        });
    }
}
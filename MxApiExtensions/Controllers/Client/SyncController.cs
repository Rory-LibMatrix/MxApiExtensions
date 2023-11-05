using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Web;
using ArcaneLibs;
using LibMatrix;
using LibMatrix.EventTypes.Spec.State;
using LibMatrix.Helpers;
using LibMatrix.Homeservers;
using LibMatrix.Responses;
using LibMatrix.RoomTypes;
using Microsoft.AspNetCore.Mvc;
using MxApiExtensions.Classes;
using MxApiExtensions.Classes.LibMatrix;
using MxApiExtensions.Extensions;
using MxApiExtensions.Services;

namespace MxApiExtensions.Controllers;

[ApiController]
[Route("/")]
public class SyncController(ILogger<SyncController> logger, MxApiExtensionsConfiguration config, AuthenticationService auth, AuthenticatedHomeserverProviderService hsProvider)
    : ControllerBase {
    public static readonly ConcurrentDictionary<string, SyncState> _syncStates = new();

    private static SemaphoreSlim _semaphoreSlim = new(1, 1);
    private Stopwatch _syncElapsed = Stopwatch.StartNew();

    [HttpGet("/_matrix/client/{_}/sync")]
    public async Task Sync(string _, [FromQuery] string? since, [FromQuery] int timeout = 1000) {
        Task? preloadTask = null;
        AuthenticatedHomeserverGeneric? hs = null;
        try {
            hs = await hsProvider.GetHomeserver();
        }
        catch (Exception e) {
            Console.WriteLine(e);
        }

        var qs = HttpUtility.ParseQueryString(Request.QueryString.Value!);
        qs.Remove("access_token");
        if (since == "null") qs.Remove("since");

        if (!config.FastInitialSync.Enabled) {
            logger.LogInformation("Starting sync for {} on {} ({})", hs.WhoAmI.UserId, hs.ServerName, hs.AccessToken);
            var result = await hs.ClientHttpClient.GetAsync($"{Request.Path}?{qs}");
            await Response.WriteHttpResponse(result);
            return;
        }

        await _semaphoreSlim.WaitAsync();
        var syncState = _syncStates.GetOrAdd($"{hs.WhoAmI.UserId}/{hs.WhoAmI.DeviceId}/{hs.ServerName}:{hs.AccessToken}", _ => {
            logger.LogInformation("Started tracking sync state for {} on {} ({})", hs.WhoAmI.UserId, hs.ServerName, hs.AccessToken);
            var ss = new SyncState {
                IsInitialSync = string.IsNullOrWhiteSpace(since),
                Homeserver = hs
            };
            if (ss.IsInitialSync) {
                preloadTask = EnqueuePreloadData(ss);
            }

            logger.LogInformation("Starting sync for {} on {} ({})", hs.WhoAmI.UserId, hs.ServerName, hs.AccessToken);

            ss.NextSyncResponseStartedAt = DateTime.Now;
            ss.NextSyncResponse = Task.Delay(15_000);
            ss.NextSyncResponse.ContinueWith(async x => {
                logger.LogInformation("Sync for {} on {} ({}) starting", hs.WhoAmI.UserId, hs.ServerName, hs.AccessToken);
                ss.NextSyncResponse = hs.ClientHttpClient.GetAsync($"/_matrix/client/v3/sync?{qs}");
                (ss.NextSyncResponse as Task<HttpResponseMessage>).ContinueWith(async x => EnqueueSyncResponse(ss, await x));
            });
            return ss;
        });
        _semaphoreSlim.Release();

        if (syncState.SyncQueue.Count > 0) {
            logger.LogInformation("Sync for {} on {} ({}) has {} queued results", hs.WhoAmI.UserId, hs.ServerName, hs.AccessToken, syncState.SyncQueue.Count);
            syncState.SyncQueue.TryDequeue(out var result);
            while (result is null)
                syncState.SyncQueue.TryDequeue(out result);
            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = "application/json";
            await Response.StartAsync();
            result.NextBatch ??= since ?? syncState.NextBatch;
            await JsonSerializer.SerializeAsync(Response.Body, result, new JsonSerializerOptions {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            await Response.CompleteAsync();
            return;
        }

        var newTimeout = Math.Clamp(timeout, 0, syncState.IsInitialSync ? syncState.SyncQueue.Count >= 2 ? 0 : 250 : timeout);
        logger.LogInformation("Sync for {} on {} ({}) is still running, waiting for {}ms, {} elapsed", hs.WhoAmI.UserId, hs.ServerName, hs.AccessToken, newTimeout,
            DateTime.Now.Subtract(syncState.NextSyncResponseStartedAt));

        try {
            if (syncState.NextSyncResponse is not null)
                await syncState.NextSyncResponse.WaitAsync(TimeSpan.FromMilliseconds(newTimeout));
            else {
                syncState.NextSyncResponse = hs.ClientHttpClient.GetAsync($"/_matrix/client/v3/sync?{qs}");
                (syncState.NextSyncResponse as Task<HttpResponseMessage>).ContinueWith(async x => EnqueueSyncResponse(syncState, await x));
                // await Task.Delay(250);
            }
        }
        catch (TimeoutException) { }

        // if (_syncElapsed.ElapsedMilliseconds > timeout)
        if(syncState.NextSyncResponse?.IsCompleted == false)
            syncState.SendStatusMessage(
                $"M={Util.BytesToString(Process.GetCurrentProcess().WorkingSet64)} TE={DateTime.Now.Subtract(syncState.NextSyncResponseStartedAt)} S={syncState.NextSyncResponse?.Status} QL={syncState.SyncQueue.Count}");
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/json";
        await Response.StartAsync();
        var response = syncState.SyncQueue.FirstOrDefault();
        if (response is null)
            response = new();
        response.NextBatch ??= since ?? syncState.NextBatch;
        await JsonSerializer.SerializeAsync(Response.Body, response, new JsonSerializerOptions {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        await Response.CompleteAsync();

        Response.Body.Close();
        if (preloadTask is not null)
            await preloadTask;
    }

    private async Task EnqueuePreloadData(SyncState syncState) {
        var rooms = await syncState.Homeserver.GetJoinedRooms();
        var dm_rooms = (await syncState.Homeserver.GetAccountDataAsync<Dictionary<string, List<string>>>("m.direct")).Aggregate(new List<string>(), (list, entry) => {
            list.AddRange(entry.Value);
            return list;
        });

        var ownHs = syncState.Homeserver.WhoAmI.UserId.Split(':')[1];
        rooms = rooms.OrderBy(x => {
            if (dm_rooms.Contains(x.RoomId)) return -1;
            var parts = x.RoomId.Split(':');
            if (parts[1] == ownHs) return 200;
            if (HomeserverWeightEstimation.EstimatedSize.ContainsKey(parts[1])) return HomeserverWeightEstimation.EstimatedSize[parts[1]] + parts[0].Length;
            return 5000;
        }).ToList();
        var roomDataTasks = rooms.Select(room => EnqueueRoomData(syncState, room)).ToList();
        logger.LogInformation("Preloading data for {} rooms on {} ({})", roomDataTasks.Count, syncState.Homeserver.ServerName, syncState.Homeserver.AccessToken);

        await Task.WhenAll(roomDataTasks);
    }

    private SemaphoreSlim _roomDataSemaphore = new(32, 32);

    private async Task EnqueueRoomData(SyncState syncState, GenericRoom room) {
        await _roomDataSemaphore.WaitAsync();
        var roomState = room.GetFullStateAsync();
        var timeline = await room.GetMessagesAsync(limit: 100, dir: "b");
        timeline.Chunk.Reverse();
        var SyncResponse = new SyncResponse {
            Rooms = new() {
                Join = new() {
                    {
                        room.RoomId,
                        new SyncResponse.RoomsDataStructure.JoinedRoomDataStructure {
                            AccountData = new() {
                                Events = new()
                            },
                            Ephemeral = new() {
                                Events = new()
                            },
                            State = new() {
                                Events = timeline.State
                            },
                            UnreadNotifications = new() {
                                HighlightCount = 0,
                                NotificationCount = 0
                            },
                            Timeline = new() {
                                Events = timeline.Chunk,
                                Limited = false,
                                PrevBatch = timeline.Start
                            },
                            Summary = new() {
                                Heroes = new(),
                                InvitedMemberCount = 0,
                                JoinedMemberCount = 1
                            }
                        }
                    }
                }
            },
            Presence = new() {
                Events = new() {
                    await GetStatusMessage(syncState, $"{DateTime.Now.Subtract(syncState.NextSyncResponseStartedAt)} {syncState.NextSyncResponse.Status} {room.RoomId}")
                }
            },
            NextBatch = ""
        };

        await foreach (var stateEvent in roomState) {
            SyncResponse.Rooms.Join[room.RoomId].State.Events.Add(stateEvent);
        }

        var joinRoom = SyncResponse.Rooms.Join[room.RoomId];
        joinRoom.Summary.Heroes.AddRange(joinRoom.State.Events
            .Where(x =>
                x.Type == "m.room.member"
                && x.StateKey != syncState.Homeserver.WhoAmI.UserId
                && (x.TypedContent as RoomMemberEventContent).Membership == "join"
            )
            .Select(x => x.StateKey));
        joinRoom.Summary.JoinedMemberCount = joinRoom.Summary.Heroes.Count;

        syncState.SyncQueue.Enqueue(SyncResponse);
        _roomDataSemaphore.Release();
    }

    private async Task<StateEventResponse> GetStatusMessage(SyncState syncState, string message) {
        return new StateEventResponse {
            TypedContent = new PresenceEventContent {
                DisplayName = "MxApiExtensions",
                Presence = "online",
                StatusMessage = message,
                // AvatarUrl = (await syncState.Homeserver.GetProfile(syncState.Homeserver.WhoAmI.UserId)).AvatarUrl
                AvatarUrl = ""
            },
            Type = "m.presence",
            StateKey = syncState.Homeserver.WhoAmI.UserId,
            Sender = syncState.Homeserver.WhoAmI.UserId,
            EventId = Guid.NewGuid().ToString(),
            OriginServerTs = 0
        };
    }

    private async Task EnqueueSyncResponse(SyncState ss, HttpResponseMessage task) {
        var sr = await task.Content.ReadFromJsonAsync<JsonObject>();
        if (sr.ContainsKey("error")) throw sr.Deserialize<MatrixException>()!;
        ss.NextBatch = sr["next_batch"].GetValue<string>();
        ss.IsInitialSync = false;
        ss.SyncQueue.Enqueue(sr.Deserialize<SyncResponse>());
        ss.NextSyncResponse = null;
    }
}
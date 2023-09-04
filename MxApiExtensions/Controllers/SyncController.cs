using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using LibMatrix;
using LibMatrix.Helpers;
using LibMatrix.Homeservers;
using LibMatrix.Responses;
using LibMatrix.RoomTypes;
using LibMatrix.StateEventTypes.Spec;
using Microsoft.AspNetCore.Mvc;
using MxApiExtensions.Classes;
using MxApiExtensions.Extensions;
using MxApiExtensions.Services;

namespace MxApiExtensions.Controllers;

[ApiController]
[Route("/")]
public class SyncController : ControllerBase {
    private readonly ILogger<SyncController> _logger;
    private readonly MxApiExtensionsConfiguration _config;
    private readonly AuthenticationService _auth;
    private readonly AuthenticatedHomeserverProviderService _hs;

    private static readonly ConcurrentDictionary<string, SyncState> _syncStates = new();

    public SyncController(ILogger<SyncController> logger, MxApiExtensionsConfiguration config, AuthenticationService auth, AuthenticatedHomeserverProviderService hs) {
        _logger = logger;
        _config = config;
        _auth = auth;
        _hs = hs;
    }

    [HttpGet("/_matrix/client/v3/sync")]
    public async Task Sync([FromQuery] string? since, [FromQuery] int timeout = 1000) {
        Task? preloadTask = null;
        AuthenticatedHomeserverGeneric? hs = null;
        try {
            hs = await _hs.GetHomeserver();
        }
        catch (Exception e) {
            Console.WriteLine();
        }
        var qs = HttpUtility.ParseQueryString(Request.QueryString.Value!);
        qs.Remove("access_token");

        if (!_config.FastInitialSync.Enabled) {
            _logger.LogInformation("Starting sync for {} on {} ({})", hs.WhoAmI.UserId, hs.HomeServerDomain, hs.AccessToken);
            var result = await hs._httpClient.GetAsync($"{Request.Path}?{qs}");
            await Response.WriteHttpResponse(result);
            return;
        }

        try {
            var syncState = _syncStates.GetOrAdd(hs.AccessToken, _ => {
                _logger.LogInformation("Started tracking sync state for {} on {} ({})", hs.WhoAmI.UserId, hs.HomeServerDomain, hs.AccessToken);
                return new SyncState {
                    IsInitialSync = string.IsNullOrWhiteSpace(since),
                    Homeserver = hs
                };
            });

            if (syncState.NextSyncResult is null) {
                _logger.LogInformation("Starting sync for {} on {} ({})", hs.WhoAmI.UserId, hs.HomeServerDomain, hs.AccessToken);

                if (syncState.IsInitialSync) {
                    preloadTask = EnqueuePreloadData(syncState);
                }

                syncState.NextSyncResultStartedAt = DateTime.Now;
                syncState.NextSyncResult = Task.Delay(30_000);
                syncState.NextSyncResult.ContinueWith(x => {
                    _logger.LogInformation("Sync for {} on {} ({}) starting", hs.WhoAmI.UserId, hs.HomeServerDomain, hs.AccessToken);
                    syncState.NextSyncResult = hs._httpClient.GetAsync($"{Request.Path}?{qs}");
                });
            }

            if (syncState.SyncQueue.Count > 0) {
                _logger.LogInformation("Sync for {} on {} ({}) has {} queued results", hs.WhoAmI.UserId, hs.HomeServerDomain, hs.AccessToken, syncState.SyncQueue.Count);
                syncState.SyncQueue.TryDequeue(out var result);

                Response.StatusCode = StatusCodes.Status200OK;
                Response.ContentType = "application/json";
                await Response.StartAsync();
                await JsonSerializer.SerializeAsync(Response.Body, result, new JsonSerializerOptions {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });
                await Response.CompleteAsync();
                return;
            }

            timeout = Math.Clamp(timeout, 0, 100);
            _logger.LogInformation("Sync for {} on {} ({}) is still running, waiting for {}ms, {} elapsed", hs.WhoAmI.UserId, hs.HomeServerDomain, hs.AccessToken, timeout,
                DateTime.Now.Subtract(syncState.NextSyncResultStartedAt));

            try {
                await syncState.NextSyncResult.WaitAsync(TimeSpan.FromMilliseconds(timeout));
            }
            catch { }

            if (syncState.NextSyncResult is Task<HttpResponseMessage> { IsCompleted: true } response) {
                _logger.LogInformation("Sync for {} on {} ({}) completed", hs.WhoAmI.UserId, hs.HomeServerDomain, hs.AccessToken);
                var resp = await response;
                await Response.WriteHttpResponse(resp);
                return;
            }

            // await Task.Delay(timeout);
            _logger.LogInformation("Sync for {} on {} ({}): sending bogus response", hs.WhoAmI.UserId, hs.HomeServerDomain, hs.AccessToken);
            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = "application/json";
            await Response.StartAsync();
            var syncResult = new SyncResult {
                // NextBatch = "MxApiExtensions::Next" + Random.Shared.NextInt64(),
                NextBatch = since ?? "",
                Presence = new() {
                    Events = new() {
                        await GetStatusMessage(syncState, $"{DateTime.Now.Subtract(syncState.NextSyncResultStartedAt)} {syncState.NextSyncResult.Status}")
                    }
                },
                Rooms = new() {
                    Invite = new(),
                    Join = new()
                }
            };
            await JsonSerializer.SerializeAsync(Response.Body, syncResult, new JsonSerializerOptions {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            await Response.CompleteAsync();
        }
        catch (MxApiMatrixException e) {
            _logger.LogError(e, "Error while syncing for {} on {} ({})", _hs.GetHomeserver().Result.WhoAmI.UserId,
                _hs.GetHomeserver().Result.HomeServerDomain, _hs.GetHomeserver().Result.AccessToken);

            Response.StatusCode = StatusCodes.Status500InternalServerError;
            Response.ContentType = "application/json";

            await Response.WriteAsJsonAsync(e.GetAsJson());
            await Response.CompleteAsync();
        }

        catch (Exception e) {
            //catch SSL connection errors and retry
            if (e.InnerException is HttpRequestException && e.InnerException.Message.Contains("The SSL connection could not be established")) {
                _logger.LogWarning("Caught SSL connection error, retrying sync for {} on {} ({})", _hs.GetHomeserver().Result.WhoAmI.UserId,
                    _hs.GetHomeserver().Result.HomeServerDomain, _hs.GetHomeserver().Result.AccessToken);
                await Sync(since, timeout);
                return;
            }

            _logger.LogError(e, "Error while syncing for {} on {} ({})", _hs.GetHomeserver().Result.WhoAmI.UserId,
                _hs.GetHomeserver().Result.HomeServerDomain, _hs.GetHomeserver().Result.AccessToken);

            Response.StatusCode = StatusCodes.Status500InternalServerError;
            Response.ContentType = "text/plain";

            await Response.WriteAsync(e.ToString());
            await Response.CompleteAsync();
        }

        Response.Body.Close();
        if (preloadTask is not null)
            await preloadTask;
    }

    private async Task EnqueuePreloadData(SyncState syncState) {
        var rooms = await syncState.Homeserver.GetJoinedRooms();
        var dm_rooms = (await syncState.Homeserver.GetAccountData<Dictionary<string, List<string>>>("m.direct")).Aggregate(new List<string>(), (list, entry) => {
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
        _logger.LogInformation("Preloading data for {} rooms on {} ({})", roomDataTasks.Count, syncState.Homeserver.HomeServerDomain, syncState.Homeserver.AccessToken);

        await Task.WhenAll(roomDataTasks);
    }

    private SemaphoreSlim _roomDataSemaphore = new(4, 4);

    private async Task EnqueueRoomData(SyncState syncState, GenericRoom room) {
        await _roomDataSemaphore.WaitAsync();
        var roomState = room.GetFullStateAsync();
        var timeline = await room.GetMessagesAsync(limit: 100, dir: "b");
        timeline.Chunk.Reverse();
        var syncResult = new SyncResult {
            Rooms = new() {
                Join = new() {
                    {
                        room.RoomId,
                        new SyncResult.RoomsDataStructure.JoinedRoomDataStructure() {
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
                    await GetStatusMessage(syncState, $"{DateTime.Now.Subtract(syncState.NextSyncResultStartedAt)} {syncState.NextSyncResult.Status} {room.RoomId}")
                }
            },
            NextBatch = ""
        };

        await foreach (var stateEvent in roomState) {
            syncResult.Rooms.Join[room.RoomId].State.Events.Add(stateEvent);
        }

        var joinRoom = syncResult.Rooms.Join[room.RoomId];
        joinRoom.Summary.Heroes.AddRange(joinRoom.State.Events
            .Where(x =>
                x.Type == "m.room.member"
                && x.StateKey != syncState.Homeserver.WhoAmI.UserId
                && (x.TypedContent as RoomMemberEventData).Membership == "join"
                )
            .Select(x => x.StateKey));
        joinRoom.Summary.JoinedMemberCount = joinRoom.Summary.Heroes.Count;

        syncState.SyncQueue.Enqueue(syncResult);
        _roomDataSemaphore.Release();
    }

    private async Task<StateEventResponse> GetStatusMessage(SyncState syncState, string message) {
        return new StateEventResponse() {
            TypedContent = new PresenceStateEventData() {
                DisplayName = "MxApiExtensions",
                Presence = "online",
                StatusMessage = message,
                // AvatarUrl = (await syncState.Homeserver.GetProfile(syncState.Homeserver.WhoAmI.UserId)).AvatarUrl
                AvatarUrl = ""
            },
            Type = "m.presence",
            StateKey = syncState.Homeserver.WhoAmI.UserId,
            Sender = syncState.Homeserver.WhoAmI.UserId,
            UserId = syncState.Homeserver.WhoAmI.UserId,
            EventId = Guid.NewGuid().ToString(),
            OriginServerTs = 0
        };
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Web;
using System.Xml;
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
public class SyncController(ILogger<SyncController> logger, MxApiExtensionsConfiguration config, AuthenticationService auth, AuthenticatedHomeserverProviderService hsProvider,
        UserContextService userContextService)
    : ControllerBase {
    private UserContextService.UserContext userContext;
    private Stopwatch _syncElapsed = Stopwatch.StartNew();
    private static SemaphoreSlim _semaphoreSlim = new(1, 1);

    [HttpGet("/_matrix/client/{_}/sync")]
    public async Task Sync(string _, [FromQuery] string? since, [FromQuery] int timeout = 1000) {
        // temporary variables
        bool startedNewTask = false;
        Task? preloadTask = null;

        // get user context based on authentication
        userContext = await userContextService.GetCurrentUserContext();
        var qs = HttpUtility.ParseQueryString(Request.QueryString.Value!);
        qs.Remove("access_token");
        if (since == "null") qs.Remove("since");

        // if (!userContext.UserConfiguration.InitialSyncPreload.Enable) {
        //     logger.LogInformation("Starting sync for {} on {} ({})", hs.WhoAmI.UserId, hs.ServerName, hs.AccessToken);
        //     var result = await hs.ClientHttpClient.GetAsync($"{Request.Path}?{qs}");
        //     await Response.WriteHttpResponse(result);
        //     return;
        // }

        //prevent duplicate initialisation
        await _semaphoreSlim.WaitAsync();
        
        //if we don't have a sync state for this user...
        if (userContext.SyncState is null) {
            logger.LogInformation("Started tracking sync state for {} on {} ({})", userContext.Homeserver.WhoAmI.UserId, userContext.Homeserver.ServerName,
                userContext.Homeserver.AccessToken);
            
            //create a new sync state
            userContext.SyncState = new SyncState {
                Homeserver = userContext.Homeserver,
                NextSyncResponse = Task.Run(async () => {
                    if (string.IsNullOrWhiteSpace(since) && userContext.UserConfiguration.InitialSyncPreload.Enable)
                        await Task.Delay(15_000);
                    logger.LogInformation("Sync for {} on {} ({}) starting", userContext.Homeserver.WhoAmI.UserId, userContext.Homeserver.ServerName,
                        userContext.Homeserver.AccessToken);
                    return await userContext.Homeserver.ClientHttpClient.GetAsync($"/_matrix/client/v3/sync?{qs}");
                })
            };
            startedNewTask = true;
            
            //if this is an initial sync, and the user has enabled this, preload data
            if (string.IsNullOrWhiteSpace(since) && userContext.UserConfiguration.InitialSyncPreload.Enable) {
                logger.LogInformation("Sync data preload for {} on {} ({}) starting", userContext.Homeserver.WhoAmI.UserId, userContext.Homeserver.ServerName,
                    userContext.Homeserver.AccessToken);
                preloadTask = EnqueuePreloadData(userContext.SyncState);
            }
        }

        if (userContext.SyncState.NextSyncResponse is null) {
            userContext.SyncState.NextSyncResponse = userContext.Homeserver.ClientHttpClient.GetAsync($"/_matrix/client/v3/sync?{qs}");
            startedNewTask = true;
        }

        _semaphoreSlim.Release();

        //get the next sync response
        var syncResponse = await GetNextSyncResponse(timeout);
        //send it to the client
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/json";
        await Response.StartAsync();
        var response = syncResponse;
        response.NextBatch ??= since ?? "null";
        await JsonSerializer.SerializeAsync(Response.Body, response, new JsonSerializerOptions {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        await Response.CompleteAsync();

        Response.Body.Close();

        //await scope-local tasks in order to prevent disposal
        if (preloadTask is not null) {
            await preloadTask;
            preloadTask.Dispose();
        }

        if (startedNewTask && userContext.SyncState?.NextSyncResponse is not null) {
            var resp = await userContext.SyncState.NextSyncResponse;
            var sr = await resp.Content.ReadFromJsonAsync<JsonObject>();
            if (sr!.ContainsKey("error")) throw sr.Deserialize<MatrixException>()!;
            userContext.SyncState.NextBatch = sr["next_batch"]!.GetValue<string>();
            // userContext.SyncState.IsInitialSync = false;
            var syncResp = sr.Deserialize<SyncResponse>();
            userContext.SyncState.SyncQueue.Enqueue(syncResp);
            userContext.SyncState.NextSyncResponse.Dispose();
            userContext.SyncState.NextSyncResponse = null;
        }
    }

    private async Task<SyncResponse> GetNextSyncResponse(int timeout = 0) {
        do {
            if (userContext.SyncState is null) throw new NullReferenceException("syncState is null!");
            // if (userContext.SyncState.NextSyncResponse is null) throw new NullReferenceException("NextSyncResponse is null");
            
            //check if upstream has responded, if so, return upstream response
            // if (userContext.SyncState.NextSyncResponse is { IsCompleted: true } syncResponse) {
            //     var resp = await syncResponse;
            //     var sr = await resp.Content.ReadFromJsonAsync<JsonObject>();
            //     if (sr!.ContainsKey("error")) throw sr.Deserialize<MatrixException>()!;
            //     userContext.SyncState.NextBatch = sr["next_batch"]!.GetValue<string>();
            //     // userContext.SyncState.IsInitialSync = false;
            //     var syncResp = sr.Deserialize<SyncResponse>();
            //     return syncResp;
            // }

            //else, return the first item in queue, if any
            if (userContext.SyncState.SyncQueue.Count > 0) {
                logger.LogInformation("Sync for {} on {} ({}) has {} queued results", userContext.SyncState.Homeserver.WhoAmI.UserId, userContext.SyncState.Homeserver.ServerName,
                    userContext.SyncState.Homeserver.AccessToken, userContext.SyncState.SyncQueue.Count);
                userContext.SyncState.SyncQueue.TryDequeue(out var result);
                while (result is null)
                    userContext.SyncState.SyncQueue.TryDequeue(out result);
                return result;
            }

            // await Task.Delay(Math.Clamp(timeout, 25, 250)); //wait 25-250ms between checks
            await Task.Delay(Math.Clamp(userContextService.SessionCount * 10 ,25, 500));
        } while (_syncElapsed.ElapsedMilliseconds < timeout + 500); //... while we haven't gone >500ms over expected timeout

        //we didn't get a response, send a bogus response
        return userContext.SyncState.SendStatusMessage(
            $"M={Util.BytesToString(Process.GetCurrentProcess().WorkingSet64)} TE={DateTime.Now.Subtract(userContext.SyncState.NextSyncResponseStartedAt)} QL={userContext.SyncState.SyncQueue.Count}",
            new());
    }


    private async Task EnqueuePreloadData(SyncState syncState) {
        await EnqueuePreloadAccountData(syncState);
        await EnqueuePreloadRooms(syncState);
    }

    private static List<string> CommonAccountDataKeys = new() {
        "gay.rory.dm_space",
        "im.fluffychat.account_bundles",
        "im.ponies.emote_rooms",
        "im.vector.analytics",
        "im.vector.setting.breadcrumbs",
        "im.vector.setting.integration_provisioning",
        "im.vector.web.settings",
        "io.element.recent_emoji",
        "m.cross_signing.master",
        "m.cross_signing.self_signing",
        "m.cross_signing.user_signing",
        "m.direct",
        "m.megolm_backup.v1",
        "m.push_rules",
        "m.secret_storage.default_key",
        "gay.rory.mxapiextensions.userconfig"
    };
    //enqueue common account data
    private async Task EnqueuePreloadAccountData(SyncState syncState) {
        var syncMsg = new SyncResponse() {
            AccountData = new() {
                Events = new()
            }
        };
        foreach (var key in CommonAccountDataKeys) {
            try {
                syncMsg.AccountData.Events.Add(new() {
                    Type = key,
                    RawContent = await syncState.Homeserver.GetAccountDataAsync<JsonObject>(key)
                });
            }
            catch {}
        }
        syncState.SyncQueue.Enqueue(syncMsg);
    }

    private async Task EnqueuePreloadRooms(SyncState syncState) {
        //get the users's rooms
        var rooms = await syncState.Homeserver.GetJoinedRooms();
        
        //get the user's DM rooms
        var mDirectContent = await syncState.Homeserver.GetAccountDataAsync<Dictionary<string, List<string>>>("m.direct");
        var dmRooms = mDirectContent.SelectMany(pair => pair.Value);

        //get our own homeserver's server_name
        var ownHs = syncState.Homeserver.WhoAmI!.UserId!.Split(':')[1];
        
        //order rooms by expected state size, since large rooms take a long time to return
        rooms = rooms.OrderBy(x => {
            if (dmRooms.Contains(x.RoomId)) return -1;
            var parts = x.RoomId.Split(':');
            if (parts[1] == ownHs) return 200;
            if (HomeserverWeightEstimation.EstimatedSize.ContainsKey(parts[1])) return HomeserverWeightEstimation.EstimatedSize[parts[1]] + parts[0].Length;
            return 5000;
        }).ToList();
        
        //start all fetch tasks
        var roomDataTasks = rooms.Select(room => EnqueueRoomData(syncState, room)).ToList();
        logger.LogInformation("Preloading data for {} rooms on {} ({})", roomDataTasks.Count, syncState.Homeserver.ServerName, syncState.Homeserver.AccessToken);

        //wait for all of them to finish
        await Task.WhenAll(roomDataTasks);
    }

    private static readonly SemaphoreSlim _roomDataSemaphore = new(4, 4);

    private async Task EnqueueRoomData(SyncState syncState, GenericRoom room) {
        //limit concurrent requests, to not overload upstream
        await _roomDataSemaphore.WaitAsync();
        //get the room's state
        var roomState = room.GetFullStateAsync();
        //get the room's timeline, reversed 
        var timeline = await room.GetMessagesAsync(limit: 100, dir: "b");
        timeline.Chunk.Reverse();
        //queue up this data as a sync response
        var syncResponse = new SyncResponse {
            Rooms = new() {
                Join = new() {
                    {
                        room.RoomId,
                        new SyncResponse.RoomsDataStructure.JoinedRoomDataStructure {
                            State = new() {
                                Events = timeline.State
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
            }
        };

        //calculate invited/joined member counts, and add other events to state
        await foreach (var stateEvent in roomState) {
            if (stateEvent is { Type: "m.room.member" }) {
                if (stateEvent.TypedContent is RoomMemberEventContent { Membership: "join" })
                    syncResponse.Rooms.Join[room.RoomId].Summary.JoinedMemberCount++;
                else if (stateEvent.TypedContent is RoomMemberEventContent { Membership: "invite" })
                    syncResponse.Rooms.Join[room.RoomId].Summary.InvitedMemberCount++;
                else continue;
            }

            syncResponse.Rooms.Join[room.RoomId].State!.Events!.Add(stateEvent!);
        }

        //finally, actually put the response in queue
        syncState.SyncQueue.Enqueue(syncResponse);
        _roomDataSemaphore.Release();
    }
}
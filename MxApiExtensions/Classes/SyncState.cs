using System.Collections.Concurrent;
using LibMatrix.Helpers;
using LibMatrix.Homeservers;
using LibMatrix.Responses;

namespace MxApiExtensions.Classes;

public class SyncState {
    public string? NextBatch { get; set; }
    public ConcurrentQueue<SyncResponse> SyncQueue { get; set; } = new();
    public bool IsInitialSync { get; set; }
    public Task? NextSyncResponse { get; set; }
    public DateTime NextSyncResponseStartedAt { get; set; } = DateTime.Now;
    public AuthenticatedHomeserverGeneric Homeserver { get; set; }
}

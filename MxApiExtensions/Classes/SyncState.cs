using System.Collections.Concurrent;
using LibMatrix.Helpers;
using LibMatrix.Homeservers;

namespace MxApiExtensions.Classes;

public class SyncState {
    public string? NextBatch { get; set; }
    public ConcurrentQueue<SyncResult> SyncQueue { get; set; } = new();
    public bool IsInitialSync { get; set; }
    public Task? NextSyncResult { get; set; }
    public DateTime NextSyncResultStartedAt { get; set; } = DateTime.Now;
    public AuthenticatedHomeserverGeneric Homeserver { get; set; }
}

using LibMatrix.EventTypes;
using LibMatrix.Interfaces;

namespace MxApiExtensions.Classes;

[MatrixEvent(EventName = EventId)]
public class MxApiExtensionsUserConfiguration : EventContent {
    public const string EventId = "gay.rory.mxapiextensions.userconfig";
    public ProtocolChangeConfiguration ProtocolChanges { get; set; } = new();
    public InitialSyncConfiguration InitialSyncPreload { get; set; } = new();

    public class InitialSyncConfiguration {
        public bool Enable { get; set; } = true;
    }

    public class ProtocolChangeConfiguration {
        public bool DisableThreads { get; set; } = false;
        public bool DisableVoip { get; set; } = false;
        public bool AutoFollowTombstones { get; set; } = false;
    }
}

namespace MxApiExtensions;

public class MxApiExtensionsConfiguration {
    public MxApiExtensionsConfiguration(IConfiguration config) {
        config.GetRequiredSection("MxApiExtensions").Bind(this);
    }

    public List<string> AuthHomeservers { get; set; } = new();
    public List<string> Admins { get; set; } = new();

    public FastInitialSyncConfiguration FastInitialSync { get; set; } = new();

    public CacheConfiguration Cache { get; set; } = new();


    public class FastInitialSyncConfiguration {
        public bool Enabled { get; set; } = true;
        public bool UseRoomInfoCache { get; set; } = true;
    }

    public class CacheConfiguration {
        public RoomInfoCacheConfiguration RoomInfo { get; set; } = new();

        public class RoomInfoCacheConfiguration {
            public TimeSpan BaseTtl { get; set; } = TimeSpan.FromMinutes(1);
            public TimeSpan ExtraTtlPerState { get; set; } = TimeSpan.FromMilliseconds(100);
        }
    }
}

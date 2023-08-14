namespace MxApiExtensions;

public class CacheConfiguration {
    public CacheConfiguration(IConfiguration config) {
        config.GetRequiredSection("MxSyncCache").Bind(this);
    }

    public string Homeserver { get; set; } = "";
}

using LibMatrix.Responses;

namespace MxApiExtensions.Classes;

public interface ISyncFilter {
    public Task<SyncResponse> Apply(SyncResponse syncResponse);
}

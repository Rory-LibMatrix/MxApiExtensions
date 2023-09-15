using LibMatrix.Responses;

namespace MxApiExtensions.Classes.LibMatrix;

/// <summary>
/// Generic room info, this will most likely be out of date due to caching!
/// This is only useful for giving a rough idea of the room state.
/// </summary>
public class RoomInfoEntry {
    public string RoomId { get; set; }
    public List<StateEventResponse?> RoomState { get; set; }

    public int StateCount { get; set; }

    public Dictionary<string, int> MemberCounts { get; set; } = new();

    // [JsonIgnore]
    public DateTime ExpiresAt { get; set; } = DateTime.Now.AddMinutes(1);
}

using System.Text.Json.Serialization;

namespace ZoneQuanta.Core.Settings;

// Where a resizable window was and how big (DIPs). The centre is a fraction of the monitor work area,
// so the same spot is found on any monitor or resolution; a negative fraction means "not moved yet".
public sealed record WindowBounds(double Width, double Height, double CenterX = -1, double CenterY = -1, bool Maximized = false, string Screen = "")
{
    [JsonIgnore]
    public bool HasPlace => CenterX >= 0 && CenterY >= 0;
}

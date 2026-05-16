using Vintagestory.API.MathTools;

namespace SimplePlayerCorpse;

public class Config
{
    public int MaxCorpseCount = int.MaxValue;
    public bool CreateWaypointOnDeath = true;
    public bool PinWaypoint = true;
    public string WaypointIcon = "bee";
    public Vec3i WaypointColor = new(255, 0, 0);
}
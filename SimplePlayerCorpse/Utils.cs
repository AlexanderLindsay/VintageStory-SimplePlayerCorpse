using Vintagestory.API.MathTools;

namespace SimplePlayerCorpse;

public static class Utils {

    public static Vec3d MakeRelativePos(Vec3d a, Vec3d b)
    {
        return new Vec3d(a.X - b.X, a.Y, a.Z - b.Z);
    } 
}
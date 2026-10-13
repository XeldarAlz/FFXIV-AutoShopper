using System.Numerics;

namespace AutoShopper.Core.Travel;

internal static class StandSpot
{
    // The point standOffMeters from the target on the side the character comes from, at the character's own height, so
    // a straight step toward it ends in front of the target and never inside it. A character right over the target
    // steps off along +Z.
    public static Vector3 Beside(Vector3 target, Vector3 from, float standOffMeters)
    {
        var toward = new Vector2(from.X - target.X, from.Z - target.Z);
        var direction = toward.LengthSquared() > 0f ? Vector2.Normalize(toward) : Vector2.UnitY;
        return new Vector3(target.X + direction.X * standOffMeters, from.Y, target.Z + direction.Y * standOffMeters);
    }
}

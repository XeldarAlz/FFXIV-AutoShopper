using System.Numerics;
using AutoShopper.Core.Travel;

namespace AutoShopper.Tests;

public sealed class StandSpotTests
{
    private const float StandOffMeters = 2.5f;
    private const float Tolerance = 0.001f;

    [Fact]
    public void TheSpotLiesOnTheCharactersSideOfTheTarget()
    {
        var board = new Vector3(149.1f, 4.0f, -32.5f);
        var character = new Vector3(146.2f, 4.2f, -27.5f);

        var spot = StandSpot.Beside(board, character, StandOffMeters);

        Assert.Equal(StandOffMeters, Vector2.Distance(new Vector2(spot.X, spot.Z), new Vector2(board.X, board.Z)), Tolerance);
        Assert.True(Vector3.Distance(spot, character) < Vector3.Distance(board, character));
        Assert.Equal(character.Y, spot.Y);
    }

    [Fact]
    public void TheSpotKeepsTheCharactersBearingFromTheTarget()
    {
        var target = new Vector3(0f, 0f, 0f);
        var character = new Vector3(0f, 1f, 10f);

        var spot = StandSpot.Beside(target, character, StandOffMeters);

        Assert.Equal(0f, spot.X, Tolerance);
        Assert.Equal(StandOffMeters, spot.Z, Tolerance);
    }

    [Fact]
    public void ACharacterOverTheTargetStepsOffAlongZ()
    {
        var target = new Vector3(5f, 2f, 7f);
        var character = new Vector3(5f, 2.5f, 7f);

        var spot = StandSpot.Beside(target, character, StandOffMeters);

        Assert.Equal(new Vector3(5f, 2.5f, 7f + StandOffMeters), spot);
    }
}

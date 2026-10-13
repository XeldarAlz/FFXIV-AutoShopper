using System.Collections.Immutable;
using System.IO;
using AutoShopper.Core.Pricing.Model;

namespace AutoShopper.Tests;

// Real Universalis answers recorded on 2026-10-12, with retainer names, retainer ids and crafter names blanked.
internal static class PricingFixtures
{
    public const uint AetherId = 2;
    public const uint PrimalId = 3;
    public const uint CrystalId = 4;
    public const uint DynamisId = 5;
    public const byte NorthAmerica = 2;

    public const uint HighQualityItem = 5057;
    public const uint FireShard = 2;
    public const uint Gil = 1;
    public const uint OneGilItem = 5333;

    public static readonly DateTime FetchedAt = new(2026, 10, 12, 23, 33, 0, DateTimeKind.Utc);

    public static readonly ScopeWorld Jenova = InAether(40, "Jenova");
    public static readonly ScopeWorld Faerie = InAether(54, "Faerie");
    public static readonly ScopeWorld Siren = InAether(57, "Siren");
    public static readonly ScopeWorld Gilgamesh = InAether(63, "Gilgamesh");
    public static readonly ScopeWorld Midgardsormr = InAether(65, "Midgardsormr");
    public static readonly ScopeWorld Adamantoise = InAether(73, "Adamantoise");
    public static readonly ScopeWorld Cactuar = InAether(79, "Cactuar");
    public static readonly ScopeWorld Sargatanas = InAether(99, "Sargatanas");
    public static readonly ScopeWorld Exodus = new(53, "Exodus", PrimalId, "Primal", NorthAmerica);
    public static readonly ScopeWorld Famfrit = new(35, "Famfrit", PrimalId, "Primal", NorthAmerica);
    public static readonly ScopeWorld Mateus = new(37, "Mateus", CrystalId, "Crystal", NorthAmerica);
    public static readonly ScopeWorld Rafflesia = new(411, "Rafflesia", DynamisId, "Dynamis", NorthAmerica);
    public static readonly ScopeWorld Phantom = new(9_999, "Phantom", DynamisId, "Dynamis", NorthAmerica);

    public static readonly ImmutableArray<ScopeWorld> AetherWorlds = [Jenova, Faerie, Siren, Gilgamesh, Midgardsormr, Adamantoise, Cactuar, Sargatanas];

    public static byte[] Read(string name)
    {
        return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Universalis", name));
    }

    public static DateTime FromUnixMilliseconds(long milliseconds)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
    }

    public static PriceChunk Chunk(PriceQueryKind kind, string segment, ImmutableArray<ScopeWorld> worlds, params uint[] itemIds)
    {
        return new PriceChunk(new PriceQuery(kind, segment, worlds), [.. itemIds]);
    }

    private static ScopeWorld InAether(uint worldId, string name)
    {
        return new ScopeWorld(worldId, name, AetherId, "Aether", NorthAmerica);
    }
}

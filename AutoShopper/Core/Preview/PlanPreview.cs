using System.Threading;
using System.Threading.Tasks;
using AutoShopper.Core.Inventory;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Preview.Model;
using AutoShopper.Core.Pricing;
using AutoShopper.Core.Pricing.Model;
using AutoShopper.Core.Shopping;
using AutoShopper.Core.Shopping.Model;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;

namespace AutoShopper.Core.Preview;

// Keeps a plan for the selected list up to date. Inputs are read on the framework thread and frozen into a
// PreviewInput; the planner runs on the thread pool, a newer input cancels the run in flight, and a result whose
// generation is no longer the newest is dropped. Nothing here acts in game.
internal sealed class PlanPreview : IDisposable
{
    // Edits arrive on every frame while a field is dragged, so planning waits for this quiet time after the last one.
    private const long SettleMs = 250;

    private readonly Configuration configuration;
    private readonly ShoppingListStore lists;
    private readonly InventoryReader inventory;
    private readonly PriceService prices;

    private ScopeKey scopeKey;
    private ResolvedScope? scope;
    private DemandSet demands = DemandSet.Empty;
    private ShoppingList? demandList;
    private int demandListRevision = -1;
    private int demandInventoryRevision = -1;
    private bool demandHasTargets;
    private PlanningKey planningKey;
    private long seenPriceGeneration;
    private long changedAtMs;
    private long inputGeneration;
    private CancellationTokenSource? planning;
    private PreviewResult? latest;
    private volatile bool planningRunning;
    private bool pricesRequested;

    public PlanPreview(Configuration configuration, ShoppingListStore lists, InventoryReader inventory, PriceService prices)
    {
        this.configuration = configuration;
        this.lists = lists;
        this.inventory = inventory;
        this.prices = prices;
        Svc.Framework.Update += OnUpdate;
    }

    public ResolvedScope? Scope => scope;

    public bool Planning => planningRunning || changedAtMs != 0;

    public PriceStartResult LastPriceRequest { get; private set; } = PriceStartResult.Started;

    public long PriceRequestCount { get; private set; }

    public PreviewResult? LatestFor(ShoppingList? list)
    {
        var result = Volatile.Read(ref latest);
        return list is not null && result is not null && result.Input.ListId == list.Id ? result : null;
    }

    public void RequestPrices() => pricesRequested = true;

    public void Dispose()
    {
        Svc.Framework.Update -= OnUpdate;
        planning?.Cancel();
        planning?.Dispose();
        planning = null;
    }

    private void OnUpdate(IFramework framework)
    {
        RefreshScope();
        RefreshDemands();
        RefreshPlanningKey();
        RefreshPrices();
        if (pricesRequested)
        {
            pricesRequested = false;
            LastPriceRequest = StartFetch();
            PriceRequestCount++;
        }

        if (changedAtMs != 0 && Environment.TickCount64 - changedAtMs >= SettleMs)
        {
            changedAtMs = 0;
            StartPlanning();
        }
    }

    private void MarkChanged() => changedAtMs = Environment.TickCount64;

    private void RefreshScope()
    {
        var key = ReadScopeKey();
        if (key == scopeKey)
        {
            return;
        }

        scopeKey = key;
        scope = key.CurrentWorldId == 0 ? null : ScopeWorlds.Resolve(configuration);
        MarkChanged();
    }

    private ScopeKey ReadScopeKey()
    {
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return new ScopeKey(configuration.ScopePreset, 0, 0, configuration.ScopeRevision);
        }

        return new ScopeKey(configuration.ScopePreset, player.CurrentWorld.RowId, player.HomeWorld.RowId, configuration.ScopeRevision);
    }

    private void RefreshDemands()
    {
        var list = lists.Selected;
        if (list is null)
        {
            if (demandList is not null)
            {
                demandList = null;
                demands = DemandSet.Empty;
                MarkChanged();
            }

            return;
        }

        var inventoryRevision = demandHasTargets ? inventory.Revision : demandInventoryRevision;
        if (ReferenceEquals(list, demandList) && lists.Revision == demandListRevision && inventoryRevision == demandInventoryRevision)
        {
            return;
        }

        var analysis = lists.Analyze(list);
        var normalized = analysis.Normalized.Demands;
        demandHasTargets = HasTargets(normalized);
        var next = DemandBuilder.Build(normalized, demandHasTargets && inventory.HasData ? OwnedFor(analysis.ItemIds) : null);
        var listChanged = !ReferenceEquals(list, demandList);
        demandList = list;
        demandListRevision = lists.Revision;
        demandInventoryRevision = inventory.Revision;
        if (listChanged || !next.SameDemands(demands))
        {
            demands = next;
            MarkChanged();
        }
    }

    private Dictionary<uint, OwnedCount> OwnedFor(ReadOnlySpan<uint> itemIds)
    {
        var owned = new Dictionary<uint, OwnedCount>(itemIds.Length);
        for (var index = 0; index < itemIds.Length; index++)
        {
            if (inventory.TryGetOwned(itemIds[index], out var count))
            {
                owned[itemIds[index]] = count;
            }
        }

        return owned;
    }

    private static bool HasTargets(ReadOnlySpan<NormalizedDemand> normalized)
    {
        for (var index = 0; index < normalized.Length; index++)
        {
            if (normalized[index].Mode == QuantityMode.ReachInventory)
            {
                return true;
            }
        }

        return false;
    }

    // A mode change alone needs no new search: all three modes were planned on the same data, so the newest result
    // is republished with the other mode selected.
    private void RefreshPlanningKey()
    {
        var key = new PlanningKey(
            configuration.PlanningMode,
            configuration.MaxStops,
            configuration.StopPenaltyGil,
            configuration.CrossDataCenterPenaltyGil,
            configuration.FewestStopsPremiumPercent,
            configuration.ReturnHomeAfterRun);
        if (key == planningKey)
        {
            return;
        }

        var onlyMode = key with { Mode = planningKey.Mode } == planningKey;
        planningKey = key;
        var current = Volatile.Read(ref latest);
        if (onlyMode && changedAtMs == 0 && !planningRunning && current is not null && current.Generation == Volatile.Read(ref inputGeneration))
        {
            var input = current.Input with { Settings = current.Input.Settings with { Mode = ToPlanMode(key.Mode) } };
            Volatile.Write(ref latest, new PreviewResult { Input = input, Plans = current.Plans, PlannedAtUtc = current.PlannedAtUtc });
            return;
        }

        MarkChanged();
    }

    private void RefreshPrices()
    {
        var generation = prices.Latest?.Generation ?? 0;
        if (generation == seenPriceGeneration)
        {
            return;
        }

        seenPriceGeneration = generation;
        MarkChanged();
    }

    private PriceStartResult StartFetch()
    {
        if (scope is null)
        {
            return PriceStartResult.NoWorlds;
        }

        return prices.TryStart(scope.Allowed, demands.ItemIds);
    }

    private void StartPlanning()
    {
        if (demandList is null)
        {
            CancelPlanning();
            Volatile.Write(ref latest, null);
            return;
        }

        if (scope is null)
        {
            CancelPlanning();
            return;
        }

        var input = new PreviewInput
        {
            Generation = Interlocked.Increment(ref inputGeneration),
            ListId = demandList.Id,
            Demands = demands,
            Prices = prices.Latest,
            Worlds = scope.PlanWorlds,
            Settings = SettingsFor(configuration, scope),
        };
        CancelPlanning();
        planning = new CancellationTokenSource();
        var token = planning.Token;
        planningRunning = true;
        _ = Task.Run(() => Plan(input, token), token);
    }

    private void CancelPlanning()
    {
        planning?.Cancel();
        planning?.Dispose();
        planning = null;
        planningRunning = false;
    }

    private void Plan(PreviewInput input, CancellationToken token)
    {
        try
        {
            var result = PreviewRunner.Run(input, TimeProvider.System, token);
            if (input.Generation != Volatile.Read(ref inputGeneration))
            {
                return;
            }

            Volatile.Write(ref latest, result);
            planningRunning = false;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, "Planning the selected list failed");
            if (input.Generation == Volatile.Read(ref inputGeneration))
            {
                planningRunning = false;
            }
        }
    }

    public static PlanSettings SettingsFor(Configuration configuration, ResolvedScope resolved) => new()
    {
        Mode = ToPlanMode(configuration.PlanningMode),
        StopPenaltyGil = configuration.StopPenaltyGil,
        CrossDataCenterPenaltyGil = configuration.CrossDataCenterPenaltyGil,
        FewestStopsPremiumPercent = configuration.FewestStopsPremiumPercent,
        MaxStops = configuration.MaxStops,
        CurrentWorld = resolved.Current.ToPlanWorld(),
        HomeWorld = resolved.Home.ToPlanWorld(),
        ReturnHome = configuration.ReturnHomeAfterRun,
    };

    public static PlanMode ToPlanMode(PlanningMode mode) => mode switch
    {
        PlanningMode.Cheapest => PlanMode.Cheapest,
        PlanningMode.FewestStops => PlanMode.FewestStops,
        _ => PlanMode.Balanced,
    };

    public static PlanningMode ToPlanningMode(PlanMode mode) => mode switch
    {
        PlanMode.Cheapest => PlanningMode.Cheapest,
        PlanMode.FewestStops => PlanningMode.FewestStops,
        _ => PlanningMode.Balanced,
    };

    private readonly record struct ScopeKey(ScopePreset Preset, uint CurrentWorldId, uint HomeWorldId, int Revision);

    private readonly record struct PlanningKey(PlanningMode Mode, int MaxStops, long StopPenalty, long CrossDataCenterPenalty, int PremiumPercent, bool ReturnHome);
}

using AutoShopper.Core;
using AutoShopper.Core.Debug;
using AutoShopper.Core.Game.Watchers;
using AutoShopper.Core.Inventory;
using AutoShopper.Core.Items;
using AutoShopper.Core.Ledger;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Market;
using AutoShopper.Core.Preview;
using AutoShopper.Core.Pricing;
using AutoShopper.Core.Recipes;
using AutoShopper.Core.Shopping;
using AutoShopper.Core.Stats;
using AutoShopper.Core.Tasks;
using AutoShopper.Core.Travel;
using AutoShopper.Windows;
using AutoShopper.Windows.Shell;
using clib;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace AutoShopper;

public sealed class Plugin : IDalamudPlugin
{
    private const string GotoSubcommand = "goto";
    private const string BoardSubcommand = "board";
    private const string AddSubcommand = "add";
    private const string StartSubcommand = "start";
    private const string StopSubcommand = "stop";
    private const string NavmeshIpcProviderMarker = "Navmesh.IPCProvider";
    // Lifestream's own budget for a data center transfer is up to an hour; a plan older than this is a leftover, not a journey in flight.
    private static readonly TimeSpan PendingJourneyMaxAge = TimeSpan.FromMinutes(90);

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    internal static Plugin Instance { get; private set; } = null!;

    internal Configuration Configuration { get; }
    internal WindowSystem WindowSystem { get; } = new("AutoShopper");
    internal RunHistory History { get; }
    internal AutoShopController Controller { get; }
    internal ItemCatalog Items { get; }
    internal RecipeExpander Recipes { get; }
    internal InventoryReader Inventory { get; }
    internal ShoppingListStore Lists { get; }
    internal PriceService Prices { get; }
    internal PlanPreview Preview { get; }
    internal MarketServices Market { get; }
    internal ShopLedger Ledger { get; }
    internal CongestionWatcher Congestion { get; }
    internal RunSummaries Summaries { get; }

    private readonly DutyWatcher dutyWatcher;
    private readonly GmAlertWatcher gmAlertWatcher;
    private readonly AppWindow appWindow;
    private readonly CommandInfo primaryCommand;
    private readonly CommandInfo aliasCommand;

    // The settings file could not be read at load; said once in chat when the character is in game, with where the old
    // file was kept, null when it could not be.
    private bool configurationReset;
    private string? configurationBackupPath;
    // Set by a logout while a journey was pending, so only the login that ends that transfer picks the journey up.
    private bool journeyLogoutSeen;

    // A step that fails undoes the ones before it, since Dalamud never disposes a plugin whose constructor threw.
    public Plugin()
    {
        Instance = this;
        var completed = new Stack<Action>();
        try
        {
            ECommonsMain.Init(PluginInterface, this);
            completed.Push(ECommonsMain.Dispose);
            CLibMain.Init(PluginInterface, this, CLibModule.Automation);
            completed.Push(CLibMain.Dispose);

            Configuration = LoadConfiguration();
            DropStaleContinuation();
            DropStalePendingJourney();

            History = new RunHistory();
            Controller = new AutoShopController();
            Items = new ItemCatalog();
            Recipes = new RecipeExpander();
            Inventory = new InventoryReader();
            completed.Push(Inventory.Dispose);
            Lists = new ShoppingListStore(Configuration, Items, Inventory);
            Prices = new PriceService(typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0");
            completed.Push(Prices.Dispose);
            Preview = new PlanPreview(Configuration, Lists, Inventory, Prices);
            completed.Push(Preview.Dispose);
            Market = new MarketServices(Configuration);
            completed.Push(Market.Dispose);
            Ledger = new ShopLedger(PluginInterface.ConfigDirectory.FullName);
            Congestion = new CongestionWatcher();
            completed.Push(Congestion.Dispose);
            Summaries = new RunSummaries(PluginInterface.ConfigDirectory.FullName, Items);
            RotateLedger();
            dutyWatcher = new DutyWatcher();
            completed.Push(dutyWatcher.Dispose);
            gmAlertWatcher = new GmAlertWatcher();
            completed.Push(gmAlertWatcher.Dispose);

            InitializeLocalization();
            Fonts.Initialize(PluginInterface.UiBuilder, PluginDirectory);
            completed.Push(Fonts.Dispose);
            appWindow = new AppWindow(this);
            completed.Push(appWindow.Dispose);
            WindowSystem.AddWindow(appWindow);
            completed.Push(WindowSystem.RemoveAllWindows);

            primaryCommand = new CommandInfo(OnCommand) { HelpMessage = Loc.T(L.Plugin.CommandHelp) };
            aliasCommand = new CommandInfo(OnCommand) { HelpMessage = Loc.T(L.Plugin.CommandHelpAlias) };
            CommandManager.AddHandler(AshopConstants.PrimaryCommand, primaryCommand);
            completed.Push(() => CommandManager.RemoveHandler(AshopConstants.PrimaryCommand));
            CommandManager.AddHandler(AshopConstants.AliasCommand, aliasCommand);
            completed.Push(() => CommandManager.RemoveHandler(AshopConstants.AliasCommand));

            PluginInterface.UiBuilder.Draw += OnDraw;
            completed.Push(() => PluginInterface.UiBuilder.Draw -= OnDraw);
            PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
            completed.Push(() => PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi);
            PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
            completed.Push(() => PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi);

            Svc.ClientState.Login += OnLogin;
            completed.Push(() => Svc.ClientState.Login -= OnLogin);
            Svc.ClientState.Logout += OnLogout;
            completed.Push(() => Svc.ClientState.Logout -= OnLogout);
            if (Svc.ClientState.IsLoggedIn)
            {
                _ = Svc.Framework.RunOnFrameworkThread(OnLoadedInGame);
            }

            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }
        catch
        {
            Undo(completed);
            throw;
        }
    }

    private static string PluginDirectory => PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty;

    public void Dispose()
    {
        Controller.Shutdown();
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        PluginInterface.UiBuilder.Draw -= OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        Svc.ClientState.Login -= OnLogin;
        Svc.ClientState.Logout -= OnLogout;
        Configuration.SaveIfPending();

        WindowSystem.RemoveAllWindows();
        appWindow.Dispose();
        Fonts.Dispose();

        CommandManager.RemoveHandler(AshopConstants.PrimaryCommand);
        CommandManager.RemoveHandler(AshopConstants.AliasCommand);

        dutyWatcher.Dispose();
        gmAlertWatcher.Dispose();
        Preview.Dispose();
        Prices.Dispose();
        Inventory.Dispose();
        Market.Dispose();
        Congestion.Dispose();

        CLibMain.Dispose();
        ECommonsMain.Dispose();
    }

    public void OnLanguageChanged()
    {
        primaryCommand.HelpMessage = Loc.T(L.Plugin.CommandHelp);
        aliasCommand.HelpMessage = Loc.T(L.Plugin.CommandHelpAlias);
    }

    public void ToggleMainUi() => appWindow.Toggle();

    public void ToggleConfigUi() => appWindow.TogglePage(AppWindow.Page.Settings);

    public void ToggleAboutUi() => appWindow.TogglePage(AppWindow.Page.About);

    public void ToggleDependenciesUi() => appWindow.TogglePage(AppWindow.Page.Plugins);

    public void ToggleHistoryUi() => appWindow.TogglePage(AppWindow.Page.History);

    public void ToggleLogUi() => appWindow.TogglePage(AppWindow.Page.Log);

    public void ToggleChangelogUi() => appWindow.TogglePage(AppWindow.Page.Changelog);

    private void OnCommand(string command, string args)
    {
        var trimmed = args.Trim();
        if (trimmed.Equals("config", StringComparison.OrdinalIgnoreCase))
        {
            ToggleConfigUi();
        }
        else if (trimmed.Equals("about", StringComparison.OrdinalIgnoreCase))
        {
            ToggleAboutUi();
        }
        else if (trimmed.Equals("deps", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("dependencies", StringComparison.OrdinalIgnoreCase))
        {
            ToggleDependenciesUi();
        }
        else if (trimmed.Equals("stats", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("history", StringComparison.OrdinalIgnoreCase))
        {
            ToggleHistoryUi();
        }
        else if (trimmed.Equals("log", StringComparison.OrdinalIgnoreCase))
        {
            ToggleLogUi();
        }
        else if (trimmed.Equals("changelog", StringComparison.OrdinalIgnoreCase))
        {
            ToggleChangelogUi();
        }
        else if (trimmed.Equals(StartSubcommand, StringComparison.OrdinalIgnoreCase))
        {
            Controller.Start();
        }
        else if (trimmed.Equals(StopSubcommand, StringComparison.OrdinalIgnoreCase))
        {
            Controller.Stop();
        }
        else if (trimmed.Equals("pause", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("resume", StringComparison.OrdinalIgnoreCase))
        {
            Controller.TogglePause();
        }
        else if (trimmed.Equals("target", StringComparison.OrdinalIgnoreCase))
        {
            TargetDumper.Dump();
        }
        else if (HasSubcommand(trimmed, GotoSubcommand))
        {
            AutoGoto.HandleCommand(trimmed[GotoSubcommand.Length..].Trim(), Controller.Running);
        }
        else if (HasSubcommand(trimmed, BoardSubcommand))
        {
            AutoBoard.HandleCommand(trimmed[BoardSubcommand.Length..].Trim(), Controller.Running);
        }
        else if (HasSubcommand(trimmed, AddSubcommand))
        {
            AddItemCommand.Handle(trimmed[AddSubcommand.Length..].Trim(), Items, Lists);
        }
        else if (trimmed.Equals(MarketDebugCommands.StatusSubcommand, StringComparison.OrdinalIgnoreCase))
        {
            MarketDebugCommands.PrintStatus(Market);
        }
        else if (HasSubcommand(trimmed, MarketDebugCommands.LookSubcommand))
        {
            MarketDebugCommands.Look(trimmed[MarketDebugCommands.LookSubcommand.Length..].Trim(), Market, Items, Controller.Running);
        }
        else
        {
            ToggleMainUi();
        }
    }

    private void OnDraw()
    {
        WindowSystem.Draw();
        Configuration.FlushPendingSave();
    }

    private static bool HasSubcommand(string arguments, string subcommand)
        => arguments.StartsWith(subcommand, StringComparison.OrdinalIgnoreCase)
        && (arguments.Length == subcommand.Length || char.IsWhiteSpace(arguments[subcommand.Length]));

    // The navmesh plugin answers pathfind IPC on fire-and-forget tasks this plugin never gets a handle to. When one
    // faults, typically a query issued while the zone mesh is still building, the finalizer would rethrow it as noise.
    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
    {
        if (eventArgs.Observed)
        {
            return;
        }

        if (!eventArgs.Exception.ToString().Contains(NavmeshIpcProviderMarker, StringComparison.Ordinal))
        {
            return;
        }

        eventArgs.SetObserved();
        RunLog.Debug($"Observed a navmesh IPC task fault: {eventArgs.Exception.GetBaseException().Message}");
    }

    private void InitializeLocalization()
    {
        var directory = Path.Combine(PluginDirectory, "Localization");
        if (string.IsNullOrEmpty(Configuration.Language))
        {
            Configuration.Language = DetectLanguage();
            Configuration.Save();
        }

        Loc.Initialize(Configuration.Language, directory);
    }

    private static string DetectLanguage()
    {
        var dalamudLanguage = PluginInterface.UiLanguage;
        if (Languages.IsKnown(dalamudLanguage))
        {
            return Languages.Resolve(dalamudLanguage).Code;
        }

        switch (Svc.ClientState.ClientLanguage)
        {
            case Dalamud.Game.ClientLanguage.German: return Languages.German.Code;
            case Dalamud.Game.ClientLanguage.French: return Languages.French.Code;
            case Dalamud.Game.ClientLanguage.Japanese: return Languages.Japanese.Code;
        }

        var osLanguage = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
        return Languages.IsKnown(osLanguage) ? Languages.Resolve(osLanguage).Code : Languages.English.Code;
    }

    private void OnLogout(int type, int code)
    {
        journeyLogoutSeen = Configuration.PendingJourney is not null;
        Controller.OnLogout();
    }

    private static void Undo(Stack<Action> completed)
    {
        while (completed.Count > 0)
        {
            var step = completed.Pop();
            try
            {
                step();
            }
            catch (Exception)
            {
                // Loading has already failed; the error Dalamud reports is the one that stopped it.
            }
        }
    }

    // A settings file that cannot be read must not keep the plugin from loading: it is copied next to itself under a
    // dated name, the plugin starts from the defaults, and the chat says so once the character is in game.
    private Configuration LoadConfiguration()
    {
        Configuration? loaded = null;
        var unreadable = false;
        try
        {
            loaded = PluginInterface.GetPluginConfig() as Configuration;
            unreadable = loaded is null && PluginInterface.ConfigFile.Exists;
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, "The settings file could not be read");
            unreadable = true;
        }

        if (unreadable)
        {
            configurationReset = true;
            configurationBackupPath = BackUpUnreadableConfiguration();
        }

        var configuration = loaded ?? Configuration.CreateFresh();
        if (configuration.Migrate())
        {
            configuration.Save();
        }

        return configuration;
    }

    private static string? BackUpUnreadableConfiguration()
    {
        var file = PluginInterface.ConfigFile;
        var backup = Path.Combine(file.DirectoryName ?? string.Empty, $"{Path.GetFileNameWithoutExtension(file.Name)}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}{file.Extension}");
        try
        {
            File.Copy(file.FullName, backup, overwrite: false);
            RunLog.Warning($"The unreadable settings file was kept as {backup}; starting from default settings.");
            return backup;
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, $"The unreadable settings file could not be kept as {backup}; starting from default settings");
            return null;
        }
    }

    private void ShowConfigurationNotice()
    {
        if (!configurationReset)
        {
            return;
        }

        configurationReset = false;
        var notice = configurationBackupPath is { } backup ? Loc.T(L.Plugin.ConfigReset, backup) : Loc.T(L.Plugin.ConfigResetUnsaved);
        Svc.Chat.PrintError($"{AshopConstants.LogPrefix} {notice}");
    }

    // A pending journey at load belongs to a transfer this plugin did not live through, and travel never starts again
    // on its own after a reload.
    private void DropStalePendingJourney()
    {
        if (Configuration.PendingJourney is not { } stale)
        {
            return;
        }

        RunLog.Info($"Dropping the pending journey to {stale.World}: the plugin did not live through its transfer.");
        Configuration.ClearPendingJourney();
    }

    // A continuation outlives its run only when the plugin did not: such a run waits for the player's Resume.
    private void DropStaleContinuation()
    {
        if (Configuration.PendingContinuation is not { } stale)
        {
            return;
        }

        RunLog.Info($"Dropping the transfer continuation of run {stale.RunId} to {stale.TargetWorld}: the plugin did not live through the transfer, so the run waits for Resume.");
        Configuration.ClearPendingContinuation();
    }

    // Finished runs move to a dated archive at load, so the journal only holds what may still need an answer.
    private void RotateLedger()
    {
        var rotation = Ledger.Rotate(DateTime.UtcNow);
        if (!rotation.Succeeded)
        {
            RunLog.Warning($"Journal: rotating the purchase journal failed: {rotation.Error}.");
            return;
        }

        if (rotation.ArchivedRuns > 0)
        {
            RunLog.Info($"Journal: {rotation.ArchivedRuns} finished run(s) moved to {rotation.ArchivePath}; {rotation.KeptRuns} kept.");
        }
    }

    // A purchase still held by the watcher with no run waiting on it has no one left to answer to.
    private void OnLogin()
    {
        Controller.OnLogin();
        if (Controller.ShopRun is not { InFlight: not null })
        {
            Market.Watcher.AbandonAny();
        }

        if (journeyLogoutSeen)
        {
            journeyLogoutSeen = false;
            ResumePendingJourney();
        }

        OnLoadedInGame();
    }

    // Loaded while already in game: the window may open and a notice is said, but nothing travels or resumes.
    private void OnLoadedInGame()
    {
        ShowConfigurationNotice();
        if (!Configuration.AutoShowOnLogin)
        {
            return;
        }

        appWindow.Show(AppWindow.Page.Shop);
    }

    // A data center transfer logs the character out; when the task did not live through that, the journey it wrote
    // first is picked up at the login that ends the transfer, and only for the character it belongs to. A task that
    // did live through it keeps the journey for itself.
    private void ResumePendingJourney()
    {
        if (Configuration.PendingJourney is not { } plan)
        {
            return;
        }

        if (plan.CharacterContentId == 0 || plan.CharacterContentId != Svc.PlayerState.ContentId)
        {
            RunLog.Info($"Dropping the pending journey to {plan.World}: the character logged in is not the one it belongs to.");
            Configuration.ClearPendingJourney();
            return;
        }

        if (Controller.Running)
        {
            RunLog.Info("A journey is pending and a task is already running; leaving it to that task.");
            return;
        }

        var age = DateTime.UtcNow - plan.StartedAtUtc;
        if (age > PendingJourneyMaxAge)
        {
            RunLog.Info($"Dropping the pending journey to {plan.World}: it started {age.TotalMinutes:F0} minutes ago.");
            Configuration.ClearPendingJourney();
            return;
        }

        RunLog.Info($"Resuming the journey to {plan.World} after login.");
        AutoJourney.Resume(plan);
    }
}

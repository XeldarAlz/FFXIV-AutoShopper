using AutoShopper.Core;
using AutoShopper.Core.Debug;
using AutoShopper.Core.Game.Watchers;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Stats;
using AutoShopper.Core.Tasks;
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

    private readonly DutyWatcher dutyWatcher;
    private readonly GmAlertWatcher gmAlertWatcher;
    private readonly AppWindow appWindow;
    private readonly CommandInfo primaryCommand;
    private readonly CommandInfo aliasCommand;

    public Plugin()
    {
        Instance = this;

        ECommonsMain.Init(PluginInterface, this);
        CLibMain.Init(PluginInterface, this, CLibModule.Automation);
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? Configuration.CreateFresh();
        if (Configuration.Migrate())
        {
            Configuration.Save();
        }

        History = new RunHistory();
        Controller = new AutoShopController();
        dutyWatcher = new DutyWatcher();
        gmAlertWatcher = new GmAlertWatcher();

        InitializeLocalization();
        Fonts.Initialize(PluginInterface.UiBuilder, PluginDirectory);
        appWindow = new AppWindow(this);
        WindowSystem.AddWindow(appWindow);

        primaryCommand = new CommandInfo(OnCommand) { HelpMessage = Loc.T(L.Plugin.CommandHelp) };
        aliasCommand = new CommandInfo(OnCommand) { HelpMessage = Loc.T(L.Plugin.CommandHelpAlias) };
        CommandManager.AddHandler(AshopConstants.PrimaryCommand, primaryCommand);
        CommandManager.AddHandler(AshopConstants.AliasCommand, aliasCommand);

        PluginInterface.UiBuilder.Draw += OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Svc.ClientState.Login += OnLogin;
        if (Svc.ClientState.IsLoggedIn)
        {
            OnLogin();
        }
    }

    private static string PluginDirectory => PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty;

    public void Dispose()
    {
        Controller.Stop();
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        PluginInterface.UiBuilder.Draw -= OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        Svc.ClientState.Login -= OnLogin;
        Configuration.SaveIfPending();

        WindowSystem.RemoveAllWindows();
        appWindow.Dispose();
        Fonts.Dispose();

        CommandManager.RemoveHandler(AshopConstants.PrimaryCommand);
        CommandManager.RemoveHandler(AshopConstants.AliasCommand);

        dutyWatcher.Dispose();
        gmAlertWatcher.Dispose();

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

    private void OnLogin()
    {
        ResumePendingJourney();
        if (!Configuration.AutoShowOnLogin)
        {
            return;
        }

        appWindow.Show(AppWindow.Page.Shop);
    }

    // A data center transfer logs the character out; when the task did not live through that, the journey it wrote
    // first is picked up here. A task that did live through it keeps the journey for itself.
    private void ResumePendingJourney()
    {
        if (Configuration.PendingJourney is not { } plan)
        {
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

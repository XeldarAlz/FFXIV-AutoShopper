using AutoShopper.Core.Localization;
using AutoShopper.Core.Tasks;
using AutoShopper.Windows.Components;
using AutoShopper.Windows.Sections;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoShopper.Windows.Shell;

internal static class ActionDock
{
    private const float PadX = 18f;
    private const float ButtonGap = 8f;

    public static void Draw(Plugin plugin, Vector2 size, float windowRounding)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        Dock.Background(drawList, origin, end, windowRounding);

        var padX = PadX * scale;
        var buttonHeight = Layout.HeroButtonHeight * scale;
        var innerWidth = size.X - padX * 2f;
        ImGui.SetCursorScreenPos(new Vector2(origin.X + padX, origin.Y + (size.Y - buttonHeight) * 0.5f));

        if (plugin.Controller.Running)
        {
            DrawRunControls(plugin, innerWidth);
        }
        else
        {
            DrawStart(plugin, innerWidth);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
    }

    private static void DrawRunControls(Plugin plugin, float innerWidth)
    {
        var controller = plugin.Controller;
        var gap = ButtonGap * ImGuiHelpers.GlobalScale;
        var half = (innerWidth - gap) * 0.5f;

        if (PauseButton.Draw(controller.PauseReason, half))
        {
            controller.TogglePause();
        }

        ImGui.SameLine(0f, gap);

        var session = controller.SessionSnapshot;
        var state = controller.Stopping ? Loc.T(L.Shop.StateStopping) : controller.Paused ? Loc.T(L.Shop.StatePaused) : Loc.T(L.Shop.StateRunning);
        var stopSub = session is null ? state : Loc.T(L.Shop.StopSub, state, Formatting.Elapsed(session.Elapsed));
        if (StopButton.Draw(stopSub, half))
        {
            controller.Stop();
        }
    }

    private static void DrawStart(Plugin plugin, float innerWidth)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var configuration = plugin.Configuration;
        var height = Layout.HeroButtonHeight * scale;
        if (DryRunControl.Confirming)
        {
            DryRunControl.DrawConfirmation(configuration, innerWidth, height);
            return;
        }

        var gap = ButtonGap * scale;
        var tileWidth = DryRunControl.TileWidth(innerWidth);
        var readiness = ShopLauncher.Assess();
        if (StartButton.Draw(ShopLauncher.Sublabel(), readiness == Readiness.Ready, ShopLauncher.Reason(readiness), innerWidth - tileWidth - gap))
        {
            ShopLauncher.Start();
        }

        ImGui.SameLine(0f, gap);
        DryRunControl.DrawTile(configuration, tileWidth, height);
    }
}

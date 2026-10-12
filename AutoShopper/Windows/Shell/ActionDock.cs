using AutoShopper.Core.Localization;
using AutoShopper.Windows.Components;
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
            DrawStart(innerWidth);
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
        var state = controller.Paused ? Loc.T(L.Shop.StatePaused) : Loc.T(L.Shop.StateRunning);
        var stopSub = session is null ? state : Loc.T(L.Shop.StopSub, state, Formatting.Elapsed(session.Elapsed));
        if (StopButton.Draw(stopSub, half))
        {
            controller.Stop();
        }
    }

    private static void DrawStart(float innerWidth)
    {
        // No readiness enables the button yet: the shop run task arrives with the shopping lists.
        var readiness = ShopLauncher.Assess();
        if (StartButton.Draw(ShopLauncher.Sublabel(), false, ShopLauncher.Reason(readiness), innerWidth))
        {
            ShopLauncher.Start();
        }
    }
}

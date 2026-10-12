using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using ECommons.DalamudServices;
using System.Numerics;

namespace AutoShopper.Windows.Components;

internal static class ItemIcon
{
    private const float Rounding = 5f;

    // Dalamud caches game icons by lookup and loads them in the background, so drawing one every frame costs a
    // dictionary hit; until the texture is ready an empty frame holds its place.
    public static void Draw(ImDrawListPtr drawList, uint iconId, Vector2 min, float size, bool highQuality = false)
    {
        var max = min + new Vector2(size, size);
        var rounding = Rounding * ImGuiHelpers.GlobalScale;
        Paint.Fill(drawList, min, max, Styling.WithAlpha(Styling.Surface0, 0.9f), rounding);
        if (iconId != 0)
        {
            var texture = Svc.Texture.GetFromGameIcon(new GameIconLookup(iconId, highQuality)).GetWrapOrEmpty();
            drawList.AddImageRounded(texture.Handle, min, max, Vector2.Zero, Vector2.One, Paint.Col(Vector4.One), rounding, ImDrawFlags.RoundCornersAll);
        }

        Paint.Stroke(drawList, min, max, Styling.WithAlpha(Styling.BorderDim, 0.55f), rounding);
    }
}

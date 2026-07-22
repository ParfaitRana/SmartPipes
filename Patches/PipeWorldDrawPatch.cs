using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SmartPipes.Framework;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;
using SObject = StardewValley.Object;

namespace SmartPipes.Patches;

[HarmonyPatch(typeof(SObject), nameof(SObject.draw), new[] { typeof(SpriteBatch), typeof(int), typeof(int), typeof(float) })]
internal static class PipeWorldDrawPatch
{
    private const string TextureAsset = "Mods/ParfaitRana.SmartPipes/Pipes";

    private static bool Prefix(SObject __instance, SpriteBatch spriteBatch, int x, int y, float alpha)
    {
        if (__instance.QualifiedItemId is not (ItemIds.Pipe or ItemIds.LegacyPipe))
            return true;

        if (PipeVisibilityPatch.ShouldShowPipes?.Invoke() == false || __instance.Location is not GameLocation location)
            return false;

        int mask = 0;
        if (IsConnected(location, x, y - 1))
            mask |= 1;
        if (IsConnected(location, x + 1, y))
            mask |= 2;
        if (IsConnected(location, x, y + 1))
            mask |= 4;
        if (IsConnected(location, x - 1, y))
            mask |= 8;

        Texture2D texture = Game1.content.Load<Texture2D>(TextureAsset);
        Rectangle source = new((mask % 4) * 16, (mask / 4) * 16, 16, 16);
        Vector2 position = new(x * Game1.tileSize - Game1.viewport.X, y * Game1.tileSize - Game1.viewport.Y);
        float layerDepth = Math.Max(0f, ((y + 1) * Game1.tileSize - 24) / 10000f);
        spriteBatch.Draw(texture, position, source, Color.White * alpha, 0f, Vector2.Zero, Game1.pixelZoom, SpriteEffects.None, layerDepth);
        return false;
    }

    private static bool IsConnected(GameLocation location, int x, int y)
    {
        if (location.Objects.TryGetValue(new Vector2(x, y), out SObject? target))
        {
            return target.QualifiedItemId is ItemIds.Pipe or ItemIds.LegacyPipe
                || target.modData.ContainsKey(ModDataKeys.Port)
                || target.modData.ContainsKey(ModDataKeys.LegacyPort)
                || target.modData.ContainsKey(ModDataKeys.ShippingValve);
        }

        if (location is not Farm farm)
            return false;

        Point center = new(x * Game1.tileSize + Game1.tileSize / 2, y * Game1.tileSize + Game1.tileSize / 2);
        return farm.buildings
            .OfType<ShippingBin>()
            .Any(bin => bin.modData.ContainsKey(ModDataKeys.ShippingValve) && bin.GetBoundingBox().Contains(center));
    }
}

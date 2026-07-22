using Microsoft.Xna.Framework;
using SmartPipes.Framework;
using SmartPipes.Models;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace SmartPipes.Services;

internal sealed class ShippingValveService
{
    public event Action? Changed;

    public PortSettings? Get(ShippingBin shippingBin)
    {
        shippingBin.modData.TryGetValue(ModDataKeys.ShippingValve, out string? raw);
        return PortSettings.Deserialize(raw);
    }

    public void Set(ShippingBin shippingBin, PortSettings settings)
    {
        shippingBin.modData[ModDataKeys.ShippingValve] = settings.Serialize();
        this.Changed?.Invoke();
    }

    public bool Remove(ShippingBin shippingBin)
    {
        bool removed = shippingBin.modData.Remove(ModDataKeys.ShippingValve);
        if (removed)
            this.Changed?.Invoke();
        return removed;
    }

    public bool IsMiniShippingBin(SObject target)
    {
        return target is Chest chest && chest.SpecialChestType == Chest.SpecialChestTypes.MiniShippingBin;
    }

    public PortSettings? Get(SObject miniShippingBin)
    {
        if (!this.IsMiniShippingBin(miniShippingBin))
            return null;

        miniShippingBin.modData.TryGetValue(ModDataKeys.ShippingValve, out string? raw);
        return PortSettings.Deserialize(raw);
    }

    public void Set(SObject miniShippingBin, PortSettings settings)
    {
        if (this.IsMiniShippingBin(miniShippingBin))
        {
            miniShippingBin.modData[ModDataKeys.ShippingValve] = settings.Serialize();
            this.Changed?.Invoke();
        }
    }

    public bool Remove(SObject miniShippingBin)
    {
        bool removed = this.IsMiniShippingBin(miniShippingBin)
            && miniShippingBin.modData.Remove(ModDataKeys.ShippingValve);
        if (removed)
            this.Changed?.Invoke();
        return removed;
    }

    public bool TryFindAt(Farm farm, Vector2 tile, out ShippingBin shippingBin)
    {
        Point cursor = new((int)(tile.X * Game1.tileSize + Game1.tileSize / 2), (int)(tile.Y * Game1.tileSize + Game1.tileSize / 2));
        shippingBin = farm.buildings
            .OfType<ShippingBin>()
            .FirstOrDefault(bin => bin.GetBoundingBox().Contains(cursor))!;
        return shippingBin is not null;
    }

    public IEnumerable<(ShippingBin Bin, PortSettings Settings)> GetInstalled(Farm farm)
    {
        foreach (ShippingBin bin in farm.buildings.OfType<ShippingBin>())
        {
            PortSettings? settings = this.Get(bin);
            if (settings is not null)
                yield return (bin, settings);
        }
    }

    public bool TryFindConnection(ShippingBin shippingBin, IReadOnlyDictionary<Vector2, int> pipeDistances, out Vector2 endpointTile, out int distance)
    {
        Rectangle bounds = shippingBin.GetBoundingBox();
        int left = bounds.Left / Game1.tileSize;
        int right = (bounds.Right - 1) / Game1.tileSize;
        int top = bounds.Top / Game1.tileSize;
        int bottom = (bounds.Bottom - 1) / Game1.tileSize;

        endpointTile = new Vector2(left, bottom);
        distance = int.MaxValue;
        foreach ((Vector2 pipe, int pipeDistance) in pipeDistances)
        {
            int x = (int)pipe.X;
            int y = (int)pipe.Y;
            bool adjacent = (x >= left && x <= right && (y == top - 1 || y == bottom + 1))
                || (y >= top && y <= bottom && (x == left - 1 || x == right + 1));
            if (!adjacent || pipeDistance + 1 >= distance)
                continue;

            distance = pipeDistance + 1;
            endpointTile = new Vector2(Math.Clamp(x, left, right), Math.Clamp(y, top, bottom));
        }

        return distance < int.MaxValue;
    }

    public SObject CreateEndpointObject(PortSettings settings)
    {
        SObject endpoint = (SObject)ItemRegistry.Create(ItemIds.ShippingValve);
        endpoint.modData[ModDataKeys.Port] = settings.Serialize();
        return endpoint;
    }
}

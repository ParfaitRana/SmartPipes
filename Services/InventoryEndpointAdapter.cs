using StardewValley;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace SmartPipes.Services;

internal static class InventoryEndpointAdapter
{
    public const string AutoGrabberId = "(BC)165";

    public static bool IsMiniShippingBin(SObject endpoint)
    {
        return endpoint is Chest chest && chest.SpecialChestType == Chest.SpecialChestTypes.MiniShippingBin;
    }

    public static bool CanExtract(SObject endpoint) => TryGetItems(endpoint, out _);

    public static bool TryGetItems(SObject endpoint, out IList<Item> items)
    {
        if (endpoint is Chest chest && !IsMiniShippingBin(endpoint))
        {
            items = chest.Items;
            return true;
        }

        if (endpoint.QualifiedItemId == AutoGrabberId && endpoint.heldObject.Value is Chest autoGrabberInventory)
        {
            items = autoGrabberInventory.Items;
            return true;
        }

        items = null!;
        return false;
    }
}

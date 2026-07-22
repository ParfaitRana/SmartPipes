using SmartPipes.Framework;
using SmartPipes.Models;
using StardewValley;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace SmartPipes.Services;

internal sealed class PortService
{
    public event Action? Changed;

    public bool IsValidTarget(SObject target)
    {
        return (target is Chest || target.bigCraftable.Value)
            && !InventoryEndpointAdapter.IsMiniShippingBin(target);
    }

    public PortSettings? Get(SObject target)
    {
        target.modData.TryGetValue(ModDataKeys.Port, out string? raw);
        return PortSettings.Deserialize(raw);
    }

    public void Set(SObject target, PortSettings settings)
    {
        target.modData[ModDataKeys.Port] = settings.Serialize();
        this.Changed?.Invoke();
    }

    public bool Remove(SObject target)
    {
        bool removed = target.modData.Remove(ModDataKeys.Port);
        if (removed)
            this.Changed?.Invoke();
        return removed;
    }

    public PortSettings CreateDefault(SObject target)
    {
        bool isStorageSource = target is Chest || target.QualifiedItemId == InventoryEndpointAdapter.AutoGrabberId;
        bool isOutputOnlyMachine = VanillaArtisanMachineAdapter.IsOutputOnly(target.QualifiedItemId);
        return new PortSettings
        {
            Mode = isStorageSource || isOutputOnlyMachine ? PortMode.Output : PortMode.Both,
            Priority = isStorageSource ? 0 : 10
        };
    }
}

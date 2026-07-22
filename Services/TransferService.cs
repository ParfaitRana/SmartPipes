using SmartPipes.Models;
using SmartPipes.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace SmartPipes.Services;

internal sealed class TransferService
{
    private readonly ModConfig config;
    private readonly IMonitor monitor;
    private readonly VanillaArtisanMachineAdapter machines;
    private readonly Dictionary<string, int> sourceCursors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> itemCursors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> targetCursors = new(StringComparer.Ordinal);

    public TransferService(ModConfig config, IMonitor monitor)
    {
        this.config = config;
        this.monitor = monitor;
        this.machines = new VanillaArtisanMachineAdapter();
    }

    public TransferResult? ProcessOne(PipeNetwork network)
    {
        List<NetworkEndpoint> sources = network.Endpoints
            .Where(endpoint => endpoint.Settings.CanExtract
                && (InventoryEndpointAdapter.CanExtract(endpoint.Object) || this.machines.HasReadyOutput(endpoint.Object)))
            .ToList();
        if (sources.Count == 0)
            return null;

        int start = this.sourceCursors.GetValueOrDefault(network.Id) % sources.Count;
        for (int offset = 0; offset < sources.Count; offset++)
        {
            int index = (start + offset) % sources.Count;
            NetworkEndpoint source = sources[index];
            TransferResult? result = this.machines.HasReadyOutput(source.Object)
                ? this.TryCollectMachineOutput(network, source)
                : this.TryTransferInventorySource(network, source);
            if (result is null)
                continue;

            this.sourceCursors[network.Id] = (index + 1) % sources.Count;
            return result;
        }

        this.sourceCursors[network.Id] = (start + 1) % sources.Count;
        return null;
    }

    private TransferResult? TryTransferInventorySource(PipeNetwork network, NetworkEndpoint source)
    {
        if (!InventoryEndpointAdapter.TryGetItems(source.Object, out IList<Item>? sourceItems))
            return null;

        Item[] candidateItems = sourceItems.Where(item => item is not null).Cast<Item>().ToArray();
        if (candidateItems.Length == 0)
            return null;

        string itemCursorKey = $"{network.Id}|{source.Tile.X},{source.Tile.Y}";
        int itemStart = this.itemCursors.GetValueOrDefault(itemCursorKey) % candidateItems.Length;
        for (int itemOffset = 0; itemOffset < candidateItems.Length; itemOffset++)
        {
            int itemIndex = (itemStart + itemOffset) % candidateItems.Length;
            Item item = candidateItems[itemIndex];
            if (!source.Settings.Allows(item) || !HasExtractableQuantity(sourceItems, item, source.Settings.MinimumKeep))
                continue;

            IEnumerable<NetworkEndpoint> targets = this.OrderTargetsFairly(network, source, network.Endpoints
                .Where(target => target.Tile != source.Tile)
                .Where(target => target.Settings.CanInsert)
                .Where(target => target.Object is Chest || this.machines.Supports(target.Object) || IsShippingValve(target.Object))
                .Where(target => target.Settings.Allows(item))
                .Where(target => source.Settings.Mode != PortMode.Both || target.Settings.Priority > source.Settings.Priority)
            );

            foreach (NetworkEndpoint target in targets)
            {
                if (IsShippingValve(target.Object))
                {
                    int shippingAvailable = Count(sourceItems, item.QualifiedItemId) - Math.Max(0, source.Settings.MinimumKeep);
                    int shipAmount = Math.Min(Math.Max(1, this.config.ItemsPerTransfer), Math.Min(item.Stack, shippingAvailable));
                    int shippedCount = shipAmount > 0
                        ? this.TryShip(network.Location, target.Object, item, shipAmount)
                        : 0;
                    if (shippedCount <= 0)
                        continue;

                    string itemName = item.DisplayName;
                    item.Stack -= shippedCount;
                    if (item.Stack <= 0)
                        sourceItems.Remove(item);
                    this.itemCursors[itemCursorKey] = (itemIndex + 1) % candidateItems.Length;
                    return new TransferResult(itemName, target.Object.DisplayName, shippedCount);
                }

                if (this.machines.Supports(target.Object))
                {
                    if (!this.machines.TryInsert(
                            target.Object,
                            item,
                            sourceItems,
                            source.Settings.Allows,
                            source.Settings.MinimumKeep,
                            Game1.player,
                            out MachineInputConsumption? consumption)
                        || consumption is null)
                    {
                        continue;
                    }

                    string inputName = item.DisplayName;
                    RemoveStackable(sourceItems, item, consumption.PrimaryCount);
                    foreach (MachineAdditionalConsumption additional in consumption.AdditionalItems)
                        RemoveById(sourceItems, additional.ItemId, additional.Count, source.Settings.Allows);
                    this.itemCursors[itemCursorKey] = (itemIndex + 1) % candidateItems.Length;
                    return new TransferResult(inputName, target.Object.DisplayName, consumption.PrimaryCount);
                }

                Chest targetChest = (Chest)target.Object;
                if (target.Settings.MaximumStock > 0 && Count(targetChest, item.QualifiedItemId) >= target.Settings.MaximumStock)
                    continue;

                int available = Count(sourceItems, item.QualifiedItemId) - Math.Max(0, source.Settings.MinimumKeep);
                int targetCapacity = target.Settings.MaximumStock > 0
                    ? target.Settings.MaximumStock - Count(targetChest, item.QualifiedItemId)
                    : int.MaxValue;
                int amount = Math.Min(Math.Max(1, this.config.ItemsPerTransfer), Math.Min(item.Stack, Math.Min(available, targetCapacity)));
                if (amount <= 0)
                    continue;

                Item moving = item.getOne();
                moving.Stack = amount;

                Item? leftover;
                try
                {
                    leftover = targetChest.addItem(moving);
                }
                catch (Exception ex)
                {
                    this.monitor.Log($"Transfer target rejected {item.QualifiedItemId}: {ex}", LogLevel.Error);
                    continue;
                }

                int accepted = amount - (leftover?.Stack ?? 0);
                if (accepted <= 0)
                    continue;

                item.Stack -= accepted;
                if (item.Stack <= 0)
                    sourceItems.Remove(item);

                this.itemCursors[itemCursorKey] = (itemIndex + 1) % candidateItems.Length;
                return new TransferResult(item.DisplayName, target.Object.DisplayName, accepted);
            }
        }

        this.itemCursors[itemCursorKey] = (itemStart + 1) % candidateItems.Length;
        return null;
    }

    private TransferResult? TryCollectMachineOutput(PipeNetwork network, NetworkEndpoint source)
    {
        Item? output = this.machines.GetOutput(source.Object, Game1.player);
            // A bidirectional machine port's filter describes what may be loaded into the
            // machine. Finished goods are governed by the destination port's filter instead;
            // otherwise an input whitelist (for example, fruit) would trap the output.
        if (output is null)
            return null;

        IEnumerable<NetworkEndpoint> targets = this.OrderTargetsFairly(network, source, network.Endpoints
                .Where(target => target.Tile != source.Tile)
                .Where(target => target.Settings.CanInsert && (target.Object is Chest || IsShippingValve(target.Object)))
                .Where(target => target.Settings.Allows(output))
        );

        foreach (NetworkEndpoint target in targets)
            {
                if (IsShippingValve(target.Object))
                {
                    int shipAmount = Math.Min(output.Stack, Math.Max(1, this.config.ItemsPerTransfer));
                    Item shipItem = output.getOne();
                    shipItem.Stack = shipAmount;
                    int shippedCount = this.TryShip(network.Location, target.Object, shipItem, shipAmount);
                    if (shippedCount <= 0)
                        continue;

                    string shippedOutputName = output.DisplayName;
                    this.machines.RemoveAcceptedOutput(source.Object, shippedCount, Game1.player);
                    return new TransferResult(shippedOutputName, target.Object.DisplayName, shippedCount);
                }

                Chest targetChest = (Chest)target.Object;
                int currentStock = Count(targetChest, output.QualifiedItemId);
                int targetCapacity = target.Settings.MaximumStock > 0
                    ? target.Settings.MaximumStock - currentStock
                    : int.MaxValue;
                int amount = Math.Min(output.Stack, Math.Min(Math.Max(1, this.config.ItemsPerTransfer), targetCapacity));
                if (amount <= 0)
                    continue;

                Item moving = output.getOne();
                moving.Stack = amount;
                Item? leftover;
                try
                {
                    leftover = targetChest.addItem(moving);
                }
                catch (Exception ex)
                {
                    this.monitor.Log($"Machine output target rejected {output.QualifiedItemId}: {ex}", LogLevel.Error);
                    continue;
                }

                int accepted = amount - (leftover?.Stack ?? 0);
                if (accepted <= 0)
                    continue;

                string outputName = output.DisplayName;
                this.machines.RemoveAcceptedOutput(source.Object, accepted, Game1.player);
                return new TransferResult(outputName, target.Object.DisplayName, accepted);
        }

        return null;
    }

    private IEnumerable<NetworkEndpoint> OrderTargetsFairly(
        PipeNetwork network,
        NetworkEndpoint source,
        IEnumerable<NetworkEndpoint> candidates)
    {
        foreach (IGrouping<int, NetworkEndpoint> priorityGroup in candidates
                     .GroupBy(target => target.Settings.Priority)
                     .OrderByDescending(group => group.Key))
        {
            List<NetworkEndpoint> targets = priorityGroup
                .OrderBy(target => target.Distance)
                .ThenBy(target => target.Tile.Y)
                .ThenBy(target => target.Tile.X)
                .ToList();
            if (targets.Count == 0)
                continue;

            string key = $"{network.Id}|{source.Tile.X},{source.Tile.Y}|{priorityGroup.Key}";
            int start = this.targetCursors.GetValueOrDefault(key) % targets.Count;
            this.targetCursors[key] = (start + 1) % targets.Count;
            for (int offset = 0; offset < targets.Count; offset++)
                yield return targets[(start + offset) % targets.Count];
        }
    }

    private static bool IsShippingValve(SObject target)
    {
        return target.QualifiedItemId == ItemIds.ShippingValve
            || target.modData.ContainsKey(ModDataKeys.ShippingValve);
    }

    private int TryShip(GameLocation location, SObject target, Item item, int amount)
    {
        if (amount <= 0)
            return 0;

        try
        {
            Item shippingItem = item.getOne();
            shippingItem.Stack = amount;

            if (target is Chest miniShippingBin
                && miniShippingBin.SpecialChestType == Chest.SpecialChestTypes.MiniShippingBin)
            {
                Item? leftover = miniShippingBin.addItem(shippingItem);
                return amount - (leftover?.Stack ?? 0);
            }

            if (location is not Farm)
                return 0;

            Game1.getFarm().getShippingBin(Game1.player).Add(shippingItem);
            return amount;
        }
        catch (Exception ex)
        {
            this.monitor.Log($"Shipping bin rejected {item.QualifiedItemId}: {ex}", LogLevel.Error);
            return 0;
        }
    }

    private static bool HasExtractableQuantity(IList<Item> items, Item item, int minimumKeep)
    {
        return Count(items, item.QualifiedItemId) > Math.Max(0, minimumKeep);
    }

    private static int Count(Chest chest, string qualifiedItemId)
    {
        return Count(chest.Items, qualifiedItemId);
    }

    private static int Count(IList<Item> items, string qualifiedItemId)
    {
        return items
            .Where(item => item?.QualifiedItemId == qualifiedItemId)
            .Sum(item => item?.Stack ?? 0);
    }

    private static int CountStackable(IList<Item> items, Item sample)
    {
        return items
            .Where(item => item is not null && sample.canStackWith(item))
            .Sum(item => item?.Stack ?? 0);
    }

    private static void RemoveStackable(IList<Item> items, Item sample, int amount)
    {
        int remaining = amount;
        foreach (Item item in items.Where(item => item is not null && sample.canStackWith(item)).ToArray())
        {
            int removed = Math.Min(item.Stack, remaining);
            item.Stack -= removed;
            remaining -= removed;
            if (item.Stack <= 0)
                items.Remove(item);
            if (remaining <= 0)
                return;
        }

        throw new InvalidOperationException($"Committed a machine input without {amount} matching source items.");
    }

    private static void RemoveById(
        IList<Item> items,
        string itemId,
        int amount,
        Func<Item, bool> sourceAllows)
    {
        int remaining = amount;
        string? qualifiedItemId = ItemRegistry.QualifyItemId(itemId);
        foreach (Item item in items
                     .Where(item => item is not null
                         && sourceAllows(item)
                         && (item.QualifiedItemId == itemId
                             || item.QualifiedItemId == qualifiedItemId
                             || item.ItemId == itemId))
                     .ToArray())
        {
            int removed = Math.Min(item.Stack, remaining);
            item.Stack -= removed;
            remaining -= removed;
            if (item.Stack <= 0)
                items.Remove(item);
            if (remaining <= 0)
                return;
        }

        throw new InvalidOperationException($"Committed a machine input without {amount} of required item {itemId}.");
    }
}

internal sealed record TransferResult(string ItemName, string TargetName, int Amount);

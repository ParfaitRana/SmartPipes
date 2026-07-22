using StardewValley;
using StardewValley.GameData.Machines;
using StardewValley.Inventories;
using SObject = StardewValley.Object;

namespace SmartPipes.Services;

/// <summary>Safely interacts with the vanilla artisan machines explicitly verified by Smart Pipes.</summary>
internal sealed class VanillaArtisanMachineAdapter
{
    private static readonly HashSet<string> OutputOnlyMachineIds = new(StringComparer.Ordinal)
    {
        "(BC)10", // Bee House
        "(BC)105", // Tapper
        "(BC)128", // Mushroom Box
        "(BC)264", // Heavy Tapper
        "(BC)MushroomLog" // Mushroom Log
    };

    private static readonly HashSet<string> SupportedMachineIds = new(StringComparer.Ordinal)
    {
        "(BC)10", // Bee House
        "(BC)12", // Keg
        "(BC)13", // Furnace
        "(BC)15", // Preserves Jar
        "(BC)16", // Cheese Press
        "(BC)17", // Loom
        "(BC)19", // Oil Maker
        "(BC)21", // Crystalarium
        "(BC)24", // Mayonnaise Machine
        "(BC)105", // Tapper
        "(BC)128", // Mushroom Box
        "(BC)163", // Cask; only naturally completed output is collected
        "(BC)264", // Heavy Tapper
        "(BC)Dehydrator", // Dehydrator
        "(BC)HeavyFurnace", // Heavy Furnace
        "(BC)FishSmoker", // Fish Smoker
        "(BC)MushroomLog" // Mushroom Log
    };

    public bool Supports(SObject machine) => SupportedMachineIds.Contains(machine.QualifiedItemId);

    public static bool IsOutputOnly(string qualifiedItemId) => OutputOnlyMachineIds.Contains(qualifiedItemId);

    public bool HasReadyOutput(SObject machine)
    {
        return this.Supports(machine)
            && machine.readyForHarvest.Value
            && machine.heldObject.Value is not null;
    }

    public Item? GetOutput(SObject machine, Farmer player)
    {
        if (!this.HasReadyOutput(machine))
            return null;

        if (machine.GetMachineData() is MachineData data)
        {
            MachineOutputRule? outputRule = data.OutputRules?
                .FirstOrDefault(rule => rule.Id == machine.lastOutputRuleId.Value);
            if (outputRule?.RecalculateOnCollect is true)
            {
                machine.OutputMachine(
                    data,
                    outputRule,
                    machine.lastInputItem.Value,
                    player,
                    machine.Location ?? player.currentLocation,
                    probe: false,
                    heldObjectOnly: true
                );
            }
        }

        return machine.heldObject.Value;
    }

    /// <summary>Try to start a machine using an isolated view of the source inventory.</summary>
    public bool TryInsert(
        SObject machine,
        Item input,
        IList<Item> sourceItems,
        Func<Item, bool> sourceAllows,
        int minimumKeep,
        Farmer player,
        out MachineInputConsumption? consumption)
    {
        consumption = null;
        if (!this.IsIdle(machine) || machine.GetMachineData() is not MachineData data)
            return false;

        Inventory availableItems = CreateAvailableInventory(sourceItems, sourceAllows, minimumKeep);

        Item probeItem = input.getOne();
        probeItem.Stack = 1;
        IInventory? previousAutoLoadInventory = SObject.autoLoadFrom;
        SObject.autoLoadFrom = availableItems;
        try
        {
            _ = MachineDataUtility.TryGetMachineOutputRule(
                machine,
                data,
                MachineOutputTrigger.ItemPlacedInMachine,
                probeItem,
                player,
                machine.Location ?? player.currentLocation,
                out _,
                out MachineOutputTriggerRule? trigger,
                out _,
                out MachineOutputTriggerRule? triggerIgnoringCount
            );

            MachineOutputTriggerRule? matchedTrigger = trigger ?? triggerIgnoringCount;
            if (matchedTrigger is null)
                return false;

            int primaryCount = Math.Max(1, matchedTrigger.RequiredCount);
            if (CountStackable(availableItems, input) < primaryCount)
                return false;

            IReadOnlyList<MachineAdditionalConsumption> additionalItems = GetAdditionalConsumption(data);
            if (additionalItems.Any(requirement => !availableItems.ContainsId(requirement.ItemId, requirement.Count)))
                return false;

            Item moving = input.getOne();
            moving.Stack = primaryCount;
            if (!machine.PlaceInMachine(data, moving, probe: true, player, showMessages: false, playSounds: false))
                return false;
            if (!machine.PlaceInMachine(data, moving, probe: false, player, showMessages: false, playSounds: false))
                return false;

            consumption = new MachineInputConsumption(primaryCount, additionalItems);
            return true;
        }
        finally
        {
            SObject.autoLoadFrom = previousAutoLoadInventory;
        }
    }

    public void RemoveAcceptedOutput(SObject machine, int amount, Farmer player)
    {
        Item? output = machine.heldObject.Value;
        if (output is null || amount <= 0)
            return;

        output.Stack -= amount;
        if (output.Stack > 0)
            return;

        Item collectedItem = output.getOne();
        collectedItem.Stack = 1;
        MachineData? data = machine.GetMachineData();
        MachineOutputRule? restartRule = null;
        if (data is not null)
        {
            _ = MachineDataUtility.TryGetMachineOutputRule(
                machine,
                data,
                MachineOutputTrigger.OutputCollected,
                collectedItem,
                player,
                machine.Location ?? player.currentLocation,
                out restartRule,
                out _,
                out _,
                out _
            );
        }

        machine.heldObject.Value = null;
        machine.readyForHarvest.Value = false;
        machine.minutesUntilReady.Value = -1;
        machine.showNextIndex.Value = false;
        machine.ResetParentSheetIndex();

        if (data is not null
            && restartRule is not null
            && machine.OutputMachine(
                data,
                restartRule,
                collectedItem,
                player,
                machine.Location ?? player.currentLocation,
                probe: false,
                heldObjectOnly: false))
        {
            return;
        }

        machine.lastInputItem.Value = null;
        machine.lastOutputRuleId.Value = null;
    }

    private bool IsIdle(SObject machine)
    {
        return this.Supports(machine)
            && machine.heldObject.Value is null
            && !machine.readyForHarvest.Value
            && machine.minutesUntilReady.Value <= 0;
    }

    private static Inventory CreateAvailableInventory(
        IList<Item> sourceItems,
        Func<Item, bool> sourceAllows,
        int minimumKeep)
    {
        int keep = Math.Max(0, minimumKeep);
        Dictionary<string, int> remainingById = sourceItems
            .Where(item => item is not null)
            .GroupBy(item => item.QualifiedItemId)
            .ToDictionary(group => group.Key, group => Math.Max(0, group.Sum(item => item.Stack) - keep));

        Inventory result = new();
        foreach (Item item in sourceItems.Where(item => item is not null && sourceAllows(item)))
        {
            int remaining = remainingById.GetValueOrDefault(item.QualifiedItemId);
            int amount = Math.Min(item.Stack, remaining);
            if (amount <= 0)
                continue;

            Item copy = item.getOne();
            copy.Stack = amount;
            result.Add(copy);
            remainingById[item.QualifiedItemId] = remaining - amount;
        }

        return result;
    }

    private static int CountStackable(IEnumerable<Item> items, Item sample)
    {
        return items.Where(sample.canStackWith).Sum(item => item.Stack);
    }

    private static IReadOnlyList<MachineAdditionalConsumption> GetAdditionalConsumption(MachineData data)
    {
        return data.AdditionalConsumedItems?
            .Where(requirement => requirement.RequiredCount > 0)
            .Select(requirement => new MachineAdditionalConsumption(requirement.ItemId, requirement.RequiredCount))
            .ToArray()
            ?? Array.Empty<MachineAdditionalConsumption>();
    }
}

internal sealed record MachineInputConsumption(
    int PrimaryCount,
    IReadOnlyList<MachineAdditionalConsumption> AdditionalItems
);

internal sealed record MachineAdditionalConsumption(string ItemId, int Count);

# Smart Pipes

Smart Pipes is a SMAPI-compatible item logistics mod for Stardew Valley 1.6.14+. It provides filtered, directional pipe networks for moving materials between containers, vanilla machines, and shipping bins.

Current release: **1.0.0**.

Version 0.10 introduces the large-network scheduler: cached topology, multiple transfers per cycle, fair source/item/target rotation, a processing-time budget, and basic congestion diagnostics. Version 0.9 introduced batch, fueled, passive, and repeating-machine support. Version 0.8 introduced original inventory art and connected-pipe visuals. The source concept sheet and deterministic sprite-build script are kept under `assets/concepts` and `scripts`.

## Build

On macOS with Stardew Valley installed in `/Applications`:

```sh
dotnet build SmartPipes.csproj
```

For another install location:

```sh
dotnet build SmartPipes.csproj -p:GamePath="/path/to/Stardew Valley"
```

Copy the output folder into the game's `Mods` directory. The alpha currently provides custom components, recipes, persistent ports, pipe network discovery, safe chest-to-chest transfer, Auto-Grabber extraction, quality filters, guarded shipping, and input/output support for vanilla Kegs, Preserves Jars, Cheese Presses, Looms, Oil Makers, Mayonnaise Machines, Casks, Dehydrators, Furnaces, Heavy Furnaces, Fish Smokers, Tappers, Heavy Tappers, Bee Houses, Crystalariums, Mushroom Logs, and Mushroom Boxes.

## Unlock story

Smart Pipes recipes no longer unlock directly at Farming level 4. Once a player has Farming level 4 and owns a supported processing machine, Robin sends a letter on the next morning. Her **Basic Farm Logistics Blueprint** then appears in the Carpenter's Shop for 2,000g. Buying it unlocks Smart Item Pipes, Smart Ports, and the Pipe Wrench, and includes one free wrench.

The purchase adds **A Simple Supply Line** to the quest log. Connect a chest and a supported input machine to the same real pipe network with Smart Ports to complete it and receive 10 pipes plus 4 ports. Lewis sends the Safe Shipping Valve recipe the following morning. Existing saves keep any recipes they already learned; the story only controls future unlocks.

For repeatable testing, use `sp_story status`, or run `sp_story reset` followed by `sp_story start`. Reset deliberately removes this player's four Smart Pipes recipes and story flags, but doesn't delete placed networks or inventory items.

## Controls

- Hold a Smart Port and use the action button on a chest or big craftable to install it.
- A chest port defaults to output; a supported machine port defaults to bidirectional mode.
- An Auto-Grabber port defaults to output and can be used as a raw-material source. Smart Pipes deliberately won't insert unrelated items into it.
- New Keg and Preserves Jar ports default to bidirectional mode so one connection can load inputs and collect finished products.
- On supported machines, the attached port's allow/deny and quality rules control which raw materials may be loaded. Finished products are checked against the downstream chest or shipping-valve rules, so a raw-material whitelist can't trap completed output inside the machine.
- Hold the Pipe Wrench and left-click an installed Smart Port or Safe Shipping Valve to open its configuration menu.
- The in-world menu uses Stardew Valley's orange dropdown and button styling. A dual-handle slider controls the accepted quality range; three additional sliders control priority (-100 to 100), source reserve (0 to 100), and destination stock cap (0 to 100). Only the item filter remains a text field, so typing can't trigger menu hotkeys. A clickable hotbar is shown below the window; the full backpack remains available on demand and only fills the filter field. Localized item names, raw IDs, and context tags can also be typed directly.
- Slider values preview continuously while dragging, but are persisted only when the mouse button is released. This avoids repeated object-data writes and sound spam.
- Hold the Pipe Wrench and use the action button to cycle port mode without opening the menu.
- Shift + action with the wrench removes and returns the port.
- Safe Shipping Valves use the same wrench-only removal rule. If a chest, machine, or Mini-Shipping Bin is broken with a tool, its attached Smart Port or valve drops separately instead of being lost or copied into the picked-up object.
- Placed pipes are hidden unless a Smart Pipes component is selected in the toolbar.
- Placed pipes are passable floor infrastructure; players and characters can walk across them.
- Pipe networks continue running in farm-building interiors and other saved locations while the player is elsewhere.
- Use `sp_help` in the SMAPI console for development commands.

## Configuration

If the optional [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) is installed, all Smart Pipes settings are available in **Settings > Mod Options > Smart Pipes**. Changes are saved and applied immediately when the menu is closed.

Without Generic Mod Config Menu, Smart Pipes works normally and SMAPI creates `config.json` in the mod folder. Close the game before editing it manually:

```json
{
  "EnableAutomation": true,
  "TransferIntervalTicks": 30,
  "ItemsPerTransfer": 1,
  "TransfersPerNetworkCycle": 8,
  "MaxProcessingMilliseconds": 4,
  "ShowTransferMessages": false,
  "ShowInstallationMessages": false,
  "HidePipesUnlessHoldingComponent": true,
  "ShowInstalledAttachmentsWithoutWrench": true
}
```

`TransfersPerNetworkCycle` controls how many successful operations one network may perform per scheduled cycle (1-32). `MaxProcessingMilliseconds` is the global scheduler budget for that game tick (1-16 ms). Networks rotate across cycles when the budget is exhausted, so a large Farm network can't permanently starve a Shed network. Lower the budget if another mod causes frame spikes; raise transfers per cycle first when throughput is the only concern.

Set `ShowInstalledAttachmentsWithoutWrench` to `false` to hide installed Smart Ports and Safe Shipping Valves until the Pipe Wrench is held. Direction arrows remain wrench-only regardless of this setting.

## Filters and quality

The Pipe Wrench menu is the primary way to edit filters. The following SMAPI console commands remain available for development and compatibility:

```text
sp_filter allow (O)348
sp_quality exact iridium
```

This example allows only iridium-quality Wine through the selected port. Quality operations are `any`, `exact`, `min`, and `max`; accepted quality names are `normal`, `silver`, `gold`, and `iridium`. Quality restrictions combine with the existing allow/deny lists, so an item must pass both rules.

## Safe Shipping Valve

Hold the Safe Shipping Valve and use the action button on the Farm's original shipping bin to install it, like attaching a Smart Port. It initializes as disabled with priority `-100`. Place at least one pipe directly beside the shipping bin's footprint to connect it to a network. Left-click the shipping bin with the Pipe Wrench to select its valve for commands; use the action button with the wrench to toggle it between disabled and input mode. Shift + action removes and returns the valve.

Mini-Shipping Bins support the same install, selection, filtering, toggle, and removal controls. Their native nine-slot capacity is respected: transfers stop safely when all slots are full, and automatically shipped items remain visible in the Mini-Shipping Bin interface until the game processes them.

The main Shipping Bin stores its valve settings in the Shipping Bin building itself, so moving the building through Robin's building-move interface keeps the installed valve and all rules attached.

The valve has no hardcoded product or quality. Its allow/deny list and quality range are combined exactly like a Smart Port. For example:

```text
sp_filter allow (O)348
sp_quality exact iridium
```

This sells only iridium Wine, while `sp_quality any` sells Wine of every quality. An enabled valve with an empty allow list accepts every item not blocked by its deny list, so configure the rules before enabling it. Source-chest `sp_keep` rules are respected.

## Cask behavior

- An empty Cask with a bidirectional Smart Port accepts inputs through the game's native Cask rules.
- Stardew Valley controls the aging timer and quality changes; Smart Pipes doesn't accelerate or rewrite them.
- Smart Pipes collects Cask contents only after the Cask naturally finishes. It doesn't force silver- or gold-quality early harvesting in this version.
- Output is removed from the Cask only after a destination chest accepts it.

For an iridium-wine output chest, select its input port with the Pipe Wrench and configure:

```text
sp_port input 100
sp_filter clear
sp_filter allow (O)348
sp_quality exact iridium
```

To keep ordinary wine flowing into the Casks, don't apply the iridium-only rule to the raw-wine source or the Cask input port.

## Artisan machine test layout

Place one connected pipe tile beside each machine, forming a trunk line:

```text
K K K K
P P P P
R     F
```

`K` is a supported artisan machine, `P` is a connected pipe, `R` is the raw-material chest, and `F` is the finished-goods chest. Install a bidirectional port on each machine, leave the raw chest as output, and set the finished chest to input. Give the finished chest an artisan-goods whitelist so unused raw materials don't use it as overflow storage.

## Dehydrator batch-input test

The Dehydrator uses the required stack count from Stardew Valley's machine data instead of a Smart Pipes hardcoded recipe. A transfer is atomic: the machine must confirm it started before any source items are removed.

1. Connect a source chest, Dehydrator, and output chest to one network. Set the source to output, the Dehydrator to both, and the output chest to input.
2. Put four valid identical ingredients in the source. The Dehydrator must remain empty and all four items must stay in the chest.
3. Add the fifth matching ingredient. The Dehydrator must start and exactly five items must be removed.
4. Repeat with the five ingredients split across two stackable chest slots. It must still consume exactly five.
5. Set the source `Keep` value to 3. Seven ingredients must not start a batch; eight must start one and leave exactly three.
6. Verify grapes produce Raisins, fruit produces Dried Fruit, and mushrooms produce Dried Mushrooms; invalid inputs must remain untouched.
7. Fill the output chest before the cycle completes. Ready output must remain in the Dehydrator, then move after space is freed.

## Fueled-machine test

Furnaces, Heavy Furnaces, and Fish Smokers read both their primary input count and extra fuel requirements from Stardew Valley's machine data. For version 0.9.3, place the primary material and Coal in the same source chest. Both must pass that source port's rules and remain above its `Keep` reserve.

1. Furnace: put five Copper Ore and one Coal in the source chest. It must consume exactly those amounts and eventually send the Copper Bar downstream. With only four ore or no Coal, nothing may be consumed.
2. Heavy Furnace: put 25 matching Ore and three Coal in the source chest. It must start one batch atomically. Test 24 ore and two Coal separately; neither incomplete case may consume anything.
3. Fish Smoker: put one valid Fish and one Coal in the source chest. It must preserve the fish type and quality in the Smoked Fish output. Missing Coal or an invalid input must remain untouched.
4. Apply an ore/fish whitelist to the machine port and allow both the primary item and Coal through the source-chest port. Completed products should still leave the machine and be governed by the downstream port's filter.
5. Fill the downstream chest before completion. Ready output must stay in the machine until space is available.

## Passive-producer and Crystalarium test

Tappers, Heavy Tappers, Bee Houses, Mushroom Logs, and Mushroom Boxes default to output-only ports. They are collected only when Stardew Valley marks their output ready. Crystalariums default to bidirectional ports and use the game's native `OutputCollected` rule to begin the next copy after a gem is transferred.

1. Install a Smart Port on a Tapper attached to a mature tree and connect one adjacent pipe. When its product is ready, it must move downstream; the Tapper must retain its normal next-production timer.
2. Repeat with a Heavy Tapper. Confirm the shorter vanilla production time isn't changed by Smart Pipes.
3. Connect a Bee House. Wild or flower Honey must enter the downstream chest only when ready, and the Bee House must produce again normally afterward. Flower-dependent Honey is recalculated at collection time.
4. Connect a Mushroom Log and a Mushroom Box. Multi-item Mushroom Log output may move over multiple transfers, and the producer must not reset until its final item is accepted.
5. Connect an empty Crystalarium, provide one valid gem through its bidirectional port, and wait for output. After each copied gem moves downstream, the Crystalarium must immediately start its next native cycle without consuming another gem.
6. Fill the destination chest for every producer. Its ready output must remain visible on the producer until destination space is available.

## Large-network scheduler test

1. Connect one raw-material chest to at least 16 identical machines and set all machine ports to equal priority. With `TransfersPerNetworkCycle` at 8, approximately eight idle machines should receive input on each scheduled cycle instead of only one.
2. Empty the machines again and repeat. Equal-priority destinations should rotate; the same front machine should not always win solely because of scan order.
3. Put several accepted material types in one source chest. Successful transfers should rotate through its item stacks instead of draining only the first stack forever.
4. Run networks in two or more locations. Under a deliberately low `MaxProcessingMilliseconds`, each network should still receive turns across successive cycles.
5. Run `sp_scan` with the cursor on a network. It reports the previous-cycle and lifetime transfer counts, consecutive idle/blocked cycles, and topology cache hits/rebuilds.
6. Place or remove a pipe, move an object, or edit a port. The next `sp_scan` should show a cache rebuild; ordinary item movement should continue increasing cache hits without rescanning topology.

## Safety

Only the multiplayer host changes inventories. A source stack is reduced only after a destination machine confirms it started, a destination chest accepts it, or the original Farm shipping bin accepts it. Machine output follows the same commit-after-acceptance rule.

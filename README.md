# Smart Pipes

Smart Pipes is a logistics mod for Stardew Valley. Build pipe networks between chests, machines, and shipping bins, then use filters and priorities to decide exactly where every item may go.

It is designed around one rule: **items only move when there is a valid destination**. A full machine or chest will never turn your ingredients into unintended shipping-bin sales.

**Current release:** [1.0.0](https://github.com/ParfaitRana/SmartPipes/releases/latest)

## Install

1. Install [SMAPI](https://smapi.io/).
2. Download `SmartPipes-1.0.0.zip` from the [latest release](https://github.com/ParfaitRana/SmartPipes/releases/latest). Do **not** use GitHub's `Code → Download ZIP`; that is the source code.
3. Extract the download. You will get a folder named `SmartPipes`.
4. Drag that folder into Stardew Valley's `Mods` folder.
5. Start the game through SMAPI.

Smart Pipes requires Stardew Valley 1.6.14+ and SMAPI 4.2.0+.

## Unlocking Smart Pipes

After reaching Farming level 4 and owning a supported processing machine, Robin will send a letter the next morning. Her **Basic Farm Logistics Blueprint** is then sold at the Carpenter's Shop for 2,000g.

Buying it unlocks recipes for the following items:

- **Smart Item Pipe** — forms the network.
- **Smart Port** — attaches to a chest or machine and controls item flow.
- **Pipe Wrench** — configures and safely removes ports and valves.

You also receive a free wrench. Complete Robin's small tutorial network to earn 10 Pipes and 4 Smart Ports. Lewis sends the **Safe Shipping Valve** recipe the following morning.

Existing saves keep any Smart Pipes recipes they already know.

## Your first network

The simplest useful setup moves raw materials from one chest into a machine, then sends finished goods to another chest.

1. Put down a **source chest**, a supported machine, and a **finished-goods chest**.
2. Place Pipes so that one pipe tile touches each object and all pipe tiles connect to one another.
3. Hold a Smart Port and use your Action Button on each chest and machine.
4. Leave the source chest as **Output**, the machine as **Both**, and the finished-goods chest as **Input**.
5. Put valid ingredients in the source chest.

The machine will start when it can accept an ingredient. When it finishes, Smart Pipes moves the product to the finished-goods chest if there is space.

Pipes are floor infrastructure: you and villagers can walk over them. By default, pipes are hidden when you are not holding a Smart Pipes item, so they do not clutter the farm.

## Smart Ports

A Smart Port is installed directly on a chest, machine, or supported storage device. It does not take up an extra map tile.

### Flow modes

| Mode | What it does |
| --- | --- |
| **Output** | Lets the network take items from this object. Use it for ingredient chests and passive producers. |
| **Input** | Lets the network put items into this object. Use it for finished-goods chests. |
| **Both** | Allows both directions. This is the normal setting for most processing machines. |
| **Disabled** | Prevents every transfer through this port. |

New chests normally default to Output. Most supported machines default to Both. Passive producers such as Bee Houses and Tappers default to Output because they only produce items.

### Configuring and removing a port

Hold the Pipe Wrench:

- **Left-click** an installed Smart Port or Safe Shipping Valve to open its configuration screen.
- Use the **Action Button** on it to cycle its flow mode.
- Hold **Shift** and use the Action Button to remove it safely and return the item to you.

Breaking an object with a tool also returns any installed Smart Port or Safe Shipping Valve, so moving a chest does not destroy its attachment.

## Filters, priorities, and stock rules

The wrench configuration screen lets you control each port precisely.

### Allow and deny lists

Type an item name or ID into the filter field, or choose an item from your hotbar/backpack, then select **Allow** or **Block**.

- An empty allow list accepts every item that is not blocked.
- If an allow list has entries, only those entries may pass.
- A block rule always wins over an allow rule.

For example, set a finished-goods chest to allow Wine and block everything else. Grape, fruit, and other unused ingredients will remain in their source chest instead of being routed into that chest.

### Quality range

Use the two handles on the quality slider to accept a range from normal through iridium quality. This is useful for separating aged Cask products or selling only iridium-quality items.

Example: set a destination chest or shipping valve to **iridium – iridium** and allow Wine. Only iridium Wine can enter it.

### Priority

When several destinations accept the same item, a higher-priority port is chosen first. Equal-priority ports take turns, so a long row of identical machines is fed fairly.

### Keep and stock cap

- **Keep** reserves that many items in an output container. A source with Keep set to 20 will never supply its last 20 matching items.
- **Stock cap** limits how many matching items an input container may receive. Use it to keep one chest from consuming all output.

## Safe Shipping Valve

The Safe Shipping Valve is the only way Smart Pipes may send items to a shipping bin. It works with the main Farm shipping bin and Mini-Shipping Bins.

1. Hold a Safe Shipping Valve and use the Action Button on the shipping bin to install it.
2. Place at least one pipe beside the bin's footprint.
3. Open the valve with the Pipe Wrench.
4. Configure its allow/block and quality rules.
5. Change its mode from **Disabled** to **Input**.

Valves start disabled on purpose. Configure the rules before enabling one. With an empty allow list, an enabled valve accepts every item that is not blocked, so a specific allow list is strongly recommended.

The main shipping-bin valve stays attached when Robin moves the shipping bin. Mini-Shipping Bins retain their native capacity: Smart Pipes stops when they are full.

## Supported objects

Smart Pipes supports ordinary and Big Chests, as well as Auto-Grabbers used as item sources. It also supports these vanilla machines and producers:

- Kegs, Preserves Jars, Cheese Presses, Mayonnaise Machines, Looms, Oil Makers, and Casks;
- Dehydrators;
- Furnaces, Heavy Furnaces, and Fish Smokers;
- Tappers and Heavy Tappers;
- Bee Houses;
- Crystalariums;
- Mushroom Logs and Mushroom Boxes.

Smart Pipes uses the game's own machine rules. It does not change recipes, fuel costs, production times, or Cask aging. In particular, Cask products are collected only after naturally finishing; the mod does not harvest silver- or gold-quality products early.

## Settings

If [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) is installed, open:

**Settings → Mod Options → Smart Pipes**

There you can change transfer speed, throughput, large-network processing budget, world visibility, and optional messages. The mod works without this menu too; SMAPI creates a `config.json` in the Smart Pipes folder.

Useful display settings:

- **Hide pipes when not building** keeps pipe tiles invisible unless you hold a Smart Pipes item.
- **Show installed attachments without wrench** controls whether attached ports and valves remain visible when the wrench is not held. Direction arrows are always wrench-only.
- **Show transfer messages** and **Show installation messages** can be disabled for a cleaner screen.

## Multiplayer and large farms

Only the multiplayer host performs inventory changes. This prevents duplicate transfers and keeps every player synchronized.

Networks keep working in farm buildings and other saved locations even while you are elsewhere. For large farms, Smart Pipes caches network layouts, shares turns fairly between machines, and limits its processing time to avoid frame spikes.

## Troubleshooting

### Nothing moves

Check these in order:

1. Every endpoint must touch a connected Pipe tile.
2. The source needs an Output or Both port; the destination needs an Input or Both port.
3. The item must pass the source and destination filters and quality ranges.
4. The destination must have room and, for machines, be ready to accept input.
5. Make sure the port or valve is not Disabled.

### My ingredients are not entering a machine

The machine may require a complete batch or fuel. For example, a Furnace needs enough ore plus Coal, and a Dehydrator needs its full ingredient stack. Smart Pipes leaves items in the source until the game's machine rules confirm that a batch can start.

### My finished item is still in the machine

The downstream chest may be full, disabled, or rejecting the item through its filter. The finished item stays safely in the machine until a legal destination becomes available.

### I cannot see pipes, ports, or arrows

Hold a Smart Pipes item to reveal hidden pipes. Hold the Pipe Wrench to show direction arrows. Attachment visibility can be changed in Mod Options.

### How do I move an attached chest or shipping bin?

Use Shift + Action with the Pipe Wrench to remove the attachment first, or break the object with its normal tool. The attachment is returned separately.

## Updating

Download the newer release, extract it, and replace the old `SmartPipes` folder in `Mods`. Your placed pipes, ports, valves, and rules are preserved. Backing up your save before any mod update is recommended.

## Help and feedback

Use `sp_help` in the SMAPI console to see optional diagnostic commands. Please report issues on the [GitHub issue tracker](https://github.com/ParfaitRana/SmartPipes/issues) and include your SMAPI log when possible.

## License

Smart Pipes is available under the [MIT License](LICENSE).

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Diagnostics;
using HarmonyLib;
using SmartPipes.Framework;
using SmartPipes.Integrations;
using SmartPipes.Menus;
using SmartPipes.Models;
using SmartPipes.Patches;
using SmartPipes.Services;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Delegates;
using StardewValley.GameData.Objects;
using StardewValley.GameData.Shops;
using StardewValley.GameData.Tools;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.Quests;
using StardewValley.Triggers;
using SObject = StardewValley.Object;

namespace SmartPipes;

internal sealed class ModEntry : Mod
{
    private ModConfig config = null!;
    private PortService ports = null!;
    private ShippingValveService shippingValves = null!;
    private NetworkScanner scanner = null!;
    private TransferService transfers = null!;
    private readonly VanillaArtisanMachineAdapter storyMachines = new();
    private GameLocation? selectedLocation;
    private Vector2? selectedTile;
    private ShippingBin? selectedShippingBin;
    private SObject? selectedMiniShippingBin;
    private SObject? selectedShippingValveProxy;
    private readonly Dictionary<string, NetworkActivity> networkActivity = new(StringComparer.Ordinal);
    private int nextNetworkIndex;
    private bool blueprintCleanupPending;

    public override void Entry(IModHelper helper)
    {
        this.config = helper.ReadConfig<ModConfig>();
        this.NormalizeConfig();
        helper.WriteConfig(this.config);

        this.ports = new PortService();
        this.shippingValves = new ShippingValveService();
        this.scanner = new NetworkScanner(this.ports, this.shippingValves);
        this.ports.Changed += this.scanner.InvalidateAll;
        this.shippingValves.Changed += this.scanner.InvalidateAll;
        this.transfers = new TransferService(this.config, this.Monitor);

        PipeVisibilityPatch.ShouldShowPipes = this.ShouldShowPipes;
        QuestDisplayPatch.Title = () => this.Helper.Translation.Get("story.quest.title");
        QuestDisplayPatch.Description = () => this.Helper.Translation.Get("story.quest.description");
        QuestDisplayPatch.Objective = () => this.Helper.Translation.Get("story.quest.objective");
        new Harmony(this.ModManifest.UniqueID).PatchAll();

        helper.Events.Content.AssetRequested += this.OnAssetRequested;
        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        helper.Events.GameLoop.DayEnding += this.OnDayEnding;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.Input.ButtonPressed += this.OnButtonPressed;
        helper.Events.Display.RenderedWorld += this.OnRenderedWorld;
        helper.Events.World.ObjectListChanged += this.OnObjectListChanged;
        helper.Events.World.BuildingListChanged += this.OnBuildingListChanged;

        helper.ConsoleCommands.Add("sp_help", "Show Smart Pipes development commands.", this.OnHelpCommand);
        helper.ConsoleCommands.Add("sp_give", "Give the four Smart Pipes components.", this.OnGiveCommand);
        helper.ConsoleCommands.Add("sp_scan", "Scan the pipe network at the cursor.", this.OnScanCommand);
        helper.ConsoleCommands.Add("sp_port", "Configure the selected port: sp_port <input|output|both|disabled> [priority].", this.OnPortCommand);
        helper.ConsoleCommands.Add("sp_keep", "Set source minimum stock: sp_keep <amount>.", this.OnKeepCommand);
        helper.ConsoleCommands.Add("sp_filter", "Edit a filter: sp_filter <allow|deny|remove|clear> [item ID or context tag].", this.OnFilterCommand);
        helper.ConsoleCommands.Add("sp_quality", "Set a quality filter: sp_quality <any|exact|min|max> [normal|silver|gold|iridium].", this.OnQualityCommand);
        helper.ConsoleCommands.Add("sp_story", "Inspect or reset the unlock story: sp_story <status|inspect|start|reset>.", this.OnStoryCommand);

        TriggerActionManager.RegisterAction(StoryIds.PurchaseBlueprintAction, this.OnPurchaseBlueprintAction);
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        this.Monitor.Log(this.Helper.Translation.Get("mod.loaded", new { version = this.ModManifest.Version }), LogLevel.Info);
        this.RegisterGenericModConfigMenu();
    }

    private void RegisterGenericModConfigMenu()
    {
        IGenericModConfigMenuApi? api = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (api is null)
            return;

        api.Register(
            this.ModManifest,
            reset: () =>
            {
                this.config = new ModConfig();
                this.RebuildConfigDependentServices();
            },
            save: () =>
            {
                this.NormalizeConfig();
                this.Helper.WriteConfig(this.config);
                this.RebuildConfigDependentServices();
            });

        api.AddSectionTitle(this.ModManifest, () => this.Helper.Translation.Get("gmcm.section.automation"));
        api.AddBoolOption(
            this.ModManifest,
            () => this.config.EnableAutomation,
            value => this.config.EnableAutomation = value,
            () => this.Helper.Translation.Get("gmcm.enable-automation.name"),
            () => this.Helper.Translation.Get("gmcm.enable-automation.tooltip"),
            fieldId: nameof(ModConfig.EnableAutomation));
        api.AddNumberOption(
            this.ModManifest,
            () => (int)this.config.TransferIntervalTicks,
            value => this.config.TransferIntervalTicks = (uint)value,
            () => this.Helper.Translation.Get("gmcm.transfer-interval.name"),
            () => this.Helper.Translation.Get("gmcm.transfer-interval.tooltip"),
            min: 1,
            max: 120,
            interval: 1,
            fieldId: nameof(ModConfig.TransferIntervalTicks));
        api.AddNumberOption(
            this.ModManifest,
            () => this.config.ItemsPerTransfer,
            value => this.config.ItemsPerTransfer = value,
            () => this.Helper.Translation.Get("gmcm.items-per-transfer.name"),
            () => this.Helper.Translation.Get("gmcm.items-per-transfer.tooltip"),
            min: 1,
            max: 64,
            interval: 1,
            fieldId: nameof(ModConfig.ItemsPerTransfer));
        api.AddBoolOption(
            this.ModManifest,
            () => this.config.ShowTransferMessages,
            value => this.config.ShowTransferMessages = value,
            () => this.Helper.Translation.Get("gmcm.show-transfer-messages.name"),
            () => this.Helper.Translation.Get("gmcm.show-transfer-messages.tooltip"),
            fieldId: nameof(ModConfig.ShowTransferMessages));

        api.AddSectionTitle(this.ModManifest, () => this.Helper.Translation.Get("gmcm.section.network"));
        api.AddNumberOption(
            this.ModManifest,
            () => this.config.TransfersPerNetworkCycle,
            value => this.config.TransfersPerNetworkCycle = value,
            () => this.Helper.Translation.Get("gmcm.transfers-per-cycle.name"),
            () => this.Helper.Translation.Get("gmcm.transfers-per-cycle.tooltip"),
            min: 1,
            max: 32,
            interval: 1,
            fieldId: nameof(ModConfig.TransfersPerNetworkCycle));
        api.AddNumberOption(
            this.ModManifest,
            () => this.config.MaxProcessingMilliseconds,
            value => this.config.MaxProcessingMilliseconds = value,
            () => this.Helper.Translation.Get("gmcm.processing-budget.name"),
            () => this.Helper.Translation.Get("gmcm.processing-budget.tooltip"),
            min: 1,
            max: 16,
            interval: 1,
            fieldId: nameof(ModConfig.MaxProcessingMilliseconds));

        api.AddSectionTitle(this.ModManifest, () => this.Helper.Translation.Get("gmcm.section.display"));
        api.AddBoolOption(
            this.ModManifest,
            () => this.config.HidePipesUnlessHoldingComponent,
            value => this.config.HidePipesUnlessHoldingComponent = value,
            () => this.Helper.Translation.Get("gmcm.hide-pipes.name"),
            () => this.Helper.Translation.Get("gmcm.hide-pipes.tooltip"),
            fieldId: nameof(ModConfig.HidePipesUnlessHoldingComponent));
        api.AddBoolOption(
            this.ModManifest,
            () => this.config.ShowInstallationMessages,
            value => this.config.ShowInstallationMessages = value,
            () => this.Helper.Translation.Get("gmcm.show-installation-messages.name"),
            () => this.Helper.Translation.Get("gmcm.show-installation-messages.tooltip"),
            fieldId: nameof(ModConfig.ShowInstallationMessages));
        api.AddBoolOption(
            this.ModManifest,
            () => this.config.ShowInstalledAttachmentsWithoutWrench,
            value => this.config.ShowInstalledAttachmentsWithoutWrench = value,
            () => this.Helper.Translation.Get("gmcm.show-attachments.name"),
            () => this.Helper.Translation.Get("gmcm.show-attachments.tooltip"),
            fieldId: nameof(ModConfig.ShowInstalledAttachmentsWithoutWrench));
    }

    private void NormalizeConfig()
    {
        this.config.TransferIntervalTicks = Math.Clamp(this.config.TransferIntervalTicks, 1u, 120u);
        this.config.ItemsPerTransfer = Math.Clamp(this.config.ItemsPerTransfer, 1, 64);
        this.config.TransfersPerNetworkCycle = Math.Clamp(this.config.TransfersPerNetworkCycle, 1, 32);
        this.config.MaxProcessingMilliseconds = Math.Clamp(this.config.MaxProcessingMilliseconds, 1, 16);
    }

    private void RebuildConfigDependentServices()
    {
        this.NormalizeConfig();
        this.transfers = new TransferService(this.config, this.Monitor);
        this.scanner.InvalidateAll();
    }

    private bool OnPurchaseBlueprintAction(string[] args, TriggerActionContext context, out string error)
    {
        error = null!;
        if (!Context.IsWorldReady)
        {
            error = "A save must be loaded before purchasing the Smart Pipes blueprint.";
            return false;
        }

        Farmer player = Game1.player;
        if (player.mailReceived.Contains(StoryIds.BlueprintPurchasedFlag))
            return true;

        AddMailFlag(player, StoryIds.BlueprintPurchasedFlag);
        this.UnlockBasicRecipes(player);
        this.EnsureTutorialQuest(player);
        this.GiveOrDrop(ItemRegistry.Create(ItemIds.Wrench));
        this.blueprintCleanupPending = true;
        this.Helper.GameContent.InvalidateCache("Data/Shops");
        Game1.drawObjectDialogue(this.Helper.Translation.Get("story.blueprint-purchased"));
        return true;
    }

    private void RestoreStoryState(Farmer player)
    {
        if (player.mailReceived.Contains(StoryIds.BlueprintPurchasedFlag))
        {
            this.UnlockBasicRecipes(player);
            if (!player.mailReceived.Contains(StoryIds.TutorialCompletedFlag))
                this.EnsureTutorialQuest(player);
            else
            {
                foreach (Quest staleQuest in player.questLog.Where(candidate => candidate.id.Value == StoryIds.TutorialQuest).ToArray())
                    player.questLog.Remove(staleQuest);
            }
        }

        if (player.mailReceived.Contains(StoryIds.LewisValveMail))
            this.UnlockValveRecipe(player, showMessage: false);

        RemoveBlueprintItems(player);
    }

    private void UpdateStoryProgression(UpdateTickedEventArgs e)
    {
        Farmer player = Game1.player;
        if (this.blueprintCleanupPending)
        {
            this.blueprintCleanupPending = false;
            RemoveBlueprintItems(player);
        }

        if (!e.IsMultipleOf(30))
            return;

        if (player.mailReceived.Contains(StoryIds.LewisValveMail))
            this.UnlockValveRecipe(player, showMessage: true);

        if (!player.mailReceived.Contains(StoryIds.BlueprintPurchasedFlag)
            || player.mailReceived.Contains(StoryIds.TutorialCompletedFlag))
        {
            return;
        }

        this.EnsureTutorialQuest(player);
        if (!this.HasCompletedTutorialNetwork())
            return;

        AddMailFlag(player, StoryIds.TutorialCompletedFlag);
        Quest? quest = player.questLog.FirstOrDefault(candidate => candidate.id.Value == StoryIds.TutorialQuest);
        quest?.questComplete();
        this.GiveOrDrop(ItemRegistry.Create(ItemIds.Pipe, 10));
        this.GiveOrDrop(ItemRegistry.Create(ItemIds.Port, 4));
        Game1.addMailForTomorrow(StoryIds.LewisValveMail);
        Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("story.tutorial-completed")));
    }

    private void UnlockBasicRecipes(Farmer player)
    {
        AddRecipe(player, ItemIds.PipeRecipe);
        AddRecipe(player, ItemIds.PortRecipe);
        AddRecipe(player, ItemIds.WrenchRecipe);
    }

    private void UnlockValveRecipe(Farmer player, bool showMessage)
    {
        if (!AddRecipe(player, ItemIds.ShippingValveRecipe) || !showMessage)
            return;

        Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("story.valve-unlocked")));
    }

    private void EnsureTutorialQuest(Farmer player)
    {
        if (player.mailReceived.Contains(StoryIds.TutorialCompletedFlag))
            return;

        Quest? existing = player.questLog.FirstOrDefault(candidate => candidate.id.Value == StoryIds.TutorialQuest);
        if (existing is not null)
        {
            this.HydrateTutorialQuest(existing);
            return;
        }

        Quest? quest = Quest.getQuestFromId(StoryIds.TutorialQuest);
        if (quest is null)
        {
            this.Monitor.Log($"Couldn't create Smart Pipes tutorial quest '{StoryIds.TutorialQuest}' from Data/Quests.", LogLevel.Error);
            return;
        }

        this.HydrateTutorialQuest(quest);
        player.questLog.Add(quest);
    }

    private void HydrateTutorialQuest(Quest quest)
    {
        // Keep the serialized quest self-contained. The quest log calls IQuest methods,
        // and tiny property getters can be inlined before Harmony gets a chance to patch them.
        quest.questTitle = this.Helper.Translation.Get("story.quest.title");
        quest.questDescription = this.Helper.Translation.Get("story.quest.description");
        quest.currentObjective = this.Helper.Translation.Get("story.quest.objective");
        quest.accepted.Value = true;
        quest.showNew.Value = true;
        quest.canBeCancelled.Value = false;
    }

    private bool HasCompletedTutorialNetwork()
    {
        bool completed = false;
        Utility.ForEachLocation(location =>
        {
            foreach (PipeNetwork network in this.scanner.FindNetworks(location))
            {
                bool hasChest = network.Endpoints.Any(endpoint => endpoint.Object is Chest);
                bool hasProcessingMachine = network.Endpoints.Any(endpoint => this.storyMachines.Supports(endpoint.Object)
                    && !VanillaArtisanMachineAdapter.IsOutputOnly(endpoint.Object.QualifiedItemId));
                if (!hasChest || !hasProcessingMachine)
                    continue;

                completed = true;
                return false;
            }

            return true;
        });
        return completed;
    }

    private bool OwnsSupportedProcessingMachine()
    {
        bool found = false;
        Utility.ForEachLocation(location =>
        {
            found = location.Objects.Values.Any(machine => this.storyMachines.Supports(machine)
                && !VanillaArtisanMachineAdapter.IsOutputOnly(machine.QualifiedItemId));
            return !found;
        });
        return found;
    }

    private void GiveOrDrop(Item item)
    {
        if (!Game1.player.addItemToInventoryBool(item))
            Game1.createItemDebris(item, Game1.player.Position, Game1.player.FacingDirection);
    }

    private static bool AddRecipe(Farmer player, string recipeId)
    {
        if (player.craftingRecipes.ContainsKey(recipeId))
            return false;

        player.craftingRecipes[recipeId] = 0;
        return true;
    }

    private static void AddMailFlag(Farmer player, string flag)
    {
        if (!player.mailReceived.Contains(flag))
            player.mailReceived.Add(flag);
    }

    private static void RemoveBlueprintItems(Farmer player)
    {
        for (int index = 0; index < player.Items.Count; index++)
        {
            if (player.Items[index]?.QualifiedItemId == ItemIds.Blueprint)
                player.Items[index] = null!;
        }
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo("Mods/ParfaitRana.SmartPipes/Items"))
        {
            e.LoadFromModFile<Texture2D>("assets/textures/items.png", AssetLoadPriority.Exclusive);
        }
        else if (e.NameWithoutLocale.IsEquivalentTo("Mods/ParfaitRana.SmartPipes/Pipes"))
        {
            e.LoadFromModFile<Texture2D>("assets/textures/pipes.png", AssetLoadPriority.Exclusive);
        }
        else if (e.NameWithoutLocale.IsEquivalentTo("Mods/ParfaitRana.SmartPipes/Attachments"))
        {
            e.LoadFromModFile<Texture2D>("assets/textures/attachments.png", AssetLoadPriority.Exclusive);
        }
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))
        {
            e.Edit(asset =>
            {
                IDictionary<string, ObjectData> data = asset.AsDictionary<string, ObjectData>().Data;
                foreach (string path in new[] { "assets/objects.json", "assets/legacy-objects.json" })
                {
                    Dictionary<string, ObjectData>? additions = this.Helper.Data.ReadJsonFile<Dictionary<string, ObjectData>>(path);
                    if (additions is null)
                        continue;

                    foreach ((string key, ObjectData value) in additions)
                        data[key] = value;
                }
            });
        }
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Mail"))
        {
            e.Edit(asset =>
            {
                IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
                data[StoryIds.RobinIntroMail] = this.Helper.Translation.Get("story.mail.robin");
                data[StoryIds.LewisValveMail] = this.Helper.Translation.Get("story.mail.lewis");
            });
        }
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Quests"))
        {
            e.Edit(asset =>
            {
                IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
                string title = this.Helper.Translation.Get("story.quest.title");
                string description = this.Helper.Translation.Get("story.quest.description");
                string objective = this.Helper.Translation.Get("story.quest.objective");
                data[StoryIds.TutorialQuest] = $"Basic/{title}/{description}/{objective}/-1//0//false";
            });
        }
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Shops"))
        {
            e.Edit(asset =>
            {
                IDictionary<string, ShopData> shops = asset.AsDictionary<string, ShopData>().Data;
                if (!shops.TryGetValue("Carpenter", out ShopData? carpenter))
                {
                    this.Monitor.Log("Couldn't add the logistics blueprint: Data/Shops has no Carpenter shop.", LogLevel.Error);
                    return;
                }

                carpenter.Items ??= new List<ShopItemData>();
                if (carpenter.Items.Any(item => item.Id == StoryIds.BlueprintShopEntry))
                    return;

                carpenter.Items.Add(new ShopItemData
                {
                    Id = StoryIds.BlueprintShopEntry,
                    ItemId = ItemIds.Blueprint,
                    Price = 2000,
                    AvailableStock = 1,
                    AvoidRepeat = true,
                    Condition = $"PLAYER_HAS_MAIL Current {StoryIds.RobinIntroMail}, !PLAYER_HAS_MAIL Current {StoryIds.BlueprintPurchasedFlag}",
                    ActionsOnPurchase = new List<string> { StoryIds.PurchaseBlueprintAction }
                });
            });
        }
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes"))
        {
            e.Edit(asset =>
            {
                Dictionary<string, string>? additions = this.Helper.Data.ReadJsonFile<Dictionary<string, string>>("assets/crafting-recipes.json");
                if (additions is null)
                    return;

                IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
                foreach ((string key, string value) in additions)
                    data[key] = value;
            });
        }
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Tools"))
        {
            e.Edit(asset =>
            {
                IDictionary<string, ToolData> data = asset.AsDictionary<string, ToolData>().Data;
                foreach (string path in new[] { "assets/tools.json", "assets/legacy-tools.json" })
                {
                    Dictionary<string, ToolData>? additions = this.Helper.Data.ReadJsonFile<Dictionary<string, ToolData>>(path);
                    if (additions is null)
                        continue;

                    foreach ((string key, ToolData value) in additions)
                        data[key] = value;
                }
            });
        }
        else if (e.NameWithoutLocale.IsEquivalentTo("Strings/Objects"))
        {
            e.Edit(asset =>
            {
                Dictionary<string, string>? additions = this.Helper.Data.ReadJsonFile<Dictionary<string, string>>("assets/strings.json");
                if (additions is null)
                    return;

                IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
                foreach ((string key, string value) in additions)
                    data[key] = value;
            });
        }
        else if (e.NameWithoutLocale.IsEquivalentTo("Strings/Tools"))
        {
            e.Edit(asset =>
            {
                Dictionary<string, string>? additions = this.Helper.Data.ReadJsonFile<Dictionary<string, string>>("assets/tool-strings.json");
                if (additions is null)
                    return;

                IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
                foreach ((string key, string value) in additions)
                    data[key] = value;
            });
        }
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        this.scanner.InvalidateAll();
        this.networkActivity.Clear();
        this.nextNetworkIndex = 0;
        this.RestoreStoryState(Game1.player);
        this.Helper.GameContent.InvalidateCache("Data/Shops");

        int migratedItems = 0;
        int migratedPipes = 0;
        int migratedPorts = 0;
        int migratedRecipes = 0;
        int migratedShippingValves = 0;

        foreach (Farmer farmer in Game1.getAllFarmers())
        {
            migratedItems += MigrateLegacyItems(farmer.Items);
            migratedRecipes += MigrateLegacyRecipes(farmer);
        }

        foreach (GameLocation location in Game1.locations)
        {
            foreach ((Vector2 tile, SObject worldObject) in location.Objects.Pairs.ToArray())
            {
                if (MigratePortData(worldObject))
                    migratedPorts++;

                if (InventoryEndpointAdapter.IsMiniShippingBin(worldObject) && this.ports.Remove(worldObject))
                {
                    Item returnedPort = ItemRegistry.Create(ItemIds.Port);
                    if (!Game1.MasterPlayer.addItemToInventoryBool(returnedPort))
                        Game1.createItemDebris(returnedPort, tile * Game1.tileSize, -1, location);
                    migratedPorts++;
                }

                if (worldObject is Chest chest)
                    migratedItems += MigrateLegacyItems(chest.Items);

                if (worldObject.QualifiedItemId == ItemIds.LegacyPipe
                    && ItemRegistry.Create(ItemIds.Pipe) is SObject replacement)
                {
                    foreach ((string key, string value) in worldObject.modData.Pairs)
                        replacement.modData[key] = value;
                    location.Objects[tile] = replacement;
                    migratedPipes++;
                }
            }
        }

        migratedShippingValves += this.MigratePlacedShippingValves();

        int total = migratedItems + migratedPipes + migratedPorts + migratedRecipes + migratedShippingValves;
        if (total > 0)
        {
            this.Monitor.Log(
                $"Migrated Smart Pipes data: {migratedItems} inventory items, {migratedPipes} placed pipes, {migratedPorts} port settings, {migratedRecipes} recipes, {migratedShippingValves} standalone shipping valves.",
                LogLevel.Info);
        }
    }

    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        if (!Context.IsMainPlayer || !Context.IsWorldReady)
            return;

        Farmer player = Game1.player;
        if (player.FarmingLevel < 4
            || player.mailReceived.Contains(StoryIds.RobinIntroMail)
            || player.mailReceived.Contains(StoryIds.BlueprintPurchasedFlag)
            || player.modData.ContainsKey(StoryIds.IntroScheduledKey)
            || !this.OwnsSupportedProcessingMachine())
        {
            return;
        }

        Game1.addMailForTomorrow(StoryIds.RobinIntroMail);
        player.modData[StoryIds.IntroScheduledKey] = "true";
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return;

        this.UpdateStoryProgression(e);

        if (!this.config.EnableAutomation || !e.IsMultipleOf(this.config.TransferIntervalTicks))
            return;

        Stopwatch stopwatch = Stopwatch.StartNew();
        List<GameLocation> locations = new();
        Utility.ForEachLocation(location =>
        {
            locations.Add(location);
            return true;
        });

        List<PipeNetwork> networks = locations
            .SelectMany(location => this.scanner.FindNetworks(location))
            .ToList();
        if (networks.Count == 0)
            return;

        int start = this.nextNetworkIndex % networks.Count;
        int processedNetworks = 0;
        while (processedNetworks < networks.Count
            && stopwatch.ElapsedMilliseconds < this.config.MaxProcessingMilliseconds)
        {
            int index = (start + processedNetworks) % networks.Count;
            PipeNetwork network = networks[index];
            int movedTransfers = 0;
            while (movedTransfers < this.config.TransfersPerNetworkCycle
                && stopwatch.ElapsedMilliseconds < this.config.MaxProcessingMilliseconds)
            {
                TransferResult? result = this.transfers.ProcessOne(network);
                if (result is null)
                    break;

                movedTransfers++;
                if (this.config.ShowTransferMessages)
                {
                    Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("transfer.moved", new
                    {
                        item = result.ItemName,
                        target = result.TargetName
                    })));
                }
            }

            NetworkActivity activity = this.networkActivity.GetValueOrDefault(network.Id) ?? new NetworkActivity();
            activity.LastTransfers = movedTransfers;
            activity.TotalTransfers += movedTransfers;
            activity.ConsecutiveNoProgressCycles = movedTransfers == 0
                ? activity.ConsecutiveNoProgressCycles + 1
                : 0;
            activity.LastProcessedTick = e.Ticks;
            this.networkActivity[network.Id] = activity;
            processedNetworks++;
            this.nextNetworkIndex = (index + 1) % networks.Count;
        }
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsPlayerFree)
            return;

        Item? held = Game1.player.CurrentItem;
        if (Game1.currentLocation is Farm farm
            && this.shippingValves.TryFindAt(farm, this.Helper.Input.GetCursorPosition().GrabTile, out ShippingBin? shippingBin))
        {
            if (held?.QualifiedItemId == ItemIds.ShippingValve && e.Button.IsActionButton())
            {
                this.Helper.Input.Suppress(e.Button);
                this.InstallShippingValve(shippingBin);
                return;
            }

            if (held?.QualifiedItemId == ItemIds.Wrench && (e.Button.IsUseToolButton() || e.Button.IsActionButton()))
            {
                this.Helper.Input.Suppress(e.Button);
                if (e.Button.IsUseToolButton())
                    this.OpenShippingValveMenu(shippingBin);
                else if (this.Helper.Input.IsDown(SButton.LeftShift) || this.Helper.Input.IsDown(SButton.RightShift))
                    this.RemoveShippingValve(shippingBin);
                else
                    this.ToggleShippingValve(shippingBin);
                return;
            }
        }

        if (held?.QualifiedItemId == ItemIds.Port && e.Button.IsActionButton())
        {
            if (this.TryGetTarget(out SObject? target, out Vector2 tile))
            {
                this.Helper.Input.Suppress(e.Button);
                this.InstallPort(target, tile);
            }
            return;
        }

        if (this.TryGetTarget(out SObject? shippingTarget, out Vector2 shippingTile)
            && this.shippingValves.IsMiniShippingBin(shippingTarget))
        {
            if (held?.QualifiedItemId == ItemIds.ShippingValve && e.Button.IsActionButton())
            {
                this.Helper.Input.Suppress(e.Button);
                this.InstallMiniShippingValve(shippingTarget);
                return;
            }

            if (held?.QualifiedItemId == ItemIds.Wrench && (e.Button.IsUseToolButton() || e.Button.IsActionButton()))
            {
                this.Helper.Input.Suppress(e.Button);
                if (e.Button.IsUseToolButton())
                    this.OpenMiniShippingValveMenu(shippingTarget);
                else if (this.Helper.Input.IsDown(SButton.LeftShift) || this.Helper.Input.IsDown(SButton.RightShift))
                    this.RemoveMiniShippingValve(shippingTarget);
                else
                    this.ToggleMiniShippingValve(shippingTarget);
                return;
            }
        }

        if (held?.QualifiedItemId == ItemIds.Wrench && this.TryGetTarget(out SObject? wrenchTarget, out Vector2 wrenchTile))
        {
            if (e.Button.IsUseToolButton())
            {
                this.Helper.Input.Suppress(e.Button);
                this.OpenPortMenu(wrenchTarget, wrenchTile);
            }
            else if (e.Button.IsActionButton())
            {
                this.Helper.Input.Suppress(e.Button);
                if (this.Helper.Input.IsDown(SButton.LeftShift) || this.Helper.Input.IsDown(SButton.RightShift))
                    this.RemovePort(wrenchTarget);
                else
                    this.CyclePort(wrenchTarget, wrenchTile);
            }
        }
    }

    private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        GameLocation location = Game1.currentLocation;
        string? heldItemId = Game1.player.CurrentItem?.QualifiedItemId;
        bool inspecting = heldItemId is ItemIds.Wrench or ItemIds.LegacyToolWrench or ItemIds.LegacyObjectWrench;
        if (!inspecting && !this.config.ShowInstalledAttachmentsWithoutWrench)
            return;

        foreach ((Vector2 tile, SObject target) in location.Objects.Pairs)
        {
            PortSettings? settings = this.ports.Get(target);
            bool isShippingValve = false;
            if (settings is null && this.shippingValves.IsMiniShippingBin(target))
            {
                settings = this.shippingValves.Get(target);
                isShippingValve = settings is not null;
            }

            if (settings is not null)
                this.DrawAttachment(e.SpriteBatch, location, tile, settings.Mode, isShippingValve, inspecting);
        }

        if (location is Farm farm)
        {
            foreach ((ShippingBin shippingBin, PortSettings settings) in this.shippingValves.GetInstalled(farm))
                this.DrawShippingBinAttachment(e.SpriteBatch, farm, shippingBin, settings.Mode, inspecting);
        }
    }

    private void DrawAttachment(SpriteBatch spriteBatch, GameLocation location, Vector2 endpointTile, PortMode mode, bool shippingValve, bool inspecting)
    {
        List<int> directions = new();
        Vector2[] offsets = { new(0, -1), new(1, 0), new(0, 1), new(-1, 0) };
        for (int direction = 0; direction < offsets.Length; direction++)
        {
            if (location.Objects.TryGetValue(endpointTile + offsets[direction], out SObject? neighbor)
                && neighbor.QualifiedItemId is ItemIds.Pipe or ItemIds.LegacyPipe)
            {
                directions.Add(direction);
            }
        }

        if (directions.Count == 0)
            directions.Add(2);

        foreach (int direction in directions)
            this.DrawAttachmentSprite(spriteBatch, endpointTile, direction, mode, shippingValve, inspecting);
    }

    private void DrawShippingBinAttachment(SpriteBatch spriteBatch, Farm farm, ShippingBin shippingBin, PortMode mode, bool inspecting)
    {
        Rectangle bounds = shippingBin.GetBoundingBox();
        int left = bounds.Left / Game1.tileSize;
        int right = (bounds.Right - 1) / Game1.tileSize;
        int top = bounds.Top / Game1.tileSize;
        int bottom = (bounds.Bottom - 1) / Game1.tileSize;
        bool drewConnection = false;

        for (int x = left; x <= right; x++)
        {
            drewConnection |= this.TryDrawShippingConnection(spriteBatch, farm, new Vector2(x, top), new Vector2(x, top - 1), 0, mode, inspecting);
            drewConnection |= this.TryDrawShippingConnection(spriteBatch, farm, new Vector2(x, bottom), new Vector2(x, bottom + 1), 2, mode, inspecting);
        }
        for (int y = top; y <= bottom; y++)
        {
            drewConnection |= this.TryDrawShippingConnection(spriteBatch, farm, new Vector2(right, y), new Vector2(right + 1, y), 1, mode, inspecting);
            drewConnection |= this.TryDrawShippingConnection(spriteBatch, farm, new Vector2(left, y), new Vector2(left - 1, y), 3, mode, inspecting);
        }

        if (!drewConnection)
            this.DrawAttachmentSprite(spriteBatch, new Vector2(left, bottom), 2, mode, shippingValve: true, inspecting);
    }

    private bool TryDrawShippingConnection(SpriteBatch spriteBatch, Farm farm, Vector2 endpointTile, Vector2 pipeTile, int direction, PortMode mode, bool inspecting)
    {
        if (!farm.Objects.TryGetValue(pipeTile, out SObject? neighbor)
            || neighbor.QualifiedItemId is not (ItemIds.Pipe or ItemIds.LegacyPipe))
        {
            return false;
        }

        this.DrawAttachmentSprite(spriteBatch, endpointTile, direction, mode, shippingValve: true, inspecting);
        return true;
    }

    private void DrawAttachmentSprite(SpriteBatch spriteBatch, Vector2 tile, int direction, PortMode mode, bool shippingValve, bool inspecting)
    {
        Texture2D texture = Game1.content.Load<Texture2D>("Mods/ParfaitRana.SmartPipes/Attachments");
        int row = inspecting
            ? (shippingValve ? 4 : 0) + (int)mode
            : shippingValve ? 9 : 8;
        Rectangle source = new(direction * 16, row * 16, 16, 16);
        Vector2 position = new(tile.X * Game1.tileSize - Game1.viewport.X, tile.Y * Game1.tileSize - Game1.viewport.Y);
        float alpha = inspecting ? 1f : 0.82f;
        spriteBatch.Draw(texture, position, source, Color.White * alpha, 0f, Vector2.Zero, Game1.pixelZoom, SpriteEffects.None, 1f);
    }

    private void OnObjectListChanged(object? sender, ObjectListChangedEventArgs e)
    {
        if (!Context.IsMainPlayer || !Context.IsWorldReady)
            return;

        this.scanner.Invalidate(e.Location);

        foreach ((Vector2 tile, SObject removed) in e.Removed)
        {
            List<Item> attachments = new();
            if (removed.modData.Remove(ModDataKeys.Port))
                attachments.Add(ItemRegistry.Create(ItemIds.Port));
            if (this.shippingValves.IsMiniShippingBin(removed)
                && removed.modData.Remove(ModDataKeys.ShippingValve))
            {
                attachments.Add(ItemRegistry.Create(ItemIds.ShippingValve));
            }

            foreach (Item attachment in attachments)
                Game1.createItemDebris(attachment, tile * Game1.tileSize, -1, e.Location);

            if (attachments.Count > 0)
            {
                Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("attachment.returned", new
                {
                    count = attachments.Count
                })));
            }
        }
    }

    private void OnBuildingListChanged(object? sender, BuildingListChangedEventArgs e)
    {
        if (Context.IsMainPlayer && Context.IsWorldReady)
            this.scanner.Invalidate(e.Location);
    }

    private void InstallPort(SObject target, Vector2 tile)
    {
        if (!this.ports.IsValidTarget(target))
        {
            Game1.showRedMessage(this.Helper.Translation.Get("port.invalid-target"));
            return;
        }

        if (this.ports.Get(target) is not null)
        {
            Game1.showRedMessage(this.Helper.Translation.Get("port.exists"));
            return;
        }

        PortSettings settings = this.ports.CreateDefault(target);
        this.ports.Set(target, settings);
        this.SelectPort(target, tile, showMessage: false);
        Game1.player.reduceActiveItemByOne();
        if (this.config.ShowInstallationMessages)
            Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("port.installed", new { mode = this.ModeName(settings.Mode) })));
    }

    private void CyclePort(SObject target, Vector2 tile)
    {
        PortSettings? settings = this.ports.Get(target);
        if (settings is null)
        {
            Game1.showRedMessage(this.Helper.Translation.Get("port.none"));
            return;
        }

        settings.Mode = settings.Mode switch
        {
            PortMode.Input => PortMode.Output,
            PortMode.Output => PortMode.Both,
            PortMode.Both => PortMode.Disabled,
            _ => PortMode.Input
        };
        this.ports.Set(target, settings);
        this.SelectPort(target, tile, showMessage: false);
        Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("port.mode", new { mode = this.ModeName(settings.Mode) })));
    }

    private void RemovePort(SObject target)
    {
        if (!this.ports.Remove(target))
        {
            Game1.showRedMessage(this.Helper.Translation.Get("port.none"));
            return;
        }

        this.ClearSelectionIfTarget(target);
        Item port = ItemRegistry.Create(ItemIds.Port);
        if (!Game1.player.addItemToInventoryBool(port))
            Game1.createItemDebris(port, Game1.player.Position, Game1.player.FacingDirection);
        Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("port.removed")));
    }

    private void InstallShippingValve(ShippingBin shippingBin)
    {
        if (this.shippingValves.Get(shippingBin) is not null)
        {
            Game1.showRedMessage(this.Helper.Translation.Get("shipping-valve.exists"));
            return;
        }

        PortSettings settings = new() { Mode = PortMode.Disabled, Priority = -100 };
        this.shippingValves.Set(shippingBin, settings);
        this.SelectShippingValve(shippingBin, showMessage: false);
        Game1.player.reduceActiveItemByOne();
        if (this.config.ShowInstallationMessages)
            Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("shipping-valve.installed")));
    }

    private void ToggleShippingValve(ShippingBin shippingBin)
    {
        PortSettings? settings = this.shippingValves.Get(shippingBin);
        if (settings is null)
        {
            Game1.showRedMessage(this.Helper.Translation.Get("shipping-valve.none"));
            return;
        }

        settings.Mode = settings.Mode == PortMode.Disabled ? PortMode.Input : PortMode.Disabled;
        this.shippingValves.Set(shippingBin, settings);
        this.SelectShippingValve(shippingBin, showMessage: false);
        Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("shipping-valve.mode", new
        {
            mode = this.ModeName(settings.Mode)
        })));
    }

    private void RemoveShippingValve(ShippingBin shippingBin)
    {
        if (!this.shippingValves.Remove(shippingBin))
        {
            Game1.showRedMessage(this.Helper.Translation.Get("shipping-valve.none"));
            return;
        }

        this.ClearShippingValveSelection(shippingBin);
        Item valve = ItemRegistry.Create(ItemIds.ShippingValve);
        if (!Game1.player.addItemToInventoryBool(valve))
            Game1.createItemDebris(valve, Game1.player.Position, Game1.player.FacingDirection);
        Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("shipping-valve.removed")));
    }

    private void SelectShippingValve(ShippingBin shippingBin, bool showMessage)
    {
        PortSettings? settings = this.shippingValves.Get(shippingBin);
        if (settings is null)
        {
            if (showMessage)
                Game1.showRedMessage(this.Helper.Translation.Get("shipping-valve.none"));
            return;
        }

        this.selectedLocation = null;
        this.selectedTile = null;
        this.selectedShippingBin = shippingBin;
        this.selectedMiniShippingBin = null;
        this.selectedShippingValveProxy = this.shippingValves.CreateEndpointObject(settings);
        if (showMessage)
            this.ShowSelectedPort(settings);
    }

    private void OpenShippingValveMenu(ShippingBin shippingBin)
    {
        PortSettings? settings = this.shippingValves.Get(shippingBin);
        if (settings is null)
        {
            Game1.showRedMessage(this.Helper.Translation.Get("shipping-valve.none"));
            return;
        }

        this.SelectShippingValve(shippingBin, showMessage: false);
        Game1.activeClickableMenu = new PortConfigMenu(
            settings,
            shippingValve: true,
            this.Helper.Translation.Get("ui.target.main-shipping-bin"),
            this.Helper.Translation,
            updated =>
            {
                this.shippingValves.Set(shippingBin, updated);
                if (this.selectedShippingValveProxy is not null)
                    this.selectedShippingValveProxy.modData[ModDataKeys.Port] = updated.Serialize();
            });
    }

    private void ClearShippingValveSelection(ShippingBin shippingBin)
    {
        if (!ReferenceEquals(this.selectedShippingBin, shippingBin))
            return;

        this.selectedShippingBin = null;
        this.selectedMiniShippingBin = null;
        this.selectedShippingValveProxy = null;
    }

    private void InstallMiniShippingValve(SObject miniShippingBin)
    {
        if (this.shippingValves.Get(miniShippingBin) is not null)
        {
            Game1.showRedMessage(this.Helper.Translation.Get("shipping-valve.exists"));
            return;
        }

        this.shippingValves.Set(miniShippingBin, new PortSettings { Mode = PortMode.Disabled, Priority = -100 });
        this.SelectMiniShippingValve(miniShippingBin, showMessage: false);
        Game1.player.reduceActiveItemByOne();
        if (this.config.ShowInstallationMessages)
            Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("shipping-valve.installed-mini")));
    }

    private void ToggleMiniShippingValve(SObject miniShippingBin)
    {
        PortSettings? settings = this.shippingValves.Get(miniShippingBin);
        if (settings is null)
        {
            Game1.showRedMessage(this.Helper.Translation.Get("shipping-valve.none"));
            return;
        }

        settings.Mode = settings.Mode == PortMode.Disabled ? PortMode.Input : PortMode.Disabled;
        this.shippingValves.Set(miniShippingBin, settings);
        this.SelectMiniShippingValve(miniShippingBin, showMessage: false);
        Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("shipping-valve.mode", new
        {
            mode = this.ModeName(settings.Mode)
        })));
    }

    private void RemoveMiniShippingValve(SObject miniShippingBin)
    {
        if (!this.shippingValves.Remove(miniShippingBin))
        {
            Game1.showRedMessage(this.Helper.Translation.Get("shipping-valve.none"));
            return;
        }

        if (ReferenceEquals(this.selectedMiniShippingBin, miniShippingBin))
        {
            this.selectedMiniShippingBin = null;
            this.selectedShippingValveProxy = null;
        }

        Item valve = ItemRegistry.Create(ItemIds.ShippingValve);
        if (!Game1.player.addItemToInventoryBool(valve))
            Game1.createItemDebris(valve, Game1.player.Position, Game1.player.FacingDirection);
        Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("shipping-valve.removed")));
    }

    private void SelectMiniShippingValve(SObject miniShippingBin, bool showMessage)
    {
        PortSettings? settings = this.shippingValves.Get(miniShippingBin);
        if (settings is null)
        {
            if (showMessage)
                Game1.showRedMessage(this.Helper.Translation.Get("shipping-valve.none"));
            return;
        }

        this.selectedLocation = null;
        this.selectedTile = null;
        this.selectedShippingBin = null;
        this.selectedMiniShippingBin = miniShippingBin;
        this.selectedShippingValveProxy = this.shippingValves.CreateEndpointObject(settings);
        if (showMessage)
            this.ShowSelectedPort(settings);
    }

    private void OpenMiniShippingValveMenu(SObject miniShippingBin)
    {
        PortSettings? settings = this.shippingValves.Get(miniShippingBin);
        if (settings is null)
        {
            Game1.showRedMessage(this.Helper.Translation.Get("shipping-valve.none"));
            return;
        }

        this.SelectMiniShippingValve(miniShippingBin, showMessage: false);
        Game1.activeClickableMenu = new PortConfigMenu(
            settings,
            shippingValve: true,
            miniShippingBin.DisplayName,
            this.Helper.Translation,
            updated =>
            {
                this.shippingValves.Set(miniShippingBin, updated);
                if (this.selectedShippingValveProxy is not null)
                    this.selectedShippingValveProxy.modData[ModDataKeys.Port] = updated.Serialize();
            });
    }

    private bool TryGetTarget(out SObject target, out Vector2 tile)
    {
        tile = this.Helper.Input.GetCursorPosition().GrabTile;
        return Game1.currentLocation.Objects.TryGetValue(tile, out target!);
    }

    private bool TryGetCommandTarget(out SObject target)
    {
        if (this.selectedShippingBin is not null
            && this.selectedShippingValveProxy is not null
            && this.shippingValves.Get(this.selectedShippingBin) is PortSettings shippingSettings)
        {
            this.selectedShippingValveProxy.modData[ModDataKeys.Port] = shippingSettings.Serialize();
            target = this.selectedShippingValveProxy;
            return true;
        }

        if (this.selectedMiniShippingBin is not null
            && this.selectedShippingValveProxy is not null
            && this.shippingValves.Get(this.selectedMiniShippingBin) is PortSettings miniSettings)
        {
            this.selectedShippingValveProxy.modData[ModDataKeys.Port] = miniSettings.Serialize();
            target = this.selectedShippingValveProxy;
            return true;
        }

        if (this.selectedLocation is not null
            && this.selectedTile is Vector2 selectedTile
            && this.selectedLocation.Objects.TryGetValue(selectedTile, out target!)
            && this.ports.Get(target) is not null)
        {
            return true;
        }

        if (this.TryGetTarget(out target!, out Vector2 tile) && this.ports.Get(target) is not null)
        {
            this.SelectPort(target, tile, showMessage: false);
            return true;
        }

        target = null!;
        return false;
    }

    private void SelectPort(SObject target, Vector2 tile, bool showMessage)
    {
        PortSettings? settings = this.ports.Get(target);
        if (settings is null)
            return;

        this.selectedLocation = Game1.currentLocation;
        this.selectedTile = tile;
        this.selectedShippingBin = null;
        this.selectedMiniShippingBin = null;
        this.selectedShippingValveProxy = null;
        if (showMessage)
            this.ShowSelectedPort(settings);
    }

    private void OpenPortMenu(SObject target, Vector2 tile)
    {
        PortSettings? settings = this.ports.Get(target);
        if (settings is null)
        {
            Game1.showRedMessage(this.Helper.Translation.Get("port.none"));
            return;
        }

        this.SelectPort(target, tile, showMessage: false);
        Game1.activeClickableMenu = new PortConfigMenu(
            settings,
            shippingValve: false,
            target.DisplayName,
            this.Helper.Translation,
            updated => this.ports.Set(target, updated));
    }

    private void ShowSelectedPort(PortSettings settings)
    {
        Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("port.selected", new
        {
            mode = this.ModeName(settings.Mode),
            priority = settings.Priority,
            allow = settings.Allow.Count,
            deny = settings.Deny.Count,
            keep = settings.MinimumKeep,
            quality = this.QualityRangeName(settings)
        })));
    }

    private void ClearSelectionIfTarget(SObject target)
    {
        if (this.selectedLocation is not null
            && this.selectedTile is Vector2 tile
            && this.selectedLocation.Objects.TryGetValue(tile, out SObject? selected)
            && ReferenceEquals(selected, target))
        {
            this.selectedLocation = null;
            this.selectedTile = null;
        }
    }

    private void SaveCommandTarget(SObject target, PortSettings settings)
    {
        this.ports.Set(target, settings);
        if (ReferenceEquals(target, this.selectedShippingValveProxy) && this.selectedShippingBin is not null)
            this.shippingValves.Set(this.selectedShippingBin, settings);
        else if (ReferenceEquals(target, this.selectedShippingValveProxy) && this.selectedMiniShippingBin is not null)
            this.shippingValves.Set(this.selectedMiniShippingBin, settings);
    }

    private void OnHelpCommand(string command, string[] args)
    {
        this.Monitor.Log(
            "Smart Pipes commands:\n" +
            "  sp_give - give development components\n" +
            "  sp_scan - scan network at cursor\n" +
            "  Left-click a port with the wrench to select it for commands\n" +
            "  sp_port <input|output|both|disabled> [priority]\n" +
            "  sp_keep <amount>\n" +
            "  sp_filter <allow|deny|remove|clear> [qualified ID or context tag]\n" +
            "  sp_quality <any|exact|min|max> [normal|silver|gold|iridium]",
            LogLevel.Info);
    }

    private void OnGiveCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
            return;

        foreach ((string id, int count) in new[]
        {
            (ItemIds.Pipe, 20),
            (ItemIds.Port, 8),
            (ItemIds.ShippingValve, 1),
            (ItemIds.Wrench, 1)
        })
        {
            Item item = ItemRegistry.Create(id, count);
            if (!Game1.player.addItemToInventoryBool(item))
                Game1.createItemDebris(item, Game1.player.Position, Game1.player.FacingDirection);
        }
    }

    private void OnStoryCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
            return;

        Farmer player = Game1.player;
        string operation = args.FirstOrDefault()?.ToLowerInvariant() ?? "status";
        switch (operation)
        {
            case "status":
                this.Monitor.Log(
                    $"Smart Pipes story: Robin mail={player.mailReceived.Contains(StoryIds.RobinIntroMail)}, "
                    + $"blueprint={player.mailReceived.Contains(StoryIds.BlueprintPurchasedFlag)}, "
                    + $"tutorial={player.mailReceived.Contains(StoryIds.TutorialCompletedFlag)}, "
                    + $"Lewis mail={player.mailReceived.Contains(StoryIds.LewisValveMail)}.",
                    LogLevel.Info);
                break;

            case "inspect":
                this.LogStoryQuestDiagnostics(player);
                break;

            case "start":
                player.mailReceived.Remove(StoryIds.RobinIntroMail);
                player.modData.Remove(StoryIds.IntroScheduledKey);
                RemoveQueuedMail(player.mailForTomorrow, StoryIds.RobinIntroMail);
                if (!player.mailbox.Contains(StoryIds.RobinIntroMail))
                    player.mailbox.Add(StoryIds.RobinIntroMail);
                this.Helper.GameContent.InvalidateCache("Data/Shops");
                this.Monitor.Log("Robin's Smart Pipes introduction letter was placed in the mailbox.", LogLevel.Info);
                break;

            case "reset":
                foreach (string flag in new[]
                {
                    StoryIds.RobinIntroMail,
                    StoryIds.BlueprintPurchasedFlag,
                    StoryIds.TutorialCompletedFlag,
                    StoryIds.LewisValveMail
                })
                {
                    player.mailReceived.Remove(flag);
                    RemoveQueuedMail(player.mailbox, flag);
                    RemoveQueuedMail(player.mailForTomorrow, flag);
                }

                player.modData.Remove(StoryIds.IntroScheduledKey);
                Quest? tutorial = player.questLog.FirstOrDefault(candidate => candidate.id.Value == StoryIds.TutorialQuest);
                if (tutorial is not null)
                    player.questLog.Remove(tutorial);
                foreach (string recipe in new[]
                {
                    ItemIds.PipeRecipe,
                    ItemIds.PortRecipe,
                    ItemIds.WrenchRecipe,
                    ItemIds.ShippingValveRecipe
                })
                {
                    player.craftingRecipes.Remove(recipe);
                }

                this.Helper.GameContent.InvalidateCache("Data/Shops");
                this.Monitor.Log("Smart Pipes story and recipe unlocks were reset for this player. Run 'sp_story start' to test immediately.", LogLevel.Warn);
                break;

            default:
                this.Monitor.Log("Usage: sp_story <status|inspect|start|reset>", LogLevel.Warn);
                break;
        }
    }

    private void LogStoryQuestDiagnostics(Farmer player)
    {
        string expectedTitle = this.Helper.Translation.Get("story.quest.title");
        string expectedDescription = this.Helper.Translation.Get("story.quest.description");
        string expectedObjective = this.Helper.Translation.Get("story.quest.objective");
        IDictionary<string, string> questData = DataLoader.Quests(Game1.content);
        questData.TryGetValue(StoryIds.TutorialQuest, out string? rawData);

        this.Monitor.Log($"Smart Pipes tutorial quest asset: {EscapeDiagnostic(rawData)}", LogLevel.Info);
        this.Monitor.Log(
            $"Expected text: title={EscapeDiagnostic(expectedTitle)}, description={EscapeDiagnostic(expectedDescription)}, objective={EscapeDiagnostic(expectedObjective)}.",
            LogLevel.Info);

        Quest[] matches = player.questLog.Where(candidate => candidate.id.Value == StoryIds.TutorialQuest).ToArray();
        this.Monitor.Log($"Tutorial quest instances in player.questLog: {matches.Length}.", LogLevel.Info);
        foreach (Quest quest in matches)
        {
            this.Monitor.Log(
                $"Quest type={quest.GetType().FullName}, id={EscapeDiagnostic(quest.id.Value)}, "
                + $"GetName={EscapeDiagnostic(quest.GetName())}, GetDescription={EscapeDiagnostic(quest.GetDescription())}, "
                + $"objectives=[{string.Join(", ", quest.GetObjectiveDescriptions().Select(EscapeDiagnostic))}], "
                + $"accepted={quest.accepted.Value}, showNew={quest.showNew.Value}, completed={quest.completed.Value}.",
                LogLevel.Info);
        }
    }

    private static string EscapeDiagnostic(string? value)
    {
        if (value is null)
            return "<null>";

        return $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}\" (length {value.Length})";
    }

    private static void RemoveQueuedMail(IList<string> mail, string id)
    {
        for (int index = mail.Count - 1; index >= 0; index--)
        {
            if (mail[index].StartsWith(id, StringComparison.Ordinal))
                mail.RemoveAt(index);
        }
    }

    private static void RemoveQueuedMail(Netcode.NetStringHashSet mail, string id)
    {
        foreach (string entry in mail.Where(value => value.StartsWith(id, StringComparison.Ordinal)).ToArray())
            mail.Remove(entry);
    }

    private void OnScanCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
            return;

        PipeNetwork? network = this.scanner.FindAtOrNextTo(Game1.currentLocation, this.Helper.Input.GetCursorPosition().GrabTile);
        if (network is null)
        {
            this.Monitor.Log(this.Helper.Translation.Get("network.none"), LogLevel.Info);
            return;
        }

        this.Monitor.Log(this.Helper.Translation.Get("network.summary", new
        {
            pipes = network.Pipes.Count,
            endpoints = network.Endpoints.Count
        }), LogLevel.Info);

        NetworkActivity? activity = this.networkActivity.GetValueOrDefault(network.Id);
        this.Monitor.Log(this.Helper.Translation.Get("network.activity", new
        {
            last = activity?.LastTransfers ?? 0,
            total = activity?.TotalTransfers ?? 0,
            stalled = activity?.ConsecutiveNoProgressCycles ?? 0,
            hits = this.scanner.CacheHits,
            misses = this.scanner.CacheMisses
        }), LogLevel.Info);

        foreach (NetworkEndpoint endpoint in network.Endpoints.OrderBy(endpoint => endpoint.Distance))
        {
            this.Monitor.Log($"  {endpoint.Tile}: {endpoint.Object.DisplayName}, {endpoint.Settings.Mode}, priority {endpoint.Settings.Priority}, distance {endpoint.Distance}", LogLevel.Info);
        }
    }

    private void OnPortCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
            return;

        if (!this.TryGetCommandTarget(out SObject? target))
        {
            this.LogNoSelectedPort();
            return;
        }

        PortSettings? settings = this.ports.Get(target);
        if (settings is null || args.Length == 0 || !Enum.TryParse(args[0], true, out PortMode mode))
        {
            this.Monitor.Log("Usage: sp_port <input|output|both|disabled> [priority]", LogLevel.Warn);
            return;
        }

        if (target.QualifiedItemId == ItemIds.ShippingValve && mode is not PortMode.Input and not PortMode.Disabled)
        {
            this.Monitor.Log("A shipping valve only supports input or disabled mode.", LogLevel.Warn);
            return;
        }
        settings.Mode = mode;
        if (args.Length > 1 && int.TryParse(args[1], out int priority))
            settings.Priority = priority;
        this.SaveCommandTarget(target, settings);
        this.Monitor.Log($"Port set to {settings.Mode}, priority {settings.Priority}.", LogLevel.Info);
    }

    private void OnKeepCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
            return;

        if (!this.TryGetCommandTarget(out SObject? target))
        {
            this.LogNoSelectedPort();
            return;
        }

        if (args.Length == 0 || !int.TryParse(args[0], out int amount))
        {
            this.Monitor.Log("Usage: sp_keep <amount>", LogLevel.Warn);
            return;
        }

        PortSettings? settings = this.ports.Get(target);
        if (settings is null)
            return;
        settings.MinimumKeep = Math.Max(0, amount);
        this.SaveCommandTarget(target, settings);
        this.Monitor.Log($"Minimum keep set to {settings.MinimumKeep}.", LogLevel.Info);
    }

    private void OnFilterCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
            return;

        if (!this.TryGetCommandTarget(out SObject? target))
        {
            this.LogNoSelectedPort();
            return;
        }

        if (args.Length == 0)
        {
            this.Monitor.Log("Usage: sp_filter <allow|deny|remove|clear> [qualified ID or context tag]", LogLevel.Warn);
            return;
        }

        PortSettings? settings = this.ports.Get(target);
        if (settings is null)
            return;

        string operation = args[0].ToLowerInvariant();
        string value = NormalizeFilterValue(string.Join(' ', args.Skip(1)));
        switch (operation)
        {
            case "allow" when value.Length > 0:
                settings.Allow.Add(value);
                break;
            case "deny" when value.Length > 0:
                settings.Deny.Add(value);
                break;
            case "remove" when value.Length > 0:
                settings.Allow.Remove(value);
                settings.Deny.Remove(value);
                break;
            case "clear":
                settings.Allow.Clear();
                settings.Deny.Clear();
                break;
            default:
                this.Monitor.Log("Usage: sp_filter <allow|deny|remove|clear> [qualified ID or context tag]", LogLevel.Warn);
                return;
        }

        this.SaveCommandTarget(target, settings);
        this.Monitor.Log($"Filter updated. Allow={settings.Allow.Count}, deny={settings.Deny.Count}.", LogLevel.Info);
    }

    private void OnQualityCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
            return;

        if (!this.TryGetCommandTarget(out SObject? target))
        {
            this.LogNoSelectedPort();
            return;
        }

        PortSettings? settings = this.ports.Get(target);
        if (settings is null || args.Length == 0)
        {
            this.LogQualityUsage();
            return;
        }

        string operation = args[0].ToLowerInvariant();
        if (operation == "any")
        {
            settings.MinimumQuality = 0;
            settings.MaximumQuality = 4;
        }
        else
        {
            if (args.Length < 2 || !TryParseQuality(args[1], out int quality))
            {
                this.LogQualityUsage();
                return;
            }

            switch (operation)
            {
                case "exact":
                    settings.MinimumQuality = quality;
                    settings.MaximumQuality = quality;
                    break;
                case "min":
                    settings.MinimumQuality = quality;
                    settings.MaximumQuality = 4;
                    break;
                case "max":
                    settings.MinimumQuality = 0;
                    settings.MaximumQuality = quality;
                    break;
                default:
                    this.LogQualityUsage();
                    return;
            }
        }

        this.SaveCommandTarget(target, settings);
        this.Monitor.Log($"Quality filter updated: {this.QualityRangeName(settings)}.", LogLevel.Info);
    }

    private void LogQualityUsage()
    {
        this.Monitor.Log("Usage: sp_quality <any|exact|min|max> [normal|silver|gold|iridium]", LogLevel.Warn);
    }

    private static bool TryParseQuality(string value, out int quality)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "normal":
            case "0":
                quality = 0;
                return true;
            case "silver":
            case "1":
                quality = 1;
                return true;
            case "gold":
            case "2":
                quality = 2;
                return true;
            case "iridium":
            case "4":
                quality = 4;
                return true;
            default:
                quality = 0;
                return false;
        }
    }

    private string QualityRangeName(PortSettings settings)
    {
        if (settings.MinimumQuality == 0 && settings.MaximumQuality == 4)
            return this.Helper.Translation.Get("quality.any");
        if (settings.MinimumQuality == settings.MaximumQuality)
            return this.Helper.Translation.Get("quality.exact", new { quality = this.QualityName(settings.MinimumQuality) });
        if (settings.MaximumQuality == 4)
            return this.Helper.Translation.Get("quality.minimum", new { quality = this.QualityName(settings.MinimumQuality) });
        return this.Helper.Translation.Get("quality.maximum", new { quality = this.QualityName(settings.MaximumQuality) });
    }

    private string QualityName(int quality) => this.Helper.Translation.Get($"quality.{quality}");

    private void LogNoSelectedPort()
    {
        this.Monitor.Log(this.Helper.Translation.Get("command.no-selected-port"), LogLevel.Warn);
    }

    private static string NormalizeFilterValue(string value)
    {
        value = value.Trim();
        return value.StartsWith("(0)", StringComparison.OrdinalIgnoreCase)
            ? $"(O){value[3..]}"
            : value;
    }

    private bool ShouldShowPipes()
    {
        return !this.config.HidePipesUnlessHoldingComponent
            || !Context.IsWorldReady
            || ItemIds.RevealsPipes(Game1.player?.CurrentItem?.QualifiedItemId);
    }

    private static int MigrateLegacyItems(IList<Item> items)
    {
        int migrated = 0;
        for (int index = 0; index < items.Count; index++)
        {
            Item? item = items[index];
            if (item is null)
                continue;

            string? replacementId = ItemIds.GetReplacement(item.QualifiedItemId);
            if (replacementId is null)
                continue;

            Item replacement = ItemRegistry.Create(replacementId, item.Stack);
            foreach ((string key, string value) in item.modData.Pairs)
                replacement.modData[key] = value;
            items[index] = replacement;
            migrated++;
        }

        return migrated;
    }

    private static bool MigratePortData(SObject target)
    {
        bool migrated = false;
        if (target.modData.TryGetValue(ModDataKeys.LegacyPort, out string? legacyRaw))
        {
            if (!target.modData.ContainsKey(ModDataKeys.Port))
                target.modData[ModDataKeys.Port] = legacyRaw.Replace("YanYim.SmartPipes", "ParfaitRana.SmartPipes", StringComparison.Ordinal);
            target.modData.Remove(ModDataKeys.LegacyPort);
            migrated = true;
        }

        if (target.modData.TryGetValue(ModDataKeys.Port, out string? currentRaw)
            && currentRaw.Contains("YanYim.SmartPipes", StringComparison.Ordinal))
        {
            target.modData[ModDataKeys.Port] = currentRaw.Replace("YanYim.SmartPipes", "ParfaitRana.SmartPipes", StringComparison.Ordinal);
            migrated = true;
        }

        return migrated;
    }

    private static int MigrateLegacyRecipes(Farmer farmer)
    {
        int migrated = 0;
        foreach ((string legacy, string current) in new Dictionary<string, string>
        {
            ["YanYim.SmartPipes_Pipe"] = "ParfaitRana.SmartPipes_Pipe",
            ["YanYim.SmartPipes_Port"] = "ParfaitRana.SmartPipes_Port",
            ["YanYim.SmartPipes_ShippingValve"] = "ParfaitRana.SmartPipes_ShippingValve",
            ["YanYim.SmartPipes_Wrench"] = "ParfaitRana.SmartPipes_Wrench"
        })
        {
            if (!farmer.craftingRecipes.TryGetValue(legacy, out int craftedCount))
                continue;

            farmer.craftingRecipes.Remove(legacy);
            if (!farmer.craftingRecipes.ContainsKey(current))
                farmer.craftingRecipes[current] = craftedCount;
            migrated++;
        }

        return migrated;
    }

    private int MigratePlacedShippingValves()
    {
        Farm farm = Game1.getFarm();
        ShippingBin? shippingBin = farm.buildings.OfType<ShippingBin>().FirstOrDefault();
        if (shippingBin is null)
            return 0;

        int migrated = 0;
        foreach ((Vector2 tile, SObject worldObject) in farm.Objects.Pairs
            .Where(pair => pair.Value.QualifiedItemId == ItemIds.ShippingValve)
            .ToArray())
        {
            if (this.shippingValves.Get(shippingBin) is null)
            {
                PortSettings settings = this.ports.Get(worldObject)
                    ?? new PortSettings { Mode = PortMode.Disabled, Priority = -100 };
                this.shippingValves.Set(shippingBin, settings);
            }
            else
            {
                Item returnedValve = ItemRegistry.Create(ItemIds.ShippingValve);
                if (!Game1.MasterPlayer.addItemToInventoryBool(returnedValve))
                    Game1.createItemDebris(returnedValve, tile * Game1.tileSize, -1, farm);
            }

            farm.Objects.Remove(tile);
            migrated++;
        }

        return migrated;
    }

    private string ModeName(PortMode mode) => this.Helper.Translation.Get($"mode.{mode.ToString().ToLowerInvariant()}");
}

internal sealed class NetworkActivity
{
    public int LastTransfers { get; set; }

    public long TotalTransfers { get; set; }

    public int ConsecutiveNoProgressCycles { get; set; }

    public ulong LastProcessedTick { get; set; }
}

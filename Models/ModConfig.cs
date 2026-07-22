namespace SmartPipes.Models;

internal sealed class ModConfig
{
    public bool EnableAutomation { get; set; } = true;

    public uint TransferIntervalTicks { get; set; } = 30;

    public int ItemsPerTransfer { get; set; } = 1;

    public int TransfersPerNetworkCycle { get; set; } = 8;

    public int MaxProcessingMilliseconds { get; set; } = 4;

    public bool ShowTransferMessages { get; set; } = false;

    public bool ShowInstallationMessages { get; set; } = false;

    public bool HidePipesUnlessHoldingComponent { get; set; } = true;

    public bool ShowInstalledAttachmentsWithoutWrench { get; set; } = true;
}

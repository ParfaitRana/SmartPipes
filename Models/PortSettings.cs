using System.Text.Json;
using StardewValley;

namespace SmartPipes.Models;

internal sealed class PortSettings
{
    public PortMode Mode { get; set; } = PortMode.Input;

    public int Priority { get; set; }

    public int MinimumKeep { get; set; }

    public int MaximumStock { get; set; }

    public int MinimumQuality { get; set; }

    public int MaximumQuality { get; set; } = 4;

    public HashSet<string> Allow { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Deny { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool CanExtract => this.Mode is PortMode.Output or PortMode.Both;

    public bool CanInsert => this.Mode is PortMode.Input or PortMode.Both;

    public bool Allows(Item item)
    {
        if (item.Quality < this.MinimumQuality || item.Quality > this.MaximumQuality)
            return false;

        IEnumerable<string> identities = GetIdentities(item);
        if (this.Deny.Count > 0 && identities.Any(this.Deny.Contains))
            return false;

        return this.Allow.Count == 0 || identities.Any(this.Allow.Contains);
    }

    public string Serialize() => JsonSerializer.Serialize(this);

    public static PortSettings? Deserialize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            return JsonSerializer.Deserialize<PortSettings>(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<string> GetIdentities(Item item)
    {
        yield return item.QualifiedItemId;
        yield return item.ItemId;

        foreach (string tag in item.GetContextTags())
            yield return tag;
    }
}

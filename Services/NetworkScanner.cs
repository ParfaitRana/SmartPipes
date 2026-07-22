using Microsoft.Xna.Framework;
using SmartPipes.Framework;
using SmartPipes.Models;
using StardewValley;
using StardewValley.Locations;
using SObject = StardewValley.Object;

namespace SmartPipes.Services;

internal sealed record NetworkEndpoint(Vector2 Tile, SObject Object, PortSettings Settings, int Distance);

internal sealed record PipeNetwork(
    string Id,
    GameLocation Location,
    HashSet<Vector2> Pipes,
    List<NetworkEndpoint> Endpoints
);

internal sealed class NetworkScanner
{
    private static readonly Vector2[] Directions =
    {
        new(0, -1),
        new(1, 0),
        new(0, 1),
        new(-1, 0)
    };

    private readonly PortService ports;
    private readonly ShippingValveService shippingValves;
    private readonly Dictionary<GameLocation, IReadOnlyList<PipeNetwork>> cache = new();

    public long CacheHits { get; private set; }

    public long CacheMisses { get; private set; }

    public NetworkScanner(PortService ports, ShippingValveService shippingValves)
    {
        this.ports = ports;
        this.shippingValves = shippingValves;
    }

    public IEnumerable<PipeNetwork> FindNetworks(GameLocation location)
    {
        if (this.cache.TryGetValue(location, out IReadOnlyList<PipeNetwork>? cached))
        {
            this.CacheHits++;
            return cached;
        }

        this.CacheMisses++;
        List<PipeNetwork> networks = new();
        HashSet<Vector2> unseen = location.Objects.Pairs
            .Where(pair => pair.Value.QualifiedItemId == ItemIds.Pipe)
            .Select(pair => pair.Key)
            .ToHashSet();

        while (unseen.Count > 0)
        {
            Vector2 start = unseen.First();
            PipeNetwork network = this.Scan(location, start);
            unseen.ExceptWith(network.Pipes);
            networks.Add(network);
        }

        this.cache[location] = networks;
        return networks;
    }

    public PipeNetwork? FindAtOrNextTo(GameLocation location, Vector2 tile)
    {
        IReadOnlyList<Vector2> candidates = new[] { tile }
            .Concat(Directions.Select(direction => tile + direction))
            .ToArray();

        foreach (PipeNetwork network in this.FindNetworks(location))
            if (candidates.Any(network.Pipes.Contains))
                return network;

        return null;
    }

    public void Invalidate(GameLocation location) => this.cache.Remove(location);

    public void InvalidateAll() => this.cache.Clear();

    private PipeNetwork Scan(GameLocation location, Vector2 start)
    {
        Queue<(Vector2 Tile, int Distance)> pending = new();
        HashSet<Vector2> pipes = new();
        Dictionary<Vector2, int> pipeDistances = new();
        Dictionary<Vector2, NetworkEndpoint> endpoints = new();
        pending.Enqueue((start, 0));

        while (pending.Count > 0)
        {
            (Vector2 tile, int distance) = pending.Dequeue();
            if (!pipes.Add(tile))
                continue;
            pipeDistances[tile] = distance;

            foreach (Vector2 direction in Directions)
            {
                Vector2 next = tile + direction;
                if (IsPipe(location, next))
                {
                    if (!pipes.Contains(next))
                        pending.Enqueue((next, distance + 1));
                    continue;
                }

                if (!location.Objects.TryGetValue(next, out SObject? target))
                    continue;

                PortSettings? settings = this.shippingValves.Get(target) ?? this.ports.Get(target);
                if (settings is null)
                    continue;

                NetworkEndpoint candidate = new(next, target, settings, distance + 1);
                if (!endpoints.TryGetValue(next, out NetworkEndpoint? current) || candidate.Distance < current.Distance)
                    endpoints[next] = candidate;
            }
        }

        if (location is Farm farm)
        {
            foreach ((var shippingBin, PortSettings settings) in this.shippingValves.GetInstalled(farm))
            {
                if (!this.shippingValves.TryFindConnection(shippingBin, pipeDistances, out Vector2 endpointTile, out int distance))
                    continue;

                SObject endpointObject = this.shippingValves.CreateEndpointObject(settings);
                endpoints[endpointTile] = new NetworkEndpoint(endpointTile, endpointObject, settings, distance);
            }
        }

        Vector2 anchor = pipes
            .OrderBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .First();
        string id = $"{location.NameOrUniqueName}:{anchor.X},{anchor.Y}";
        List<NetworkEndpoint> orderedEndpoints = endpoints.Values
            .OrderBy(endpoint => endpoint.Tile.Y)
            .ThenBy(endpoint => endpoint.Tile.X)
            .ToList();
        return new PipeNetwork(id, location, pipes, orderedEndpoints);
    }

    private static bool IsPipe(GameLocation location, Vector2 tile)
    {
        return location.Objects.TryGetValue(tile, out SObject? value)
            && value.QualifiedItemId == ItemIds.Pipe;
    }
}

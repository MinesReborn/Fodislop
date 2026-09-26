#nullable enable

using System.Linq;
using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Inventory;

namespace Kern.Networking.Processors;

/// <summary>
/// Applies native inventory snapshots to <see cref="IInventoryState"/>.
/// A packet whose <c>Changes</c> contains at least one entry per known item
/// type is an authoritative full snapshot (server sends every type, zeros
/// included); a smaller packet is a partial update and is merged instead.
/// The server's metadata packet is keyed by <see cref="SelectItemPacket.Item"/>,
/// not by a client slot, so it is applied directly to that type.
/// </summary>
public sealed class InventoryProcessor(IInventoryState model, IItemCatalog catalog) :
    IPacketProcessor<InventoryPacket>,
    IPacketProcessor<SelectItemPacket>,
    IPacketProcessor<DeselectItemPacket>
{
    private readonly int _knownTypeCount = catalog.AllTypes.Count();

    public void Process(InventoryPacket packet)
    {
        if (packet.Changes.Count >= _knownTypeCount)
        {
            model.ApplyFullSnapshot(packet.Changes);
            return;
        }

        model.MergeChanges(packet.Changes);
    }

    public void Process(SelectItemPacket packet) =>
        model.ApplyItemMetadata(packet.Item, packet.Name, packet.Description);

    public void Process(DeselectItemPacket packet) =>
        model.ClearSelection();
}

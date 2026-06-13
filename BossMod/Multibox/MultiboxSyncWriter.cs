using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace BossMod;

// Main client side: creates a named memory-mapped file and writes sync state each frame
sealed class MultiboxSyncWriter : IMultiboxSyncWriter
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private long _sequence;

    public MultiboxSyncWriter(string groupName)
    {
        var mmfName = $"Global\\BossModReborn_Sync_{groupName}";
        _mmf = MemoryMappedFile.CreateOrOpen(mmfName, MultiboxSyncState.TotalSize);
        _accessor = _mmf.CreateViewAccessor(0, MultiboxSyncState.TotalSize);
        Service.Log($"[MultiboxSync] Writer created for group '{groupName}'");
    }

    public void Write(ref MultiboxSyncState state)
    {
        state.FrameSequence = ++_sequence;
        _accessor.Write(0, ref state);
    }

    public void Dispose()
    {
        _accessor.Dispose();
        _mmf.Dispose();
        Service.Log("[MultiboxSync] Writer disposed");
    }
}

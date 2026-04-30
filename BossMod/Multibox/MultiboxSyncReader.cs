using System.IO.MemoryMappedFiles;

namespace BossMod;

// Alt client side: opens an existing named memory-mapped file and reads sync state each frame
sealed class MultiboxSyncReader : IMultiboxSyncReader
{
    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;
    private readonly string _groupName;
    private long _lastSequence;
    private DateTime _nextRetry;

    public MultiboxSyncReader(string groupName)
    {
        _groupName = groupName;
        TryOpen();
    }

    public bool TryRead(out MultiboxSyncState state)
    {
        state = default;

        if (_accessor == null)
        {
            if (DateTime.UtcNow < _nextRetry)
                return false;
            TryOpen();
            if (_accessor == null)
                return false;
        }

        try
        {
            _accessor.Read(0, out state);
        }
        catch
        {
            Close();
            return false;
        }

        if (state.FrameSequence == _lastSequence)
            return false; // stale data

        _lastSequence = state.FrameSequence;
        return true;
    }

    public void Dispose()
    {
        Close();
        Service.Log("[MultiboxSync] Reader disposed");
    }

    private void TryOpen()
    {
        try
        {
            var mmfName = $"Global\\BossModReborn_Sync_{_groupName}";
            _mmf = MemoryMappedFile.OpenExisting(mmfName);
            _accessor = _mmf.CreateViewAccessor(0, MultiboxSyncState.TotalSize);
            Service.Log($"[MultiboxSync] Reader connected to group '{_groupName}'");
        }
        catch
        {
            Close();
            _nextRetry = DateTime.UtcNow.AddSeconds(2);
        }
    }

    private void Close()
    {
        _accessor?.Dispose();
        _accessor = null;
        _mmf?.Dispose();
        _mmf = null;
    }
}

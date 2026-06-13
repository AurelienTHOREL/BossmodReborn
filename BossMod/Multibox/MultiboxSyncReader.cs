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
    // Accumulate pulse flags across reads: between two consumed frames the writer may have
    // produced (and immediately cleared) intermediate frames carrying a pulse. We watch the
    // FrameSequence delta and OR pulse flags every frame so a one-frame pulse can't be missed.
    private byte _accumulatedDiveEndFlags;
    private byte _accumulatedCommandFlags;

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

        // Always OR the writer's current pulse bits — even if the sequence didn't advance,
        // the writer may have toggled flags within the same sequence on a tight loop.
        _accumulatedDiveEndFlags |= state.DiveEndFlags;
        _accumulatedCommandFlags |= state.CommandFlags;

        if (state.FrameSequence == _lastSequence)
            return false; // stale data

        _lastSequence = state.FrameSequence;

        // Hand the consumer the full accumulated set, then drain.
        state.DiveEndFlags = _accumulatedDiveEndFlags;
        state.CommandFlags = _accumulatedCommandFlags;
        _accumulatedDiveEndFlags = 0;
        _accumulatedCommandFlags = 0;
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

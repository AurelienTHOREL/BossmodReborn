using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace BossMod;

// Alt client side: writes its own report slot to a shared MMF
sealed class MultiboxAltReportWriter : IMultiboxAltReportWriter
{
    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;
    private readonly string _groupName;
    private DateTime _nextRetry;

    public MultiboxAltReportWriter(string groupName)
    {
        _groupName = groupName;
        TryOpen();
    }

    public void Write(ref MultiboxAltReportSlot slot, int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= MultiboxAltReport.MaxSlots)
            return;

        if (_accessor == null)
        {
            if (DateTime.UtcNow < _nextRetry)
                return;
            TryOpen();
            if (_accessor == null)
                return;
        }

        try
        {
            var offset = slotIndex * Marshal.SizeOf<MultiboxAltReportSlot>();
            _accessor.Write(offset, ref slot);
        }
        catch
        {
            Close();
        }
    }

    public void Dispose()
    {
        Close();
        Service.Log("[MultiboxSync] AltReport writer disposed");
    }

    private void TryOpen()
    {
        try
        {
            var mmfName = $"Global\\BossModReborn_AltReport_{_groupName}";
            _mmf = MemoryMappedFile.OpenExisting(mmfName);
            _accessor = _mmf.CreateViewAccessor(0, MultiboxAltReport.TotalSize);
            Service.Log($"[MultiboxSync] AltReport writer connected to group '{_groupName}'");
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

// Main client side: creates the MMF and reads all alt report slots
sealed class MultiboxAltReportReader : IMultiboxAltReportReader
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;

    public MultiboxAltReportReader(string groupName)
    {
        var mmfName = $"Global\\BossModReborn_AltReport_{groupName}";
        _mmf = MemoryMappedFile.CreateOrOpen(mmfName, MultiboxAltReport.TotalSize);
        _accessor = _mmf.CreateViewAccessor(0, MultiboxAltReport.TotalSize);
        Service.Log($"[MultiboxSync] AltReport reader created for group '{groupName}'");
    }

    public bool TryRead(out MultiboxAltReport report)
    {
        report = default;
        try
        {
            _accessor.Read(0, out report);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _accessor.Dispose();
        _mmf.Dispose();
        Service.Log("[MultiboxSync] AltReport reader disposed");
    }
}

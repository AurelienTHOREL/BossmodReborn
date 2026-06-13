namespace BossMod;

interface IMultiboxSyncWriter : IDisposable
{
    void Write(ref MultiboxSyncState state);
}

interface IMultiboxSyncReader : IDisposable
{
    bool TryRead(out MultiboxSyncState state);
}

interface IMultiboxAltReportWriter : IDisposable
{
    void Write(ref MultiboxAltReportSlot slot, int slotIndex);
}

interface IMultiboxAltReportReader : IDisposable
{
    bool TryRead(out MultiboxAltReport report);
}

static class MultiboxMessageType
{
    public const byte SyncState = 0x00;
}

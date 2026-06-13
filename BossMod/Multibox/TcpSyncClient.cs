using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace BossMod;

// Alt client TCP reader: connects to main's TCP server and receives MultiboxSyncState
sealed class TcpSyncClient : IMultiboxSyncReader, IMultiboxAltReportWriter
{
    private readonly string _address;
    private readonly int _port;
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _readThread;
    private readonly object _stateLock = new();
    private MultiboxSyncState _latestState;
    private long _lastSequence;
    private byte _accumulatedDiveEndFlags;
    private byte _accumulatedCommandFlags;
    private readonly object _reportLock = new();
    private MultiboxAltReportSlot _pendingReport;
    private int _pendingSlotIndex = -1;
    private bool _hasReport;

    public TcpSyncClient(string address, int port)
    {
        _address = address;
        _port = port;

        _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "MboxTcpRead" };
        _readThread.Start();

        Service.Log($"[MultiboxSync] TCP client connecting to {address}:{port}");
    }

    public bool TryRead(out MultiboxSyncState state)
    {
        lock (_stateLock)
        {
            state = _latestState;
            // Return accumulated pulse flags (may span multiple TCP frames)
            state.DiveEndFlags = _accumulatedDiveEndFlags;
            state.CommandFlags = _accumulatedCommandFlags;
            _accumulatedDiveEndFlags = 0;
            _accumulatedCommandFlags = 0;
        }

        if (state.FrameSequence == _lastSequence)
            return false; // stale

        _lastSequence = state.FrameSequence;
        return true;
    }

    public void Write(ref MultiboxAltReportSlot slot, int slotIndex)
    {
        lock (_reportLock)
        {
            _pendingReport = slot;
            _pendingSlotIndex = slotIndex;
            _hasReport = true;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _readThread.Join(3000);
        _cts.Dispose();
        Service.Log("[MultiboxSync] TCP client disposed");
    }

    private void ReadLoop()
    {
        var lenBuf = new byte[4];
        var payloadBuf = new byte[MultiboxSyncState.TotalSize];
        var slotSize = Marshal.SizeOf<MultiboxAltReportSlot>();
        var reportPayloadSize = 1 + slotSize; // 1 byte slot index + slot data
        var reportBuf = new byte[4 + reportPayloadSize]; // length prefix + payload

        while (!_cts.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = new TcpClient();
                client.NoDelay = true;
                client.Connect(_address, _port);
                Service.Log($"[MultiboxSync] TCP client connected to {_address}:{_port}");

                var stream = client.GetStream();
                while (!_cts.IsCancellationRequested)
                {
                    // Read 4-byte length prefix
                    ReadExactly(stream, lenBuf, 4);
                    var payloadLen = BitConverter.ToUInt32(lenBuf, 0);

                    // Legacy SyncState: payload is exactly MultiboxSyncState.TotalSize (512 bytes), no type byte
                    if (payloadLen == (uint)MultiboxSyncState.TotalSize)
                    {
                        if (payloadBuf.Length < (int)payloadLen)
                            payloadBuf = new byte[(int)payloadLen];
                        ReadExactly(stream, payloadBuf, (int)payloadLen);
                        var state = MemoryMarshal.Read<MultiboxSyncState>(payloadBuf.AsSpan(0, (int)payloadLen));
                        lock (_stateLock)
                        {
                            _accumulatedDiveEndFlags |= state.DiveEndFlags;
                            _accumulatedCommandFlags |= state.CommandFlags;
                            _latestState = state;
                        }
                    }
                    else if (payloadLen > 0 && payloadLen <= 1024 * 1024)
                    {
                        if (payloadBuf.Length < (int)payloadLen)
                            payloadBuf = new byte[(int)payloadLen];
                        ReadExactly(stream, payloadBuf, (int)payloadLen);

                        var msgType = payloadBuf[0];
                        switch (msgType)
                        {
                            case MultiboxMessageType.SyncState:
                            {
                                var state = MemoryMarshal.Read<MultiboxSyncState>(
                                    payloadBuf.AsSpan(1, MultiboxSyncState.TotalSize));
                                lock (_stateLock)
                                {
                                    _accumulatedDiveEndFlags |= state.DiveEndFlags;
                                    _accumulatedCommandFlags |= state.CommandFlags;
                                    _latestState = state;
                                }
                                break;
                            }
                        }
                    }

                    // Send pending alt report back to server
                    SendPendingReport(stream, reportBuf, reportPayloadSize);
                }
            }
            catch when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Service.Log($"[MultiboxSync] TCP client error: {ex.Message}");
            }
            finally
            {
                try { client?.Dispose(); } catch { }
            }

            // Retry after delay
            if (!_cts.IsCancellationRequested)
            {
                try { _cts.Token.WaitHandle.WaitOne(2000); }
                catch { break; }
            }
        }
    }

    private void SendPendingReport(NetworkStream stream, byte[] reportBuf, int payloadSize)
    {
        bool hasReport;
        MultiboxAltReportSlot report;
        int slotIndex;

        lock (_reportLock)
        {
            hasReport = _hasReport;
            report = _pendingReport;
            slotIndex = _pendingSlotIndex;
            _hasReport = false;
        }

        if (!hasReport || slotIndex < 0)
            return;

        // Write length prefix
        BitConverter.TryWriteBytes(reportBuf.AsSpan(0, 4), (uint)payloadSize);
        // Write slot index byte
        reportBuf[4] = (byte)slotIndex;
        // Write slot data
        MemoryMarshal.Write(reportBuf.AsSpan(5), in report);

        stream.Write(reportBuf, 0, 4 + payloadSize);
    }

    private static void ReadExactly(NetworkStream stream, byte[] buffer, int count)
    {
        var offset = 0;
        while (offset < count)
        {
            var read = stream.Read(buffer, offset, count - offset);
            if (read == 0)
                throw new IOException("Connection closed");
            offset += read;
        }
    }
}

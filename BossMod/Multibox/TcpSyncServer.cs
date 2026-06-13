using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace BossMod;

// Main client TCP server: broadcasts MultiboxSyncState to all connected alt clients.
// Main thread cost is near-zero: Write() just memcpy's the latest state into a buffer and
// wakes a background sender thread, which handles all blocking socket I/O off the UI thread.
// Latest-wins semantics — if the sender is slower than the producer, intermediate frames are
// dropped (safe for state sync since only the newest frame matters).
sealed class TcpSyncServer : IMultiboxSyncWriter, IMultiboxAltReportReader
{
    private readonly TcpListener _listener;
    private readonly List<TcpClient> _clients = [];
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _acceptThread;
    private readonly Thread _senderThread;
    // Latest-wins mailbox: main thread writes here, sender thread drains
    private readonly byte[] _latestBuffer = new byte[4 + MultiboxSyncState.TotalSize]; // length prefix + payload
    private readonly byte[] _sendBuffer = new byte[4 + MultiboxSyncState.TotalSize];
    private readonly object _latestLock = new();
    private readonly ManualResetEventSlim _latestReady = new(false);
    private bool _latestDirty;
    // Rate limit: cap broadcasts to ~60 Hz so sender thread doesn't hog cycles at high frame rates
    private const int MinSendIntervalTicks = (int)(TimeSpan.TicksPerMillisecond * 16); // ~60 Hz
    private long _lastSendTicks;
    private readonly object _reportLock = new();
    private MultiboxAltReport _altReport;

    public event Action<TcpClient>? OnClientConnected;

    public TcpSyncServer(string address, int port)
    {
        var ip = IPAddress.Parse(address);
        _listener = new TcpListener(ip, port);
        _listener.Start();

        // Write length prefix (constant) into both buffers
        BitConverter.TryWriteBytes(_latestBuffer.AsSpan(0, 4), (uint)MultiboxSyncState.TotalSize);
        BitConverter.TryWriteBytes(_sendBuffer.AsSpan(0, 4), (uint)MultiboxSyncState.TotalSize);

        _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "MboxTcpAccept" };
        _acceptThread.Start();

        _senderThread = new Thread(SenderLoop) { IsBackground = true, Name = "MboxTcpSender" };
        _senderThread.Start();

        Service.Log($"[MultiboxSync] TCP server listening on {address}:{port}");
    }

    // Called from the main (UI) thread every frame. Must be near-zero cost: never touches sockets.
    public void Write(ref MultiboxSyncState state)
    {
        lock (_latestLock)
        {
            // OR pulse flags from any previously-buffered-but-unsent frame so the rate-limited
            // sender can't coalesce a one-frame pulse away.
            if (_latestDirty)
            {
                ref var pending = ref MemoryMarshal.AsRef<MultiboxSyncState>(_latestBuffer.AsSpan(4));
                state.DiveEndFlags |= pending.DiveEndFlags;
                state.CommandFlags |= pending.CommandFlags;
            }
            MemoryMarshal.Write(_latestBuffer.AsSpan(4), in state);
            _latestDirty = true;
        }
        _latestReady.Set();
    }

    private void SenderLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                _latestReady.Wait(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Rate limit: if we sent recently, wait out the remainder before consuming
            var elapsedTicks = DateTime.UtcNow.Ticks - _lastSendTicks;
            if (elapsedTicks < MinSendIntervalTicks)
            {
                var remainingMs = (int)((MinSendIntervalTicks - elapsedTicks) / TimeSpan.TicksPerMillisecond);
                if (remainingMs > 0)
                {
                    try { Thread.Sleep(remainingMs); }
                    catch { /* shutdown race */ }
                    if (_cts.IsCancellationRequested)
                        break;
                }
            }

            // Drain latest state atomically into our sender-owned buffer
            lock (_latestLock)
            {
                if (!_latestDirty)
                {
                    _latestReady.Reset();
                    continue;
                }
                _latestBuffer.AsSpan().CopyTo(_sendBuffer);
                _latestDirty = false;
                _latestReady.Reset();
            }

            lock (_clients)
            {
                for (var i = _clients.Count - 1; i >= 0; --i)
                {
                    try
                    {
                        _clients[i].GetStream().Write(_sendBuffer);
                        // Non-blocking read of alt reports from this client
                        ReadAltReport(_clients[i]);
                    }
                    catch
                    {
                        try { _clients[i].Dispose(); } catch { }
                        _clients.RemoveAt(i);
                    }
                }
            }

            _lastSendTicks = DateTime.UtcNow.Ticks;
        }
    }

    public bool TryRead(out MultiboxAltReport report)
    {
        lock (_reportLock)
            report = _altReport;
        return true;
    }

    private void ReadAltReport(TcpClient client)
    {
        const int headerSize = 4;
        var slotSize = Marshal.SizeOf<MultiboxAltReportSlot>();
        var expectedPayload = 1 + slotSize; // 1 byte slot index + slot data

        if (client.Available < headerSize + expectedPayload)
            return;

        var stream = client.GetStream();
        Span<byte> lenBuf = stackalloc byte[headerSize];
        stream.ReadExactly(lenBuf);
        var payloadLen = BitConverter.ToUInt32(lenBuf);

        if (payloadLen != (uint)expectedPayload)
        {
            // Unexpected size, drain and skip
            if (payloadLen > 0 && payloadLen < 4096)
            {
                var drain = new byte[(int)payloadLen];
                stream.ReadExactly(drain);
            }
            return;
        }

        Span<byte> payload = stackalloc byte[expectedPayload];
        stream.ReadExactly(payload);

        var slotIndex = payload[0];
        if (slotIndex < MultiboxAltReport.MaxSlots)
        {
            var slot = MemoryMarshal.Read<MultiboxAltReportSlot>(payload[1..]);
            lock (_reportLock)
                _altReport.Slot(slotIndex) = slot;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        // Wake the sender thread so it observes cancellation and exits
        _latestReady.Set();

        lock (_clients)
        {
            foreach (var c in _clients)
            {
                try { c.Dispose(); } catch { }
            }
            _clients.Clear();
        }

        _acceptThread.Join(2000);
        _senderThread.Join(2000);
        _latestReady.Dispose();
        _cts.Dispose();
        Service.Log("[MultiboxSync] TCP server disposed");
    }

    private void AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var client = _listener.AcceptTcpClient();
                client.NoDelay = true;
                client.SendTimeout = 10; // ms — drop frame rather than stall UI thread
                lock (_clients)
                    _clients.Add(client);
                OnClientConnected?.Invoke(client);
                Service.Log($"[MultiboxSync] TCP client connected: {client.Client.RemoteEndPoint}");
            }
            catch when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Service.Log($"[MultiboxSync] TCP accept error: {ex.Message}");
            }
        }
    }
}

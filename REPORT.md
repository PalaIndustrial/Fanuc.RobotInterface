## COMPREHENSIVE LIFECYCLE INVESTIGATION REPORT: RobotIF v3.0.4

### SUMMARY
Investigation of **v3.0.4 tagged code** vs working tree reveals a TCP-based synchronous robot interface with task-based async wrapper providing native session monitoring and limited concurrency control. No explicit CancellationToken support exists in core logic; cancellation propagates indirectly through watcher thread lifecycle.

---

## LIFECYCLE ANALYSIS

### **CONNECT PHASE (Synchronous)**

**RobotIF.Connect() - v3.0.4 Tag**
File: `Fanuc.RobotInterface\RobotIF.cs`, Lines 25-88

```csharp
public void Connect(string host, ushort port, int timeout)
{
    if (IsConnected)
        throw new IOException("Already connected.");
    try
    {
        Disconnect();  // Cleans up any prior state
        var client = new TcpClient
        {
            ReceiveTimeout = timeout,    // 10000ms default
            SendTimeout = timeout
        };
        client.Client.NoDelay = true;
        client.Connect(host, port);      // **BLOCKING - Timeout via TcpClient.Connect**

        TcpClient = client;
        Host = host;
        SequenceNumber = 0;

        // Handshake 1: INIT packet
        SendReceiveInternal(new ShortRequestPacket() { ... });
        // Handshake 2: UNKNOWN_8 packet
        SendReceiveInternal(new ShortRequestPacket() { ... });
    }
    catch (Exception ex)
    {
        Disconnect();  // **Cleanup on failure**
        throw new IOException($"Connecting to {host}:{port} failed.", ex);
    }
    PropertyChanged?.Invoke(...);
}
```

**RISKS - Connect Phase:**
- **Line 32**: Unconditional `Disconnect()` call before connect can hide prior connection state
- **Line 39**: `TcpClient.Connect()` blocks indefinitely if `timeout` parameter is malformed (e.g., negative)
- **Line 39**: Timeout only applies to socket-level TCP handshake, NOT to INIT/UNKNOWN_8 packet exchanges (handled separately by `ReceiveTimeout`/`SendTimeout` on socket)
- **Lines 47-59**: No validation that `INIT` or `UNKNOWN_8` responses are actually received; duplicate/corrupted packets silently accepted
- **Line 84**: If exception in handshake (e.g., `INIT` times out), `Disconnect()` called but `TcpClient` may still have pending receive buffers (see `ReceiveInternal()` below)

**Testability (v3.0.4):**
- No `ConnectAsync` in base RobotIF; ExRobotIF wraps via `Task.Run()`
- TcpClient hardcoded; no fake TCP support
- Direct socket Send/Receive calls

---

### **DISCONNECT PHASE**

**RobotIF.Disconnect() - v3.0.4 Tag**
File: `Fanuc.RobotInterface\RobotIF.cs`, Lines 93-103

```csharp
public void Disconnect()
{
    if (IsConnected)
    {
        TcpClient?.Close();      // Closes underlying socket
        TcpClient?.Dispose();
        TcpClient = null;        // Clears reference

        PropertyChanged?.Invoke(this,
            new PropertyChangedEventArgs(nameof(IsConnected)));
    }
}
```

**RISKS - Disconnect Phase:**
- **Line 95**: Check `IsConnected => TcpClient?.Connected ?? false` races with simultaneous `Close()` call
- **Line 97**: No guarantee buffer flushes before close
- **No watcher thread termination in base RobotIF** - managed in ExRobotIF only

**ExRobotIF.Disconnect() - Working Tree**
File: `Fanuc.RobotInterface.Extensions\ExRobotIF.IRobotInterface.cs`, Lines 13-24

```csharp
public void Disconnect()
{
    if (IsConnected)
    {
        _TaskFactory?.StartNew(() => _Robot.Disconnect())?.Wait();  // **Line 17**
        _CancellationTokenSource?.Cancel();                         // **Line 18**
        _TaskFactory = null;                                        // **Line 20**
        OnPropertyChanged(nameof(IsConnected));
    }
}
```

**CRITICAL LIFECYCLE ISSUE:**
- **Line 17**: Wraps `_Robot.Disconnect()` in a task scheduled on `_TaskFactory` (which uses `_CancellationTokenSource`), then waits
- **Line 18**: Calls `Cancel()` on the CancellationTokenSource **after** waiting for the task
- **Ordering Problem**: If `_Robot.Disconnect()` is already running, `Cancel()` may not interrupt it (task already in-flight)
- **Watcher thread** (spawned during ConnectAsync) is **NOT explicitly joined**; it stops only when `_Robot.IsConnected` returns false

---

### **MONITORING/WATCHER THREAD**

**ExRobotIF.ConnectAsync() - v3.0.4 Tag**
File: `Fanuc.RobotInterface.Extensions\ExRobotIF.cs`, Lines 63-127

```csharp
public Task ConnectAsync(string host, ushort port, int timeout)
{
    if (IsConnected)
        return Task.CompletedTask;

    var scheduler = new LimitedConcurrencyLevelTaskScheduler(1);  // **Single-threaded serialization**
    _CancellationTokenSource = new CancellationTokenSource();
    _TaskFactory = new TaskFactory(_CancellationTokenSource.Token, ..., scheduler);

    return _TaskFactory.StartNew(() =>
    {
        _Robot.Connect(host, port, timeout);
        if (_Robot.IsConnected)
        {
            _Robot.WriteCommand("CLRASG");
            // Set up signal assignments...

            _WatcherThread = new Thread(() =>          // **Line 84**
            {
                while (_Robot.IsConnected)  // **Line 91: Polling loop**
                {
                    try
                    {
                        foreach (var v in AllWritableSignals)
                            v.PushValue();              // Send pending writes to robot
                        foreach (var v in AllReadableSignals)
                            v.PullValue();              // Fetch reads from robot
                    }
                    catch
                    {
                        Disconnect();                   // **Line 120: Exception -> Disconnect**
                    }
                }
            });
            _WatcherThread.Start();                     // **Line 124**
        }
        OnPropertyChanged(nameof(IsConnected));
    });
}
```

**NATIVE SESSION OWNERSHIP:**
- **Lines 68-70**: `LimitedConcurrencyLevelTaskScheduler(1)` ensures only 1 operation runs at a time
- **Line 74**: Core RobotIF.Connect() executes serialized on this scheduler
- **Line 84-124**: Watcher thread runs independently; **NOT constrained by scheduler**
- **Session ownership**: RobotIF holds `TcpClient` reference; ExRobotIF monitors signal state
- **No session "ownership" protocol** - robot resource is single-threaded by convention, not by code

**RISKS - Monitoring:**
- **Line 91**: Watcher thread **polling loop** with no sleep; burns CPU
- **Line 118**: Bare `catch` swallows exceptions; no logging
- **Line 120**: Calling `Disconnect()` from watcher thread while ExRobotIF.Disconnect() may be running creates race
- **No graceful shutdown**: Watcher thread relies on `TcpClient.Connected` to exit; no explicit join or timeout
- **Unobserved Task exceptions**: If ExRobotIF.Disconnect() is never called and connection drops, watcher thread silently exits but PropertyChanged not raised

---

### **CANCELLATION & TIMEOUT HANDLING**

**Timeout at Connection Level:**
- `RobotIF.Connect(timeout)` passes timeout to TcpClient: `ReceiveTimeout = timeout; SendTimeout = timeout` (**v3.0.4 Line 36-37**)
- Affects socket I/O only; not the `Connect()` call itself
- `client.Connect(host, port)` blocks indefinitely if no default timeout; no parameter passed

**CancellationToken Support:**
- **ExRobotIF constructor** creates `_CancellationTokenSource`, passes token to `TaskFactory` (v3.0.4 Line 23, 70)
- **Token is NOT propagated** to actual robot I/O methods (Connect, Disconnect, Read, Write)
- Watcher thread does NOT check `CancellationTokenSource.Token.IsCancellationRequested`
- `ConnectAsync()` can only be cancelled by:
  1. Calling `Disconnect()` (stops watcher via `IsConnected` check)
  2. Dropping the returned Task (doesn't actually cancel in-flight operations)

**CRITICAL GAP:**
No way to gracefully interrupt `_Robot.Connect()` if robot is unresponsive; `SendReceiveInternal()` calls are blocking and non-cancellable.

---

### **EXCEPTION HANDLING & CLEANUP**

**Connect Failure Cleanup:**
File: `RobotIF.cs`, Lines 82-86
```csharp
catch (Exception ex)
{
    Disconnect();  // Cleanup
    throw new IOException($"Connecting to {host}:{port} failed.", ex);
}
```
Explicit cleanup on exception

**Receive Failure (ReceiveInternal):**
File: `RobotIF.cs`, Lines 430-441 (from last 50 lines output)
```csharp
private byte[] ReceiveInternal()
{
    var hn = TcpClient.Client.Receive(HeaderBuffer, ...);
    if (hn != PacketBase.HEADER_LENGTH)
        throw new IOException($"Error receiving header, want {HEADER_LENGTH} bytes, got {hn} only.");
    // Payload receive loop...
    return [...HeaderBuffer, ...PayloadBuffer.Take(xlen)];
}
```
Exception in Receive leaves TcpClient in undefined state; next operation may hang

**Watcher Thread Exception:**
Line 118: `catch { Disconnect(); }` silently swallows all exceptions

---

### **IsConnected CHECK**

**Property Definition:**
File: `RobotIF.cs`, Line 20
```csharp
public bool IsConnected => TcpClient?.Connected ?? false;
```

**RISKS:**
- **Race condition**: `TcpClient?.Connected` is evaluated at call time; socket may close between check and use
- **Does NOT reflect pending operations**: Returns true even if watcher thread is mid-exception
- **Dependent on watcher thread's polling**: Remote disconnection not detected until next PullValue()/PushValue() cycle

**Working Tree Addition:**
File: `IRobotIF.cs`, Line 6 (working tree)
```csharp
string Host { get; }
```
Allows consumers to detect which host is connected; not present in v3.0.4 tag.

---

## FEASIBLE CANCELLATION/SHUTDOWN STRATEGY

### **Current Implementation (v3.0.4):**

1. **Synchronous Connect:**
   - No cancellation; blocks until success/failure
   - Timeout only affects socket ops, not SRTP handshake timeouts

2. **Watcher Thread Lifecycle:**
   - Starts on successful connect
   - Exits when `TcpClient.Connected` becomes false
   - No explicit termination signal

3. **Cancellation via Disconnect:**
   ```csharp
   // ExRobotIF.Disconnect()
   _TaskFactory?.StartNew(() => _Robot.Disconnect())?.Wait();
   _CancellationTokenSource?.Cancel();  // Too late - task already running
   _TaskFactory = null;
   ```

### **RECOMMENDED STRATEGY** (for future versions):

**1. Add Explicit Watcher Thread Termination:**
```csharp
private volatile bool _ShutdownRequested = false;

// In ConnectAsync, change watcher loop:
while (_Robot.IsConnected && !_ShutdownRequested)
{
    // ... push/pull logic
    Thread.Sleep(10);  // Prevent CPU spin
}

// In Disconnect:
_ShutdownRequested = true;
_WatcherThread?.Join(5000);  // Wait up to 5 seconds
```

**2. Add ConnectAsync Timeout via Task.Wait(TimeSpan):**
```csharp
public async Task ConnectAsync(string host, ushort port, int timeout)
{
    var connectTask = _TaskFactory.StartNew(...);
    if (!connectTask.Wait(TimeSpan.FromMilliseconds(timeout)))
    {
        _CancellationTokenSource.Cancel();
        throw new TimeoutException($"Connect to {host}:{port} timed out after {timeout}ms");
    }
}
```

**3. Propagate CancellationToken to RobotIF:**
```csharp
// Change RobotIF.Connect signature:
public void Connect(string host, ushort port, int timeout, CancellationToken ct)
{
    // Check ct.IsCancellationRequested before/during Send/Receive
}

// ExRobotIF.ConnectAsync calls with _CancellationTokenSource.Token
```

**4. Add Watcher Thread Exception Logging:**
```csharp
catch (Exception ex)
{
    Debug.WriteLine($"Watcher thread error: {ex}");
    Disconnect();
}
```

---

## TESTABILITY & FAKE TCP

### **Current Barriers (v3.0.4):**
- **TcpClient hardcoded** in RobotIF; no abstraction
- **SendReceiveInternal() directly on socket** (Lines 437-439):
  ```csharp
  private void SendInternal(IRequestPacket packet) => TcpClient?.Client?.Send(packet.GetBytes());
  private byte[] ReceiveInternal() { TcpClient.Client.Receive(...) }
  ```
- **No ISocket dependency injection**

### **To Enable Fake TCP:**
1. Extract `ISocket` interface:
   ```csharp
   public interface ISocket
   {
       void Send(byte[] data);
       int Receive(byte[] buffer, int length, SocketFlags flags);
       void Close();
   }
   ```
2. Inject into RobotIF constructor
3. Create `FakeSocket` mock that replays recorded SRTP conversations
4. **Watcher Thread Issue**: Still runs real Thread; need to mock time or add `IThreadFactory`

---

## TAGGED vs WORKING TREE DIFFERENCES

### **v3.0.4 Tag -> Current Working Tree Changes:**

| Aspect | v3.0.4 Tag | Working Tree |
|--------|-----------|--------------|
| `IRobotIF.Host` | Not present | `string Host { get; }` |
| `IRobotIF.ExecuteKCL/Karel` | Present in interface | Removed (moved to extension?) |
| `RobotIF.Host` property | Only private field | Exposed in tag Line 22 |
| ExRobotIF structure | Same | Same (verified Line 1-150) |
| LimitedConcurrencyLevelTaskScheduler | Unchanged | Unchanged |

**Impact**: Working tree is backward-incompatible with v3.0.4 (KCL/Karel methods removed from core interface).

---

## CRITICAL FINDINGS SUMMARY

| Risk | Severity | Location | Mitigation |
|------|----------|----------|-----------|
| **Watcher thread CPU spin** | Medium | ExRobotIF.cs:91 | Add `Thread.Sleep(10)` |
| **No watcher thread join on Disconnect** | High | ExRobotIF.IRobotInterface.cs:17 | Add explicit `_WatcherThread.Join(timeout)` |
| **CancellationToken unused** | High | ExRobotIF.cs:70 | Propagate token to RobotIF.Connect() |
| **Bare catch in watcher** | Medium | ExRobotIF.cs:118 | Log exceptions before Disconnect() |
| **Send/Receive buffer race on close** | High | RobotIF.cs:95-98 | Add lock around TcpClient state transitions |
| **Timeout only on socket, not SRTP handshake** | Medium | RobotIF.cs:36-39 | Add explicit timeout wrapper around SendReceiveInternal() |
| **No fake TCP testability** | Low | RobotIF.cs:437-439 | Extract ISocket interface (non-breaking change possible via adapter) |

---

## ACTIONABLE RECOMMENDATIONS

1. **Immediate (Safety-Critical):**
   - Add watcher thread explicit join in Disconnect() with 5-second timeout
   - Log watcher thread exceptions (don't silently catch)
   - Add sleep to prevent CPU spin

2. **Short-term (Robustness):**
   - Implement timeout wrapper for SRTP handshake (ConnectAsync timeout enforcement)
   - Add `CancellationToken` parameter to RobotIF.Connect() and propagate through Send/Receive

3. **Long-term (Testability):**
   - Extract ISocket abstraction for fake TCP testing
   - Consider redesigning watcher thread as cancellable Task instead of Thread

**No changes to machine interlocks suggested** - these are orthogonal to lifecycle management.
# Project State and Lifecycle Investigation Report

Reviewed: 2026-11-13

## Scope and evidence

This report describes the current working tree, not the `v3.0.4` tag.
Since the original review at commit `39b8931`, the transport and extended
lifecycle have been revised and exercised with local TCP test servers. These
tests are not a substitute for verification against an actual controller.
Severity describes potential application reliability and data-integrity impact,
not a machine-safety certification.

The review covers project configuration, the core TCP transport, the extended
interface's lifecycle and holders, SRTP framing, HTTP helpers, and the desktop
application's connection path. It is not an exhaustive audit of every converter,
controller command, or UI binding.

## Current project structure

| Project | Responsibility | Targets |
|---|---|---|
| `Fanuc.SRTP` | SRTP packet models and serialization | `net48`, `netstandard2.0`, `net10.0` |
| `Fanuc.RobotInterface` | `IRobotIF`, synchronous `RobotIF`, I/O and HTTP extensions, robot data models | `net48`, `netstandard2.0`, `net10.0` |
| `Fanuc.RobotInterface.Extensions` | `IExRobotIF`, `ExRobotIF`, signal/data holders, dictionaries and scheduler | `net48`, `netstandard2.0`, `net10.0` |
| `FANUCRobotTest` | Interactive WPF application using `RobotIF` | `net10.0-windows10.0.18362.0` |

- `Directory.Build.props` sets shared version `3.0.4` and library revision `0`,
  producing library version `3.0.4.0`. README and migration documentation use
  the release label `3.0.4`; that label alone does not identify this source state.
- The solution is `Fanuc.RobotInterface.slnx`.
- Extended interface types retain the `Fanuc.RobotInterface` namespace despite
  moving to the Extensions assembly. SRTP types use `Fanuc.SRTP`.
- `IRobotIF.Host` is required. `RobotIF` implements it explicitly; `ExRobotIF`
  exposes a public property.
- `ExecuteKCL`, `ExecuteKarel`, and `ResetAlarm` are core extension methods,
  not `IRobotIF` instance members. `ClearAlarm` remains an interface member.
- Dictionaries report their actual count and forward collection/property
  notifications. Their concrete implementation and the scheduler are internal.
- `CHANGELOG.md` and `MIGRATION.md` document the package split, compatibility
  changes, framework changes, and I/O fixes.

### Build and execution prerequisites

The source requires an SDK supporting .NET 10 and `.slnx`. Signing is enabled
using `C:\Users\Palatis\.dotnet\Palatis.snk` in shared properties and again in
`Fanuc.SRTP.csproj`; a different machine needs an appropriate key or explicit
build-property overrides. Changing signing changes assembly identity.

The desktop application targets Windows and uses WPF-UI, not ModernWpf.

## Connection and shutdown behavior

### Core `RobotIF`

Source: `Fanuc.RobotInterface\RobotIF.cs`, especially `Connect`, `Disconnect`,
`SendInternal`, `ReceiveInternal`, and `SendReceiveInternal`.

1. `Connect` serializes connection attempts with complete protocol operations.
   It publishes the new client for interruption, configures it, then calls
   synchronous `TcpClient.Connect(host, port)`.
2. It resets the sequence number and exchanges `INIT` and `UNKNOWN_8` packets.
3. It rejects `SHORT_FAILED` responses but does not comprehensively validate
   response identity or expected payload shape.
4. On failure it disposes the client and wraps the error in an `IOException`.
   Successful completion raises `IsConnected` notification.

The default port is 60008 and the default I/O timeout is 10,000 ms.
The timeout configures synchronous socket send/receive operations, including
those used for the SRTP handshake. It does **not** configure the TCP connect
deadline or an overall handshake deadline. Zero permits unbounded socket I/O;
invalid timeout values may fail during socket configuration.

`IsConnected` requires a completed handshake and reads `TcpClient.Connected`;
it is not a heartbeat or a guarantee that the next operation will succeed.
`Disconnect` detaches and disposes the client without waiting for the operation
lock, allowing blocked socket I/O to be interrupted. Queued operations cannot
cross into a later connection. An incomplete send or receive invalidates the
connection, preventing a delayed response from being mistaken for a later
request's acknowledgment. The stored host remains after disconnect.
Neither the core interface nor implementation exposes caller cancellation or
an `IDisposable` lifecycle contract.

### Extended `ExRobotIF`

Sources: `Fanuc.RobotInterface.Extensions\ExRobotIF.cs`,
`ExRobotIF.IRobotInterface.cs`, and `ExRobotIF.Data.cs`.

- The constructor accepts a non-null `RobotIF`, not an arbitrary `IRobotIF`.
  The caller can still retain and use the same instance directly, so exclusive
  session ownership is not enforced.
- `ConnectAsync` coordinates a single session. It sends `CLRASG`, resets the
  allocator, rebuilds registered assignments and starts a background watcher.
  Initialization failures close the session; a later connection can retry.
- The watcher pulls instantiated holders and waits 10 ms after each pass.
  New complex holders are assigned before their first read or write. Existing
  holders and subscriptions survive reconnect.
- Writable holders expose `WriteAsync`; `Value` is the latest polled value.
  Writes are not coalesced or automatically replayed. Position and raw array
  values are snapshotted before scheduling.
- `Disconnect` cancels queued work, interrupts the socket, drains the scheduler
  and joins the watcher before a later session starts. A watcher callback can
  initiate disconnect without joining itself. Synchronous reconnect from a
  stopping watcher is rejected; asynchronous reconnect waits for shutdown.
- `LastError` and property notifications expose initialization, polling and
  cleanup errors. The async methods schedule blocking work; they are not
  nonblocking socket APIs.

Socket closure interrupts active transport I/O, but a blocked user callback,
HTTP operation or TCP connect without a deadline can delay shutdown. Callers
cannot supply their own cancellation token. Dropping a returned task does not
cancel it.

### HTTP helpers and desktop application

Source: `Fanuc.RobotInterface\IRobotIFExtension.cs`.

KCL and Karel helpers use a shared `HttpClient`, construct HTTP requests from
`IRobotIF.Host`, and synchronously wait for responses. Their timeout is separate
from the TCP timeout. They do not enforce an SRTP-connected-state guard.
`ExRobotIF.AsyncExecuteKCL` and `AsyncExecuteKarel` schedule those blocking
helpers; calling the synchronous extension methods directly bypasses that
scheduler. `RobotIF.Host` remains populated after disconnect, so an HTTP call
can still target the previous controller.

`FANUCRobotTest\UI\MainWindow.xaml.cs` uses `new ExRobotIF(new RobotIF())`.
Its button handler connects/disconnects synchronously on the UI
thread. Closing the window calls `Disconnect` before application shutdown.
Long-running connection or queued I/O can therefore delay UI response or exit.

## Confirmed improvements already present

- Digital writes preserve neighboring bits using one or two boundary reads
  before a byte-aligned write. They no longer unconditionally zero those bits.
- `RobotIF` rejects out-of-range 1-based addresses, unrepresentable read/write
  ranges, odd SNPX byte counts and oversized command/SNPX payloads (F9/F10).
- Offset-based signal methods and holders also validate the caller's 1-based
  index before applying their offset.
- String register and string system-variable holders pad odd byte counts before
  SNPX writes.
- `RobotDataDictionary.Count` returns `_Items.Count`; dictionary notifications
  are forwarded to consumers.
- I/O extension methods add operation context to errors where implemented.
- `RobotIF` loops until a request, response header and declared payload are
  fully transferred, and reports EOF instead of spinning on a truncated frame
  (F1/F2). Loopback tests cover fragmentation and early disconnect; a large
  request reaches a slow peer intact. The send loop was also checked with a
  temporary 1,024-byte send cap; the committed tests do not deterministically
  force a partial `Socket.Send` return.

Digital read-modify-write is serialized with other operations on the same
`RobotIF` instance, but is not atomic against controller-side changes or another
client. `CLRASG` is still sent on each extended connection; its effect on other
clients has not been verified.

## Current findings

These are source-confirmed implementation gaps. Their consequences describe
the triggering conditions, not observed production incidents.

| ID | Priority | Finding and consequence | Source |
|---|---|---|---|
| F5 | High | Socket I/O can be interrupted and watcher overlap is prevented, but shutdown has no universal bound while TCP connect, an HTTP operation or a consumer callback remains blocked. | `ExRobotIF.cs`: `FinishStop`; `RobotIF.cs`: `Connect` |
| F11 | Medium | Reads validate ACK format and minimum payload length, but fixed six-byte short payloads can contain indistinguishable padding. Handshake and write ACK types are not validated, and response identity and other fields are not correlated with requests; their semantics require investigation. | `RobotIF.cs`: handshake and read/write methods |
| F13 | Medium | The background watcher now has a fixed 10 ms inter-pass wait, but idle CPU and active refresh cadence have not been separately measured or made configurable. | `ExRobotIF.cs`: `Watch` |

F3, F4, F6, F7 and F8 were addressed by the lifecycle changes. F9 and F10 were
addressed by input validation. F12's dirty state was removed in favor of explicit
per-write tasks; a loopback test covers consecutive writes and their responses.

`RobotIF.Refresh` and `Refresh2` additionally remain public methods that throw
`NotImplementedException`; neither is part of `IRobotIF`.

## Corrections to the previous report

- This is a working-tree assessment, not an asserted comparison against tagged
  source. API migration details belong to `CHANGELOG.md` and `MIGRATION.md`.
- The connect timeout is not a TCP connect timeout; socket I/O timeouts do apply
  to the SRTP handshake's send/receive operations.
- Handshake responses are received and checked for `SHORT_FAILED`. The missing
  piece is comprehensive validation, not the absence of all response checks.
- Built-in holders route normal reads and writes through the wrapper's task
  factory. Closing the socket interrupts active socket I/O; merely timing out a
  task would not terminate an executing synchronous operation.
- The wrapper now accepts only `RobotIF`; both layers can be tested using a local
  TCP server without changing their APIs. A transport abstraction could make
  deterministic fault injection easier but is not required for all protocol tests.

## Recommended next work

1. **Transport correctness:** add deterministic partial-send coverage and
   investigate response identity before rejecting unverified field patterns.
2. **Cancellation and shutdown:** implement actual connect and handshake
   deadlines and caller cancellation, and verify shutdown limits. A deadline must
   interrupt the underlying operation, not merely stop waiting for its task.
3. **Polling:** verify idle and active polling cadence and consider a
   configurable interval.
4. **Regression coverage:** extend local TCP tests for response validation and
   protocol correlation, and verify behavior against a controller when available.

These recommendations concern application lifecycle and protocol correctness;
they do not replace controller interlocks or other machine-safety mechanisms.

## Validation status

- The Visual Studio workspace build succeeded. The last full library test run
  passed 229 tests across .NET Framework 4.8, .NET 8 (using .NET Standard 2.0
  assets) and .NET 10. The last desktop test run passed 15 Windows/.NET 10 tests.
  A combined Test Explorer run reported a test-storage error after reporting
  154 passes, so the projects were verified separately.
- Loopback tests cover transport serialization, initialization failure, blocked
  I/O shutdown, reconnect, assignment allocation and write failure/no replay.
  No live robot connection or deterministic partial-send fault injection was
  performed.
- Read ACK validation, length checks and extended-payload tracing were exercised
  by targeted transport tests on .NET Framework 4.8, .NET 8 and .NET 10.
- Loopback tests cover input validation and maximum representable read/write
  boundaries without sending invalid requests.

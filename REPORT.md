# Project State and Lifecycle Investigation Report

Reviewed: 2026-11-13

## Scope and evidence

This report describes the current working tree, not the `v3.0.4` tag.
The reviewed source baseline is commit `39b8931`, with local solution-item
changes present. Findings below are based on source inspection; failure
scenarios have not been reproduced against a robot or a simulated controller.
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

1. `Connect` rejects an already-connected instance and calls `Disconnect`.
2. It creates a `TcpClient`, sets receive/send timeouts and `NoDelay`, then
   calls synchronous `TcpClient.Connect(host, port)`.
3. After TCP connection succeeds, it stores the client and host, resets the
   sequence number, and exchanges `INIT` and `UNKNOWN_8` packets.
4. It rejects `SHORT_FAILED` responses but does not comprehensively validate
   response identity or expected payload shape.
5. Exceptions inside the connection block trigger `Disconnect` and are wrapped
   in an `IOException`. Successful completion raises `IsConnected` notification.

The default port is 60008 and the default I/O timeout is 10,000 ms.
The timeout configures synchronous socket send/receive operations, including
those used for the SRTP handshake. It does **not** configure the TCP connect
deadline or an overall handshake deadline. Zero permits unbounded socket I/O;
invalid timeout values may fail during socket configuration.

`IsConnected` reads `TcpClient.Connected`; it is not a heartbeat or a guarantee
that the next operation will succeed. `Disconnect` only closes, disposes, and
clears the client when that property currently reports true. It does not clear
the stored host. Neither the core interface nor implementation exposes
cancellation or an `IDisposable` lifecycle contract.

### Extended `ExRobotIF`

Sources: `Fanuc.RobotInterface.Extensions\ExRobotIF.cs`,
`ExRobotIF.IRobotInterface.cs`, and `ExRobotIF.Data.cs`.

- The constructor accepts a non-null `RobotIF`, not an arbitrary `IRobotIF`.
  The caller can still retain and use the same instance directly, so exclusive
  session ownership is not enforced.
- `ConnectAsync` returns an already-completed task when connected. Otherwise it
  creates a new single-concurrency scheduler, cancellation source and task
  factory, and schedules the synchronous backend connection.
- Once connected, it sends `CLRASG`, recreates assignments for existing complex
  holders, starts a watcher thread, and raises `IsConnected` notification.
- The watcher pushes dirty values and pulls all instantiated readable holders.
  Holder I/O normally calls back through the wrapper's scheduled methods.
  Thus, although the watcher thread itself is outside the scheduler, its normal
  backend I/O is serialized with other tasks on the same factory.
- There is no polling delay or cancellation check in the watcher loop. With no
  holders it can busy-spin; with holders, network and scheduling work normally
  limit the iteration rate.
- The watcher catches update exceptions and calls `Disconnect`, without exposing
  the original error. It is a foreground thread and is never explicitly joined.
- `Disconnect` checks the backend connection state, schedules backend
  disconnection, waits for it, then cancels the factory token and clears the
  factory. It does not dispose the cancellation source or join/reset the watcher.
- Synchronous methods block on `.Wait()` or `.Result`. The async methods schedule
  blocking work; they are not nonblocking socket APIs.

The token can prevent pending scheduled work from starting. It does not abort
an executing socket operation, and callers cannot supply their own token.
Disconnect is queued behind existing work rather than immediately interrupting
it. Dropping a returned task does not cancel anything.

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
- Digital and group writes reject address/payload ranges beyond their supported
  protocol limits.
- String register and string system-variable holders pad odd byte counts before
  SNPX writes.
- `RobotDataDictionary.Count` returns `_Items.Count`; dictionary notifications
  are forwarded to consumers.
- I/O extension methods add operation context to errors where implemented.

The digital read-modify-write is still not atomic with respect to another
caller or controller-side changes. These improvements do not establish
thread safety for direct concurrent use of `RobotIF`.

## Current findings

These are source-confirmed implementation gaps. Their consequences describe
the triggering conditions, not observed production incidents.

| ID | Priority | Finding and consequence | Source |
|---|---|---|---|
| F1 | High | A single receive must return the entire 56-byte header. TCP may legally split it, so a valid fragmented response is rejected. A single send also ignores the returned byte count, allowing a partially sent request to be treated as complete. | `RobotIF.cs`: `ReceiveInternal`, `SendInternal` |
| F2 | High | The payload receive loop does not handle a zero-byte receive. An orderly peer close partway through an extended response leaves the offset unchanged and can spin indefinitely instead of reporting EOF. | `RobotIF.cs`: `ReceiveInternal` |
| F3 | High | Cleanup depends on `IsConnected`. A client already reporting disconnected is neither disposed nor cleared. Also, the newly created client is assigned to the field only after TCP connect succeeds, so failures before that assignment leave the local client outside the catch block's cleanup path. | `RobotIF.cs`: `Connect`, `Disconnect` |
| F4 | High | No core lock protects request/response exchanges, sequence numbers, buffers or connection transitions. Direct concurrent calls, a shared backend, or overlapping connection attempts can interleave exchanges or race disposal. Wrapper serialization covers only work using the same task factory. | `RobotIF.cs`; `ExRobotIF.cs`: `ConnectAsync` |
| F5 | High | Extended shutdown has no watcher termination/join protocol and cannot interrupt queued or active blocking I/O. If the backend already reports disconnected, the entire cleanup body is skipped. A rapid reconnect can let an old watcher observe the new connection and continue alongside the new watcher. | `ExRobotIF.cs`: watcher loop; `ExRobotIF.IRobotInterface.cs`: `Disconnect` |
| F6 | High | Extended initialization has no failure cleanup after backend connection. If `CLRASG` or a holder assignment fails, the connect task faults while the backend can remain connected without a watcher. A subsequent `ConnectAsync` then returns completed solely because `IsConnected` is true. | `ExRobotIF.cs`: `ConnectAsync` |
| F7 | Medium | Watcher exceptions lose their original diagnostic information. If the backend has already become disconnected, wrapper `Disconnect` can skip both cancellation and notification. An exception thrown by cleanup itself also escapes the watcher catch. | `ExRobotIF.cs`; `ExRobotIF.IRobotInterface.cs` |
| F8 | Medium | `AssignmentOffset` starts at 1 but is not reset when reconnect sends `CLRASG`. Repeated reconnects allocate progressively higher offsets despite clearing controller assignments; the allocator has no range check. | `ExRobotIF.Data.cs`: `AssignmentOffset`, `AsyncWriteAssignment`; `ExRobotIF.cs`: `ConnectAsync` |
| F9 | Medium | Read-side range checks are weaker than write-side checks. For example, `ReadGI(65537, 1)` decrements the index and casts it to `ushort`, targeting offset zero instead of rejecting it. `WriteSNPX(0, ...)` also passes validation and casts the decremented index to 65535. | `RobotIF.cs`: `ReadGIO`, `ReadDIO`, `ReadSNPX`, `WriteSNPX` |
| F10 | Medium | SNPX converts byte counts to word counts by integer division without rejecting odd counts. Oversized command/SNPX payload lengths and indexes are narrowed to `ushort` without complete validation. Holder-level string padding does not protect direct callers. | `RobotIF.cs`: `WriteCommand`, `WriteSNPX`, `ReadSNPX`; `Fanuc.SRTP\PacketBase.cs`: `ExtraPayload` |
| F11 | Medium | Response checks mostly reject only `SHORT_FAILED`; they do not correlate sequence/service/segment and expected lengths. Short group reads silently leave missing values as zero; digital/SNPX reads can return fewer values than requested. | `RobotIF.cs`: handshake and read/write methods |
| F12 | Medium | Writable holder state is unsynchronized. If a local value changes during `WriteValue`, `PushValue` can clear the dirty flag for the newer value even though only the earlier value was sent. | `ExRobotIF.Data.cs`: `_LocalUpdate`, `PushValue` |
| F13 | Medium | An empty watcher has no wait and can consume CPU continuously. Foreground watcher lifetime also depends on successful shutdown rather than an explicit owner-managed termination contract. | `ExRobotIF.cs`: watcher creation and loop |

`RobotIF.Refresh` and `Refresh2` additionally remain public methods that throw
`NotImplementedException`; neither is part of `IRobotIF`.

## Corrections to the previous report

- This is a working-tree assessment, not an asserted comparison against tagged
  source. API migration details belong to `CHANGELOG.md` and `MIGRATION.md`.
- The connect timeout is not a TCP connect timeout; socket I/O timeouts do apply
  to the SRTP handshake's send/receive operations.
- Handshake responses are received and checked for `SHORT_FAILED`. The missing
  piece is comprehensive validation, not the absence of all response checks.
- The watcher is not entirely outside I/O serialization: built-in holders route
  normal reads and writes through the wrapper's task factory.
- A task timeout or cancellation of pending tasks does not terminate an executing
  synchronous connect/receive. Merely wrapping work in a timed wait would leave
  the underlying operation running.
- The wrapper now accepts only `RobotIF`; both layers can be tested using a local
  TCP server without changing their APIs. A transport abstraction could make
  deterministic fault injection easier but is not required for all protocol tests.

## Recommended next work

1. **Transport correctness:** loop until all request/header bytes are transferred,
   treat zero-byte reads as EOF, and reject malformed or mismatched responses
   and insufficient payloads.
2. **Lifecycle ownership:** dispose clients regardless of connection status,
   clean up failed local connects and partial extended initialization, and
   serialize connection state transitions as well as request/response traffic.
3. **Cancellation and shutdown:** define an explicit watcher stop/await protocol
   that avoids self-join, prevents overlap across reconnects, and surfaces
   polling failures. An actual connect deadline must cancel or dispose the
   underlying operation, not just stop waiting for its task.
4. **Polling and holder state:** add configurable pacing or an idle wait, reset
   and bound assignment allocation after `CLRASG`, and synchronize pending writes
   so newer values cannot lose their dirty state.
5. **Input contracts:** consistently validate address ranges, byte/word alignment
   and payload limits across reads, writes and direct SNPX calls.
6. **Regression coverage:** use a local TCP server for wrapper failures/reconnects,
   fragmented headers/payloads, mid-payload EOF,
   truncated responses and protocol correlation. Cover boundary I/O preservation,
   assignment reset and concurrent holder updates without a physical robot.

These recommendations concern application lifecycle and protocol correctness;
they do not replace controller interlocks or other machine-safety mechanisms.

## Validation status

- Findings were checked against the current source and project files.
- The Visual Studio workspace build succeeded during this review. This does not
  establish runtime behavior or coverage of every target/configuration.
- Since the initial review, separate MSTest projects were added for the libraries
  and desktop application. Baseline tests passed on .NET Framework 4.8, .NET 8
  (using .NET Standard 2.0 assets), .NET 10, and Windows/.NET 10 respectively.
  This is initial coverage, not verification of the findings above.
- No live robot connection or runtime fault-injection
  test was performed.

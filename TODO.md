# TODO

Updated: 2026-11-14. Based on [REPORT.md](REPORT.md).

Work from top to bottom: `URGENT` > `HIGH` > `MIDIUM` > `LOW` > `NICETOHAVE`.
Items within each section are also ordered. Priorities here reflect implementation
order and impact, not a change to the report's severity assessment or a
machine-safety classification. Unchecked items remain open.

Each fix should include focused regression coverage. The library tests now use
local TCP servers for framing; additional lifecycle and fault-injection coverage
is still needed. `FANUCRobotTest` remains an interactive application.

## URGENT

- [x] **[URGENT] T01 - Stop infinite receive loops on peer disconnect (F2).**
  In `RobotIF.ReceiveInternal`, treat a zero-byte payload receive as EOF and
  surface the failure. Verify that a disconnect partway through an extended
  response terminates promptly without spinning.
- [x] **[URGENT] T02 - Handle partial TCP sends and fragmented headers (F1).**
   - [x] **T02.1**: progress-aware send/header loops and EOF handling, with loopback
         coverage for fragmented/truncated headers and large requests to a slow peer.
- [ ] **[URGENT] T03 - Complete shutdown and reconnect lifetime verification (F5, F13).**
  Shutdown now interrupts socket I/O, drains the session, and joins its background
  watcher before reconnecting. Loopback tests cover blocked polling, reconnect,
  and watcher-initiated disconnect. A universal shutdown bound is not established:
  timeouts can be disabled, hostname resolution may continue on older targets,
  user callbacks may block, and real-controller behavior remains untested.

## HIGH

- [x] **[HIGH] T04 - Serialize transport exchanges and connection transitions (F4).**
  Protect sequence numbers, shared buffers, complete request/response exchanges,
  and connect/disconnect state. Prevent overlapping `ExRobotIF.ConnectAsync`
  attempts from installing independent schedulers/watchers. Preserve the ability
  to interrupt I/O during shutdown; do not hold a lock that prevents that cleanup.
- [x] **[HIGH] T05 - Dispose clients on every connection failure and disconnect (F3).**
  Clean up locally created clients when TCP connect or configuration fails, and
  close/dispose stored clients regardless of `TcpClient.Connected`. Verify
  repeated failed connects and repeated disconnects do not retain resources.
- [x] **[HIGH] T06 - Roll back failed extended initialization (F6).**
  If `CLRASG` or assignment creation fails, disconnect the backend and clear
  partial session resources. A later connect must retry initialization rather
  than report success for a connected backend without a watcher.
- [ ] **[HIGH] T07 - Validate response identity and expected payload lengths (F11).**
   - [x] **T07.1**: reject read payloads shorter than required, accepting and
     tracing excess extended bytes without assuming they are invalid. Short
     responses have six payload bytes whose padding cannot be distinguished.
   - [x] **T07.1a**: validate read ACK types against their response data format
     after receiving the declared frame; treat an empty long payload as empty.
   - [ ] **T07.2**: investigate request/response field relationships using
     controller captures before enforcing identity checks for handshake, reads
     and writes. Do not assume undocumented fields are echoed.
- [x] **[HIGH] T08 - Reject invalid addresses, counts and payload sizes (F9, F10).**
  Validate 1-based read/write ranges (including offset signals and holders),
  SNPX byte/word alignment, and command and payload limits before sending.
  Loopback tests cover invalid and maximum valid values without changing
  digital/group neighboring-bit behavior.
- [x] **[HIGH] T09 - Verify distinct queued holder writes (F12).**
  Holder writes now use explicit `WriteAsync` calls rather than shared dirty
  state. A loopback regression verifies that consecutive writes queued behind a
  blocked write are sent separately and complete on their own acknowledgments.
- [x] **[HIGH] T10 - Apply the existing timeout to TCP connect.**
  Use cancellation-aware connect on .NET 5 and newer; on older targets, bound
  the wait and close the client on timeout. The timeout remains approximate and
  per operation, not an overall handshake deadline. Caller cancellation is not
  provided; hostname resolution may continue after timeout on older targets.

## MIDIUM

- [x] **[MIDIUM] T11 - Surface polling failures and consistent connection notifications (F7).**
  Preserve the original watcher exception through an observable error mechanism
  consistent with the library API. Report cleanup failures explicitly and notify
  consumers when a connection is lost even if the backend already reports false.
- [x] **[MIDIUM] T12 - Reset and bound assignment allocation on reconnect (F8).**
  Reset `AssignmentOffset` after successful `CLRASG`, rebuild holder assignments,
  and reject allocations beyond the supported range. Verify repeated reconnects
  do not keep increasing offsets and that an exhausted allocation fails clearly.
- [ ] **[MIDIUM] T15 - Define HTTP timeout, cancellation and client lifetime.**
  Host and HTTP port are configured independently of SRTP, and KCL/Karel calls
  work without an SRTP session. Define disposal of each robot's lazy HTTP client
  and explicit HTTP timeout/cancellation behavior independently of TCP I/O.

## LOW

- [ ] **[LOW] T14 - Keep desktop connection and shutdown work off the UI thread.**
  Update `FANUCRobotTest\UI\MainWindow.xaml.cs` to await lifecycle operations,
  prevent conflicting button actions, and display failures without freezing the
  UI. Coordinate window-close behavior with T03 and T10.
- [ ] **[LOW] T17 - Make signing configuration portable.**
  Replace reliance on a machine-specific key path with configurable signing
  inputs and documented local-build steps. Preserve release assembly identity;
  do not commit signing keys or silently disable release signing.
- [ ] **[LOW] T18 - Document and verify the build/integration matrix.**
  Separate managed-library builds from Windows desktop requirements.
  Record coverage for `net48`, `netstandard2.0`, and `net10.0`, and distinguish
  automated TCP tests from manual controller checks. One successful
  workspace build is not evidence that every target and integration was tested.

## NICETOHAVE

These are optional design improvements, not additional confirmed defects.

- [ ] **[NICETOHAVE] T13 - Avoid unnecessary idle polling wakeups (F13).**
  The watcher waits after every pass, including empty passes. The delay is
  configurable, defaults to 10 ms, and wakes on cancellation. Consider
  event-driven idle waiting if periodic wakeups become significant.
- [ ] **[NICETOHAVE] T02.2 - Test partial-send retries deterministically.**
  Prove that a partial `Socket.Send` return is retried; the slow-peer test does
  not guarantee that condition.
- [ ] **[NICETOHAVE] T19 - Consider an injectable core transport.**
  If the local TCP harness cannot conveniently exercise partial sends or other
  faults, consider a small transport seam for deterministic fault injection.
- [ ] **[NICETOHAVE] T20 - Consider task-based polling and nonblocking I/O.**
  After lifecycle behavior is covered, evaluate cancellable task-based polling,
  truly asynchronous transport/HTTP operations, and an explicit disposal
  contract. Retain compatibility where feasible; these refactors are not
  prerequisites for correcting the existing implementation.

## WONTFIX

- [x] **[WONTFIX] T16 - Implement `Refresh` and `Refresh2`.**
  These experimental vendor-library placeholders were removed. `ExRobotIF`
  uses its polling loop to refresh holder values instead.

## Finding coverage

| Report finding | Work items |
|---|---|
| F1 | T02 |
| F2 | T01 |
| F3 | T05 |
| F4 | T04 |
| F5 | T03, T10 |
| F6 | T06 |
| F7 | T11 |
| F8 | T12 |
| F9 | T08 |
| F10 | T08 |
| F11 | T07 |
| F12 | T09 |
| F13 | T03, T13 |

Already completed improvements listed in the report are not reopened here.
Regression coverage should retain them, especially neighboring digital-bit
preservation, write-range validation, string padding, and dictionary notifications.

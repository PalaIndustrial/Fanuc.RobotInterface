# TODO

Updated: 2026-11-13. Based on [REPORT.md](REPORT.md).

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
  TCP connect has no explicit deadline (T10), HTTP calls and user callbacks may
  block, and real-controller behavior remains untested.

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
  Validate protocol-defined response fields against the request for handshake,
  reads and writes. Reject malformed, mismatched and truncated responses instead
  of returning short arrays or zero-filled group values. Cover both short and
  extended responses.
- [ ] **[HIGH] T08 - Reject invalid addresses, counts and payload sizes (F9, F10).**
  Validate read ranges, SNPX indexes, byte/word alignment and command/SNPX payload
  limits before narrowing to `ushort` or sending anything. Cover index zero,
  index 65537, odd SNPX byte counts, maximum valid ranges and overflowing ranges.
  Preserve existing digital/group write checks and neighboring-bit preservation.
- [x] **[HIGH] T09 - Verify distinct queued holder writes (F12).**
  Holder writes now use explicit `WriteAsync` calls rather than shared dirty
  state. A loopback regression verifies that consecutive writes queued behind a
  blocked write are sent separately and complete on their own acknowledgments.
- [ ] **[HIGH] T10 - Add real connect deadlines and caller cancellation.**
  Define TCP-connect and overall-handshake deadlines separately from per-I/O
  timeouts. Propagate cancellation to the underlying operation and extended
  lifecycle; a timed wait alone must not leave work running. Verify timeout,
  cancellation, cleanup, and retry behavior across supported targets.

## MIDIUM

- [x] **[MIDIUM] T11 - Surface polling failures and consistent connection notifications (F7).**
  Preserve the original watcher exception through an observable error mechanism
  consistent with the library API. Report cleanup failures explicitly and notify
  consumers when a connection is lost even if the backend already reports false.
- [x] **[MIDIUM] T12 - Reset and bound assignment allocation on reconnect (F8).**
  Reset `AssignmentOffset` after successful `CLRASG`, rebuild holder assignments,
  and reject allocations beyond the supported range. Verify repeated reconnects
  do not keep increasing offsets and that an exhausted allocation fails clearly.
- [ ] **[MIDIUM] T13 - Pace polling and wait when there are no holders (F13).**
  The watcher now waits 10 ms after each polling pass, including empty passes,
  and wakes on cancellation. Verify idle behavior and active refresh cadence;
  consider whether the interval should be configurable.
- [ ] **[MIDIUM] T14 - Keep desktop connection and shutdown work off the UI thread.**
  Update `FANUCRobotTest\UI\MainWindow.xaml.cs` to await lifecycle operations,
  prevent conflicting button actions, and display failures without freezing the
  UI. Coordinate window-close behavior with T03 and T10.
- [ ] **[MIDIUM] T15 - Define HTTP host, timeout and disconnected-call behavior.**
  Decide and document whether direct KCL/Karel helpers may target a remembered
  host after SRTP disconnect. Provide explicit HTTP timeout/cancellation behavior,
  independent of TCP I/O settings, and keep direct and wrapper APIs intentional.
  Preserve existing behavior unless a deliberate compatibility change is made.

## LOW

- [ ] **[LOW] T16 - Resolve the public `Refresh` and `Refresh2` placeholders.**
  Decide whether to implement, deprecate, or document these unsupported methods;
  both currently throw `NotImplementedException` and are outside `IRobotIF`.
  Consider source/binary compatibility before removing either member.
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

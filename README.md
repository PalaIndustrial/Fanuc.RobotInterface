# FANUC.RobotInterface

v3.0.4

# Overview

A library that allows you to connect to FANUC industrial robot arm. Should work with these robot controllers:

- R-J3iB 7D80/45 or later
- R-J3iB 7D81/09 or later
- R-J3iB 7D82/01 or later
- R-J3iB Mate 7D91/01 or later
- R-30iA All Versions (**)
- R-30iA Mate All Versions (**)
- R-30iB All Versions (**)
- R-30iB Plus All Versions (**)

** One of the following options is required on these controllers:
- R650 "FRA Params" + R553 "HMI Device (SNPX)"
- R651 "FRL Params"

Tested with R-30iB.

Intended for direct or local network connections. Behavior across complex
routed networks has not been tested.

# Example

Connect to a controller and read digital input 1:

```csharp
using Fanuc.RobotInterface;

var robot = new RobotIF();
try
{
    robot.Connect("192.168.1.10", 60008, 10000);
    bool input = robot.ReadDI(1, 1)[0];
}
finally
{
    robot.Disconnect();
}
```

For managed SNPX assignments, use `ExRobotIF`. Holders can be registered before
or after connecting and retained across reconnects:

```csharp
var robot = new ExRobotIF(new RobotIF());
robot.R[1].PropertyChanged += (_, e) =>
{
    if (e.PropertyName == nameof(IValueHolder<float>.Value))
        Console.WriteLine(robot.R[1].Value);
};

try
{
    await robot.ConnectAsync("192.168.1.10", 60008, 10000);
    await robot.R[1].WriteAsync(42f);
}
finally
{
    robot.Disconnect();
}
```

`Value` is the latest polled value; it is not a write request. Await each
`WriteAsync` to observe the write response or a failure. Write response types are
not yet validated beyond rejecting `SHORT_FAILED`. A response does not establish
that a write affected an existing target or that a workflow or motion has
completed. A failed or interrupted write may already have reached the
controller: it is not automatically replayed.
Subscriptions run on the polling thread, so UI consumers must marshal updates
to their UI thread and avoid blocking callbacks.

Polling and initialization failures are exposed through `LastError` and
`PropertyChanged` notifications. `Disconnect()` interrupts active socket I/O
and waits for the watcher; a callback that blocks can still delay shutdown.
The async methods schedule synchronous I/O. The connection timeout also applies
to TCP connect, but is approximate and is not an overall handshake deadline.
Zero disables the configured timeouts; hostname resolution may continue after
a timeout on older targets. Caller cancellation is not available.

Connecting `ExRobotIF` sends `CLRASG` before rebuilding its assignments. The
effect of that command on other controller clients has not been verified. Give
the wrapper a dedicated `RobotIF`; direct use of the same backend bypasses its
assignment and lifecycle management.

`RobotIF` uses 1-based indexes. Invalid addresses and ranges, odd SNPX byte
counts, and payloads that exceed the protocol limits are rejected before sending.
Offset-based signal methods and holders also require 1-based indexes. SNPX indexes
address words, while SNPX counts and payload sizes are in bytes.

Reads require `SHORT_ACK` for inline data or `LONG_ACK` for extended data and
reject other response types or insufficient payload bytes. The entire declared
frame is received before validation. A zero-length `LONG_ACK` has no read data.
Extra bytes in extended read responses are ignored and reported through
`System.Diagnostics.Trace` when the library is built with `TRACE` defined;
applications need a trace listener to capture these diagnostics. Short responses
expose a fixed six-byte payload, so padding cannot always be distinguished from
actual data.

# Automated tests

The solution includes separate MSTest projects for the libraries and the WPF
application.

# Disclaimer

**The authohr of this software is not affiliated with GE or FANUC Corporation in any way. All trademarks and registered trademarks are property of their respective owners, and company, product and service names mentioned in this readme or appearing in source code or other artifacts in this repository are used for identification purposes only.**

**Use of these names does not imply endorsement by either GE nor FANUC Corporation.**

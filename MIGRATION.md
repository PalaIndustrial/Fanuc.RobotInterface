# Migration guide

## Migrating from 3.0.4 to 3.0.5

Rebuild consumers and dependent libraries together.

### Connection and HTTP

- Construct `RobotIF` with the controller host and optional `srtpPort` and
  `httpPort` (defaults 60008 and 80). Call `Connect(timeout)` or
  `ExRobotIF.ConnectAsync(timeout)` without endpoint arguments. Create a new
  `RobotIF` for a different endpoint.
- `ExRobotIF` now requires a concrete `RobotIF`, not an arbitrary `IRobotIF`.
  Give it a dedicated backend; direct use of the same backend bypasses its
  assignment and lifecycle management.
- `IRobotIF` implementers must add `Host`, `SrtpPort`, and `HttpPort` and
  update `Connect`. `IExRobotIF` implementers must also update `ConnectAsync`,
  expose `LastError`, and implement `AsyncExecuteKCL` and `AsyncExecuteKarel`.
  `WriteAssignment` was removed; use `AsyncWriteAssignment`.
- `ResetAlarm` is now an extension method: import `Fanuc.RobotInterface`.
  `ExecuteKCL` and `ExecuteKarel` remain interface methods, but use HTTP
  independently of the SRTP connection. SRTP disconnect does not cancel HTTP
  calls, and the HTTP client lifetime is not yet defined.
- TCP connect now uses the configured timeout as well as each send/receive.
  It is approximate, not an overall handshake deadline; zero disables the
  timeouts and hostname resolution may continue after timeout on older targets.

### Holders and sessions

- Writable holder `Value` is now read-only. Replace assignments with
  `await holder.WriteAsync(value)`. The polled `Value` updates separately;
  await the write to observe its response or failure. A failed write may have
  reached the controller and is not automatically replayed.
- `IsDirty`, `PullValue`, `PushValue`, and the public holder plumbing interfaces
  were removed. Holder constructors and the protected `Robot` property now use
  `ExRobotIF`; `WriteAssignment` overrides on complex holders are protected.
  Review custom holders and implementations of `IWritableValueHolder`.
- `IRobotDataDictionary` and the value-holder interfaces moved from the main
  assembly to Extensions, retaining their namespaces. `RobotDataDictionary`,
  `ObservableConcurrentDictionary`, and `LimitedConcurrencyLevelTaskScheduler`
  are now internal; replace direct uses with your own implementations or suitable
  alternatives.
- Initialization and polling failures are exposed through `LastError` and
  notifications. Disconnect interrupts socket I/O and waits for shutdown;
  blocking callbacks can delay it. Synchronous wrapper calls now propagate
  underlying exceptions rather than wrapping them in `AggregateException`.
- Polling waits 10 ms after each pass by default. The `ExRobotIF` constructor
  accepts a longer `pollingDelay`; values below 10 are clamped to 10. The delay
  is not a fixed refresh period.
- `RobotIF.Refresh` and `Refresh2` were removed; they previously threw
  `NotImplementedException`. Extended holders are refreshed by polling.

### Validation and SRTP packets

- Invalid 1-based addresses and ranges, odd SNPX byte counts, oversized
  payloads, and invalid assignment lengths are rejected. Reads reject unexpected
  ACK types and insufficient payloads; excess extended bytes are ignored and
  traced. Review error handling for operations previously accepted or truncated.
- In `ExtendedRequestPacket`, `Count` no longer sets the extra-payload length.
  Set `ExtraPayload` or `Payload` to the actual bytes, independently of
  `Count`. Payloads over 65,535 bytes now throw `ArgumentOutOfRangeException`
  instead of producing a truncated length.

## Migrating from 3.0.3 to 3.0.4

### Packages, namespaces, and binary compatibility

- `ExRobotIF`, `IExRobotIF`, and the nested holder types on `ExRobotIF` moved from the `Fanuc.RobotInterface` assembly to `Fanuc.RobotInterface.Extensions`. Add a reference to the `Fanuc.RobotInterface.Extensions` 3.0.4 package. Their namespace remains `Fanuc.RobotInterface`; existing `using` directives do not need to change.
- SRTP types moved from the `Fanuc.RobotInterface` assembly and `Fanuc.RobotInterface.SRTP` namespace to the `Fanuc.SRTP` assembly and namespace. Replace the old namespace with `Fanuc.SRTP`. The main project references `Fanuc.SRTP`; add a direct package reference if your project uses its types directly.
- Rebuild consumers and dependent libraries against the new assemblies, and deploy their dependencies together. Update assembly-qualified type names, reflection, and serialized type metadata that refer to moved types.
- The main assembly changed from unsigned to strong-named and its assembly version changed from `3.0.3.0` to `3.0.4.0`. This changes its assembly identity even for types that did not move. Update explicit assembly references and any applicable .NET Framework binding configuration; do not assume an old binary can simply load the replacement DLL.

### Property notifications

`NotifyPropertyBase` was removed. `RobotIF`, `ExRobotIF`, and `ExRobotIF.RobotDataHolderBase<T>` no longer inherit from it or implement `INotifyPropertyChanging`; they still implement `INotifyPropertyChanged`. Replace references, casts, and constraints involving `NotifyPropertyBase` with an appropriate notification interface or your own base class. Code that subscribed to `PropertyChanging` must provide its own before-change mechanism; `PropertyChanged` fires after the change and is not equivalent.

Subclasses using the old protected `SetField`, `RaisePropertyChanging`, or `RaisePropertyChanged` helpers must provide their own equivalents. `RobotDataHolderBase<T>` retains a protected `RaisePropertyChanged(string name)`, but not the old parameterless or event-argument overloads.

### I/O behavior

- `RobotIF.WriteDI` and `WriteDO` now read neighboring bits before an unaligned write and preserve them, instead of padding with zeroes. This adds one or two read requests and can fail if those reads fail or return insufficient data. Ensure the controller permits the reads, and review timing and concurrency assumptions: the read-modify-write is not atomic.
- Digital writes that exceed the 16-bit address range after byte alignment, or whose aligned bit count exceeds 65,535, now throw `ArgumentOutOfRangeException`. Group writes also reject ranges beyond the 16-bit address space and payloads over 32,767 values. Split large writes into valid ranges.
- I/O extension **write** methods and grouped/analog/PMC-D **read** extension methods now wrap underlying exceptions in `IOException` with the original exception in `InnerException`. Update exception handling accordingly. Direct `IRobotIF` instance calls are not wrapped by these extension methods; instance `ReadGI`, `ReadGO`, `WriteGI`, and `WriteGO` take precedence over same-named extensions in normal method-call syntax.
- The provided `RobotDataDictionary` implementation of `IRobotDataDictionary.Count` now reports the actual item count rather than always returning zero. Review logic that relied on the previous behavior.

### Framework and source-build changes

The libraries now target .NET Framework 4.8, .NET Standard 2.0, and .NET 10 instead of .NET Framework 4.8 and .NET 6. A .NET 6 application can use the .NET Standard 2.0 asset, but no longer receives a dedicated .NET 6 asset. Verify compatibility for other target frameworks against .NET Standard 2.0.

Source builds use `Fanuc.RobotInterface.slnx` instead of `FANUCRobotTest.sln`, require an SDK that supports .NET 10 and `.slnx`, and configure strong-name signing with a machine-specific key path in `Directory.Build.props` and `Fanuc.SRTP.csproj`. Supply a suitable key or explicitly override signing for a local build; disabling or changing the key changes the resulting assembly identity. The included test application now targets .NET 10 for Windows and uses WPF-UI instead of ModernWpf.

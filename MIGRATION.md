# Migrating from 3.0.3 to 3.0.4

## Packages, namespaces, and binary compatibility

- `ExRobotIF`, `IExRobotIF`, and the nested holder types on `ExRobotIF` moved from the `Fanuc.RobotInterface` assembly to `Fanuc.RobotInterface.Extensions`. Add a reference to the `Fanuc.RobotInterface.Extensions` 3.0.4 package. Their namespace remains `Fanuc.RobotInterface`; existing `using` directives do not need to change.
- SRTP types moved from the `Fanuc.RobotInterface` assembly and `Fanuc.RobotInterface.SRTP` namespace to the `Fanuc.SRTP` assembly and namespace. Replace the old namespace with `Fanuc.SRTP`. The main project references `Fanuc.SRTP`; add a direct package reference if your project uses its types directly.
- Rebuild consumers and dependent libraries against the new assemblies, and deploy their dependencies together. Update assembly-qualified type names, reflection, and serialized type metadata that refer to moved types.
- The main assembly changed from unsigned to strong-named and its assembly version changed from `3.0.3.0` to `3.0.4.0`. This changes its assembly identity even for types that did not move. Update explicit assembly references and any applicable .NET Framework binding configuration; do not assume an old binary can simply load the replacement DLL.

## Property notifications

`NotifyPropertyBase` was removed. `RobotIF`, `ExRobotIF`, and `ExRobotIF.RobotDataHolderBase<T>` no longer inherit from it or implement `INotifyPropertyChanging`; they still implement `INotifyPropertyChanged`. Replace references, casts, and constraints involving `NotifyPropertyBase` with an appropriate notification interface or your own base class. Code that subscribed to `PropertyChanging` must provide its own before-change mechanism; `PropertyChanged` fires after the change and is not equivalent.

Subclasses using the old protected `SetField`, `RaisePropertyChanging`, or `RaisePropertyChanged` helpers must provide their own equivalents. `RobotDataHolderBase<T>` retains a protected `RaisePropertyChanged(string name)`, but not the old parameterless or event-argument overloads.

## I/O behavior

- `RobotIF.WriteDI` and `WriteDO` now read neighboring bits before an unaligned write and preserve them, instead of padding with zeroes. This adds one or two read requests and can fail if those reads fail or return insufficient data. Ensure the controller permits the reads, and review timing and concurrency assumptions: the read-modify-write is not atomic.
- Digital writes that exceed the 16-bit address range after byte alignment, or whose aligned bit count exceeds 65,535, now throw `ArgumentOutOfRangeException`. Group writes also reject ranges beyond the 16-bit address space and payloads over 32,767 values. Split large writes into valid ranges.
- I/O extension **write** methods and grouped/analog/PMC-D **read** extension methods now wrap underlying exceptions in `IOException` with the original exception in `InnerException`. Update exception handling accordingly. Direct `IRobotIF` instance calls are not wrapped by these extension methods; instance `ReadGI`, `ReadGO`, `WriteGI`, and `WriteGO` take precedence over same-named extensions in normal method-call syntax.
- The provided `RobotDataDictionary` implementation of `IRobotDataDictionary.Count` now reports the actual item count rather than always returning zero. Review logic that relied on the previous behavior.

## Framework and source-build changes

The libraries now target .NET Framework 4.8, .NET Standard 2.0, and .NET 10 instead of .NET Framework 4.8 and .NET 6. A .NET 6 application can use the .NET Standard 2.0 asset, but no longer receives a dedicated .NET 6 asset. Verify compatibility for other target frameworks against .NET Standard 2.0.

Source builds use `Fanuc.RobotInterface.slnx` instead of `FANUCRobotTest.sln`, require an SDK that supports .NET 10 and `.slnx`, and configure strong-name signing with a machine-specific key path in `Directory.Build.props` and `Fanuc.SRTP.csproj`. Supply a suitable key or explicitly override signing for a local build; disabling or changing the key changes the resulting assembly identity. The included test application now targets .NET 10 for Windows and uses WPF-UI instead of ModernWpf.

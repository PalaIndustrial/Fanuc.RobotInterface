# Changelog

## 3.0.4

### Breaking changes

- Moved the extended robot interface (`ExRobotIF` and `IExRobotIF`) into the separate `Fanuc.RobotInterface.Extensions` package. Consumers using these types must reference that package.
- Moved SRTP types into the `Fanuc.SRTP` package and changed their namespace from `Fanuc.RobotInterface.SRTP` to `Fanuc.SRTP`.
- Removed `NotifyPropertyBase`; `RobotIF` and `ExRobotIF` now expose property-change notifications without inheriting from it.
- The libraries now target .NET Framework 4.8, .NET Standard 2.0, and .NET 10 instead of directly targeting .NET 6.

### Fixed

- Preserve neighboring digital I/O values when a write must be aligned to byte boundaries. Reject digital and group I/O writes that exceed protocol address or payload limits.
- Correct `RobotDataDictionary.Count` to report the number of items.

### Changed

- Expose `RobotDataDictionary` publicly and forward its collection and property-change notifications.
- Include operation context in I/O extension method errors.
- Migrate FANUCRobotTest from ModernWpf to WPF-UI and update it to .NET 10.
- Share library version and package settings through `Directory.Build.props` and include the README in each NuGet package.
- Document supported controller families and required controller options.

## 3.0.3

### Fixed

- Pad odd-length string register and system variable writes to an even byte count for SNPX.

## 3.0.2

### Added

- Add .NET Framework 4.8 support alongside .NET 6.

## 3.0.1

### Changed

- Replace the `ParallelExtensionsExtras.NetFxStandard` package dependency with included scheduler and observable dictionary implementations.

## 3.0.0

### Added

- Initial robot interface with digital and group I/O, SNPX, KCL and Karel operations.
- Extended interface for robot signals, registers, system variables, positions, tasks and alarms.
- WPF test application for connecting to and inspecting a robot.
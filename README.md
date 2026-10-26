# TaskManager++

A Windows desktop system monitor built with C# and WPF. It shows system-wide physical memory and commit charge alongside per-process CPU and memory usage.

## Run

Install the .NET 8 SDK, then run from this folder:

```powershell
dotnet run
```

The project targets Windows because its live memory counters use the Windows Performance Information API.

## Memory figures

- **Physical in use / available** use the Windows system-wide performance counters. Available includes free and reclaimable memory.
- **Hardware reserved** compares physically installed memory with memory visible to Windows; Windows may report this counter as unavailable on some systems.
- **Commit charge** is committed virtual memory, not physical RAM; it can be backed by RAM or the page file.
- **System cache** and kernel pools are additional diagnostics and overlap with physical in-use/available accounting. The cards are not intended to be summed.
- Per-process **working set** is resident RAM currently mapped into that process. **Private RAM** is its resident private portion; **shared RAM** is the remaining resident portion. Processes may share physical pages, so summing their working sets can double-count RAM.
- Per-process **private commit** is private committed memory, not the same as private resident RAM.

The process list refreshes every two seconds, supports search and column sorting, and can request termination of a selected process after confirmation. Protected or elevated processes may deny memory reads or termination.

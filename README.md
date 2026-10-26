# TaskManager++

A Windows desktop system monitor built with C# and WPF. It focuses on system RAM and per-process CPU and memory usage.

## Run

Install the .NET 8 SDK, then run from this folder:

```powershell
dotnet run
```

The project targets Windows because its live memory counters use the Windows Performance Information API.

## Memory figures

- **RAM in use** is the process working set: physical RAM currently resident in that process.
- **Memory allocated** is private committed memory. It can be larger than RAM in use because some committed pages may be paged out; it is included to help explain cases where a game's allocated memory is much higher than its resident RAM.
- Summing working sets across several processes can double-count shared physical pages.

The process list refreshes every two seconds, supports search and column sorting, and can request termination of a selected process after confirmation. Search for `cs2` to see totals for matching Counter-Strike 2 processes. Protected or elevated processes may deny memory reads or termination.

Click the **Physical Memory in Use** card to open a pie chart of process RAM in use. It shows the ten largest processes by working set and groups the remaining processes into **Other processes**. The system-wide physical RAM-in-use figure is also shown for comparison. Working sets include shared pages, so summing them can double-count RAM and exceed the system-wide figure.

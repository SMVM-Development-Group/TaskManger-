# TaskManager++

A Windows desktop system monitor built with C# and WPF. It focuses on system RAM and per-process CPU and memory usage.

## Run

Install the .NET 8 SDK, then run from this folder:

```powershell
dotnet run
```

The project targets Windows because its live memory counters use the Windows Performance Information API.

## Publish as a desktop app

Publish a self-contained Windows x64 app as a single executable:

```powershell
dotnet publish -c Release -o .\App
```

Run `App\TaskManagerPlusPlus.exe` directly or create a Windows desktop shortcut to it. The published app does not require the .NET Desktop Runtime to be installed.

## Memory figures

- **RAM in use** is the process working set: physical RAM currently resident in that process.
- **Memory allocated** is private committed memory. It can be larger than RAM in use because some committed pages may be paged out; it is included to help explain cases where a game's allocated memory is much higher than its resident RAM.
- Summing working sets across several processes can double-count shared physical pages.

The process list refreshes every two seconds, supports search and column sorting, and can request termination of a selected process after confirmation. Search for `cs2` to see totals for matching Counter-Strike 2 processes. Protected or elevated processes may deny memory reads or termination.

Click the **Physical Memory in Use** card to open a pie chart of process RAM in use. It shows the ten largest processes by working set and groups the remaining processes into **Other processes**. The system-wide physical RAM-in-use figure is also shown for comparison. Working sets include shared pages, so summing them can double-count RAM and exceed the system-wide figure.

## AI process analysis

Use **AI settings** to save an NVIDIA API key and choose a model. **Load from API** retrieves the model IDs available to that key from NVIDIA; the selector also accepts a model ID manually. The selected model is stored locally under `%LOCALAPPDATA%\TaskManager++`. The chat window also includes a model selector and **Load models** button; it defaults to your saved model, and a model chosen in chat applies to chat replies for that session. DeepSeek chat requests use NVIDIA's thinking-mode template and a larger token budget. **Scan top 5 RAM users** reports only actionable concerns; if none stand out, it says so. Choose **Open AI chat** with no process selected to start a general conversation, or select a process first to start with that process as context. The chat process selector lets you switch between general chat and any running process; switching clears the previous conversation so answers do not mix contexts. **Analyze selected process** remains available for a focused scan. The AI API key is encrypted for the current Windows user and stored locally; process names, PIDs, and CPU/memory figures are sent to NVIDIA only when you request an analysis or chat response.

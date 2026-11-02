# MBrowser 4.4.0 RC12 — task-manager process identity

CEF uses multiple OS processes (browser, renderers, GPU, network, storage and
utility workers). Keeping them separate is necessary for stability and
sandboxing. This change does **not** try to merge them into a single process.

By default, RC12 self-hosts Chromium subprocesses via the main MBrowser.exe:
- `Program.Main` routes `--type=renderer`, `--type=gpu-process` and other
  CEF subprocess command lines into `CefSharp.BrowserSubprocess.SelfHost.Main`
  before normal WinForms startup.
- `CefSettings.BrowserSubprocessPath` points to `MBrowser.exe`, not
  `CefSharp.BrowserSubprocess.exe`.
- The parent and subprocesses receive the same explicit
  `MIGLEcorp.MBrowser` process AppUserModelID.
- `Cef.Initialize` disables the dependency check for supported self-hosting.

The main application and workers now have a common executable name and a
parent/child process relationship. This helps Windows recognize them as part
of the same application. **Windows Task Manager controls its own UI grouping**:
a single expandable app group in the Processes tab is a desired outcome,
not a documented guarantee; the Details tab will still show multiple PIDs.

To revert to stock CefSharp worker processes if a regression is found, set
`MBROWSER_LEGACY_CEF_SUBPROCESS=1` before starting MBrowser, then restart it.
The original CefSharp.BrowserSubprocess.exe is still included.

Observed on Windows 11 with a **disposable --cef-smoke profile**:
- 1 main MBrowser.exe
- 4 MBrowser.exe workers directly parented to it, including renderer, GPU
  and utility processes
- 0 CefSharp.BrowserSubprocess.exe processes from the test installation
- CEF renderer initialized and smoke exited 0.

Keep users' main profile unmodified during isolated smoke tests; install
only after the full set of smoke tests and installer verification pass.

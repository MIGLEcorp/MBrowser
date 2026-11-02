# CEF frame lifecycle and managed callback safety

## Failure observed on RC10 (2026-11-02)

Windows Event Viewer, .NET Runtime event 1026, logged an unhandled
`System.Exception` when opening FunPay:

```
The underlying frame is no longer valid - please check the IsValid property before calling!
at CefSharp.Internals.CefFrameWrapper.ThrowIfFrameInvalid()
at CefSharp.Internals.CefFrameWrapper.ExecuteJavaScriptAsync(...)
at MBrowser.MainForm.<CreateCefView>...FrameLoadEnd... (MainForm.CefEngine.cs:146)
```

This is a **managed application crash**, not proof of a native Chromium
`libcef.dll` fault. Navigation can invalidate a frame between a validity
check and the managed script call. CefSharp throws a plain
`System.Exception` for this condition, not an `ObjectDisposedException`.

## RC11 fix

- `CefFrameLoadGuard.TryRun` checks frame validity and isolates the
  synchronous native callback from thrown managed exceptions.
- Both `FrameLoadStart` and `FrameLoadEnd` use this guard. It treats
  invalidated/disposed frames as normal navigation races.
- Other unexpected exceptions are logged and the callback returns safely.
- An invalid FrameLoadEnd is not allowed to mark the page as ready or
  schedule memory optimizations on the wrong document.
- A core regression test reproduces the exact thrown exception and
  verifies recovery, error reporting and successful valid-frame injection.

No user preferences, cookies, sessions or saved passwords are touched.
CEF native binaries and their resources remain the optimized RC10 build.

## Validation plan

1. Run 42 core tests, including frame invalidation.
2. Run isolated `--cef-smoke`, `--cef-credentials-smoke` and
   `--cef-delayed-optimization-smoke`.
3. Visit `https://funpay.com/` using `--cef-url-smoke` on an isolated profile,
   and verify Windows application event log contains no new `MBrowser.exe`
   unhandled exceptions. Live FunPay may have unrelated network/site issues.
4. Test the installer in `/TESTMODE=1` before upgrading the working copy.

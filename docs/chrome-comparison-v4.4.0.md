# MBrowser 4.4.0 stable — Chrome comparison (2026-11-02)

Test machine: Windows 11, MBrowser CEF 154 (optimized native build), installed Google Chrome 156. Same machine/network, clean temporary profiles, one navigation per site, no login. Chrome was launched **headless** via Playwright, MBrowser CEF in a **visible window** using --cef-url-smoke. All timings are browser-reported Navigation Timing `domContentLoadedEventEnd` in milliseconds.

| Website | Chrome 156 | MBrowser CEF 154 | MBrowser vs Chrome |
|---|---:|---:|---:|
| Google Accounts | 1,861 ms | 2,698 ms | +45.0% |
| GitHub | 3,681 ms | 4,278 ms | +16.2% |
| FunPay | 3,270 ms | 3,033 ms | -7.2% |

A negative percentage means MBrowser finished sooner in this sample. A one-run spot-check is **not** a rigorous benchmark: network variance, caching, page variations, headless versus visible conditions, and browser engine version differences can change the result. Do **not** describe MBrowser as always faster than Chrome or promise lower RAM without controlled measurements.

The custom build uses `dcheck_always_on=false` and `enable_expensive_dchecks=false`. Unlike an old WebView2 wrapper, this is a CEF Chromium browser with its own multi-process runtime. RC12 routes GPU, renderer and utility processes through `MBrowser.exe` to help Windows Task Manager group them, but **does not collapse multiple OS processes into one**.

Additional stable pre-release verification required: 42 core tests, native CEF renderer, FaceDetector, credentials, deferred optimizations, process-identity checks (including legacy fallback), isolated installer and preservation of the existing user profile.

Raw files from the same machine (not in version control): `artifacts/chrome-v4.4-comparison.txt`, `artifacts/chrome-funpay-results.txt`, `artifacts/mbrowser-v4.4-comparison.txt`.

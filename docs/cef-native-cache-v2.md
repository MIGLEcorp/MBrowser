# CEF 154 native cache-v2 experiment — MBrowser

Status: **experimental source patch, not a production DLL** until the native build and A/B validation pass.

## Disk/build migration

The existing Ninja output directory was safely copied (robocopy with preserved timestamps) to:

`H:\\zhopa\\MBrowser\\artifacts\\cef154_native_out\\Release_GN_x64`

Its original location `C:\\src\\cef154\\chromium\\src\\out\\Release_GN_x64` is now an NTFS junction to that directory. Source/destination libcef.dll SHA256 matched exactly before switching. The source backup was removed only after verifying robocopy /L /MIR reported no differences and Ninja could parse the build graph. Roughly 12 GB of disk space was recovered on C:.

To rebuild (Windows, Build Tools + Ninja + Chromium Python required):

```powershell
powershell -ExecutionPolicy Bypass -File H:\\zhopa\\MBrowser\\tools\\build_cef_cache_v2.ps1 -Jobs 6
```

## Opt-in cache policy

Patch: `patches/chromium/cef154-compact-cache-v2.patch` against the Chromium 154 tree. It includes the original optional feature declarations and code, not just the two new constants:

| Decoded-image cache | Stock Chromium | Earlier MBrowser opt-in | V2 opt-in |
| --- | --- | --- | --- |
| GPU persistent entries | 2000 | 512 | 256 |
| Software decoded entries | 1000 | 256 | 128 |

`MBrowserCompactDecodedImageCache` remains **disabled by default** and should **not** be enabled for every user automatically. It evicts unused entries earlier but may increase decode/re-rasterization costs, page scrolling latency, GPU restarts, and memory churn on some sites.

## Evidence and release gate

A preliminary 420-unique-image gallery benchmark at `http://127.0.0.1:18885/index.html` (self-contained local fixture in `artifacts/ram-benchmark/unique_gallery.py`) was inconsistent: base mode registered ~690/374 MiB (Working Set/Private), then its smoke process stuck at shutdown; the earlier compact mode later registered ~819/741 MiB with extra child processes, likely not a valid comparable sample. **No positive memory claim follows from these runs.**

Before packaging a new libcef.dll, require:
1. Successful link and SHA256 of native DLL, matching original CEF resources and snapshot.
2. Portable CEF smoke: blank startup, page navigation, media playback, sign-in, and shutdown; no hangs.
3. Multiple A/B runs with distinct images plus real websites and a cold/warm run distinction; report Private Bytes, Working Set, P95 load, FPS and GPU resets.
4. Reject native patch if it increases memory, FPS drops or errors. Never replace the installed browser until verified.

In the meantime the released MBrowser 4.4.6 keeps the prior verified libcef.dll unchanged.

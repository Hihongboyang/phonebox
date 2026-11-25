# Feasibility evidence (2026-11-24)

Everything below was read from the repository at commit `25bbe1c` (version 0.9.29) or measured on the development
machine on 2026-11-24, before any code of this change existed. Re-run a probe before relying on it on another
machine or date.

## Development machine

- Windows 10.0.26200, Intel Core i7-11700 (8 cores, 16 threads), 16 GB RAM, Intel UHD Graphics 750 and no discrete GPU.
- System proxy enabled: `127.0.0.1:10808`. HTTP clients that follow the system proxy reach international services through it.
- Toolchain before the change: .NET SDK 8.0.427 only; `adb` on PATH; no Java and no Android SDK; Node 22.22.2; npm 10.9.7;
  Python 3.13.2; OpenSpec CLI 1.4.1; `gh` not installed.
- .NET SDK 10.0.404 was installed during this change with winget (`Microsoft.DotNet.SDK.10`).
- PhoneBox was not running. Data folders exist under `%LOCALAPPDATA%\PhoneBox` and `%APPDATA%\PhoneBox`.
- The repository clones `ersinkoc/phonebox` with no fork remote.

## Clipboard history

A background process (Windows PowerShell started by the agent harness, no foreground window) called the WinRT
clipboard API. Only the status and the count were printed, never the content.

| Call | Result |
| --- | --- |
| `Clipboard.IsHistoryEnabled()` | `True` |
| `Clipboard.GetHistoryItemsAsync()` | Status `Success`, 17 items, the newest item carries content |

Reading the clipboard history from a background Win32 process works on this machine. The design reads the current
clipboard instead, which is the newest history record.

## Network reachability

Each request was sent through the system proxy with no key, and `hello` was the only text that left the machine.
The time is until the error response, not a translation.

| Endpoint | Result |
| --- | --- |
| `translate.googleapis.com/translate_a/single?client=gtx` | HTTP 429 |
| `clients5.google.com/translate_a/t?client=dict-chrome-ex` | HTTP 429 |
| `translation.googleapis.com/language/translate/v2` | HTTP 403 |
| `dict.youdao.com/dictvoice?audio=hello&type=2` | HTTP 200, `audio/mpeg`, 10,605 bytes, 133 ms |
| `api.cognitive.microsofttranslator.com/translate` | HTTP 415 (no body sent), 447 ms |
| `api-free.deepl.com/v2/translate` | HTTP 403, 1085 ms |
| `fanyi-api.baidu.com/api/trans/vip/translate` | HTTP 200 `{"error_code":"52003","error_msg":"UNAUTHORIZED USER"}`, 139 ms |
| `open.bigmodel.cn/api/paas/v4/chat/completions` | HTTP 401, 115 ms |
| `mt.aliyuncs.com` | HTTP 400, 220 ms |
| `open.volcengineapi.com` | HTTP 400, 2444 ms |
| `speech.platform.bing.com/consumer/speech/synthesize` | HTTP 400, 773 ms |

## Code facts

| Fact | Anchor |
| --- | --- |
| `Clipboard.GetText()` returns the clipboard's text, or `""` when it holds something else | `agent/PhoneBox.Agent/Clipboard.cs:29` |
| A clipboard read runs on a short-lived STA thread and throws `TimeoutException` after 5 s, or `InvalidOperationException` when the clipboard is busy | `Clipboard.cs:72-86` |
| Tests swap the clipboard with `Clipboard.TestBackend` | `Clipboard.cs:26` |
| The tray runs a WinForms message loop on an STA thread | `Tray.cs:31-37`, `Tray.cs:117` |
| Nothing in the agent registers a global hotkey | search for `RegisterHotKey` returns no hit in `agent/` |
| `KeySender.Parse(spec)` and `KeySender.WinVk(key)` turn a key spec such as `ctrl+alt+d` into keys | `KeySender.cs:30`, `KeySender.cs:135` |
| `PushStore.Emit(kind, data)` queues a panel event; `Emitted` wakes live streams | `PushStore.cs:119-133` |
| `PanelEvent` kinds: notify, goto, back, refresh, reload, approval, approval-done | `PushStore.cs:9-10` |
| Events are replayed for at most 60 s and nothing filters them by kind | `PushStore.cs:136-140`, `Program.cs:586`, `Program.cs:850` |
| `POST /api/notify` payload: title, text, level, vibrate, page, seconds | `Program.cs:552-560` |
| `handleNotify` shows a toast, buzzes unless `vibrate === false`, and keeps the text in the notification list | `main.js:1197-1213` |
| The toast's message is one line with an ellipsis and the toast takes no taps | `style.css:327-329` |
| `handleEvent` dispatches panel events with an else-if chain and no default branch, so an unknown kind is ignored | `main.js:1427-1435` |
| `#approval` is a full-screen overlay at z-index 90; `#overview` is at 15; the toast at 20 | `style.css:1218`, `style.css:241-242`, `style.css:323` |
| `window.onAndroidBack` closes overlays in order and never dismisses an approval prompt | `main.js:2234-2241` |
| The approval card sets `lastTouch` and calls `undim()` so the panel is not dimmed while it is read | `main.js:1256-1261` |
| `bridge` wraps `window.Android`, adds the bridge key, keeps only methods the installed phone app has, and is `null` outside the phone app | `main.js:61-66` |
| The bridge is `MainActivity.Bridge`; each method checks `ok(key)` | `MainActivity.kt:422-423` |
| `listen()` sanitises a language tag with `take(20).filter { it.isLetterOrDigit() \|\| it == '-' }` | `MainActivity.kt:616-617` |
| Dictation is created lazily, cancelled in `onPause` and `onDestroy` | `MainActivity.kt:74`, `:342`, `:356` |
| The manifest declares `<queries>` with `android.speech.RecognitionService` for Android 11+ | `AndroidManifest.xml:12-18` |
| The phone app: minSdk 21, targetSdk 35, versionName 0.9.29 | `android/app/build.gradle.kts:14-21` |
| No text-to-speech code exists in the phone app | search for `TextToSpeech` returns no hit in `android/` |
| `Lan.AllowedOverLan(path)` is the phone API allow-list, used by USB too | `Lan.cs:90-102`, `UsbLink.cs:79` |
| `/api/settings` gives credentials only to the same-origin desktop fetch and redacts `steam.apiKey` for everyone else | `Program.cs:396-415` |
| `ConfigStore.PreserveSteamApiKey` keeps a redacted key when the client sends it back as null | `Config.cs:557-563`, `Program.cs:432-433` |
| After a settings save the PUT handler calls `SettingsSaved()` on the services that need it | `Program.cs:457-461` |
| `AgentConfig.ToPublic()` is an explicit projection; a section that is not listed never reaches the phone | `Config.cs:78` |
| `LyricsService` takes an optional `HttpMessageHandler` and clock, so tests inject a fake handler | `LyricsService.cs:38` |
| The Steam section in the settings page binds `steam.enabled` and `steam.apiKey` (a password input) | `settings.html:687-691` |
| The settings page's Test button posts to a local-only endpoint | `settings.html:2413-2416` |
| The agent embeds the panel files for the live preview in the layout editor | `PhoneBox.Agent.csproj:64-65` |
| The agent's Windows build is `net10.0-windows10.0.19041.0`; non-Windows builds exclude `Tray.cs` and the other platform files | `PhoneBox.Agent.csproj:7-8`, `:38-40` |
| The test project builds the agent as `net10.0`, where `WINDOWS` is not defined | `PhoneBox.Agent.Tests.csproj:4`, `:23` |
| UI tests emit panel events with `window.PhoneBoxDemo.emit(kind, data)` and mock the bridge with `window.Android` plus `window.__phoneboxKey` | `tools/pages/demo.js:146`, `tools/ui-tests/wake-button.spec.js:19-21` |
| `i18n.js` lists 23 languages; the i18n coverage test fails on English left on screen in another language | `i18n.js`, `tools/ui-tests/i18n-coverage.spec.js` |
| `*.txt` files are ignored by git | `.gitignore` |

## Build and CI

| Fact | Anchor |
| --- | --- |
| The `build` workflow runs on a push to `main`, on pull requests and on demand | `.github/workflows/build.yml:3-7` |
| The Android job runs `lintDebug assembleDebug testDebugUnitTest` with JDK 17 and uploads `phonebox-apk` | `build.yml:23-43` |
| The agent job publishes a self-contained single-file agent per OS and uploads `Agent (<rid>)` | `build.yml:70-199` |
| A release APK is signed with the owner's key only when `PHONEBOX_STORE_FILE` is set; otherwise the debug key is used | `android/app/build.gradle.kts:24-39`, `docs/DEVELOPMENT.md:133-138` |
| `scripts/install.ps1` publishes the agent, stops the running one, installs to `%LOCALAPPDATA%\PhoneBox\app` and starts it | `scripts/install.ps1:1-12` |
| A development agent can run beside an installed one with `--config <file> --port <port>` | `docs/DEVELOPMENT.md:125-131` |

## External facts

| Fact | Source | How it was read |
| --- | --- | --- |
| Android WebView does not support the Speech synthesis API | <https://caniwebview.com/features/web-feature-speech-synthesis/> | page read |
| Apps targeting Android 11 that use text-to-speech should declare `android.intent.action.TTS_SERVICE` under `<queries>` | <https://stackoverflow.com/questions/74928642/why-is-android-11-mandating-intent-action-tts-service>, quoting the Android reference | page read |
| Baidu general text translation, unverified account: 50,000 characters a month free, 1 request per second, 1000 characters per request, 28 common languages. Verified as an individual: 1,000,000 characters a month, 10 requests per second, 6000 characters per request. Beyond the free characters 49 CNY per million | <https://api.fanyi.baidu.com/product/111> (official) | search snippet; the page is a single-page app |
| Baidu signature: MD5 of `appid + q + salt + key`, 32 lowercase hex characters; `q` is UTF-8 | <https://fanyi-api.baidu.com/doc/23> (official API document) | search snippet |
| Baidu response: `from`, `to`, `trans_result[]` with `src` and `dst`; `\n` in `q` separates paragraphs that come back as separate results | <https://cloud.tencent.com/developer/article/2220709>, <https://baidufanyi.apifox.cn/doc-1006705> (third party and FAQ mirror) | search snippets; **not confirmed against the live API** |

## Not verified

- Whether the Baidu response's `from` carries the detected language when the request sends `from=auto`, and the exact language codes it uses. To confirm with a real APPID and key.
- Whether the 1000-character request limit counts characters or bytes.
- Whether the phone has English voice data for its TTS engine.
- Whether each CI run signs the debug APK with the same key.
- Whether a hotkey registered by a process that is not elevated fires while an elevated window has the focus.
- Whether workflows are disabled by default in a fresh fork.

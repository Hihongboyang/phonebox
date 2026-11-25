## Context

PhoneBox pairs a desktop agent (C#) with an Android phone app whose panel is a WebView page. The owner reads English and other languages on a Windows PC and wants the Chinese translation on the phone with one hotkey, and the original read aloud on request.

The feature was designed in an interview before this change. Its inputs are kept with the change and are the reference for everything below:

- `evidence/design-v2.md`: the design as agreed and the owner's eight decisions.
- `evidence/feasibility-2026-11-24.md`: the code facts, the measurements on the development machine and the external sources, including what was not verified.

What exists and is reused: `Clipboard.GetText()`, `PushStore.Emit` with the state poll and the live stream, `KeySender.Parse`, `ConfigStore` with `PUT /api/settings` and its `SettingsSaved()` calls, the Steam key's redaction, `handleEvent`, the approval card's use of `undim()` and `lastTouch`, the `bridge` wrapper and `MainActivity.Bridge`, and the pattern of `Dictation.kt`.

What is missing and gets built: a global hotkey, a translation provider call, a card on the panel, and text-to-speech in the phone app.

Constraints that shaped the design:

- The test project builds the agent as `net10.0`, where `WINDOWS` is not defined. Only code outside `#if WINDOWS` can be tested.
- The Android app cannot be compiled on the development machine (no JDK, no Android SDK). The `build` workflow compiles it.
- `CONTRIBUTING.md`: a new outbound connection is off by default and gets a row in `PRIVACY.md`; secrets never reach the phone; the panel builds its elements with `textContent`.
- The panel and the settings page exist in 23 languages, and a test fails on English left on screen in another language.
- The development machine has no discrete GPU, so a local translation model is out.

Stakeholders: the owner, who runs the agent on Windows and one paired phone, and the upstream repository whose conventions the change follows.

## Goals / Non-Goals

**Goals:**

- One hotkey press shows the Chinese translation of the clipboard text on the phone within about two seconds.
- The original text can be heard with one tap, using the phone's own text-to-speech engine.
- Nothing leaves the computer except to Baidu Translate, and only on an explicit hotkey press.
- Every rule the agent applies is covered by a unit test that runs in the existing test project.
- The change follows the repository's conventions closely enough to be proposed upstream.

**Non-Goals:**

The Non-Goals of `proposal.md` apply. In addition, this design does not make the hotkey available on macOS or Linux: `Hotkeys` exists there as a class that does nothing, so the rest of the code needs no platform branches.

## Decisions

| # | Decision | Alternatives rejected, and why | Consequence |
| --- | --- | --- | --- |
| D1 | The translation provider is Baidu's general text translation API. | Zhipu: a language model that needs its thinking mode switched off, and its translation agent is billed by the token. Aliyun, Tencent, Volcengine: more signing code, or real-name verification. Azure and DeepL: reachable only through the system proxy. Google's free endpoint: HTTP 429 from this network. A local model: the machine has no discrete GPU and the owner ruled it out. A local dictionary: English words only; a possible later addition. | The APPID and the key live in the config. An unverified account gets one request a second, 1000 characters per request and 28 languages. |
| D2 | Only the source text is read aloud. | A Read-aloud button for the translation too: the owner does not want it. | One voice tag per event. |
| D3 | The Translate card is transient: 30 seconds for a translation, 10 seconds for an error. | A card that stays until dismissed: the owner does not want it. The toast: its message is one line, it takes no taps and it enters the notification history. A widget on a page with a `goto` event: it switches pages and stays. | The card has a timer, and a new event restarts it. |
| D4 | A new panel event kind, `translate`. | Extra fields on `notify`: the toast's limits above, and it buzzes. A pushed value: it is persisted to `data.json` and needs a widget on a page. | `PanelEvent` gets one more kind. A phone app that does not know it ignores it. |
| D5 | The source text is read by Android's `TextToSpeech` through a new `speak` method of the bridge. The agent is not involved. | The WebView's `speechSynthesis`: Android WebView does not have it. Audio fetched by the agent and streamed to the phone: it needs a new path on the phone API allow-list, and the only free endpoint measured (Youdao) is unofficial. It stays the fallback if the phone has no usable engine. Sending the text back to the agent to speak: the phone sends ids, never commands. | Nothing is added to the phone API allow-list. The manifest declares the TTS service under `<queries>`. |
| D6 | The hotkey is `Ctrl+Alt+D`, configurable. It does not send Ctrl+C before the clipboard is read. | Sending Ctrl+C from the hotkey: the owner turned it off, because some editors copy a whole line when nothing is selected. Reading the selection through UI Automation: more code, and uneven across applications. | The text has to be copied before the hotkey is pressed. |
| D7 | Text that is Chinese under the Chinese-skip rule is dropped silently. | Translating Chinese into English: not requested. A toast saying it was skipped: noise. | The rule excludes kana and Hangul, so Japanese and Korean text with Han characters still translates. |
| D8 | `Hotkeys` registers the hotkey with `RegisterHotKey` on a message-only window, on an STA thread of its own. One file is compiled on every OS, with the Windows code under `#if WINDOWS`. | Inside `Tray`: two responsibilities in one class, and the tray file is left out of the other builds, so the coupling would be fragile. One class per OS: a second convention next to the single-file way `Clipboard.cs` already uses. | On macOS and Linux `Hotkeys` registers nothing and never raises `Pressed`. |
| D9 | Three agent classes: `Hotkeys` (Win32), `TranslateService` (the flow), `BaiduTranslate` (the provider's HTTP). | One class: HTTP cannot be replaced in tests. A provider interface with several implementations: one provider is wanted and local models are out. | `BaiduTranslate` takes an optional `HttpMessageHandler`, as `LyricsService` does. |
| D10 | Everything testable stays outside `#if WINDOWS`: the hotkey spec parsing, the Chinese-skip rule, normalising and cutting the text, building and parsing the request, and building the event. | Tests behind `#if WINDOWS`: they are not compiled into the test project. Testing through the real clipboard and the real hotkey: the result would depend on the machine. | Tests supply the clipboard through `Clipboard.TestBackend` and raise `Pressed` by calling `TranslateService.RunAsync` directly. |
| D11 | Requests to Baidu go out one at a time, at least one second apart. A new hotkey press cancels the translation in flight. | One request per press: a double press runs into Baidu's rate limit (`54003`). | At most one request is in flight, and only the latest text is shown. |
| D12 | `maxChars` defaults to 1000. Longer text is cut and ends with `…`. | An error card for text that is too long: worse than a partial translation. Splitting the text into several requests: it uses up the one-request-a-second budget. | A verified account can raise `maxChars` up to 6000. |
| D13 | An error reaches the card as a code, `provider` or `network`, plus a `detail` string. The panel translates the code. | An English sentence from the agent: the panel runs in 23 languages. | Two more interface texts. |
| D14 | The agent turns the language that Baidu reports into a voice tag. A language outside the map gives `null`, and the card has no Read-aloud button. | Letting the phone guess: a wrong voice reads the text as gibberish. Always offering the button with the phone's default voice: the same problem. | The map is one small table in `BaiduTranslate`. |
| D15 | A tap on the Read-aloud button while it reads stops the reading. The button is highlighted while it reads. The phone app reports the end of an utterance through the existing `js` callback, as `window.onAndroidTts('done')`. | No way to stop: a long paragraph cannot be interrupted. A separate Stop button: a second control for a rare case. Showing the muted icon while reading: that icon means something else. | One more global callback name. |
| D16 | `TranslateConfig` is off by default, stays out of `ToPublic()`, and its key is redacted and kept the way the Steam key is: `PreserveTranslateKey` next to `PreserveSteamApiKey`. | A new way to keep secrets: a second convention. Keeping the key outside `config.json`: every other integration keeps its secret there. | Two short methods with the same shape. |
| D17 | Two endpoints on the local port only: `POST /api/translate/test` and `GET /api/translate/status`. | An endpoint the phone can call: not needed with D5, and each allow-listed path is a security decision. Testing only through the hotkey: a wrong key cannot be told from a broken hotkey. | Nothing is added to `Lan.AllowedOverLan`. |
| D18 | The current clipboard text is read. The Windows clipboard history is not used. | Falling back to `GetHistoryItemsAsync` when the clipboard holds no text: it works on this machine, but it adds a WinRT dependency for a rare case. | A picture or a file on the clipboard ends the run silently. |
| D19 | There is no "translating" card. The card appears with the result. | A placeholder card while waiting: a flash for most texts, since the answer takes one to two seconds. | The first feedback is the result. |
| D20 | The TTS engine is started when the phone app starts. | Starting it at the first tap: the first tap would fail with not ready. | An idle connection to the engine while the panel is open. |
| D21 | The card sits at the bottom centre, z-index 16, at most 60vh high, and scrolls inside. | The top of the screen: it covers the clock and status widgets. A full-screen card: too heavy for a sentence. | It is above the page overview (15) and below the page-pause overlay (18), the toast (20) and an approval prompt (90). Superseded by D26 after the owner saw the real size on the phone. |
| D26 | After seeing the card on the phone the owner asked for the largest possible display: the card is near full screen (1.5 vw margins, vertically centred, at most 88 vh high) with 28 px source text and 34 px translation. | Keeping the toast-sized card (D21): the 13.5 px text is unreadable across a desk. Enlarging only the fonts inside the small card: a big type in a small box is worse than a big box. | It still sits below the page-pause overlay, toast and approval prompt, and scrolls inside when the text is longer than 88 vh. |
| D22 | The agent is built on this machine with the .NET 10 SDK. The APK comes from the `build` workflow in a fork. | Both from CI: a slow loop for the agent, where the hotkey and the translation logic live. A local JDK and Android SDK: the owner chose CI. | The agent and the APK come from the same commit. |
| D23 | About 18 new interface texts, in all 23 languages. | English only: it fails the i18n coverage test, and the owner's panel is in Chinese. | The translations are written in this change and checked by the existing tests. |
| D24 | Logs record the length, the duration and the outcome of a translation, and exception type names. Never the text, never an exception message. | The usual `ex.Message`: a JSON or HTTP exception can quote the request. | A unit test checks that the text never reaches a log. |
| D25 | `maxChars` is a number field on the settings page. | Config file only: `CONTRIBUTING.md` says a new setting goes into the settings page. | One more field and two more texts. |

## Risks / Trade-offs

- [The Baidu response may not report the detected language, or its language codes may differ from the map.] → The integration check with a real APPID and key is a task of this change. If `from` is missing, the map step is revisited with a decision record before the change is called done.
- [The Android code cannot be compiled here.] → CI (`lintDebug`) is the compile. `Speaker.kt` is kept small and follows `Dictation.kt`. The checks on a phone are manual tasks in `tasks.md` and stay unchecked until the owner runs them.
- [The phone may have no voice for a language.] → `speak` answers `no-voice` or `no-tts`, and the panel says so in a toast. The agent-audio fallback from D5 remains available.
- [A hotkey of a process that is not elevated may not fire while an elevated window has the focus.] → Shown here as a known limit of Windows. Not verified; see `evidence/`.
- [Another program may own `Ctrl+Alt+D`.] → The settings page shows `taken`, and the hotkey is configurable.
- [Each CI run may sign the debug APK with a different key.] → Compare the first two builds. If they differ, a fixed debug key goes into the fork.
- [The clipboard can hold a secret.] → The feature is off by default and runs only on an explicit hotkey press. The text is cut to `maxChars` and the card shows what was sent. Nothing is logged.
- [One request a second on an unverified account.] → D11. A verified account gets ten.
- [18 texts in 23 languages.] → Written in this change. The i18n coverage test and `python tools/check-release.py` catch English left behind and broken placeholders.

## Module Map

| File | Status | What lives in it |
| --- | --- | --- |
| `agent/PhoneBox.Agent/Hotkeys.cs` | new | `Hotkeys`: `Pressed`, `Apply`, `Parse`, `State`; the Win32 calls under `#if WINDOWS` |
| `agent/PhoneBox.Agent/TranslateService.cs` | new | `TranslateService`: `Start`, `SettingsSaved`, `RunAsync`, `Status`, `TestAsync`, `HotkeySpec`, `IsChinese`, `Normalize`, `Cut`; the records `TranslateCardEvent` and `TranslateFailure` |
| `agent/PhoneBox.Agent/BaiduTranslate.cs` | new | `BaiduTranslate`: `TranslateAsync`, `Sign`, `VoiceTag`; the record `BaiduTranslation` |
| `agent/PhoneBox.Agent/Config.cs` | modified | `TranslateConfig`; `AgentConfig.Translate`; its normalisation; `PreserveTranslateKey` |
| `agent/PhoneBox.Agent/Program.cs` | modified | Service registration; `Start` at startup; `SettingsSaved` and `PreserveTranslateKey` in the settings PUT; redaction of `translate.key` in the settings GET; the two endpoints |
| `agent/PhoneBox.Agent/PushStore.cs` | modified | The list of kinds in the comment above `PanelEvent` |
| `agent/PhoneBox.Agent/config.default.json`, `config.default.linux.json`, `config.default.macos.json` | modified | The `translate` defaults with a comment |
| `agent/PhoneBox.Agent/settings.html` | modified | The Translate card in Settings › General and its Test and status script |
| `agent/PhoneBox.Agent.Tests/TranslateTests.cs` | new | The unit tests of the agent side |
| `android/app/src/main/assets/www/index.html` | modified | The `#translate` card |
| `android/app/src/main/assets/www/style.css` | modified | The card's styles |
| `android/app/src/main/assets/www/main.js` | modified | `showTranslate`, `hideTranslate`, the `translate` branch of `handleEvent`, Back, `speak` in the bridge list, the `onAndroidTts` handler |
| `android/app/src/main/assets/www/i18n.js` and the `locale-*.js` files | modified | The new texts |
| `tools/ui-tests/translate.spec.js`, `tools/ui-tests/settings-translate.spec.js` | new | The Playwright tests of the card and of the settings card |
| `tools/pages/demo.js` | modified only if a test needs it | A demo answer for `/api/translate/status` and `/api/translate/test` |
| `android/app/src/main/java/net/oxog/phonebox/deskpanel/Speaker.kt` | new | `Speaker`: `start`, `speak`, `stop`, `shutdown` |
| `android/app/src/main/java/net/oxog/phonebox/deskpanel/MainActivity.kt` | modified | The lazy `speaker`, its lifecycle calls, `Bridge.speak` |
| `android/app/src/main/AndroidManifest.xml` | modified | The TTS service under `<queries>` |
| `PRIVACY.md` | modified | One row |
| `docs/GUIDE.md` | modified | A short section on the translate hotkey |

`docs/GLOSSARY.md`, `AGENTS.md` and `openspec/` belong to the spec-first setup and are not part of the feature.

## Data Flow

The shapes are declared once, in `contracts.md`. This table names who owns each stage.

| Stage | Owner | What happens | What leaves the computer |
| --- | --- | --- | --- |
| 1. Hotkey press | `Hotkeys` | Windows sends `WM_HOTKEY`; `Pressed` is raised | Nothing |
| 2. Clipboard | `TranslateService` | `Clipboard.GetText()` on a thread-pool thread; a busy clipboard ends the run | Nothing |
| 3. Filter | `TranslateService` | Line endings are normalised; empty and Chinese text ends the run; long text is cut | Nothing |
| 4. Translate | `BaiduTranslate` | One POST with the text, `from=auto`, `to=zh`, the APPID, a salt and the signature | The text (cut to `maxChars`) and the APPID, to `fanyi-api.baidu.com` only. The key is used for the signature and is not sent |
| 5. Voice tag | `BaiduTranslate` | The reported language becomes a voice tag, or `null` | Nothing |
| 6. Event | `TranslateService` | `PushStore.Emit("translate", ...)` with a `TranslateCardEvent` | The source text and the translation, to the phone |
| 7. Transport | existing | The state poll or the live stream delivers the event | Nothing new |
| 8. Card | `main.js` | `showTranslate` fills the card with `textContent` and starts the timer | Nothing |
| 9. Read aloud | `main.js`, `MainActivity.Bridge`, `Speaker` | `bridge.speak(text, tag)` reaches `Speaker.speak`, which uses the phone's TTS engine; the end comes back as `window.onAndroidTts('done')` | Nothing |

Logs: stage 2 to 6 write one line with the length, the duration and the outcome. The text and the key never reach a log (D24).

## UI Design

### The Translate card (panel)

```text
┌──────────────────────────────────────────────────────────────┐
│                                                              │
│  The quick brown fox jumps over                      [🔊]    │  ← source text, 28 px, wraps and scrolls
│  the lazy dog.                                               │
│                                                              │
│  敏捷的棕色狐狸跳过了懒狗。                                    │  ← Chinese translation, 34 px
│                                                              │
└──────────────────────────────────────────────────────────────┘
   near full screen (1.5 vw margins, vertically centred, ≤ 88 vh), z-index 16
```

| State | What is on screen |
| --- | --- |
| Empty | Nothing. The card is hidden until a `translate` event arrives. |
| Loading | No card of its own. The card appears with the result (D19). |
| Success | Source text, the Read-aloud button at the end of the source text, the translation below. Hides after 30 seconds. |
| Reading aloud | The button is highlighted. A tap stops the reading and removes the highlight. |
| No voice available | The button is hidden (voice tag `null`, or a phone app without `speak`). A tap that the phone cannot honour shows a toast: "This phone cannot read text aloud" or "This phone has no voice for this language". |
| Error | The card shows the translated error text and, for `provider`, Baidu's code and message. Red border, like the error toast. Hides after 10 seconds. |
| Replaced | A new event fills the same card and restarts the timer. |
| Dismissed | The Back key hides the card at once. |

The panel is undimmed when the card appears, the way it is for an approval prompt.

### The Translate card (Settings › General)

```text
┌─ Translate (Baidu Translate) ─────────────────────────────────────┐
│ [○] Translate the clipboard with a hotkey and show it on the phone │
│ Create an APPID and key at fanyi-api.baidu.com ...                  │
│ Hotkey            [ctrl+alt+d     ]   APPID       [          ]      │
│ Key               [••••••••••••• ]   Longest text [1000    ] chars  │
│ [ Test ]  Hotkey registered                                       │
└───────────────────────────────────────────────────────────────────┘
```

| State | What is on screen |
| --- | --- |
| Loaded | The fields show the saved values. The status line shows the hotkey state: registered, taken by another program, not a valid hotkey, or off. |
| Test running | "Testing..." in the status line. |
| Test passed | "Translation works", followed by the Chinese for "hello". |
| Test failed | "Test failed", followed by Baidu's error or "Could not reach the translation service". |
| Saved | The hotkey is registered again and the status line refreshes. |

## Migration Plan

- No data migration. The `translate` section is optional in an existing `config.json`; normalisation fills its defaults.
- Roll out: build and install the agent; build the APK in CI and install it. The debug APK and the official release are signed with different keys, so the first install replaces the official app and the phone is paired again.
- Roll back: turn the switch off, which unregisters the hotkey and stops every request. Reinstalling the previous agent and APK removes the feature entirely.
- A new phone app with an old agent, or an old phone app with a new agent, works: the event kind and the bridge method are ignored by whichever side does not know them.

## Open Questions

None is open. Three inputs come from the owner and gate specific tasks in `tasks.md`: a Baidu APPID and key, a fork of the repository, and a paired phone.

To confirm while integrating, each with a fallback already written above:

- Whether the Baidu response reports the detected language in `from` when the request sends `from=auto`, and the exact language codes.
- Whether the 1000-character limit counts characters or bytes.
- Whether each CI run signs with the same key.

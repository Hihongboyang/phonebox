## Why

Reading English or another language on the PC means leaving the work to look the text up in a browser or a dictionary app. The PhoneBox on the desk can show the Chinese translation instead: one hotkey, and the translation appears on the phone with a button to hear the original pronounced.

The pieces exist already. The agent reads the clipboard (`Clipboard.GetText()` in `Clipboard.cs`) and delivers panel events to the phone (`PushStore.Emit` in `PushStore.cs`). The phone app exposes native abilities to the panel (`MainActivity.Bridge` in `MainActivity.kt`). This change joins them and adds the three missing parts: a global hotkey, a translation provider and text-to-speech.

The design was settled in an interview. The interview's design paper and the measurements behind it are kept with this change:

- `evidence/design-v2.md`: the design as agreed, with the owner's decisions.
- `evidence/feasibility-2026-11-24.md`: code facts, machine and network measurements, sources, and what was not verified.

## What Changes

Each feature point becomes requirements in the specs.

- **F1 Translate hotkey.** A global Windows hotkey, default `Ctrl+Alt+D`, registered by the agent. It is registered only when the feature is enabled and Baidu Translate has an APPID and a key, and it is registered again when the settings are saved. Its state (`registered`, `taken`, `invalid`, `off`) is shown on the settings page.
- **F2 Clipboard translation.** On a hotkey press the agent reads the current clipboard text and normalises its line endings. It stops silently when the text is empty, and it does not translate text that is Chinese under the Chinese-skip rule. Text longer than `maxChars` (default 1000) is cut and ends with `…`.
- **F3 Baidu Translate.** The agent calls Baidu's general text translation API with `from=auto` and `to=zh`. Paragraphs that come back separately are joined with newlines. Requests go out one at a time, at least one second apart. When the hotkey is pressed again, only the latest text is translated. The APPID and the key never leave the computer for anywhere but Baidu, and are never logged.
- **F4 Translate card event.** A new panel event kind `translate`. It carries a translation (`source`, `target`, `lang`, `seconds` 30) or an error (`error` `provider` or `network`, `detail`, `seconds` 10). A phone app that does not know the kind ignores it.
- **F5 Translate card.** A transient overlay at the bottom of the panel with the source text, one Read-aloud button and the translation. It appears only on a `translate` event, hides by itself after `seconds`, is replaced by the next event and closes on Back. The panel stays awake while it is shown.
- **F6 Read-aloud button.** Reads the source text aloud with the phone's TTS engine through a new `speak` method of the bridge. A second tap stops the reading. The button is absent when the voice tag is `null`, when the event is an error, or when the installed phone app has no `speak`. When the phone cannot read the text (`no-tts`, `no-voice`) the panel says so in a toast. The translation is never read.
- **F7 Voice tag.** The agent maps the language that Baidu reports to a BCP 47 voice tag (`en` to `en-US`, `jp` to `ja`, ...). A language outside the map gives `null`.
- **F8 Settings.** A `translate` section in the config, off by default, whose key is kept out of the public config and redacted the way the Steam key is. A Translate section on the settings page: the enable switch, the hotkey, the APPID, the key (a password field), a Test button and the hotkey state. Two endpoints for the local port only: `POST /api/translate/test` and `GET /api/translate/status`.
- **F9 Phone app.** `Speaker.kt` wraps `TextToSpeech`. It is created in `onCreate`, stopped in `onPause` and released in `onDestroy`. The manifest declares the TTS service under `<queries>`, as it already does for the speech recognizer.
- **F10 Privacy and texts.** A row for Baidu Translate in `PRIVACY.md`. Logs record the length, the duration and the outcome of a translation, never the text. The new interface texts exist in all 23 languages.

Two delivery decisions change no behaviour: the agent is built on this machine with the .NET 10 SDK, and the APK is built by the `build` workflow in a fork of the repository.

## Non-Goals

- Reading the translation aloud. Only the source text has a Read-aloud button.
- A Translate card that stays until it is dismissed, a history of translations, or a widget that shows the last translation.
- A local translation model or a local dictionary.
- Sending Ctrl+C from the hotkey before the clipboard is read.
- Translating Chinese into another language, or translating into anything but Simplified Chinese.
- Any translation provider other than Baidu Translate.
- A notification on the PC when no phone is connected.
- Reading text from the Windows clipboard history instead of the current clipboard.

## Capabilities

### New Capabilities

- `clipboard-translation`: the agent side. The translate hotkey, reading and filtering the clipboard text, calling Baidu Translate, emitting the Translate card event, the settings and the two local endpoints. Covers F1, F2, F3, F4, F7, F8 and F10.
- `translate-card`: the panel and the phone app. Showing the Translate card and reading its source text aloud. Covers F5, F6 and F9.

### Modified Capabilities

None. `openspec/specs/` holds no spec yet, and no existing requirement changes. The change only reuses what is there:

- `Clipboard.GetText()` and the test seam `Clipboard.TestBackend` in `Clipboard.cs`.
- `KeySender.Parse` and `KeySender.WinVk` in `KeySender.cs`, which already turn a key spec such as `ctrl+alt+d` into keys.
- `PushStore.Emit`, the state poll (`/api/state`) and the live stream (`/api/stream`), which deliver panel events without looking at their kind.
- `ConfigStore`, `PUT /api/settings` and the `SettingsSaved()` calls that follow a save.
- The redaction and the keeping of the Steam key (`ConfigStore.PreserveSteamApiKey`, `GET /api/settings`).
- `handleEvent` in `main.js`, which dispatches panel events.
- `undim()` and `lastTouch`, used the way the approval card uses them so the panel is not dimmed while it is read.
- The `bridge` wrapper in `main.js` and `MainActivity.Bridge` in the phone app.
- The pattern of `Dictation.kt`: created lazily, results delivered through the `js` callback, cancelled when the panel pauses.
- The icons `volume-2` and `volume-x` in `main.js`.
- The optional `HttpMessageHandler` constructor parameter of `LyricsService`, which lets tests answer HTTP themselves.

## Impact

Code:

- Agent: three new files (`Hotkeys.cs`, `TranslateService.cs`, `BaiduTranslate.cs`); changes to `Config.cs`, `Program.cs`, `PushStore.cs` (a comment), the three `config.default*.json` files and `settings.html`.
- Panel: changes to `index.html`, `style.css`, `main.js`, `i18n.js` and the `locale-*.js` files.
- Phone app: one new file (`Speaker.kt`); changes to `MainActivity.kt` and `AndroidManifest.xml`.
- Tests: new xUnit tests in `agent/PhoneBox.Agent.Tests`; new Playwright specs in `tools/ui-tests`.

Interfaces:

- One new panel event kind, `translate`.
- One new bridge method, `speak`.
- Two new endpoints on the local port: `POST /api/translate/test` and `GET /api/translate/status`. Nothing is added to the phone API allow-list.
- One new section in `config.json`: `translate`.

Dependencies: no new NuGet, npm or Gradle dependency.

Systems outside the repository:

- Baidu Translate (`fanyi-api.baidu.com`) receives the clipboard text and the APPID. An outbound connection, so it is off by default and gets a row in `PRIVACY.md`.
- The Windows hotkey API (`RegisterHotKey`).
- The text-to-speech engine installed on the phone.

Provided by the owner, needed for some tasks only:

- A Baidu Translate APPID and key, entered on the settings page. Integration checks against the live API wait for it.
- A fork of the repository on GitHub. The APK build waits for it.
- A paired phone. The checks of the hotkey, the card and the reading aloud on a device wait for it.

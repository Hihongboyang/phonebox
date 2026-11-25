# Contracts: add-translate-hotkey

This document declares the shapes once. The files under Contract Home implement them verbatim; `design.md` and the specs use these names without describing them again. Changing a declared shape needs a decision record in `design.md` first.

## Contract Home

| Shapes | File |
| --- | --- |
| `TranslateConfig`, `AgentConfig.Translate`, `ConfigStore.PreserveTranslateKey`, `ConfigStore.RemoveSecrets` | `agent/PhoneBox.Agent/Config.cs` |
| `Hotkeys` (states, `Apply`, `Parse`) | `agent/PhoneBox.Agent/Hotkeys.cs` |
| `TranslateCardEvent`, `TranslateFailure`, `TranslateService` | `agent/PhoneBox.Agent/TranslateService.cs` |
| `BaiduTranslation`, `BaiduTranslate` (`Endpoint`, `Sign`, `VoiceTag`) | `agent/PhoneBox.Agent/BaiduTranslate.cs` |
| Endpoints `POST /api/translate/test` and `GET /api/translate/status` | `agent/PhoneBox.Agent/Program.cs` |
| Panel event kind `translate` | `PanelEvent` in `agent/PhoneBox.Agent/PushStore.cs` (the kind) and `TranslateCardEvent` (the data) |
| `#translate`, `showTranslate`, `hideTranslate`, `onAndroidTts`, `bridge.speak` | `android/app/src/main/assets/www/index.html` and `main.js` |
| Texts | `android/app/src/main/assets/www/i18n.js` and `locale-*.js` |
| `Bridge.speak`, `Speaker` | `android/app/src/main/java/net/oxog/phonebox/deskpanel/MainActivity.kt` and `Speaker.kt` |
| Settings card element ids | `agent/PhoneBox.Agent/settings.html` |

## Interface Contracts

### Panel event `translate` (agent to phone)

Carried by `PushStore.Emit` to the state poll and the live stream like every panel event. The owner of the data shape is the record `TranslateCardEvent` in `TranslateService.cs`; it is serialised with `ConfigStore.WriteOpts` (camelCase, null properties left out), which gives this wire shape:

```ts
type TranslateCardEvent =
  | { source: string; target: string; lang: string | null; seconds: 30 }   // a translation
  | { error: "provider" | "network"; detail?: string; seconds: 10 };       // a failure: no source, no target, no lang
```

- `lang` is the voice tag of the source text (a BCP 47 tag) or null when the language has no known voice.
- `detail` is `<error_code> <error_msg>` for `provider` and absent or a short status for `network`.
- `seconds` is how long the card stays. Constants: `TranslateService.SuccessSeconds` 30, `TranslateService.ErrorSeconds` 10.

### Bridge method `speak` (panel to phone app)

```ts
// window.Android.speak, called through the panel's wrapper, which adds the bridge key:
//   bridge.speak(text, tag) -> SpeakResult
type SpeakResult = "" | "stopped" | "no-tts" | "no-voice" | "denied";

interface Android {
  speak(key: string, text: string, tag: string): SpeakResult;
}
```

- `""`: the text is being read. `"stopped"`: a text that was being read has been stopped instead.
- `"no-tts"`: the phone has no text-to-speech engine, or it is not ready. `"no-voice"`: the engine has no voice for `tag`.
- `"denied"`: the key is not the bridge key; nothing is read.
- `text` is read up to 2000 characters. `tag` keeps its letters, digits and hyphens, up to 20 characters.

### Callback `onAndroidTts` (phone app to panel)

```ts
// evaluated in the panel's main frame through the phone app's js callback
declare function onAndroidTts(kind: "done"): void;   // window.onAndroidTts
```

`"done"` arrives when an utterance ends, fails or is stopped. It is its own global next to `window.onAndroidSpeech`, so the Dictation widget is not involved.

### Endpoint `POST /api/translate/test` (settings page to agent)

Local port only; not on the phone API allow-list.

```ts
// request body (JSON), either field may be left out to use the saved one
interface TranslateTestRequest { appId?: string; key?: string }

// 200
interface TranslateTestOk { result: string }      // the Chinese for "hello"
// 400
interface TranslateTestFailed { error: string }   // "<error_code> <error_msg>", or a network message
```

### Endpoint `GET /api/translate/status` (settings page to agent)

Local port only; not on the phone API allow-list.

```ts
interface TranslateStatus {
  active: boolean;                                   // enabled, with an APPID and a key
  hotkey: "registered" | "taken" | "invalid" | "off";
}
```

### Outbound request to Baidu Translate (agent to Baidu)

```ts
// POST https://fanyi-api.baidu.com/api/trans/vip/translate   (BaiduTranslate.Endpoint), timeout 8 s,
// body application/x-www-form-urlencoded
interface BaiduTranslateForm {
  from: "auto";
  to: "zh";
  appid: string;      // translate.appId
  salt: string;       // a new random value per request
  sign: string;       // BaiduTranslate.Sign(appid, q, salt, key): lowercase hex MD5 of UTF-8 (appid + q + salt + key)
}

type BaiduTranslateResponse =
  | { from?: string; to?: string; trans_result: { src: string; dst: string }[] }   // the translation: dst joined with "\n"
  | { error_code: string; error_msg: string };                                    // becomes error "provider"
// timeout, connection failure, a non-success status or an unreadable body become error "network"
```

```ts
// BaiduTranslate.VoiceTag: the language Baidu reports in `from` -> the voice tag; anything else -> null
const VOICE_TAGS: Readonly<Record<string, string>> = {
  en: "en-US", jp: "ja", kor: "ko", fra: "fr-FR", de: "de-DE", spa: "es-ES", pt: "pt-PT",
  it: "it-IT", ru: "ru-RU", th: "th-TH", vie: "vi-VN", ara: "ar-SA",
};
```

### Hotkey spec

```ts
// translate.hotkey, parsed by Hotkeys.Parse with KeySender.WinVk
type HotkeySpec = `${"ctrl" | "alt" | "shift" | "win"}+${string}`;   // one or more modifiers, then exactly one other key
// e.g. "ctrl+alt+d" -> { Modifiers: 0x0002 | 0x0001, VirtualKey: 0x44 }
// no modifier, no key, an unknown key or a second key -> null, and Hotkeys.Apply answers "invalid"
```

## Data Contracts

### `config.json`, key `translate`

```ts
// AgentConfig.Translate (TranslateConfig in Config.cs), written with ConfigStore.WriteOpts
interface TranslateConfig {
  enabled: boolean;     // false by default
  hotkey: string;       // "ctrl+alt+d" by default; normalisation restores it when blank
  appId?: string;       // null when blank
  key?: string;         // null when blank; a secret
  maxChars: number;     // 1000 by default, kept between 100 and 6000
}
```

- The section is optional in an existing `config.json`; normalisation fills the defaults.
- It is not part of `AgentConfig.ToPublic()`, so it never reaches the phone.
- `GET /api/settings` gives it in full to the settings page only. For every other caller `ConfigStore.RemoveSecrets` leaves out `translate.key` (and `steam.apiKey`, as before).
- `PUT /api/settings`: when the body has no `translate.key` and one is stored, `ConfigStore.PreserveTranslateKey` puts the stored one back before saving.

### Agent names

```ts
// agent/PhoneBox.Agent/Config.cs
class TranslateConfig { Enabled: boolean; Hotkey: string; AppId: string | null; Key: string | null; MaxChars: number }
class ConfigStore {
  static PreserveTranslateKey(proposed: AgentConfig | null, storedKey: string | null): boolean;
  static RemoveSecrets(settings: JsonObject | null): void;
}

// agent/PhoneBox.Agent/Hotkeys.cs   (compiled on every OS; registers a key on Windows only)
const REGISTERED = "registered"; const TAKEN = "taken"; const INVALID = "invalid"; const OFF = "off";
class Hotkeys {               // an IHostedService
  Pressed: Action;            // event
  State: typeof REGISTERED | typeof TAKEN | typeof INVALID | typeof OFF;
  Apply(spec: string | null): string | null;              // null spec unregisters; returns null, or why it failed
  static Parse(spec: string): { Modifiers: number; VirtualKey: number } | null;
}

// agent/PhoneBox.Agent/TranslateService.cs
class TranslateFailure { Code: "provider" | "network"; Detail: string | null }
class TranslateCardEvent { Source: string | null; Target: string | null; Lang: string | null; Error: "provider" | "network" | null; Detail: string | null; Seconds: number }  // Source, Target and Lang are null on an error, so the JSON leaves them out
class TranslateService {
  const SuccessSeconds = 30; const ErrorSeconds = 10;
  static MinGap: TimeSpan;                                   // 1 s; a test may shorten it
  Start(): void;                                             // at agent start: hook Pressed, apply the hotkey spec
  SettingsSaved(): void;                                     // after a settings save: apply the hotkey spec again
  RunAsync(ct: CancellationToken): Promise<void>;            // one hotkey press
  Status(): TranslateStatus;
  TestAsync(appId: string | null, key: string | null, ct: CancellationToken): Promise<{ Result: string | null; Error: string | null }>;
  static Active(cfg: TranslateConfig): boolean;
  static HotkeySpec(cfg: AgentConfig): string | null;
  static IsChinese(text: string): boolean;
  static Normalize(text: string): string;
  static Cut(text: string, maxChars: number): string;
}

// agent/PhoneBox.Agent/BaiduTranslate.cs
class BaiduTranslation { Target: string; Lang: string | null }
class BaiduTranslate {
  constructor(log: ILogger<BaiduTranslate>, handler: HttpMessageHandler | null = null, salt: () => string | null = null);   // the last two are for tests
  static readonly Endpoint = "https://fanyi-api.baidu.com/api/trans/vip/translate";
  static Sign(appId: string, q: string, salt: string, key: string): string;
  static VoiceTag(baiduLanguage: string | null): string | null;
  TranslateAsync(text: string, cfg: TranslateConfig, ct: CancellationToken): Promise<{ Result: BaiduTranslation | null; Error: TranslateFailure | null }>;
}
```

Test seams, all `internal`: `Clipboard.TestBackend` (exists), `TranslateService.TestApply` (a `Func<string?, string?>` that receives the spec instead of `Hotkeys.Apply`), `TranslateService.MinGap`, and the two optional constructor parameters of `BaiduTranslate`.

### Panel names

```ts
// android/app/src/main/assets/www
const CARD = { root: "#translate", source: "#trSource", target: "#trTarget", speak: "#trSpeak" };   // in index.html
function showTranslate(d: TranslateCardEvent): void;   // main.js, exported
function hideTranslate(): void;                        // main.js, exported
// "speak" is added to the BRIDGE list in main.js, so bridge.speak exists only when the phone app has it
// while the phone reads, #trSpeak has the class "on"; an error card has the class "err"

// i18n keys, in STRINGS.en of i18n.js and under "strings" of every locale
const PANEL_KEYS = ["panel.translate.speak", "panel.translate.provider", "panel.translate.network", "panel.translate.noTts", "panel.translate.noVoice"];
const SETTINGS_KEYS = ["s.translate.title", "s.translate.enable", "s.translate.hint", "s.translate.hotkey", "s.translate.appId", "s.translate.key",
  "s.translate.maxChars", "s.translate.stateOn", "s.translate.stateOff", "s.translate.stateTaken", "s.translate.stateInvalid",
  "s.translate.testing", "s.translate.testOk", "s.translate.testFail"];
```

### Settings card names

```ts
// agent/PhoneBox.Agent/settings.html, Settings › General
const INPUTS = {
  "translate.enabled":   "checkbox",
  "translate.hotkey":    "#translateHotkeyInput",
  "translate.appId":     "#translateAppIdInput",
  "translate.key":       "#translateKeyInput",       // type=password
  "translate.maxChars":  "#translateMaxCharsInput",  // type=number
};
const BUTTONS = { test: "#translateTest", state: "#translateState" };   // data-bind paths are the keys of INPUTS
```

### Phone app names

```ts
// android/app/src/main/java/net/oxog/phonebox/deskpanel/Speaker.kt
class Speaker {             // constructed on the main thread with the activity and the js callback
  start(): void;            // onCreate
  speak(text: string, tag: string): SpeakResult;
  stop(): void;             // onPause
  shutdown(): void;         // onDestroy
}
// MainActivity.Bridge.speak(key, text, tag): SpeakResult
// AndroidManifest.xml: <queries> gains <intent><action android:name="android.intent.action.TTS_SERVICE"/></intent></queries>
```

## Compatibility

- **An older phone app with the new agent**: `handleEvent` ignores the kind `translate` and nothing is shown. The agent loses nothing else.
- **The new phone app with an older agent**: no `translate` event ever arrives and `speak` is never called.
- **The panel in a web browser** (the demo, the layout editor's live preview): `bridge` is null, so `#trSpeak` is hidden and the card shows its texts.
- **A `translate` event with missing optional fields**: `lang` absent is null, `detail` absent is nothing, `seconds` absent is 30. The panel never throws on an event it half-knows.
- **`config.json` without `translate`**: the defaults are filled in by normalisation. Unknown properties in the section are ignored.
- **The webhook API** (`POST /api/notify` and the rest) is unchanged; `translate` is an internal event kind and cannot be emitted through a webhook.
- Evolution is additive only: a new optional field, a new `SpeakResult` value, a new voice tag. A change to a declared shape needs a decision record in `design.md` first.

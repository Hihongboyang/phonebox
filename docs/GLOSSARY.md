# PhoneBox glossary

One term per concept. Use these terms exactly in code, specs and UI text.

The entry format and the section structure belong to the spec-first workflow (`openspec/`): add a new entry at the
end of its section, never rename, reorder or remove an entry. A term that clashes with an existing entry is a
question for the maintainers, not something to edit away.

## Pieces of the system

| Term | Meaning | Anchor |
| --- | --- | --- |
| Agent | The desktop program (`PhoneBoxAgent.exe`): watches the computer and answers the phone over USB or Wi-Fi, and serves the settings page and the layout editor on 127.0.0.1:8765. | `agent/PhoneBox.Agent/` |
| Phone app | The Android app that hosts the panel in the system WebView and owns the native proxy and the bridge. | `android/app/src/main/java/net/oxog/phonebox/deskpanel/` |
| Panel | The phone's full-screen UI: pages of widgets, plain ES modules loaded from the phone app's assets. | `android/app/src/main/assets/www/main.js` |
| Settings page | The agent's local web page for its settings and the layout editor. | `agent/PhoneBox.Agent/settings.html` |
| Config | The agent's saved settings (`config.json`), read through `ConfigStore` and saved from the settings page with `PUT /api/settings`. | `Config.cs` |
| Public config | The projection of the config that the phone receives. Secrets, commands and feed addresses stay out of it. | `AgentConfig.ToPublic()` in `Config.cs` |

## How the phone and the agent talk

| Term | Meaning | Anchor |
| --- | --- | --- |
| Transport | The way a phone reaches its agent: USB (an adb reverse tunnel to a port of its own, pinned TLS, the phone app's USB key) or Wi-Fi (HTTPS, pinned certificate, paired key). | `UsbLink.cs`, `Lan.cs` |
| Native proxy | The part of the phone app that intercepts the panel's `/agent/*` requests and forwards them over the transport. The WebView never talks to the computer directly. | `MainActivity.proxy`, `AgentLink.kt` |
| Bridge | `window.Android`, the JavaScript interface the phone app adds to the WebView. Every call carries the bridge key. | `MainActivity.Bridge` |
| Bridge key | A secret made for each run of the phone app (`window.__phoneboxKey`). Only the panel's own frames receive it; a call without it does nothing. | `MainActivity.bridgeKey` |
| Phone API allow-list | The request paths a phone may use over USB and Wi-Fi. Settings, pairing and test endpoints stay off it. | `Lan.AllowedOverLan` in `Lan.cs` |
| Panel event | A short-lived message from the agent to the phone, delivered by the state poll or the live stream and replayed for at most a minute. Kinds: `notify`, `goto`, `back`, `refresh`, `reload`, `approval`, `approval-done`. | `PushStore.Emit` and `PanelEvent` in `PushStore.cs` |
| Live stream | The server-sent-events stream that delivers panel events to the phone at once. | `/api/stream` |
| Webhook | The HTTP entry for programs outside the agent (scripts, CI, Home Assistant): push a value, notify, switch page, refresh. | `/api/push`, `/api/notify`, `/api/goto`, `/api/refresh` in `Program.cs` |
| Push value | A value stored in the agent under a key through the webhook API and shown by Push widgets. It is kept across restarts in `data.json`. | `PushStore.Set`, `/api/push/{key}` |

## What the panel shows

| Term | Meaning | Anchor |
| --- | --- | --- |
| Page | One screen of the panel: a layout of widgets. | `PageConfig` in `Config.cs` |
| Widget | A tile on a page: an entry in the panel's widget registry, plus its styles and its catalogue entry. | `widgets/`, `widgets.json` |
| Notification | A `notify` panel event, shown as a toast and kept in the panel's list of recent notifications. | `handleNotify` in `main.js` |
| Toast | The short message at the bottom of the panel. It hides by itself and takes no taps. | `toast` in `main.js` |
| Approval prompt | A permission prompt from a coding-agent hook, shown as a full-screen card with answer buttons. | `#approval` in `index.html` |

## Automation

| Term | Meaning | Anchor |
| --- | --- | --- |
| Shortcut | A named action kept in the config: start a program, press keys, type text or run a macro. The phone sends only its id. | `Shortcuts.cs` |
| Rule | An automation in the agent: a condition checked every second and, when it starts to hold, an action (switch page, notify, push a value, run a shortcut). | `RuleEngine.cs` |

## Clipboard translation

Added by the change `add-translate-hotkey`. Files marked *(new)* exist once that change is implemented.

| Term | Meaning | Anchor |
| --- | --- | --- |
| Translate hotkey | The global Windows keyboard shortcut that starts a clipboard translation. Default `Ctrl+Alt+D`. | `Hotkeys.cs` *(new)* |
| Clipboard translation | One run, started by the translate hotkey: read the clipboard's text, skip it under the Chinese-skip rule, translate it with Baidu Translate, emit a Translate card event. | `TranslateService.cs` *(new)* |
| Chinese-skip rule | Text that contains Han characters and no kana or Hangul counts as Chinese. It is not translated and no card is shown. | `TranslateService.cs` *(new)* |
| Baidu Translate | The translation provider: Baidu's general text translation API, called by the agent with the APPID and key from the settings. | `BaiduTranslate.cs` *(new)* |
| Translate card | The transient panel overlay with the source text, one Read-aloud button and the Chinese translation. It appears only on a `translate` panel event and hides by itself after 30 seconds. | `#translate` in `index.html` |
| Read-aloud button | The single speaker button on the Translate card. It reads the source text aloud with the phone's TTS engine, and a second tap stops it. The translation is never read. | `Bridge.speak` in `MainActivity.kt`, `Speaker.kt` *(new)* |
| Voice tag | The BCP 47 language tag of the voice that reads the source text, for example `en-US`. It is `null` when the source language has no known voice, and then the card has no Read-aloud button. | Field `lang` of the `translate` panel event |
| TTS engine | The text-to-speech service installed on the phone. A language can be read only when the phone has that language's voice data. | `Speaker.kt` *(new)* |

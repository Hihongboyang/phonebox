## ADDED Requirements

Names are the ones the code uses; `contracts.md` declares their shapes once. The panel tests are Playwright specs in `tools/ui-tests`. The phone app cannot be compiled on the development machine, so its scenarios are checked by hand and compiled and linted by the `build` workflow.

### Requirement: A translate event shows the Translate card

On a panel event of kind `translate` the panel SHALL show the card `#translate` with the source text in `#trSource` and the translation in `#trTarget`, both written with `textContent`. The card SHALL hide by itself after the event's `seconds` (30 when absent). A later `translate` event SHALL replace the content and restart the timer. The Back key (`window.onAndroidBack`) SHALL hide the card. While the card is shown the panel SHALL be undimmed, as it is for an approval prompt.

#### Scenario: A translation arrives
- **WHEN** the event `translate` arrives with `source` `hello world`, `target` `你好，世界` and `lang` `en-US`
- **THEN** `#translate` is visible and shows both texts
- **Test** `shows the source text and the translation` in `tools/ui-tests/translate.spec.js`

#### Scenario: The card hides by itself
- **WHEN** an event with `seconds` 0.3 has been shown for 0.3 seconds
- **THEN** `#translate` is hidden
- **Test** `hides by itself after its seconds`

#### Scenario: A second translation arrives
- **WHEN** a new `translate` event arrives while the card is shown
- **THEN** the card shows the new texts and hides 30 seconds after the new event, not after the first
- **Test** `a new event replaces the card and restarts its timer`

#### Scenario: The Back key
- **WHEN** `window.onAndroidBack()` is called while the card is shown
- **THEN** `#translate` is hidden at once
- **Test** `Back hides the card`

#### Scenario: The panel was dimmed
- **WHEN** a `translate` event arrives while the panel is dimmed
- **THEN** the panel is no longer dimmed while the card is shown
- **Test** `the panel is awake while the card is shown`

#### Scenario: Markup in the text
- **WHEN** the source text is `<b>bold</b> & more`
- **THEN** `#trSource` shows those characters and contains no `<b>` element
- **Test** `shows markup as text`

### Requirement: The Read-aloud button reads the source text

The card SHALL have one button, `#trSpeak`, that reads the source text aloud by calling `bridge.speak(source, lang)`. It SHALL be hidden when the event is an error, when `lang` is null, and when the phone app has no `speak`. While the phone reads, the button SHALL have the class `on`; `window.onAndroidTts('done')` SHALL remove it. When `bridge.speak` returns `no-tts` or `no-voice`, the panel SHALL show a toast with `panel.translate.noTts` or `panel.translate.noVoice`. The translation SHALL never be read.

#### Scenario: A tap
- **WHEN** `#trSpeak` is tapped on a card with source `hello` and `lang` `en-US`
- **THEN** `window.Android.speak` is called once with the bridge key, `hello` and `en-US`
- **Test** `a tap asks the phone to read the source text`

#### Scenario: No voice tag
- **WHEN** the event has `lang` null
- **THEN** `#trSpeak` is hidden and the card shows the texts
- **Test** `no button without a voice tag`

#### Scenario: An error event
- **WHEN** the event carries an `error`
- **THEN** `#trSpeak` is hidden
- **Test** `no button on an error`

#### Scenario: Outside the phone app
- **WHEN** the panel runs without the bridge (the web demo, the layout editor)
- **THEN** the card shows the texts and `#trSpeak` is hidden
- **Test** `no button outside the phone app`

#### Scenario: No speech engine
- **WHEN** `window.Android.speak` returns `no-tts`
- **THEN** a toast shows the text of `panel.translate.noTts`
- **Test** `a phone without a speech engine gets a toast`

#### Scenario: No voice for the language
- **WHEN** `window.Android.speak` returns `no-voice`
- **THEN** a toast shows the text of `panel.translate.noVoice`
- **Test** `a phone without a voice for the language gets a toast`

#### Scenario: Reading aloud
- **WHEN** `window.Android.speak` returns an empty string, and later `window.onAndroidTts('done')` is called
- **THEN** `#trSpeak` has the class `on` in between and not after
- **Test** `the button shows that the phone is reading, until the phone reports the end`

### Requirement: An error event shows an error card

When the event carries `error`, the card SHALL show the text of `panel.translate.<error>` followed by `detail` when there is one, with the class `err`, and SHALL hide after the event's `seconds`.

#### Scenario: The provider reports an error
- **WHEN** the event has `error` `provider` and `detail` `52003 UNAUTHORIZED USER`
- **THEN** the card has the class `err` and its text contains the text of `panel.translate.provider` and `52003 UNAUTHORIZED USER`
- **Test** `an error from the provider is shown with its detail`

#### Scenario: The service cannot be reached
- **WHEN** the event has `error` `network` and no detail
- **THEN** the card has the class `err` and shows the text of `panel.translate.network`
- **Test** `a network error is shown`

### Requirement: The new texts exist in every language

The keys `panel.translate.speak`, `panel.translate.provider`, `panel.translate.network`, `panel.translate.noTts`, `panel.translate.noVoice` and `s.translate.*` SHALL exist in the panel's and the settings page's dictionaries for all 23 languages.

#### Scenario: The panel is in Chinese
- **WHEN** the panel language is Chinese and an error card is shown
- **THEN** the card shows the Chinese text of `panel.translate.provider`
- **Test** `the card's texts are translated`

#### Scenario: The settings page is in another language
- **WHEN** the settings page is shown in any language other than English
- **THEN** no English of the Translate card is left on screen
- **Test** the existing `tools/ui-tests/i18n-coverage.spec.js`

### Requirement: The phone app reads with its own text-to-speech engine

`MainActivity.Bridge.speak(key, text, tag)` SHALL check the bridge key and call `Speaker.speak(text, tag)`, which reads with the phone's `TextToSpeech` in the voice of the BCP 47 tag `tag`. `Speaker` SHALL be started in `onCreate`, stopped in `onPause` and released in `onDestroy`. The manifest SHALL declare `android.intent.action.TTS_SERVICE` under `<queries>`.

These scenarios are checked by hand on a phone. The `build` workflow compiles and lints the code.

#### Scenario: A language with voice data
- **WHEN** `speak` is called with a valid key, a text and `en-US`, and the phone has an English voice
- **THEN** the text is read aloud and the call returns an empty string

#### Scenario: No speech engine
- **WHEN** the phone has no text-to-speech engine
- **THEN** the call returns `no-tts` and nothing is read

#### Scenario: No voice for the language
- **WHEN** the phone has no voice data for the tag
- **THEN** the call returns `no-voice` and nothing is read

#### Scenario: A wrong key
- **WHEN** `speak` is called with any key but the bridge key
- **THEN** the call returns `denied` and nothing is read

#### Scenario: A tap while reading
- **WHEN** `speak` is called while an utterance is playing
- **THEN** the playing utterance stops, the call returns `stopped` and the panel receives `window.onAndroidTts('done')`

#### Scenario: The end of an utterance
- **WHEN** an utterance ends or fails
- **THEN** the panel receives `window.onAndroidTts('done')`

#### Scenario: The panel pauses
- **WHEN** the panel pauses or the app is destroyed while a text is read
- **THEN** the reading stops

### Requirement: The settings page has a Translate card

Settings › General SHALL have a Translate card bound to `translate.enabled`, `translate.hotkey`, `translate.appId`, `translate.key` and `translate.maxChars`. Its Test button SHALL POST the APPID and key to `/api/translate/test` and show the answer in `#translateState`. On load and after a save the card SHALL read `GET /api/translate/status` and show the hotkey state.

#### Scenario: The saved settings
- **WHEN** the settings page is loaded with `translate.hotkey` `ctrl+shift+t` and `translate.appId` `123`
- **THEN** the card's inputs show `ctrl+shift+t` and `123`
- **Test** `the Translate card shows the saved settings` in `tools/ui-tests/settings-translate.spec.js`

#### Scenario: Test passes
- **WHEN** Test is clicked and `/api/translate/test` answers `{"result":"你好"}`
- **THEN** `#translateState` shows the text of `s.translate.testOk` and `你好`
- **Test** `Test shows the translation`

#### Scenario: Test fails
- **WHEN** Test is clicked and `/api/translate/test` answers `{"error":"52003 UNAUTHORIZED USER"}`
- **THEN** `#translateState` shows the text of `s.translate.testFail` and the error
- **Test** `Test shows the error`

#### Scenario: The hotkey state
- **WHEN** the settings page is loaded and `/api/translate/status` answers `{"active":true,"hotkey":"registered"}`
- **THEN** `#translateState` shows the text of `s.translate.stateOn`
- **Test** `the hotkey state is shown`

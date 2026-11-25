# clipboard-translation Specification

## Purpose
TBD - created by archiving change add-translate-hotkey. Update Purpose after archive.
## Requirements
### Requirement: The translate hotkey is registered only when the feature is ready

The agent SHALL register the translate hotkey (`translate.hotkey`, default `ctrl+alt+d`) only while `translate.enabled` is true and both `translate.appId` and `translate.key` are set. `TranslateService.Start` SHALL register it when the agent starts, and `TranslateService.SettingsSaved` SHALL register it again, or unregister it, after every settings save.

#### Scenario: Enabled and configured
- **WHEN** `translate` is enabled with hotkey `ctrl+alt+d`, an APPID and a key
- **THEN** `TranslateService.HotkeySpec(cfg)` returns `ctrl+alt+d`
- **Test** `HotkeySpec_returnsTheHotkeyWhenEnabledAndConfigured` in `agent/PhoneBox.Agent.Tests/TranslateTests.cs`

#### Scenario: No APPID
- **WHEN** `translate` is enabled without an APPID
- **THEN** `TranslateService.HotkeySpec(cfg)` returns null
- **Test** `HotkeySpec_isNullWithoutAppId`

#### Scenario: No key
- **WHEN** `translate` is enabled without a key
- **THEN** `TranslateService.HotkeySpec(cfg)` returns null
- **Test** `HotkeySpec_isNullWithoutKey`

#### Scenario: Disabled
- **WHEN** `translate.enabled` is false
- **THEN** `TranslateService.HotkeySpec(cfg)` returns null
- **Test** `HotkeySpec_isNullWhenDisabled`

#### Scenario: The agent starts
- **WHEN** `TranslateService.Start()` runs with a ready `translate` section
- **THEN** the hotkey spec is handed to `Hotkeys.Apply`
- **Test** `Start_registersTheHotkey`

#### Scenario: The settings are saved
- **WHEN** `TranslateService.SettingsSaved()` runs after the hotkey was changed to `ctrl+shift+t`
- **THEN** `Hotkeys.Apply` receives `ctrl+shift+t`
- **Test** `SettingsSaved_registersTheHotkeyAgain`

### Requirement: A hotkey press translates the clipboard text

On a hotkey press the agent SHALL read the current clipboard text, turn `\r\n` and `\r` into `\n` and trim it. When the text is not empty, is not Chinese and the feature is ready, the agent SHALL translate it with Baidu Translate and emit exactly one panel event of kind `translate` with the source text, the translation, the voice tag and `seconds` 30.

#### Scenario: An English sentence
- **WHEN** the clipboard holds `The quick brown fox` and Baidu answers `敏捷的棕色狐狸`
- **THEN** one `translate` event is emitted with `source` `The quick brown fox`, `target` `敏捷的棕色狐狸`, `lang` `en-US` and `seconds` 30
- **Test** `RunAsync_emitsTheTranslation`

#### Scenario: Windows line endings
- **WHEN** the clipboard holds `first\r\nsecond`
- **THEN** the request and the event carry `first\nsecond`
- **Test** `RunAsync_normalisesLineEndings`

#### Scenario: Nothing to translate
- **WHEN** the clipboard is empty or holds no text
- **THEN** no request is sent and no event is emitted
- **Test** `RunAsync_doesNothingForEmptyText`

#### Scenario: Chinese text
- **WHEN** the clipboard holds `今天的天气真好`
- **THEN** no request is sent and no event is emitted
- **Test** `RunAsync_skipsChineseText`

#### Scenario: Japanese with kanji
- **WHEN** the clipboard holds `私は学生です`
- **THEN** the text is translated and the event is emitted
- **Test** `RunAsync_translatesJapaneseWithKanji`

#### Scenario: Korean text
- **WHEN** the clipboard holds `안녕하세요`
- **THEN** the text is translated and the event is emitted
- **Test** `RunAsync_translatesKorean`

#### Scenario: The feature is not ready
- **WHEN** a press arrives while `translate` has no APPID or key
- **THEN** nothing is read, sent or emitted
- **Test** `RunAsync_doesNothingWhenNotConfigured`

#### Scenario: The clipboard is busy
- **WHEN** reading the clipboard throws `InvalidOperationException` or `TimeoutException`
- **THEN** no request is sent, no event is emitted and nothing is thrown
- **Test** `RunAsync_endsQuietlyWhenTheClipboardIsBusy`

#### Scenario: Text longer than maxChars
- **WHEN** the clipboard holds 1500 characters and `maxChars` is 1000
- **THEN** the request and the event carry the first 999 characters followed by `…`
- **Test** `RunAsync_cutsLongText`

### Requirement: Baidu Translate receives a signed request

The agent SHALL POST a form to `https://fanyi-api.baidu.com/api/trans/vip/translate` with the fields `q`, `from` `auto`, `to` `zh`, `appid`, `salt` and `sign`. `sign` SHALL be the lowercase hexadecimal MD5 of `appid + q + salt + key` in UTF-8 (`BaiduTranslate.Sign`). The translation SHALL be the `dst` of every result, joined with `\n`.

#### Scenario: The signature
- **WHEN** `BaiduTranslate.Sign("1", "hello", "2", "s")` is called
- **THEN** it returns the MD5 of `1hello2s` as 32 lowercase hexadecimal characters
- **Test** `Sign_isTheLowercaseMd5OfAppidTextSaltAndKey`

#### Scenario: The request
- **WHEN** `BaiduTranslate.TranslateAsync("hello", cfg, ct)` is called with APPID `1`, key `s` and salt `2`
- **THEN** the request is a POST to `BaiduTranslate.Endpoint` whose form has `q=hello`, `from=auto`, `to=zh`, `appid=1`, `salt=2` and `sign` `BaiduTranslate.Sign("1", "hello", "2", "s")`
- **Test** `TranslateAsync_postsTheSignedForm`

#### Scenario: Several paragraphs
- **WHEN** Baidu answers two results, `第一段` and `第二段`
- **THEN** `BaiduTranslation.Target` is `第一段\n第二段`
- **Test** `TranslateAsync_joinsParagraphsWithNewlines`

### Requirement: A failure reaches the card as an error event

When Baidu answers with an `error_code`, the agent SHALL emit a `translate` event with `error` `provider` and `detail` `<error_code> <error_msg>`. When the request times out, cannot connect, gets a non-success status or an unreadable body, the event SHALL have `error` `network`. Both SHALL have `seconds` 10. `TranslateService.RunAsync` SHALL not throw.

#### Scenario: Baidu reports an error
- **WHEN** Baidu answers `{"error_code":"52003","error_msg":"UNAUTHORIZED USER"}`
- **THEN** the event has `error` `provider`, `detail` `52003 UNAUTHORIZED USER` and `seconds` 10
- **Test** `RunAsync_emitsAProviderError`

#### Scenario: Baidu cannot be reached
- **WHEN** the request fails with a timeout or a connection error
- **THEN** the event has `error` `network` and `seconds` 10
- **Test** `RunAsync_emitsANetworkError`

#### Scenario: Any other failure
- **WHEN** something else throws during a run
- **THEN** `RunAsync` returns without throwing and emits nothing or an error event
- **Test** `RunAsync_neverThrows`

### Requirement: Requests to Baidu go out one at a time

The agent SHALL keep at most one request to Baidu Translate in flight, and two requests SHALL be at least `TranslateService.MinGap` (one second) apart. A hotkey press that arrives while a translation is in flight SHALL cancel it, so only the latest text is shown.

#### Scenario: A second press during a translation
- **WHEN** a translation of text A is waiting for Baidu and a new press reads text B
- **THEN** the request for A is cancelled, no event is emitted for A and the only event carries B
- **Test** `RunAsync_newPressSupersedesTheOneInFlight`

#### Scenario: Two translations in a row
- **WHEN** two translations are started right after each other
- **THEN** the second request reaches the handler at least `MinGap` after the first
- **Test** `RunAsync_keepsRequestsASecondApart`

### Requirement: The source language becomes a voice tag

The agent SHALL turn the language that Baidu reports in `from` into a BCP 47 voice tag with `BaiduTranslate.VoiceTag`: `en` to `en-US`, `jp` to `ja`, `kor` to `ko`, and the other languages of its table likewise. A language outside the table, or none, SHALL give null.

#### Scenario: Known languages
- **WHEN** `BaiduTranslate.VoiceTag` is called with `en`, `jp` and `kor`
- **THEN** it returns `en-US`, `ja` and `ko`
- **Test** `VoiceTag_mapsTheLanguagesBaiduReports`

#### Scenario: Unknown or missing language
- **WHEN** `BaiduTranslate.VoiceTag` is called with `xxx` and with null
- **THEN** it returns null both times
- **Test** `VoiceTag_mapsTheLanguagesBaiduReports`

### Requirement: The text and the key never reach a log

The agent SHALL log one line per run with the length of the text, the duration and the outcome. It SHALL never log the clipboard text, the translation or the key, and on a failure only the type name of the exception.

#### Scenario: A successful run
- **WHEN** `Hello secret word` is translated with key `top-secret`
- **THEN** no log message or state contains `secret word` or `top-secret`
- **Test** `RunAsync_neverLogsTheTextOrTheKey`

### Requirement: The hotkey spec is validated

`Hotkeys.Parse` SHALL accept a spec made of any of `ctrl`, `alt`, `shift`, `win` and exactly one other key, and return the Windows modifier bits and virtual-key code. It SHALL return null for a key without a modifier, a modifier without a key, or an unknown key. `Hotkeys.Apply` SHALL return `invalid` for such a spec and register nothing.

#### Scenario: Ctrl+Alt+D
- **WHEN** `Hotkeys.Parse("ctrl+alt+d")` is called
- **THEN** it returns the modifier bits `0x0002 | 0x0001` and the virtual-key code `0x44`
- **Test** `Parse_acceptsCtrlAltD`

#### Scenario: A key without a modifier
- **WHEN** `Hotkeys.Parse("d")` is called
- **THEN** it returns null
- **Test** `Parse_rejectsAKeyWithoutAModifier`

#### Scenario: An incomplete or unknown spec
- **WHEN** `Hotkeys.Parse` is called with `ctrl+`, `ctrl+alt` and `bananas`
- **THEN** it returns null each time
- **Test** `Parse_rejectsIncompleteOrUnknownKeys`

#### Scenario: Applying an invalid spec
- **WHEN** `Hotkeys.Apply("ctrl+")` is called
- **THEN** it returns `invalid` and `Hotkeys.State` is `invalid`
- **Test** `Apply_reportsAnInvalidSpec`

### Requirement: The settings page can test the feature

`POST /api/translate/test` SHALL translate `hello` with the APPID and key it is given, falling back to the saved ones, and answer `{ result }` or `{ error }`. `GET /api/translate/status` SHALL answer `active` (the feature is ready) and `hotkey` (one of `registered`, `taken`, `invalid`, `off`). Both SHALL exist on the local port only.

#### Scenario: The credentials work
- **WHEN** `TranslateService.TestAsync("1", "s", ct)` is called and Baidu answers `你好`
- **THEN** the result is `你好` and there is no error
- **Test** `TestAsync_returnsTheTranslation`

#### Scenario: The credentials are wrong
- **WHEN** Baidu answers `{"error_code":"52003","error_msg":"UNAUTHORIZED USER"}`
- **THEN** the error is `52003 UNAUTHORIZED USER`
- **Test** `TestAsync_reportsAnError`

#### Scenario: The status
- **WHEN** `TranslateService.Status()` is called on a ready configuration with the hotkey registered
- **THEN** it answers `active` true and `hotkey` `registered`
- **Test** `Status_reportsActiveAndTheHotkeyState`

### Requirement: The config keeps the provider credentials private

`AgentConfig.Translate` (JSON `translate`) SHALL hold `enabled`, `hotkey`, `appId`, `key` and `maxChars`. A fresh config SHALL have the feature off, hotkey `ctrl+alt+d` and `maxChars` 1000. Normalisation SHALL restore the default hotkey when it is blank, turn a blank APPID or key into null, and keep `maxChars` between 100 and 6000. The section SHALL NOT be part of `AgentConfig.ToPublic()`, so it never reaches the phone. `ConfigStore.PreserveTranslateKey` SHALL put the stored key back when a settings save sends none. The three `config.default*.json` files SHALL contain the section with `enabled` false.

#### Scenario: A fresh config
- **WHEN** a new `AgentConfig` is normalised
- **THEN** `Translate.Enabled` is false, `Translate.Hotkey` is `ctrl+alt+d`, `Translate.AppId` and `Translate.Key` are null and `Translate.MaxChars` is 1000
- **Test** `Translate_hasSafeDefaults` in `agent/PhoneBox.Agent.Tests/TranslateConfigTests.cs`

#### Scenario: Values are cleaned
- **WHEN** a config with hotkey `  `, APPID ` 123 `, key ` ` and `maxChars` 50000 is normalised
- **THEN** the hotkey is `ctrl+alt+d`, the APPID is `123`, the key is null and `maxChars` is 6000
- **Test** `Normalize_translate_cleansTheValues`

#### Scenario: The public config
- **WHEN** `AgentConfig.ToPublic()` is serialised
- **THEN** the JSON has no `translate` property
- **Test** `ToPublic_keepsTranslateOut`

#### Scenario: A save without the key
- **WHEN** the proposed config has no `translate.key` and a key is stored
- **THEN** `ConfigStore.PreserveTranslateKey` returns true and the proposed config carries the stored key
- **Test** `PreserveTranslateKey_keepsTheStoredKey`

#### Scenario: A save with a key, or with none stored
- **WHEN** the proposed config carries a key, or no key is stored
- **THEN** `ConfigStore.PreserveTranslateKey` returns false and changes nothing
- **Test** `PreserveTranslateKey_leavesOtherSavesAlone`

#### Scenario: The settings are read by anything but the settings page
- **WHEN** the settings JSON is passed to `ConfigStore.RemoveSecrets`
- **THEN** `steam.apiKey` and `translate.key` are gone and every other property stays
- **Test** `RemoveSecrets_hidesTheSteamAndTranslateKeys`

#### Scenario: The default config files
- **WHEN** the three `config.default*.json` files are read
- **THEN** each has a `translate` section with `enabled` false
- **Test** `DefaultConfigFiles_carryTheTranslateSection`

### Requirement: The outbound connection is documented

`PRIVACY.md` SHALL list `fanyi-api.baidu.com` with what is sent and how to turn the feature off.

#### Scenario: The privacy note exists
- **WHEN** `PRIVACY.md` is read
- **THEN** one row names `fanyi-api.baidu.com`, the clipboard text and the switch that turns the feature off
- **Checked by** the task of `tasks.md` that reads the row, together with `python tools/check-release.py`


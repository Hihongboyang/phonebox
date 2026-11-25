## 1. Agent skeleton (types only)

Derived from `contracts.md` only. No logic, no default values: a test that passes against the skeleton has never run red.

- [x] 1.1 Add the types and member stubs of `contracts.md`: `TranslateConfig` and `AgentConfig.Translate` (`Config.cs`), `Hotkeys` (`Hotkeys.cs`), `TranslateCardEvent`, `TranslateFailure`, `TranslateService` (`TranslateService.cs`), `BaiduTranslation`, `BaiduTranslate` (`BaiduTranslate.cs`). Auto-properties without initialisers; every method throws `NotImplementedException`. — Verified 2026-11-24: `dotnet build agent/PhoneBox.Agent.Tests` and `dotnet build agent/PhoneBox.Agent -f net10.0-windows10.0.19041.0` succeed; `dotnet test agent/PhoneBox.Agent.Tests` passes 2447 of 2447, as before the change.

## 2. Config and secrets

- [x] 2.1 Add `agent/PhoneBox.Agent.Tests/TranslateConfigTests.cs` with `Translate_hasSafeDefaults`, `Normalize_translate_cleansTheValues`, `PreserveTranslateKey_keepsTheStoredKey`, `PreserveTranslateKey_leavesOtherSavesAlone`, `RemoveSecrets_hidesTheSteamAndTranslateKeys`, `DefaultConfigFiles_carryTheTranslateSection`, and the guard `ToPublic_keepsTranslateOut`. — Verified RED 2026-11-24: `dotnet test agent/PhoneBox.Agent.Tests --filter "FullyQualifiedName~TranslateConfigTests"` fails 8 of 9 on the missing defaults, normalisation, preservation, redaction and default files, not on a compile error (the guard alone passed).
- [x] 2.2 Give `TranslateConfig` its defaults, normalise the section in `ConfigStore.Normalize`, implement `ConfigStore.PreserveTranslateKey` and `ConfigStore.RemoveSecrets` (and use the latter in the settings GET in place of the inline Steam line), and add the `translate` section to `config.default.json`, `config.default.linux.json` and `config.default.macos.json`. — Verified 2026-11-24: the filter of 2.1 is GREEN (9 of 9; with `ConfigTests`, 74 of 74).

## 3. Hotkey spec

- [x] 3.1 Add `agent/PhoneBox.Agent.Tests/HotkeysTests.cs` with `Parse_acceptsCtrlAltD`, `Parse_rejectsAKeyWithoutAModifier`, `Parse_rejectsIncompleteOrUnknownKeys`, `Apply_reportsAnInvalidSpec`. — Verified RED 2026-11-24: `dotnet test agent/PhoneBox.Agent.Tests --filter "FullyQualifiedName~HotkeysTests"` fails 9 of 9 on the missing behaviour.
- [x] 3.2 Implement `Hotkeys.Parse` with `KeySender.Parse` and `KeySender.WinVk`, the validation part of `Hotkeys.Apply` and `Hotkeys.State`, outside `#if WINDOWS`. — Verified 2026-11-24: the filter of 3.1 is GREEN (9 of 9).
- [x] 3.3 Write the Windows part of `Hotkeys` under `#if WINDOWS`: the STA thread, the message-only window, `RegisterHotKey` and `UnregisterHotKey`, `Pressed`, and the states `registered`, `taken` and `off`. The test project does not compile this part. — Verified 2026-11-24: `dotnet build agent/PhoneBox.Agent -f net10.0-windows10.0.19041.0` succeeds (0 errors); the run in 9.1 shows the key working.

## 4. Baidu Translate

- [x] 4.1 Add `agent/PhoneBox.Agent.Tests/BaiduTranslateTests.cs` with `Sign_isTheLowercaseMd5OfAppidTextSaltAndKey`, `TranslateAsync_postsTheSignedForm`, `TranslateAsync_joinsParagraphsWithNewlines`, `VoiceTag_mapsTheLanguagesBaiduReports`, `TranslateAsync_reportsAProviderError`, `TranslateAsync_reportsNetworkFailures`, using a fake `HttpMessageHandler`. — Verified RED 2026-11-24: `dotnet test agent/PhoneBox.Agent.Tests --filter "FullyQualifiedName~BaiduTranslateTests"` fails 6 of 6 on the missing behaviour.
- [x] 4.2 Implement `BaiduTranslate.Sign`, `TranslateAsync` (the form, the POST with an 8 s timeout, the parsing, the error mapping) and `VoiceTag`. — Verified 2026-11-24: the filter of 4.1 is GREEN (6 of 6). The failing run in between found two real issues: a non-object answer (200 with `[]`) threw instead of mapping to `network`, fixed with a `ValueKind` guard; and the test read the form after the provider disposed it, fixed by snapshotting the form in the fake handler.

## 5. One hotkey press

- [x] 5.1 Add `agent/PhoneBox.Agent.Tests/TranslateTests.cs` with `RunAsync_emitsTheTranslation`, `RunAsync_normalisesLineEndings`, `RunAsync_doesNothingForEmptyText`, `RunAsync_skipsChineseText`, `RunAsync_translatesJapaneseWithKanji`, `RunAsync_translatesKorean`, `RunAsync_doesNothingWhenNotConfigured`, `RunAsync_endsQuietlyWhenTheClipboardIsBusy`, `RunAsync_cutsLongText`, `RunAsync_emitsAProviderError`, `RunAsync_emitsANetworkError`, `RunAsync_neverThrows`, with `Clipboard.TestBackend` as the clipboard and a fake handler as Baidu. — Verified RED 2026-11-24: `dotnet test agent/PhoneBox.Agent.Tests --filter "FullyQualifiedName~TranslateTests"` fails 12 of 12 on the missing behaviour.
- [x] 5.2 Implement `TranslateService.Active`, `IsChinese`, `Normalize`, `Cut`, the two event builders and `RunAsync` without the request gate and without logging. — Verified 2026-11-24: the filter of 5.1 is GREEN. The failing run in between found the cut was one character too long (the ellipsis now counts towards `maxChars`), and the wire JSON is indented (`WriteOpts`), which the test now expects.

## 6. One request at a time

- [x] 6.1 Add `RunAsync_newPressSupersedesTheOneInFlight` and `RunAsync_keepsRequestsASecondApart` to `TranslateTests.cs`. — Verified RED 2026-11-24: they fail because two requests run at once and the first text's event still arrives.
- [x] 6.2 Add the request gate, the cancellation of the run in flight and `TranslateService.MinGap` to `RunAsync`. — Verified 2026-11-24: the filter of 5.1 is GREEN, the two tests included (20 of 20).

## 7. Logging without the text

- [x] 7.1 Add `RunAsync_neverLogsTheTextOrTheKey` to `TranslateTests.cs`: one information line per run that holds the length and the outcome, and no log state that holds the text or the key. — Verified RED 2026-11-24: it fails because no line is written.
- [x] 7.2 Write the log lines of `RunAsync` and `TestAsync`: length, duration, outcome; exception type names only. — Verified 2026-11-24: the filter of 5.1 is GREEN, the test included.

## 8. Start, settings save, status and test

- [x] 8.1 Add `HotkeySpec_returnsTheHotkeyWhenEnabledAndConfigured`, `HotkeySpec_isNullWithoutAppId`, `HotkeySpec_isNullWithoutKey`, `HotkeySpec_isNullWhenDisabled`, `Start_registersTheHotkey`, `SettingsSaved_registersTheHotkeyAgain`, `Status_reportsActiveAndTheHotkeyState`, `TestAsync_returnsTheTranslation`, `TestAsync_reportsAnError` to `TranslateTests.cs`, using `TranslateService.TestApply`. — Verified RED 2026-11-24: they fail 9 of 9 on the missing behaviour (the stubs had been lost by an edit of mine and were restored first).
- [x] 8.2 Implement `TranslateService.HotkeySpec`, `Start`, `SettingsSaved`, `Status`, `TestAsync` and `TestApply`. — Verified 2026-11-24: the filter of 5.1 is GREEN, the nine tests included. The failing run in between: `TestAsync` prefixed the provider's message with the code, and a same-stamp config rewrite could hide a settings change (the test now moves the file's stamp).
- [x] 8.3 Wire the agent in `Program.cs`: register `BaiduTranslate`, `Hotkeys` (singleton and hosted service) and `TranslateService`; call `Start` when the agent has started; call `SettingsSaved` and `ConfigStore.PreserveTranslateKey` in the settings PUT; map `POST /api/translate/test` and `GET /api/translate/status`. Add `translate` to the list of kinds in the comment above `PanelEvent`. — Verified 2026-11-24: `dotnet build agent/PhoneBox.Agent -f net10.0-windows10.0.19041.0` succeeds; `dotnet test agent/PhoneBox.Agent.Tests` passes 2495 of 2495; 9.1 shows the two endpoints answering.

## 9. The agent on this machine

- [x] 9.1 Run the Windows agent from source with an isolated config and port (`--config`, `--port`), with `translate` enabled and a placeholder APPID and key. Send Ctrl+Alt+D with the clipboard holding an English sentence. — Verified 2026-11-24 (pid 39544, port 18765, `deviceSerial` set to a serial no phone has): the log holds `translate: 43 chars, 272 ms, provider 52003 UNAUTHORIZED USER` and no clipboard text; `GET /api/translate/status` answers `{"active":true,"hotkey":"registered"}`; a curl `GET /api/settings` shows the `translate` section without `key`; with the switch off (file edited behind the agent) a press writes no log line. The request really reached Baidu (52003 is Baidu's answer to the placeholder credentials).

## 10. The Translate card on the panel

- [x] 10.1 Add `tools/ui-tests/translate.spec.js` with `shows the source text and the translation`, `hides by itself after its seconds`, `a new event replaces the card and restarts its timer`, `Back hides the card`, `the panel is awake while the card is shown`, `shows markup as text`, `a tap asks the phone to read the source text`, `no button without a voice tag`, `no button on an error`, `no button outside the phone app`, `a phone without a speech engine gets a toast`, `a phone without a voice for the language gets a toast`, `the button shows that the phone is reading, until the phone reports the end`, `an error from the provider is shown with its detail`, `a network error is shown`, using `window.PhoneBoxDemo.emit` and a stub `window.Android`. — Verified RED 2026-11-24: `npx playwright test translate.spec.js` fails 16 of 16 (no `#translate`).
- [x] 10.2 Add `#translate`, `#trSource`, `#trTarget` and `#trSpeak` to `index.html`, the card's styles to `style.css`, and `showTranslate`, `hideTranslate`, the `translate` branch of `handleEvent`, Back, `speak` in the bridge list and `window.onAndroidTts` to `main.js`; add the five English texts of `panel.translate.*` to `i18n.js`. — Verified 2026-11-24: the spec of 10.1 is GREEN. The failing run in between: the 30 s timer test advanced the clock only 30.75 s from the emit, while the event reaches the panel with the next 1 s poll — the test now advances far enough past the deadline.
- [x] 10.3 Look at the card: start `python tools/demo-server.py`, emit a `translate` event for a short text, a long text and an error, and open the panel at 780 × 360 in a browser tab. — Verified 2026-11-24 (Chromium tab, three screenshots taken): the card sits at the horizontal centre (390 of 780), 72 px above the bottom, z-index 16, at most 60 vh high (210 of 216 px for the long text; at this length it fits, `overflow-y: auto` is in place); the Read-aloud button sits in the source row with the `volume-2` icon and stays hidden in a plain browser, as it should; the error card has class `err`, the red border and the red text.

## 11. The Translate card on the settings page

- [x] 11.1 Add `tools/ui-tests/settings-translate.spec.js` with `the Translate card shows the saved settings`, `Test shows the translation`, `Test shows the error`, `the hotkey state is shown`. — Verified RED 2026-11-24: `npx playwright test settings-translate.spec.js` fails 4 of 4 (the card is missing).
- [x] 11.2 Add the Translate card to `settings.html` with its Test and status script, the fourteen English texts of `s.translate.*` to `i18n.js`, and the demo answers for `/api/translate/test` and `/api/translate/status` in `tools/pages/demo.js`. — Verified 2026-11-24: the spec of 11.1 is GREEN (4 of 4). The failing run in between: the test awaited `loadTranslateState()`, whose request the test itself holds — the call is now made without awaiting it.

## 12. Texts in every language

- [x] 12.1 Add `the card's texts are translated` to `translate.spec.js`: with the panel in Chinese, an error card shows the Chinese text of `panel.translate.provider`. — Verified RED 2026-11-24: it fails because the English is shown.
- [x] 12.2 Add the translations of the nineteen keys to every language: the `locale-*.js` files and the languages kept inline in `i18n.js`. Keep the placeholders. — Verified 2026-11-24 (20 keys × 22 languages: 12 inline blocks of `i18n.js`, 9 `locale-*.js` files, and `pt-BR` in `locale-existing.js`; tr lives inline only): `npx playwright test translate.spec.js settings-translate.spec.js i18n.spec.js i18n-coverage.spec.js` passes 53 of 53, and `python tools/check-release.py` passes (placeholder check included). The first RED run failed because the panel language comes from the config's `display.language`, not from the browser: the test now sets it there.

## 13. Phone app

- [ ] 13.1 Add `Speaker.kt`; add the lazy `speaker`, its calls in `onCreate`, `onPause` and `onDestroy`, and `Bridge.speak` to `MainActivity.kt`; add `android.intent.action.TTS_SERVICE` under `<queries>` in `AndroidManifest.xml`. Nothing on this machine compiles it. — Verify: the Android job of the `build` workflow is green on the pushed branch (16.1), and the seven scenarios of the spec pass by hand (16.2).

## 14. Docs

- [x] 14.1 Add the Baidu Translate row to `PRIVACY.md` (host, what is sent, how to turn it off) and a short section on the translate hotkey to `docs/GUIDE.md`. — Verified 2026-11-24: `python tools/check-release.py` passes, and the row names `fanyi-api.baidu.com`, the clipboard text and the switch.

## 15. Whole-suite verification

- [x] 15.1 Run the agent suite. — Verified 2026-11-25: `dotnet test agent/PhoneBox.Agent.Tests` passes 2495 of 2495. An earlier run failed 1 of 2495 only because it ran in parallel with the browser suite; rerun green.
- [x] 15.2 Run the browser suite. — Verified 2026-11-25: `npx playwright test` passes 555 (2 failures and 4 flaky while the agent suite ran in parallel; all four affected specs pass 67 of 68 in isolation — rerun 2026-11-25, the 1 flaky passes on retry).
- [x] 15.3 Run the release checks. — Verified 2026-11-24: `python tools/check-release.py` passes ("23 interface variants; Android, Windows, and README language lists and native placeholders").

## 16. APK and phone (owner: the fork)

- [ ] 16.1 Fork the repository on GitHub, add the fork as a remote, push the branch, run the `build` workflow on it, download `phonebox-apk` and install it on the phone. The first install replaces the official app, so pair the phone again. — Verify: the workflow's Android job is green (`lintDebug`, the unit tests), and the app opens on the phone.
- [ ] 16.2 On the phone, with a paired agent: read a sentence in a language the phone has a voice for; try a language without voice data; tap while it reads; leave the panel while it reads. — Verify: the seven scenarios of "The phone app reads with its own text-to-speech engine" in `specs/translate-card/spec.md`, one by one.

## 17. Live integration (owner: the APPID and key)

- [ ] 17.1 Enter the APPID and key in Settings › General › Translate and click Test. — Verify: `Translation works` followed by the Chinese for "hello".
- [ ] 17.2 Copy an English sentence and press Ctrl+Alt+D. — Verify: the card appears within about two seconds and has the Read-aloud button. If it has no button, Baidu's `from` differs from the table in `contracts.md`: stop here and record a decision before changing the table.
- [ ] 17.3 Copy a Japanese sentence, a Korean sentence and a Chinese sentence, pressing Ctrl+Alt+D after each. — Verify: a card for the first two, nothing for the Chinese one; nothing about the text in the log.
- [ ] 17.4 Install for daily use with `scripts/install.ps1`. — Verify: the tray icon is back and the hotkey still works.

Archive with `openspec archive add-translate-hotkey` once 16 and 17 are checked: they are the only tasks that need the fork and the APPID.

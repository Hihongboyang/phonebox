// The Translate card: a `translate` panel event shows the source text, one Read-aloud button and the Chinese
// translation, and the card hides by itself. Against the web demo with a mocked clock and a stub of the app's bridge.
// Spec: openspec/changes/add-translate-hotkey/specs/translate-card/spec.md
const { test, expect } = require('@playwright/test');

// fastForward fires each due timer at most once per call, and the order of the timers matters here (the panel's 1 s
// poll against the card's own timer), so time is stepped through in quarters of a second
async function step(page, ms) { for (let t = 0; t < ms; t += 250) await page.clock.fastForward(250); }

/**
 * Opens the panel on one page, with the clock paused. `speak` is what the stub of the app's bridge answers to
 * speak(), or null to leave the method out, which is a panel without the bridge (a web browser, the live preview).
 * `display` extends Settings › Display.
 */
async function show(page, { speak = '', display = {} } = {}) {
  await page.clock.install({ time: new Date('2026-01-01T12:00:00Z') });
  await page.addInitScript((speak) => {
    window.__phoneboxKey = 'k';
    window.__speaks = [];
    window.Android = { buzz: () => {}, haptic: () => {} };
    if (speak !== null) window.Android.speak = (key, text, tag) => { window.__speaks.push([key, text, tag]); return speak; };
  }, speak);
  await page.goto('/panel/style.css');
  await page.evaluate((display) => {
    localStorage.clear();
    localStorage.setItem('pb-demo-config', JSON.stringify({
      night: { enabled: false }, rules: [], shortcuts: [], display: { idleReturnSec: 0, ...display },
      pages: [{ id: 'one', title: 'One', widgets: [{ id: 'n1', type: 'note', x: 0, y: 0, w: 12, h: 6, options: { text: 'Page one' } }] }],
    }));
  }, display);
  await page.goto('/panel/');
  await expect(page.locator('.w-note').first()).toBeVisible();
  // Until the stored config arrives the panel runs on its defaults, whose night screen covers 00:30-07:30
  await expect(page.locator('body')).not.toHaveClass(/night/);
  await page.clock.pauseAt(new Date('2026-01-01T12:00:10Z'));
}

const card = (page) => page.locator('#translate');
const speakButton = (page) => page.locator('#trSpeak');
const toast = (page) => page.locator('#toast');
const emit = (page, data) => page.evaluate((d) => window.PhoneBoxDemo.emit('translate', d), data);
const text = (page, key) => page.evaluate((k) => window.PhoneBoxI18n.t(k), key);
const back = (page) => page.evaluate(() => window.onAndroidBack());
const DONE = { source: 'hello world', target: '你好，世界', lang: 'en-US', seconds: 30 };
// a queued event reaches the panel with its next 1 s poll
const shown = (page, data) => emit(page, data).then(() => step(page, 1250));

test('shows the source text and the translation', async ({ page }) => {
  await show(page);
  await shown(page, DONE);
  await expect(card(page)).toBeVisible();
  await expect(page.locator('#trSource')).toHaveText('hello world');
  await expect(page.locator('#trTarget')).toHaveText('你好，世界');
});

test('hides by itself after its seconds', async ({ page }) => {
  await show(page);
  await shown(page, { ...DONE, seconds: 1 });
  await expect(card(page)).toBeVisible();
  await step(page, 1500);
  await expect(card(page)).toBeHidden();
  // and the next event brings it back
  await shown(page, DONE);
  await expect(card(page)).toBeVisible();
});

test('hides after 30 seconds when the event names no time', async ({ page }) => {
  await show(page);
  const { seconds, ...without } = DONE;
  await shown(page, without);
  await page.clock.fastForward(20_000);
  await step(page, 7500);
  await expect(card(page)).toBeVisible();
  await step(page, 4000); // 32.75 s in all: the event reaches the panel with the next 1 s poll, so its 30 s end after it
  await expect(card(page)).toBeHidden();
});

test('a new event replaces the card and restarts its timer', async ({ page }) => {
  await show(page);
  await shown(page, { ...DONE, source: 'first', seconds: 1 });
  await emit(page, { ...DONE, source: 'second', target: '第二句', seconds: 1 });
  await step(page, 750); // the second event arrives with the next poll, as the first card's second runs out
  await expect(card(page)).toBeVisible();
  await expect(page.locator('#trSource')).toHaveText('second');
  await expect(page.locator('#trTarget')).toHaveText('第二句');
  await step(page, 500); // the first card would be gone by now; the second one is not
  await expect(card(page)).toBeVisible();
  await step(page, 1500);
  await expect(card(page)).toBeHidden();
});

test('Back hides the card', async ({ page }) => {
  await show(page);
  await shown(page, DONE);
  await expect(card(page)).toBeVisible();
  await back(page);
  await expect(card(page)).toBeHidden();
});

test('the panel is awake while the card is shown', async ({ page }) => {
  await show(page, { display: { idleDimMin: 1 } });
  await page.clock.fastForward(61_000);
  await step(page, 1500);
  await expect(page.locator('body')).toHaveClass(/dimmed/);
  await shown(page, DONE);
  await expect(page.locator('body')).not.toHaveClass(/dimmed/);
  await step(page, 20_000); // the idle time started again with the card
  await expect(page.locator('body')).not.toHaveClass(/dimmed/);
  await expect(card(page)).toBeVisible();
});

test('shows markup as text', async ({ page }) => {
  await show(page);
  await shown(page, { ...DONE, source: '<b>bold</b> & more', target: '<i>斜体</i>' });
  await expect(page.locator('#trSource')).toHaveText('<b>bold</b> & more');
  await expect(page.locator('#trTarget')).toHaveText('<i>斜体</i>');
  await expect(page.locator('#trSource b, #trTarget i')).toHaveCount(0);
});

test('a tap asks the phone to read the source text', async ({ page }) => {
  await show(page);
  await shown(page, DONE);
  await speakButton(page).click();
  expect(await page.evaluate(() => window.__speaks)).toEqual([['k', 'hello world', 'en-US']]); // never the translation
});

test('no button without a voice tag', async ({ page }) => {
  await show(page);
  await shown(page, { ...DONE, lang: null });
  await expect(card(page)).toBeVisible();
  await expect(page.locator('#trTarget')).toHaveText('你好，世界');
  await expect(speakButton(page)).toBeHidden();
});

test('no button on an error', async ({ page }) => {
  await show(page);
  await shown(page, { error: 'network', seconds: 10 });
  await expect(card(page)).toBeVisible();
  await expect(speakButton(page)).toBeHidden();
});

test('no button outside the phone app', async ({ page }) => {
  await show(page, { speak: null });
  await shown(page, DONE);
  await expect(card(page)).toBeVisible();
  await expect(page.locator('#trTarget')).toHaveText('你好，世界');
  await expect(speakButton(page)).toBeHidden();
});

test('a phone without a speech engine gets a toast', async ({ page }) => {
  await show(page, { speak: 'no-tts' });
  await shown(page, DONE);
  await speakButton(page).click();
  await expect(toast(page)).toHaveClass(/\bon\b/);
  await expect(toast(page)).toContainText(await text(page, 'panel.translate.noTts'));
});

test('a phone without a voice for the language gets a toast', async ({ page }) => {
  await show(page, { speak: 'no-voice' });
  await shown(page, DONE);
  await speakButton(page).click();
  await expect(toast(page)).toHaveClass(/\bon\b/);
  await expect(toast(page)).toContainText(await text(page, 'panel.translate.noVoice'));
});

test('the button shows that the phone is reading, until the phone reports the end', async ({ page }) => {
  await show(page);
  await shown(page, DONE);
  await speakButton(page).click();
  await expect(speakButton(page)).toHaveClass(/\bon\b/);
  await page.evaluate(() => window.onAndroidTts('done'));
  await expect(speakButton(page)).not.toHaveClass(/\bon\b/);
});

test('an error from the provider is shown with its detail', async ({ page }) => {
  await show(page);
  await shown(page, { error: 'provider', detail: '52003 UNAUTHORIZED USER', seconds: 1 });
  await expect(card(page)).toHaveClass(/\berr\b/);
  await expect(page.locator('#trTarget')).toContainText(await text(page, 'panel.translate.provider'));
  await expect(page.locator('#trTarget')).toContainText('52003 UNAUTHORIZED USER');
  await step(page, 1500);
  await expect(card(page)).toBeHidden();
});

test('a network error is shown', async ({ page }) => {
  await show(page);
  await shown(page, { error: 'network', seconds: 10 });
  await expect(card(page)).toHaveClass(/\berr\b/);
  await expect(page.locator('#trTarget')).toHaveText(await text(page, 'panel.translate.network'));
});

test('the card\'s texts are translated', async ({ page }) => {
  // the panel language comes from the config (Settings › Display), not from the browser
  await show(page, { display: { language: 'zh' } });
  await shown(page, { error: 'provider', detail: '52003', seconds: 10 });
  await expect(page.locator('#trTarget')).toContainText('百度翻译返回了错误');
  await expect(page.locator('#trTarget')).toContainText('52003');
});

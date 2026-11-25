// Settings › General › Translate: the card shows the saved settings, Test asks for a translation of "hello" with the
// APPID and key in the form, and the hotkey's state is shown. Against the web demo; the two /api/translate/ routes are
// answered by a wrapper around the page's fetch, so nothing here needs an agent or a Baidu account.
// Spec: openspec/changes/add-translate-hotkey/specs/translate-card/spec.md
const { test, expect } = require('@playwright/test');

const SAVED = { enabled: true, hotkey: 'ctrl+shift+t', appId: '123', key: 'abc', maxChars: 2500 };
const state = (page) => page.locator('#translateState');
const text = (page, key, vars) => page.evaluate(([k, v]) => window.PhoneBoxI18n.t(k, v || {}), [key, vars]);

// every /api/translate/ request waits until the test answers it (in the order they were made) and is recorded
async function holdTranslateRequests(page) {
  await page.evaluate(() => {
    const original = window.fetch;
    window.__held = [];
    window.__translateRequests = [];
    window.fetch = (url, options = {}) => {
      if (!String(url).startsWith('/api/translate/')) return original(url, options);
      window.__translateRequests.push({ url: String(url), method: (options.method || 'GET').toUpperCase(), body: options.body ? JSON.parse(options.body) : null });
      return new Promise((resolve) => window.__held.push((body, status = 200) => resolve(body instanceof Response ? body : Response.json(body, { status }))));
    };
  });
}
const answer = (page, index, body, status) => page.evaluate(([i, body, status]) => window.__held[i](body, status), [index, body, status]);
const requests = (page) => page.evaluate(() => window.__translateRequests);

async function open(page, saved) {
  // the demo keeps its settings in the browser, so the page loads this translate section
  await page.addInitScript((saved) => localStorage.setItem('pb-demo-config', JSON.stringify({ translate: saved })), saved);
  await page.goto('/settings/#general');
  // the page's own first load sets the saved baseline last; nothing may be changed before it ends
  await page.waitForFunction(() => typeof saved === 'string' && saved !== '');
}

test('the Translate card shows the saved settings', async ({ page }) => {
  await open(page, SAVED);
  await expect(page.locator('[data-bind="translate.enabled"]')).toBeChecked();
  await expect(page.locator('#translateHotkeyInput')).toHaveValue('ctrl+shift+t');
  await expect(page.locator('#translateAppIdInput')).toHaveValue('123');
  await expect(page.locator('#translateKeyInput')).toHaveValue('abc');
  await expect(page.locator('#translateMaxCharsInput')).toHaveValue('2500');
  // the key is a password field
  expect(await page.locator('#translateKeyInput').getAttribute('type')).toBe('password');
});

test('Test shows the translation', async ({ page }) => {
  await open(page, SAVED);
  await holdTranslateRequests(page);
  await page.locator('#translateTest').click();
  await expect(state(page)).toHaveText(await text(page, 's.translate.testing'));
  // the credentials in the form, which may differ from the saved ones
  expect((await requests(page))[0]).toEqual({ url: '/api/translate/test', method: 'POST', body: { appId: '123', key: 'abc' } });
  await answer(page, 0, { result: '你好' });
  await expect(state(page)).toHaveText(await text(page, 's.translate.testOk', { result: '你好' }));
});

test('Test shows the error', async ({ page }) => {
  await open(page, SAVED);
  await holdTranslateRequests(page);
  await page.locator('#translateTest').click();
  await answer(page, 0, { error: '52003 UNAUTHORIZED USER' }, 400);
  await expect(state(page)).toHaveText(await text(page, 's.translate.testFail', { error: '52003 UNAUTHORIZED USER' }));
});

test('the hotkey state is shown', async ({ page }) => {
  await open(page, SAVED);
  await holdTranslateRequests(page);
  // not awaited: the request it sends waits for the test to answer it
  await page.evaluate(() => { loadTranslateState(); });
  expect((await requests(page))[0]).toEqual({ url: '/api/translate/status', method: 'GET', body: null });
  await answer(page, 0, { active: true, hotkey: 'registered' });
  await expect(state(page)).toHaveText(await text(page, 's.translate.stateOn', { hotkey: 'ctrl+shift+t' }));
});

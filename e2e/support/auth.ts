import type { APIRequestContext, Browser, Page, PlaywrightWorkerArgs } from '@playwright/test';
import { apiURL, uiURL, required, type Account } from './config';

export async function accessToken(api: APIRequestContext, account: Account): Promise<string> {
  const response = await api.post(`${apiURL}/api/auth/login`, {
    data: { username: required(`E2E_${account}_USERNAME`), password: required(`E2E_${account}_PASSWORD`, false) },
  });
  if (response.status() !== 200)
    throw new Error(`E2E ${account} login failed (${response.status()}); check credentials, enabled/confirmed state and lockout.`);
  const session = await response.json();
  if (typeof session.accessToken !== 'string' || session.tokenType !== 'Bearer')
    throw new Error('Password login did not return a bearer access token.');
  return session.accessToken;
}

export async function apiSession(playwright: PlaywrightWorkerArgs['playwright'], account: Account) {
  const anonymous = await playwright.request.newContext();
  let token: string;
  try { token = await accessToken(anonymous, account); }
  finally { await anonymous.dispose(); }
  const api = await playwright.request.newContext({
    baseURL: apiURL, extraHTTPHeaders: { Authorization: `Bearer ${token}` },
  });
  try {
    const response = await api.get('/api/auth/me');
    if (response.status() !== 200) throw new Error(`E2E ${account} session verification failed (${response.status()}).`);
    const profile = await response.json();
    if (profile.username.toLowerCase() !== required(`E2E_${account}_USERNAME`).toLowerCase()
        || profile.roles.includes('Global Admin') !== (account === 'ADMIN'))
      throw new Error(`E2E ${account} account has incorrect username or database roles.`);
    return api;
  } catch (error) { await api.dispose(); throw error; }
}

export async function login(page: Page, account: Account) {
  const token = await accessToken(page.request, account);
  // Seed once so logout/expiry cannot silently log back in after navigation.
  await page.goto(uiURL);
  await page.evaluate(value => sessionStorage.setItem('webhook-router-access-token', value), token);
  await page.reload();
}

export async function authenticatedPage(browser: Browser, account: Account) {
  const context = await browser.newContext();
  const page = await context.newPage();
  try { await login(page, account); return { context, page }; }
  catch (error) { await context.close(); throw error; }
}

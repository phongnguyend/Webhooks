import { randomUUID } from 'node:crypto';
import { test, expect } from '../support/fixtures';
import { apiSession, authenticatedPage, accessToken } from '../support/auth';
import { apiURL, required } from '../support/config';

test('password login returns an API JWT without an authentication cookie', async ({ playwright }) => {
  const api = await playwright.request.newContext({ baseURL: apiURL });
  try {
    const response = await api.post('/api/auth/login', {
      data: { username: required('E2E_USER_USERNAME'), password: required('E2E_USER_PASSWORD', false) },
    });
    expect(response.status()).toBe(200);
    expect(response.headers()['set-cookie']).toBeUndefined();
    expect(response.headers()['cache-control']).toBe('no-store');
    const session = await response.json();
    expect(session.tokenType).toBe('Bearer');
    expect(Date.parse(session.expiresAt)).toBeGreaterThan(Date.now());
    const me = await api.get('/api/auth/me', { headers: { Authorization: `Bearer ${session.accessToken}` } });
    expect(me.status()).toBe(200);
    const profile = await me.json();
    expect(profile.username.toLowerCase()).toBe(required('E2E_USER_USERNAME').toLowerCase());
    const payload = JSON.parse(Buffer.from(session.accessToken.split('.')[1], 'base64url').toString());
    expect(payload.sub).toBe(profile.id);
  } finally { await api.dispose(); }
});

test('Global Admin sees another user tenant read-only; regular users cannot see admin tenants', async ({ playwright, browser, page, api, tenantIds }) => {
  const name = `e2e-${randomUUID()}`;
  const created = await api.post('/api/tenants', { data: { name, isEnabled: true } });
  expect(created.status()).toBe(201);
  const tenant = await created.json();
  tenantIds.push(tenant.id);
  const admin = await apiSession(playwright, 'ADMIN');
  let adminTenantId: string | undefined;
  try {
    const adminCreated = await admin.post('/api/tenants', { data: { name: `${name}-admin`, isEnabled: true } });
    expect(adminCreated.status()).toBe(201);
    adminTenantId = (await adminCreated.json()).id;
    const visible = await (await api.get('/api/tenants')).json();
    expect(visible.some((t: { id: string }) => t.id === adminTenantId)).toBe(false);
    expect((await api.get(`/api/tenants/${adminTenantId}/topics`)).status()).toBe(404);
    expect((await api.patch(`/api/tenants/${adminTenantId}/enabled`, { data: { isEnabled: false } })).status()).toBe(404);
    await page.reload();
    await expect(page.getByRole('button', { name: 'Sign out', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Users', exact: true })).toHaveCount(0);
    const session = await authenticatedPage(browser, 'ADMIN');
    try {
      await session.page.locator('.tenant-row').filter({ hasText: name }).filter({ hasNotText: `${name}-admin` }).click();
      await expect(session.page.getByRole('heading', { name, exact: true })).toBeVisible();
      await expect(session.page.getByRole('button', { name: 'Users', exact: true })).toBeVisible();
      await expect(session.page.getByRole('button', { name: 'Edit tenant', exact: true })).toBeDisabled();
      expect((await admin.get(`/api/tenants/${tenant.id}/topics`)).status()).toBe(200);
      expect((await admin.patch(`/api/tenants/${tenant.id}/enabled`, { data: { isEnabled: false } })).status()).toBe(404);
    } finally { await session.context.close(); }
  } finally {
    if (adminTenantId) expect((await admin.delete(`/api/tenants/${adminTenantId}`)).status()).toBe(204);
    await admin.dispose();
  }
});

test('tampered JWT and anonymous requests are rejected by the real API', async ({ playwright }) => {
  const api = await playwright.request.newContext({ baseURL: apiURL });
  try {
    const token = await accessToken(api, 'USER');
    const parts = token.split('.');
    const payload = JSON.parse(Buffer.from(parts[1], 'base64url').toString());
    payload.sub = randomUUID();
    parts[1] = Buffer.from(JSON.stringify(payload)).toString('base64url');
    const response = await api.get('/api/auth/me', { headers: { Authorization: `Bearer ${parts.join('.')}` } });
    expect(response.status()).toBe(401);
    expect((await api.get('/api/tenants')).status()).toBe(401);
    const rejected = await api.post('/api/auth/login', {
      data: { username: `missing-${randomUUID()}@e2e.test`, password: 'Not-a-real-password!123' },
    });
    expect(rejected.status()).toBe(401);
    expect(await rejected.json()).toEqual({ error: 'Invalid username or password.' });
    expect(rejected.headers()['set-cookie']).toBeUndefined();
  } finally { await api.dispose(); }
});

test('sign out removes the injected session and survives reload', async ({ page }) => {
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Continue with Google' })).toBeVisible();
  expect(await page.evaluate(() => sessionStorage.getItem('webhook-router-access-token'))).toBeNull();
  await page.reload();
  await expect(page.getByRole('button', { name: 'Continue with Google' })).toBeVisible();
});

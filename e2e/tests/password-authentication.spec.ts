import { test, expect } from '@playwright/test';
import { apiSession, authenticatedPage } from '../support/auth';
import { apiURL, required } from '../support/config';

test('Global Admin controls password authentication and resets the test user password', async ({ browser, playwright }) => {
  const admin = await apiSession(playwright, 'ADMIN');
  const regular = await apiSession(playwright, 'USER');
  const anonymous = await playwright.request.newContext({ baseURL: apiURL });
  const user = (await (await admin.get('/api/users')).json()).find((u: { username: string }) => u.username.toLowerCase() === required('E2E_USER_USERNAME').toLowerCase());
  const password = required('E2E_USER_PASSWORD', false);
  let session: Awaited<ReturnType<typeof authenticatedPage>> | undefined;
  try {
    expect(user?.allowPasswordAuthentication).toBe(true);
    expect((await regular.put(`/api/users/${user.id}/password-authentication`, { data: { allowPasswordAuthentication: false } })).status()).toBe(403);
    expect((await admin.put(`/api/users/${user.id}`, { data: { ...user, allowPasswordAuthentication: false, password: 'Ignored-extra-field!123' } })).status()).toBe(204);
    const unchanged = (await (await admin.get('/api/users')).json()).find((u: { id: string }) => u.id === user.id);
    expect(unchanged.allowPasswordAuthentication).toBe(true);
    expect((await regular.get('/api/auth/me')).status()).toBe(200);
    session = await authenticatedPage(browser, 'ADMIN');
    const page = session.page;
    await page.getByRole('button', { name: 'Users', exact: true }).click();
    const row = page.getByRole('row').filter({ has: page.getByRole('cell', { name: user.email, exact: true }) });
    await row.getByRole('button', { name: 'Password authentication', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Password authentication' });
    await dialog.getByRole('checkbox', { name: 'Enable password authentication' }).uncheck();
    await dialog.getByRole('button', { name: 'Save changes' }).click();
    await expect(dialog).toHaveCount(0);
    const credentials = { username: user.username, password };
    expect((await anonymous.post('/api/auth/login', { data: credentials })).status()).toBe(401);
    expect((await regular.get('/api/auth/me')).status()).toBe(401);
    await row.getByRole('button', { name: 'Password authentication', exact: true }).click();
    await dialog.getByRole('checkbox', { name: 'Enable password authentication' }).check();
    await dialog.getByLabel('New password', { exact: true }).fill(password);
    await dialog.getByRole('button', { name: 'Save changes' }).click();
    await expect(dialog).toHaveCount(0);
    expect((await anonymous.post('/api/auth/login', { data: credentials })).status()).toBe(200);
    const saved = (await (await admin.get('/api/users')).json()).find((u: { id: string }) => u.id === user.id);
    expect(saved).toMatchObject({ allowPasswordAuthentication: true, hasPassword: true });
    expect(saved.password).toBeUndefined();
    expect(saved.passwordHash).toBeUndefined();
    const audit = await (await admin.get('/api/activity-logs', { params: { search: user.id } })).json();
    for (const eventType of ['PasswordChanged', 'PasswordAuthenticationEnabled', 'PasswordAuthenticationDisabled']) {
      expect(audit.items.some((entry: { eventType: string; entityId: string }) => entry.eventType === eventType && entry.entityId === user.id)).toBe(true);
    }
  } finally {
    try {
      if (user) expect((await admin.put(`/api/users/${user.id}/password-authentication`, { data: { allowPasswordAuthentication: user.allowPasswordAuthentication, password } })).status()).toBe(204);
    } finally {
      await session?.context.close();
      await anonymous.dispose();
      await regular.dispose();
      await admin.dispose();
    }
  }
});

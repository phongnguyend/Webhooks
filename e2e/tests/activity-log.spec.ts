import { randomUUID } from 'node:crypto';
import { test, expect } from '@playwright/test';
import { apiSession, authenticatedPage } from '../support/auth';
import { apiURL, required } from '../support/config';

test('Global Admin can view login activity; regular users cannot read the audit log', async ({ playwright, browser }) => {
  const admin = await apiSession(playwright, 'ADMIN');
  const regular = await apiSession(playwright, 'USER');
  const anonymous = await playwright.request.newContext({ baseURL: apiURL });
  let session: Awaited<ReturnType<typeof authenticatedPage>> | undefined;
  try {
    expect((await anonymous.get('/api/activity-logs')).status()).toBe(401);
    expect((await regular.get('/api/activity-logs')).status()).toBe(403);
    const profile = await (await regular.get('/api/auth/me')).json();
    const success = await admin.get('/api/activity-logs', { params: { eventType: 'LoginSucceeded', search: profile.id } });
    expect(success.status()).toBe(200);
    const successes = await success.json();
    expect(successes.items.length).toBeGreaterThan(0);
    expect(successes.items[0]).toMatchObject({ eventType: 'LoginSucceeded', entityType: 'User', entityId: profile.id, actorUserId: profile.id });
    expect(typeof successes.items[0].metadata).toBe('string');
    expect(JSON.parse(successes.items[0].metadata)).toMatchObject({ provider: 'Password' });
    expect(successes.items[0].provider).toBeUndefined();
    expect(successes.items[0].reason).toBeUndefined();
    const occurredAt = successes.items[0].occurredAt;
    const exactTime = await admin.get('/api/activity-logs', { params: { search: profile.id, from: occurredAt, to: occurredAt } });
    expect(exactTime.status()).toBe(200);
    expect((await exactTime.json()).items.some((item: { id: string }) => item.id === successes.items[0].id)).toBe(true);
    const dateBounds: Record<string, string>[] = [{ from: occurredAt }, { to: occurredAt }];
    for (const bounds of dateBounds) {
      const filtered = await admin.get('/api/activity-logs', { params: { search: profile.id, ...bounds } });
      expect(filtered.status()).toBe(200);
      for (const item of (await filtered.json()).items) {
        const timestamp = Date.parse(item.occurredAt);
        if (bounds.from) expect(timestamp).toBeGreaterThanOrEqual(Date.parse(occurredAt));
        if (bounds.to) expect(timestamp).toBeLessThanOrEqual(Date.parse(occurredAt));
      }
    }
    expect((await admin.get('/api/activity-logs?from=2030-01-01T00:00:00Z&to=2020-01-01T00:00:00Z')).status()).toBe(400);
    expect((await admin.get('/api/activity-logs?from=invalid')).status()).toBe(400);
    const before = await (await admin.get('/api/activity-logs', { params: { eventType: 'LoginFailed' } })).json();
    expect((await anonymous.post('/api/auth/login', { data: { username: `unknown-${randomUUID()}@e2e.test`, password: 'Invalid-test-password!123' } })).status()).toBe(401);
    const failures = await (await admin.get('/api/activity-logs', { params: { eventType: 'LoginFailed' } })).json();
    expect(failures.total).toBeGreaterThan(before.total);
    expect(failures.items.some((entry: { metadata: string }) => JSON.parse(entry.metadata).reason === 'UnknownAccount')).toBe(true);
    expect((await admin.get('/api/activity-logs?pageSize=101')).status()).toBe(400);
    expect((await admin.get('/api/activity-logs?category=Unknown')).status()).toBe(400);
    const authentication = await (await admin.get('/api/activity-logs?category=Authentication')).json();
    expect(authentication.items.length).toBeGreaterThan(0);
    for (const entry of authentication.items) {
      expect(['LoginSucceeded', 'LoginFailed', 'UserLockedOut']).toContain(entry.eventType);
    }
    const incompatible = await (await admin.get('/api/activity-logs?category=Tenants&eventType=LoginSucceeded')).json();
    expect(incompatible.total).toBe(0);
    session = await authenticatedPage(browser, 'ADMIN');
    await session.page.getByRole('button', { name: 'Activity log', exact: true }).click();
    await expect(session.page.getByRole('heading', { name: 'Activity log', exact: true })).toBeVisible();
    const fromInput = session.page.getByLabel('From (local time)', { exact: true });
    const toInput = session.page.getByLabel('To (local time)', { exact: true });
    // datetime-local normalizes zero seconds away; fill its canonical value.
    await fromInput.fill('2099-01-02T00:00');
    await toInput.fill('2099-01-01T00:00');
    await session.page.getByRole('button', { name: 'Search', exact: true }).click();
    await expect(session.page.getByRole('alert')).toHaveText('From must be earlier than or equal to To.');
    await toInput.fill('2099-01-03T00:00');
    const filteredRequest = session.page.waitForRequest(request => {
      const url = new URL(request.url());
      return url.pathname === '/api/activity-logs' && url.searchParams.has('from') && url.searchParams.has('to');
    });
    const expectedFrom = await fromInput.evaluate((element: HTMLInputElement) => new Date(element.value).toISOString());
    await session.page.getByRole('button', { name: 'Search', exact: true }).click();
    expect(new URL((await filteredRequest).url()).searchParams.get('from')).toBe(expectedFrom);
    await expect(session.page.getByRole('cell', { name: 'No activity found.', exact: true })).toBeVisible();
    await session.page.getByRole('button', { name: 'Clear dates', exact: true }).click();
    await expect(fromInput).toHaveValue('');
    await expect(toInput).toHaveValue('');
    const category = session.page.getByRole('combobox', { name: 'Category', exact: true });
    const event = session.page.getByRole('combobox', { name: 'Event', exact: true });
    await category.fill('ten');
    await session.page.getByRole('option', { name: 'Tenants', exact: true }).click();
    await event.click();
    await expect(session.page.getByRole('option', { name: 'Tenant created', exact: true })).toBeVisible();
    await expect(session.page.getByRole('option', { name: 'Login succeeded', exact: true })).toHaveCount(0);
    await event.fill('created');
    await event.press('Enter');
    await expect(event).toHaveValue('Tenant created');
    await category.fill('auth');
    await category.press('Enter');
    await expect(event).toHaveValue('All events');
    await category.click();
    await session.page.getByRole('option', { name: 'All categories', exact: true }).click();
    await event.click();
    await expect(session.page.getByRole('listbox', { name: 'Event', exact: true }).getByRole('group')).toHaveCount(4);
    await event.fill('no-such-event');
    await expect(session.page.getByText('No matches found.', { exact: true })).toBeVisible();
    await event.press('Escape');
    await expect(event).toHaveValue('All events');
    await event.fill('login succeeded');
    await event.press('ArrowDown');
    await event.press('Enter');
    await session.page.getByLabel('Search entities or users', { exact: true }).fill(required('E2E_USER_USERNAME'));
    await session.page.getByRole('button', { name: 'Search', exact: true }).click();
    await expect(session.page.getByRole('cell', { name: 'Login succeeded', exact: true }).first()).toBeVisible();
    await session.page.getByRole('button', { name: 'Users', exact: true }).click();
    const userRow = session.page.getByRole('row').filter({ has: session.page.getByRole('cell', { name: profile.email, exact: true }) });
    await userRow.getByRole('button', { name: 'View activities', exact: true }).click();
    await expect(session.page.getByRole('heading', { name: 'Activity log', exact: true })).toBeVisible();
    await expect(session.page.getByLabel('Search entities or users', { exact: true })).toHaveValue(profile.id);
    await expect(session.page.getByRole('cell', { name: 'Login succeeded', exact: true }).first()).toBeVisible();
    await session.page.getByRole('button', { name: 'Activity log', exact: true }).click();
    await expect(session.page.getByLabel('Search entities or users', { exact: true })).toHaveValue('');
  } finally {
    await session?.context.close();
    await anonymous.dispose();
    await regular.dispose();
    await admin.dispose();
  }
});

import { test as base, expect, type APIRequestContext } from '@playwright/test';
import { apiSession, login } from './auth';

export const test = base.extend<{ api: APIRequestContext; tenantIds: string[] }>({
  api: async ({ playwright }, use) => {
    const api = await apiSession(playwright, 'USER');
    try { await use(api); } finally { await api.dispose(); }
  },
  tenantIds: async ({ api }, use) => {
    const ids: string[] = [];
    try { await use(ids); }
    finally {
      for (const id of ids) {
        const response = await api.delete(`/api/tenants/${id}`);
        expect([204, 404], `Cleanup failed for test tenant ${id}`).toContain(response.status());
      }
    }
  },
  page: async ({ page, api }, use) => {
    void api; // Verify the real API session before opening the browser.
    await login(page, 'USER');
    await expect(page.getByRole('button', { name: 'Sign out', exact: true })).toBeVisible();
    await use(page);
  },
});
export { expect };

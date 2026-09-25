import { randomUUID } from 'node:crypto';
import { test, expect } from '../support/fixtures';
import { apiSession } from '../support/auth';

test('Tenant and topic lifecycle is audited without Service Bus credentials', async ({ api, tenantIds, playwright }) => {
  const admin = await apiSession(playwright, 'ADMIN');
  try {
    const profile = await (await api.get('/api/auth/me')).json();
    const created = await api.post('/api/tenants', { data: { name: `Audit ${randomUUID()}`, isEnabled: true } });
    expect(created.status()).toBe(201);
    const tenant = await created.json();
    tenantIds.push(tenant.id);
    const tenantPath = `/api/tenants/${tenant.id}`;
    const secret = 'AuditTestKeyNeverLog123';
    const route = {
      key: 'audit-route', name: 'Audit route', isEnabled: true, isSharePointWebhook: false,
      useManagedIdentity: false, serviceBusEntityType: 'Queue', serviceBusEntityName: 'unused-queue',
      serviceBusConnectionString: `Endpoint=sb://unused.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=${secret}`,
    };
    const topicResponse = await api.post(`${tenantPath}/topics`, { data: route });
    expect(topicResponse.status()).toBe(201);
    const topic = await topicResponse.json();
    const topicPath = `${tenantPath}/topics/${topic.id}`;
    expect((await api.put(topicPath, { data: { ...route, name: 'Updated route', serviceBusConnectionString: `${route.serviceBusConnectionString}changed` } })).status()).toBe(200);
    expect((await api.put(tenantPath, { data: { name: 'Updated audit tenant', isEnabled: false } })).status()).toBe(200);
    for (const path of [tenantPath, topicPath]) {
      expect((await api.patch(`${path}/enabled`, { data: { isEnabled: false } })).status()).toBe(204);
      expect((await api.patch(`${path}/enabled`, { data: { isEnabled: false } })).status()).toBe(204);
      expect((await api.patch(`${path}/enabled`, { data: { isEnabled: true } })).status()).toBe(204);
    }
    expect((await api.delete(topicPath)).status()).toBe(204);
    const cascadeResponse = await api.post(`${tenantPath}/topics`, { data: route });
    expect(cascadeResponse.status()).toBe(201);
    const cascade = await cascadeResponse.json();
    expect((await api.delete(tenantPath)).status()).toBe(204);
    for (const [entityType, entityId] of [['Tenant', tenant.id], ['Topic', topic.id]]) {
      const response = await admin.get('/api/activity-logs', { params: { entityType, entityId } });
      expect(response.status()).toBe(200);
      const { items } = await response.json();
      expect(items.map((item: { eventType: string }) => item.eventType).sort()).toEqual(
        ['Created', 'Updated', 'Disabled', 'Enabled', 'Deleted'].map(action => `${entityType}${action}`).sort());
      for (const item of items) {
        expect(item).toMatchObject({ entityType, entityId, actorUserId: profile.id });
        expect(item.id).toMatch(/^[0-9a-f-]{36}$/i);
        expect(item.metadata).not.toContain(secret);
        expect(item.metadata).not.toContain('SharedAccessKey');
      }
      if (entityType === 'Topic') {
        const update = items.find((item: { eventType: string }) => item.eventType === 'TopicUpdated');
        expect(JSON.parse(update.metadata).details.connectionStringChanged).toBe(true);
      }
    }
    const cascadeLog = await admin.get('/api/activity-logs', { params: { entityType: 'Topic', entityId: cascade.id, eventType: 'TopicDeleted' } });
    const { items } = await cascadeLog.json();
    expect(items).toHaveLength(1);
    expect(JSON.parse(items[0].metadata)).toMatchObject({ tenantId: tenant.id, details: { reason: 'TenantDeleted' } });
  } finally { await admin.dispose(); }
});

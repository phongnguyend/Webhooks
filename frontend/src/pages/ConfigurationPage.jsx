import { Check, Copy, Database, Edit3, Layers3, Plus, Send, Trash2, Power, PowerOff } from 'lucide-react'
import RecordTimestamps from '../components/RecordTimestamps'
import Empty from '../components/Empty'
import Status from '../components/Status'
import { API_URL } from '../config'

export default function ConfigurationPage({ currentUser, tenants, selectedTenantId, setSelectedTenantId, selectedTenant, topics, loading, copied, copy, setDialog, toggleTenant, deleteTenant, toggleTopic, deleteTopic }) {
  const canManage = selectedTenant?.createdByUser?.id === currentUser.id
  return (
<section className="management">
          <aside className="tenant-sidebar">
            <div className="section-heading"><div><span className="eyebrow">Workspace</span><h2>Tenants <span>{tenants.length}</span></h2></div><button className="icon-button primary" onClick={() => setDialog({ type: 'tenant', item: null })} title="New tenant"><Plus size={18} /></button></div>
            <div className="tenant-list">
              {tenants.map((tenant) => <button key={tenant.id} className={`tenant-row ${tenant.id === selectedTenantId ? 'selected' : ''}`} onClick={() => setSelectedTenantId(tenant.id)}>
                <span className="tenant-avatar">{tenant.name.slice(0, 2).toUpperCase()}</span>
                <span className="tenant-row-copy"><strong>{tenant.name}</strong><small>{tenant.topicCount} topic{tenant.topicCount === 1 ? '' : 's'}</small></span>
                <span className={`status-dot ${tenant.isEnabled ? 'enabled' : ''}`} />
              </button>)}
              {!loading && tenants.length === 0 && <Empty icon={Layers3} title="No tenants yet" text="Create a tenant to define your first webhook route." />}
            </div>
          </aside>

          <section className="topic-workspace">
            {selectedTenant ? <>
              <div className="workspace-header">
                <div><span className="eyebrow">Tenant{!canManage && ' · Read only'}</span><div className="title-line"><h2>{selectedTenant.name}</h2><Status enabled={selectedTenant.isEnabled} /></div><p className="route-preview">POST {API_URL}/tenants/{selectedTenant.id}/topics/<em>topic-key</em></p><RecordTimestamps createdAt={selectedTenant.createdAt} updatedAt={selectedTenant.updatedAt} createdByUser={selectedTenant.createdByUser} updatedByUser={selectedTenant.updatedByUser} /></div>
                <div className="header-actions">
                  <button className="secondary-button" onClick={() => copy(selectedTenant.id)} title="Copy tenant ID">
                    {copied ? <Check size={15} /> : <Copy size={15} />}
                    {copied ? 'Copied' : 'Copy tenant ID'}
                  </button>
                  <button className="secondary-button" disabled={!canManage} onClick={() => toggleTenant(selectedTenant)}>{selectedTenant.isEnabled ? <PowerOff size={15} aria-hidden="true" /> : <Power size={15} aria-hidden="true" />}{selectedTenant.isEnabled ? 'Disable' : 'Enable'}</button>
                  <button className="icon-button" disabled={!canManage} onClick={() => setDialog({ type: 'tenant', item: selectedTenant })} title="Edit tenant"><Edit3 size={17} /></button>
                  <button className="icon-button danger" disabled={!canManage} onClick={() => deleteTenant(selectedTenant)} title="Delete tenant"><Trash2 size={17} /></button>
                </div>
              </div>
              <div className="content-heading"><div><h3>Topic routes</h3><p>Each route publishes to its configured Azure Service Bus destination.</p></div><button className="primary-button" disabled={!canManage} onClick={() => setDialog({ type: 'topic', item: null })}><Plus size={16} /> Add topic</button></div>
              <div className="topic-grid">
                {topics.map((topic) => <article className={`topic-card ${!topic.isEnabled ? 'disabled' : ''}`} key={topic.id}>
                  <div className="topic-card-top"><span className="topic-icon"><Database size={18} /></span><Status enabled={topic.isEnabled} /></div>
                  <h4>{topic.name}</h4><p className="topic-key">/{topic.key}{topic.isSharePointWebhook && <span className="sharepoint-label">SharePoint webhook</span>}</p>
                  <div className="destination"><span>Azure Service Bus {(topic.serviceBusEntityType || 'Topic').toLowerCase()}</span><strong>{topic.serviceBusEntityName}</strong><small>{topic.useManagedIdentity ? topic.fullyQualifiedNamespace : 'Connection string credentials'}</small></div>
                  <div className="endpoint"><code>{API_URL}/tenants/{selectedTenant.id}/topics/{topic.key}</code><button onClick={() => copy(`${API_URL}/tenants/${selectedTenant.id}/topics/${topic.key}`)} title="Copy endpoint"><Copy size={14} /></button></div>
                  <RecordTimestamps createdAt={topic.createdAt} updatedAt={topic.updatedAt} createdByUser={topic.createdByUser} updatedByUser={topic.updatedByUser} />
                  <div className="card-actions"><button disabled={!canManage} onClick={() => toggleTopic(topic)}>{topic.isEnabled ? <PowerOff size={15} aria-hidden="true" /> : <Power size={15} aria-hidden="true" />}{topic.isEnabled ? 'Disable' : 'Enable'}</button><span /><button disabled={!canManage} onClick={() => setDialog({ type: 'test', item: topic })}><Send size={15} /> Test</button><button disabled={!canManage} onClick={() => setDialog({ type: 'topic', item: topic })}><Edit3 size={15} /> Edit</button><button className="danger-text" disabled={!canManage} aria-label="Delete topic" title="Delete topic" onClick={() => deleteTopic(topic)}><Trash2 size={15} /></button></div>
                </article>)}
                {topics.length === 0 && <div className="wide-empty"><Empty icon={Database} title="No topic routes" text="Add a route and map it to an Azure Service Bus topic or queue." /><button className="primary-button" disabled={!canManage} onClick={() => setDialog({ type: 'topic', item: null })}><Plus size={16} /> Add topic</button></div>}
              </div>
            </> : <Empty icon={Layers3} title="Select or create a tenant" text="Tenant configuration and topic routes will appear here." />}
          </section>
        </section>
  )
}

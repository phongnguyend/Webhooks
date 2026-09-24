import { useEffect, useMemo, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import ThemeSwitcher from './ThemeSwitcher'
import {
  Braces, Check, ChevronRight, CircleAlert, Clock3, Copy, Database, Edit3, Eye,
  Layers3, Plus, Radio, Search, Send, Settings2, Trash2, Webhook, X,
} from 'lucide-react'

const API_URL = (import.meta.env.VITE_API_URL || 'http://localhost:5229').replace(/\/$/, '')
const GOOGLE_CLIENT_ID = import.meta.env.VITE_GOOGLE_CLIENT_ID || ''
const GOOGLE_REDIRECT_URI = import.meta.env.VITE_GOOGLE_REDIRECT_URI
  || (typeof window === 'undefined' ? '' : `${window.location.origin}${window.location.pathname}`)
const GOOGLE_STATE_KEY = 'webhook-router-google-state'
const GOOGLE_NONCE_KEY = 'webhook-router-google-nonce'
const TOKEN_KEY = 'webhook-router-token'
const emptyTenant = { name: '', isEnabled: true }
const emptyTopic = {
  key: '', name: '', isEnabled: true, isSharePointWebhook: false,
  useManagedIdentity: true, fullyQualifiedNamespace: '', serviceBusConnectionString: '', serviceBusEntityName: '', serviceBusEntityType: 'Topic',
}

async function api(path, options) {
  const token = sessionStorage.getItem(TOKEN_KEY)
  const response = await fetch(`${API_URL}${path}`, {
    ...options,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...options?.headers },
  })
  if (response.status === 401 && token) {
    sessionStorage.removeItem(TOKEN_KEY)
    window.dispatchEvent(new Event('webhook-router-auth-expired'))
  }
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}))
    throw new Error(problem.error || problem.detail || `Request failed (${response.status})`)
  }
  return response.status === 204 ? null : response.json()
}

function randomUrlToken() {
  const bytes = new Uint8Array(32)
  crypto.getRandomValues(bytes)
  return btoa(String.fromCharCode(...bytes)).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '')
}

function decodeJwtPayload(token) {
  const encoded = token.split('.')[1]?.replaceAll('-', '+').replaceAll('_', '/')
  if (!encoded) throw new Error('Google returned an invalid ID token.')
  const padded = encoded.padEnd(Math.ceil(encoded.length / 4) * 4, '=')
  const bytes = Uint8Array.from(atob(padded), (character) => character.charCodeAt(0))
  return JSON.parse(new TextDecoder().decode(bytes))
}

function consumeGoogleRedirect() {
  if (typeof window === 'undefined' || !window.location.hash) return { token: null, error: '' }
  const response = new URLSearchParams(window.location.hash.slice(1))
  const token = response.get('id_token')
  const oauthError = response.get('error')
  if (!token && !oauthError) return { token: null, error: '' }

  window.history.replaceState({}, document.title, `${window.location.pathname}${window.location.search}`)
  const expectedState = sessionStorage.getItem(GOOGLE_STATE_KEY)
  const expectedNonce = sessionStorage.getItem(GOOGLE_NONCE_KEY)
  sessionStorage.removeItem(GOOGLE_STATE_KEY)
  sessionStorage.removeItem(GOOGLE_NONCE_KEY)

  if (oauthError) return { token: null, error: response.get('error_description') || 'Google sign-in was cancelled.' }
  if (!expectedState || response.get('state') !== expectedState) return { token: null, error: 'Google sign-in state validation failed.' }

  try {
    if (!expectedNonce || decodeJwtPayload(token).nonce !== expectedNonce)
      return { token: null, error: 'Google sign-in nonce validation failed.' }
    sessionStorage.setItem(TOKEN_KEY, token)
    return { token, error: '' }
  } catch (error) {
    return { token: null, error: error.message || 'Google returned an invalid ID token.' }
  }
}

const googleRedirect = consumeGoogleRedirect()

function parsePayload(payload) {
  if (typeof payload !== 'string') return payload
  try { return JSON.parse(payload) } catch { return payload }
}

function formatDate(value) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? 'Unknown time' : new Intl.DateTimeFormat(undefined, {
    month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit',
  }).format(date)
}

function eventId(event) { return event.id || `${event.receivedAt}-${event.tenantId}-${event.topicName}` }

export default function App() {
  const [accessToken, setAccessToken] = useState(() => googleRedirect.token || sessionStorage.getItem(TOKEN_KEY))
  const [user, setUser] = useState(null)
  const [authLoading, setAuthLoading] = useState(true)
  const [loginError, setLoginError] = useState(googleRedirect.error)
  const [view, setView] = useState('manage')
  const [events, setEvents] = useState([])
  const [selectedEventId, setSelectedEventId] = useState(null)
  const [connectionStatus, setConnectionStatus] = useState('connecting')
  const [query, setQuery] = useState('')
  const [copied, setCopied] = useState(false)
  const [tenants, setTenants] = useState([])
  const [selectedTenantId, setSelectedTenantId] = useState(null)
  const [topics, setTopics] = useState([])
  const [loading, setLoading] = useState(true)
  const [notice, setNotice] = useState(null)
  const [dialog, setDialog] = useState(null)

  useEffect(() => {
    if (!accessToken) { setUser(null); setAuthLoading(false); return }
    setAuthLoading(true)
    api('/api/auth/me')
      .then(setUser)
      .catch(() => { sessionStorage.removeItem(TOKEN_KEY); setAccessToken(null); setUser(null) })
      .finally(() => setAuthLoading(false))
  }, [accessToken])

  useEffect(() => {
    const expired = () => { setAccessToken(null); setUser(null) }
    window.addEventListener('webhook-router-auth-expired', expired)
    return () => window.removeEventListener('webhook-router-auth-expired', expired)
  }, [])

  async function loadTenants(preferredId) {
    const result = await api('/api/tenants')
    setTenants(result)
    setSelectedTenantId((current) => {
      const wanted = preferredId || current
      return result.some((item) => item.id === wanted) ? wanted : result[0]?.id || null
    })
    return result
  }

  async function loadTopics(tenantId) {
    if (!tenantId) { setTopics([]); return }
    setTopics(await api(`/api/tenants/${tenantId}/topics`))
  }

  useEffect(() => {
    if (!user) return
    loadTenants().catch(showError).finally(() => setLoading(false))
  }, [user])

  useEffect(() => { if (user) loadTopics(selectedTenantId).catch(showError) }, [selectedTenantId, user])

  useEffect(() => {
    if (!user) return
    let active = true
    let connection
    async function connect() {
      try {
        const history = await api('/')
        if (active) {
          setEvents(history)
          setSelectedEventId((current) => current || (history[0] ? eventId(history[0]) : null))
        }
        connection = new signalR.HubConnectionBuilder().withUrl(`${API_URL}/hubs/events`, {
          withCredentials: false,
          accessTokenFactory: () => accessToken,
        })
          .withAutomaticReconnect().configureLogging(signalR.LogLevel.Warning).build()
        connection.on('WebhookReceived', (event) => {
          if (!active) return
          setEvents((current) => [event, ...current].slice(0, 500))
          setSelectedEventId((current) => current || eventId(event))
        })
        connection.onreconnecting(() => active && setConnectionStatus('reconnecting'))
        connection.onreconnected(() => active && setConnectionStatus('connected'))
        connection.onclose(() => active && setConnectionStatus('disconnected'))
        await connection.start()
        if (active) setConnectionStatus('connected')
      } catch (error) {
        console.error(error)
        if (active) setConnectionStatus('disconnected')
      }
    }
    connect()
    return () => { active = false; connection?.stop() }
  }, [user, accessToken])

  function showError(error) { setNotice({ kind: 'error', text: error.message || 'Something went wrong.' }) }
  function showSuccess(text) { setNotice({ kind: 'success', text }); window.setTimeout(() => setNotice(null), 2600) }

  async function saveTenant(values) {
    try {
      const editing = dialog.item
      const saved = await api(editing ? `/api/tenants/${editing.id}` : '/api/tenants', {
        method: editing ? 'PUT' : 'POST', body: JSON.stringify(values),
      })
      setDialog(null)
      await loadTenants(saved.id)
      showSuccess(editing ? 'Tenant updated.' : 'Tenant created.')
    } catch (error) { showError(error) }
  }

  async function saveTopic(values) {
    try {
      const editing = dialog.item
      await api(`/api/tenants/${selectedTenantId}/topics${editing ? `/${editing.id}` : ''}`, {
        method: editing ? 'PUT' : 'POST', body: JSON.stringify(values),
      })
      setDialog(null)
      await Promise.all([loadTopics(selectedTenantId), loadTenants(selectedTenantId)])
      showSuccess(editing ? 'Topic updated.' : 'Topic created.')
    } catch (error) { showError(error) }
  }

  async function saveProfile(values) {
    try {
      const updated = await api('/api/auth/me', { method: 'PUT', body: JSON.stringify(values) })
      setUser(updated)
      setDialog(null)
      showSuccess('Profile updated.')
    } catch (error) { showError(error); throw error }
  }

  async function toggleTenant(tenant) {
    try {
      await api(`/api/tenants/${tenant.id}/enabled`, { method: 'PATCH', body: JSON.stringify({ isEnabled: !tenant.isEnabled }) })
      await loadTenants(tenant.id)
    } catch (error) { showError(error) }
  }

  async function toggleTopic(topic) {
    try {
      await api(`/api/tenants/${selectedTenantId}/topics/${topic.id}/enabled`, { method: 'PATCH', body: JSON.stringify({ isEnabled: !topic.isEnabled }) })
      await loadTopics(selectedTenantId)
    } catch (error) { showError(error) }
  }

  async function deleteTenant(tenant) {
    if (!window.confirm(`Delete “${tenant.name}” and all of its topics?`)) return
    try {
      await api(`/api/tenants/${tenant.id}`, { method: 'DELETE' })
      await loadTenants()
      showSuccess('Tenant deleted.')
    } catch (error) { showError(error) }
  }

  async function deleteTopic(topic) {
    if (!window.confirm(`Delete “${topic.name}”?`)) return
    try {
      await api(`/api/tenants/${selectedTenantId}/topics/${topic.id}`, { method: 'DELETE' })
      await Promise.all([loadTopics(selectedTenantId), loadTenants(selectedTenantId)])
      showSuccess('Topic deleted.')
    } catch (error) { showError(error) }
  }

  async function sendTestPayload(payload) {
    try {
      await api(`/tenants/${selectedTenantId}/topics/${dialog.item.key}`, {
        method: 'POST',
        body: payload,
      })
      setDialog(null)
      setView('events')
      showSuccess('Test payload published to Azure Service Bus.')
    } catch (error) { showError(error) }
  }

  const selectedTenant = tenants.find((tenant) => tenant.id === selectedTenantId)
  const indexedEvents = useMemo(() => events.map((event) => ({ ...event, _id: eventId(event) })), [events])
  const filteredEvents = useMemo(() => {
    const value = query.trim().toLowerCase()
    return value ? indexedEvents.filter((event) => [event.tenantId, event.topicName, event.payload]
      .some((field) => String(field || '').toLowerCase().includes(value))) : indexedEvents
  }, [indexedEvents, query])
  const selectedEvent = indexedEvents.find((event) => event._id === selectedEventId) || indexedEvents[0]
  const formattedPayload = selectedEvent ? JSON.stringify(parsePayload(selectedEvent.payload), null, 2) : ''
  const userDisplayName = user ? [user.firstName, user.lastName].filter(Boolean).join(' ') || user.username || user.email : ''

  async function copy(value = formattedPayload) {
    if (!value) return
    await navigator.clipboard.writeText(value)
    setCopied(true)
    window.setTimeout(() => setCopied(false), 1600)
  }

  function startGoogleRedirect() {
    setLoginError('')
    if (!GOOGLE_CLIENT_ID) {
      setLoginError('Set VITE_GOOGLE_CLIENT_ID to enable Google sign-in.')
      return
    }

    const state = randomUrlToken()
    const nonce = randomUrlToken()
    sessionStorage.setItem(GOOGLE_STATE_KEY, state)
    sessionStorage.setItem(GOOGLE_NONCE_KEY, nonce)
    const authorizeUrl = new URL('https://accounts.google.com/o/oauth2/v2/auth')
    authorizeUrl.search = new URLSearchParams({
      client_id: GOOGLE_CLIENT_ID,
      redirect_uri: GOOGLE_REDIRECT_URI,
      response_type: 'id_token',
      response_mode: 'fragment',
      scope: 'openid email profile',
      prompt: 'select_account',
      state,
      nonce,
    }).toString()
    window.location.assign(authorizeUrl)
  }

  function logout() {
    sessionStorage.removeItem(TOKEN_KEY)
    setAccessToken(null)
    setUser(null)
    setTenants([])
    setTopics([])
    setEvents([])
  }

  if (authLoading) return <div className="auth-screen"><div className="auth-theme"><ThemeSwitcher /></div><div className="auth-card"><span className="brand-mark"><Webhook size={24} /></span><p>Loading Webhook Router…</p></div></div>

  if (!user) return <div className="auth-screen"><div className="auth-theme"><ThemeSwitcher /></div><div className="auth-card"><span className="brand-mark auth-logo"><Webhook size={26} /></span><span className="eyebrow">Webhook Router</span><h1>Route webhooks with confidence.</h1><p>Sign in to manage your private tenants, topic routes, and live event stream.</p><button type="button" className="google-button" onClick={startGoogleRedirect}><GoogleLogo /> Continue with Google</button>{loginError && <p className="login-error">{loginError}</p>}</div></div>

  return (
    <main className="app-shell">
      <header className="topbar">
        <div className="brand"><span className="brand-mark"><Webhook size={21} /></span><div><h1>Webhook Router</h1><p>Tenant routing console</p></div></div>
        <nav className="tabs" aria-label="Primary navigation">
          <button className={view === 'manage' ? 'active' : ''} onClick={() => setView('manage')}><Settings2 size={16} /> Configuration</button>
          <button className={view === 'events' ? 'active' : ''} onClick={() => setView('events')}><Eye size={16} /> Events <span>{events.length}</span></button>
        </nav>
        <div className="account-area"><ThemeSwitcher /><div className={`connection-pill ${connectionStatus}`}><span className="pulse-dot" />{connectionStatus === 'connected' ? 'Live' : connectionStatus}</div><div className="user-menu"><button className="profile-trigger" onClick={() => setDialog({ type: 'profile' })} title={user.email}><span>{userDisplayName.slice(0, 1).toUpperCase()}</span><small>{userDisplayName}</small></button><button className="signout-button" onClick={logout}>Sign out</button></div></div>
      </header>

      {notice && <div className={`toast ${notice.kind}`}><span>{notice.text}</span><button onClick={() => setNotice(null)} aria-label="Dismiss"><X size={15} /></button></div>}

      {view === 'manage' ? (
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
                <div><span className="eyebrow">Tenant</span><div className="title-line"><h2>{selectedTenant.name}</h2><Status enabled={selectedTenant.isEnabled} /></div><p className="route-preview">POST {API_URL}/tenants/{selectedTenant.id}/topics/<em>topic-key</em></p></div>
                <div className="header-actions">
                  <button className="secondary-button" onClick={() => copy(selectedTenant.id)} title="Copy tenant ID">
                    {copied ? <Check size={15} /> : <Copy size={15} />}
                    {copied ? 'Copied' : 'Copy tenant ID'}
                  </button>
                  <button className="secondary-button" onClick={() => toggleTenant(selectedTenant)}>{selectedTenant.isEnabled ? 'Disable' : 'Enable'}</button>
                  <button className="icon-button" onClick={() => setDialog({ type: 'tenant', item: selectedTenant })} title="Edit tenant"><Edit3 size={17} /></button>
                  <button className="icon-button danger" onClick={() => deleteTenant(selectedTenant)} title="Delete tenant"><Trash2 size={17} /></button>
                </div>
              </div>
              <div className="content-heading"><div><h3>Topic routes</h3><p>Each route publishes to its configured Azure Service Bus destination.</p></div><button className="primary-button" onClick={() => setDialog({ type: 'topic', item: null })}><Plus size={16} /> Add topic</button></div>
              <div className="topic-grid">
                {topics.map((topic) => <article className={`topic-card ${!topic.isEnabled ? 'disabled' : ''}`} key={topic.id}>
                  <div className="topic-card-top"><span className="topic-icon"><Database size={18} /></span><Status enabled={topic.isEnabled} /></div>
                  <h4>{topic.name}</h4><p className="topic-key">/{topic.key}{topic.isSharePointWebhook && <span className="sharepoint-label">SharePoint webhook</span>}</p>
                  <div className="destination"><span>Azure Service Bus {(topic.serviceBusEntityType || 'Topic').toLowerCase()}</span><strong>{topic.serviceBusEntityName}</strong><small>{topic.useManagedIdentity ? topic.fullyQualifiedNamespace : 'Connection string credentials'}</small></div>
                  <div className="endpoint"><code>{API_URL}/tenants/{selectedTenant.id}/topics/{topic.key}</code><button onClick={() => copy(`${API_URL}/tenants/${selectedTenant.id}/topics/${topic.key}`)} title="Copy endpoint"><Copy size={14} /></button></div>
                  <div className="card-actions"><button onClick={() => toggleTopic(topic)}>{topic.isEnabled ? 'Disable' : 'Enable'}</button><span /><button onClick={() => setDialog({ type: 'test', item: topic })}><Send size={15} /> Test</button><button onClick={() => setDialog({ type: 'topic', item: topic })}><Edit3 size={15} /> Edit</button><button className="danger-text" onClick={() => deleteTopic(topic)}><Trash2 size={15} /></button></div>
                </article>)}
                {topics.length === 0 && <div className="wide-empty"><Empty icon={Database} title="No topic routes" text="Add a route and map it to an Azure Service Bus topic or queue." /><button className="primary-button" onClick={() => setDialog({ type: 'topic', item: null })}><Plus size={16} /> Add topic</button></div>}
              </div>
            </> : <Empty icon={Layers3} title="Select or create a tenant" text="Tenant configuration and topic routes will appear here." />}
          </section>
        </section>
      ) : (
        <EventInspector events={events} filteredEvents={filteredEvents} selectedEvent={selectedEvent} selectedEventId={selectedEventId} setSelectedEventId={setSelectedEventId} query={query} setQuery={setQuery} formattedPayload={formattedPayload} copied={copied} copy={copy} status={connectionStatus} />
      )}

      {dialog?.type === 'tenant' && <TenantDialog item={dialog.item} onClose={() => setDialog(null)} onSave={saveTenant} />}
      {dialog?.type === 'topic' && <TopicDialog item={dialog.item} onClose={() => setDialog(null)} onSave={saveTopic} />}
      {dialog?.type === 'test' && <TestPayloadDialog tenantId={selectedTenantId} topic={dialog.item} onClose={() => setDialog(null)} onSend={sendTestPayload} />}
      {dialog?.type === 'profile' && <ProfileDialog user={user} onClose={() => setDialog(null)} onSave={saveProfile} onSignOut={logout} />}
    </main>
  )
}

function GoogleLogo() {
  return <svg viewBox="0 0 24 24" aria-hidden="true"><path fill="#4285F4" d="M21.6 12.2c0-.7-.1-1.4-.2-2H12v3.9h5.4a4.6 4.6 0 0 1-2 3v2.5h3.2c1.9-1.8 3-4.3 3-7.4Z"/><path fill="#34A853" d="M12 22c2.7 0 5-.9 6.6-2.4l-3.2-2.5c-.9.6-2 1-3.4 1a5.8 5.8 0 0 1-5.5-4H3.2v2.6A10 10 0 0 0 12 22Z"/><path fill="#FBBC05" d="M6.5 14a6 6 0 0 1 0-4V7.4H3.2a10 10 0 0 0 0 9.2L6.5 14Z"/><path fill="#EA4335" d="M12 5.9c1.5 0 2.8.5 3.8 1.5l2.9-2.8A9.7 9.7 0 0 0 3.2 7.4L6.5 10A5.8 5.8 0 0 1 12 5.9Z"/></svg>
}

function ProfileDialog({ user, onClose, onSave, onSignOut }) {
  const [copiedField, setCopiedField] = useState('')
  const [form, setForm] = useState({ firstName: user.firstName || '', lastName: user.lastName || '', phoneNumber: user.phoneNumber || '' })
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  async function copyProfileField(field, value) {
    if (!value) return
    await navigator.clipboard.writeText(value)
    setCopiedField(field)
    window.setTimeout(() => setCopiedField(''), 1600)
  }

  async function submit(event) {
    event.preventDefault()
    setError('')
    setSaving(true)
    try { await onSave(form) }
    catch (saveError) { setError(saveError.message || 'Unable to update profile.'); setSaving(false) }
  }

  const displayName = [user.firstName, user.lastName].filter(Boolean).join(' ') || user.username

  return <div className="modal-backdrop" onMouseDown={(event) => event.target === event.currentTarget && onClose()}>
    <form className="modal profile-modal" role="dialog" aria-modal="true" aria-labelledby="profile-title" onSubmit={submit}>
      <div className="modal-header"><h2 id="profile-title">Your profile</h2><button type="button" onClick={onClose} aria-label="Close"><X size={19} /></button></div>
      <div className="modal-body">
        <div className="profile-summary"><span>{(user.firstName || user.email).slice(0, 1).toUpperCase()}</span><div><strong>{displayName}</strong><small>Signed in with Google</small></div></div>
        <div className="field-row"><Field label="First name"><input autoFocus maxLength="100" value={form.firstName} onChange={(event) => setForm({ ...form, firstName: event.target.value })} placeholder="First name" /></Field><Field label="Last name"><input maxLength="100" value={form.lastName} onChange={(event) => setForm({ ...form, lastName: event.target.value })} placeholder="Last name" /></Field></div>
        <Field label="Email" hint="Managed by your Google account."><div className="topic-input-with-copy"><input readOnly value={user.email} /><button type="button" onClick={() => copyProfileField('email', user.email)} title="Copy email" aria-label="Copy email">{copiedField === 'email' ? <Check size={15} /> : <Copy size={15} />}</button></div></Field>
        <Field label="Phone number" hint="Changing the number resets its verification status."><div className="topic-input-with-copy"><input type="tel" maxLength="50" value={form.phoneNumber} onChange={(event) => setForm({ ...form, phoneNumber: event.target.value })} placeholder="+1 555 010 2000" /><button type="button" onClick={() => copyProfileField('phone', form.phoneNumber)} disabled={!form.phoneNumber} title="Copy phone number" aria-label="Copy phone number">{copiedField === 'phone' ? <Check size={15} /> : <Copy size={15} />}</button></div></Field>
        <Field label="Application user ID" hint="Database-generated ID used for tenant ownership.">
          <div className="topic-input-with-copy"><input readOnly value={user.id} /><button type="button" onClick={() => copyProfileField('id', user.id)} title="Copy user ID" aria-label="Copy user ID">{copiedField === 'id' ? <Check size={15} /> : <Copy size={15} />}</button></div>
        </Field>
        {error && <p className="form-error" role="alert">{error}</p>}
      </div>
      <div className="modal-footer profile-footer"><button type="button" className="danger-profile-button" onClick={onSignOut}>Sign out</button><div><button type="button" className="secondary-button" onClick={onClose}>Cancel</button><button type="submit" className="primary-button" disabled={saving}>{saving ? 'Saving…' : 'Save changes'}</button></div></div>
    </form>
  </div>
}

function Status({ enabled }) { return <span className={`status-badge ${enabled ? 'enabled' : ''}`}>{enabled ? 'Enabled' : 'Disabled'}</span> }
function Empty({ icon: Icon, title, text }) { return <div className="empty-state"><Icon size={29} /><strong>{title}</strong><span>{text}</span></div> }

function TenantDialog({ item, onClose, onSave }) {
  const [form, setForm] = useState(item ? { name: item.name, isEnabled: item.isEnabled } : emptyTenant)
  return <Dialog title={item ? 'Edit tenant' : 'Create tenant'} onClose={onClose} onSubmit={() => onSave(form)}>
    <Field label="Tenant name"><input required maxLength="200" autoFocus value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="Acme Corporation" /></Field>
    <Toggle checked={form.isEnabled} onChange={(value) => setForm({ ...form, isEnabled: value })} label="Tenant enabled" text="Disabled tenants reject all incoming webhooks." />
  </Dialog>
}

function TopicDialog({ item, onClose, onSave }) {
  const [form, setForm] = useState(item ? {
    key: item.key, name: item.name, isEnabled: item.isEnabled, isSharePointWebhook: item.isSharePointWebhook,
    useManagedIdentity: item.useManagedIdentity, fullyQualifiedNamespace: item.fullyQualifiedNamespace || '',
    serviceBusConnectionString: '', serviceBusEntityName: item.serviceBusEntityName, serviceBusEntityType: item.serviceBusEntityType || 'Topic',
  } : emptyTopic)
  const [entityCopied, setEntityCopied] = useState(false)

  async function copyServiceBusEntity() {
    if (!form.serviceBusEntityName) return
    await navigator.clipboard.writeText(form.serviceBusEntityName)
    setEntityCopied(true)
    window.setTimeout(() => setEntityCopied(false), 1600)
  }

  return <Dialog title={item ? 'Edit topic route' : 'Add topic route'} onClose={onClose} onSubmit={() => onSave(form)}>
    <div className="field-row"><Field label="Display name"><input required maxLength="200" autoFocus value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="Order events" /></Field><Field label="Route key"><input required maxLength="100" pattern="[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*" value={form.key} onChange={(e) => setForm({ ...form, key: e.target.value.toLowerCase() })} placeholder="orders" /></Field></div>
    <fieldset className="auth-fieldset"><legend>Authentication</legend><div className="auth-options">
      <button type="button" className={form.useManagedIdentity ? 'active' : ''} onClick={() => setForm({ ...form, useManagedIdentity: true, serviceBusConnectionString: '' })}><strong>Managed Identity</strong><small>Use this application's Azure identity</small></button>
      <button type="button" className={!form.useManagedIdentity ? 'active' : ''} onClick={() => setForm({ ...form, useManagedIdentity: false, fullyQualifiedNamespace: '' })}><strong>Connection String</strong><small>Use a namespace access key</small></button>
    </div></fieldset>
    {form.useManagedIdentity ? <Field label="Fully qualified namespace" hint="Example: my-namespace.servicebus.windows.net"><input required maxLength="300" value={form.fullyQualifiedNamespace} onChange={(e) => setForm({ ...form, fullyQualifiedNamespace: e.target.value })} placeholder="my-namespace.servicebus.windows.net" /></Field> : <Field label="Connection string" hint={item?.hasServiceBusConnection ? 'Leave blank to keep the stored connection string.' : 'The secret is stored but never returned by the API.'}><input type="password" required={!item?.hasServiceBusConnection} maxLength="2000" value={form.serviceBusConnectionString} onChange={(e) => setForm({ ...form, serviceBusConnectionString: e.target.value })} placeholder={item?.hasServiceBusConnection ? 'Connection string stored — enter to replace' : 'Endpoint=sb://…'} autoComplete="new-password" /></Field>}
    <fieldset className="auth-fieldset"><legend>Destination type</legend><div className="auth-options">
      <button type="button" className={form.serviceBusEntityType === 'Topic' ? 'active' : ''} onClick={() => setForm({ ...form, serviceBusEntityType: 'Topic' })}><strong>Topic</strong><small>Publish to a Service Bus topic</small></button>
      <button type="button" className={form.serviceBusEntityType === 'Queue' ? 'active' : ''} onClick={() => setForm({ ...form, serviceBusEntityType: 'Queue' })}><strong>Queue</strong><small>Send to a Service Bus queue</small></button>
    </div></fieldset>
    <Field label={`Azure Service Bus ${form.serviceBusEntityType.toLowerCase()}`} hint={`The ${form.serviceBusEntityType.toLowerCase()} must already exist in the selected namespace.`}>
      <div className="topic-input-with-copy">
        <input required maxLength="260" value={form.serviceBusEntityName} onChange={(e) => setForm({ ...form, serviceBusEntityName: e.target.value })} placeholder={form.serviceBusEntityType === 'Queue' ? 'incoming-orders' : 'order-created'} />
        <button type="button" onClick={copyServiceBusEntity} disabled={!form.serviceBusEntityName} title={`Copy Azure Service Bus ${form.serviceBusEntityType.toLowerCase()}`} aria-label={`Copy Azure Service Bus ${form.serviceBusEntityType.toLowerCase()}`}>
          {entityCopied ? <Check size={15} /> : <Copy size={15} />}
        </button>
      </div>
    </Field>
    <Toggle checked={form.isSharePointWebhook} onChange={(value) => setForm({ ...form, isSharePointWebhook: value })} label="SharePoint webhook" text="Echo SharePoint validation tokens without publishing them to Service Bus." />
    <Toggle checked={form.isEnabled} onChange={(value) => setForm({ ...form, isEnabled: value })} label="Topic route enabled" text="Disabled routes reject incoming webhooks." />
  </Dialog>
}

function TestPayloadDialog({ tenantId, topic, onClose, onSend }) {
  const [payload, setPayload] = useState('{\n  "message": "Hello from Webhook Router"\n}')
  const [error, setError] = useState('')
  const [sending, setSending] = useState(false)

  async function submit() {
    try {
      JSON.parse(payload)
    } catch {
      setError('Enter a valid JSON payload.')
      return
    }

    setError('')
    setSending(true)
    try { await onSend(payload) } finally { setSending(false) }
  }

  return <Dialog title={`Test ${topic.name}`} onClose={onClose} onSubmit={submit} submitLabel={sending ? 'Sending…' : 'Send payload'} submitDisabled={sending}>
    <div className="test-route"><span>POST</span><code>{API_URL}/tenants/{tenantId}/topics/{topic.key}</code></div>
    <Field label="JSON payload" hint={`This uses the real webhook route and publishes to the configured Azure Service Bus ${(topic.serviceBusEntityType || 'Topic').toLowerCase()}.`}>
      <textarea autoFocus value={payload} onChange={(event) => { setPayload(event.target.value); setError('') }} spellCheck="false" />
    </Field>
    {error && <p className="form-error" role="alert">{error}</p>}
  </Dialog>
}

function Dialog({ title, onClose, onSubmit, submitLabel = 'Save', submitDisabled = false, children }) {
  return <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}><form className="modal" onSubmit={(e) => { e.preventDefault(); onSubmit() }}><div className="modal-header"><h2>{title}</h2><button type="button" onClick={onClose} aria-label="Close"><X size={19} /></button></div><div className="modal-body">{children}</div><div className="modal-footer"><button type="button" className="secondary-button" onClick={onClose}>Cancel</button><button className="primary-button" type="submit" disabled={submitDisabled}>{submitLabel}</button></div></form></div>
}
function Field({ label, hint, children }) { return <label className="form-field"><span>{label}</span>{children}{hint && <small>{hint}</small>}</label> }
function Toggle({ checked, onChange, label, text }) { return <label className="toggle-row"><input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} /><span className="switch" /><span><strong>{label}</strong><small>{text}</small></span></label> }

function EventInspector({ events, filteredEvents, selectedEvent, selectedEventId, setSelectedEventId, query, setQuery, formattedPayload, copied, copy, status }) {
  return <section className="event-workspace"><aside className="event-panel"><div className="section-heading"><div><span className="eyebrow">Incoming</span><h2>Events <span>{events.length}</span></h2></div><Radio size={20} /></div><label className="search-field"><Search size={17} /><span className="sr-only">Search events</span><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Filter events…" /></label><div className="event-list">{filteredEvents.map((event) => <button className={`event-card ${selectedEventId === event._id ? 'selected' : ''}`} key={event._id} onClick={() => setSelectedEventId(event._id)}><span className="event-icon"><Braces size={17} /></span><span className="event-copy"><strong>{event.topicName}</strong><span className="tenant-label">{event.tenantId}</span><span className="timestamp"><Clock3 size={12} /> {formatDate(event.receivedAt)}</span></span><ChevronRight size={17} /></button>)}{filteredEvents.length === 0 && <Empty icon={Radio} title={events.length ? 'No matching events' : 'Waiting for events'} text={events.length ? 'Try a different filter.' : 'Published webhooks appear here instantly.'} />}</div></aside><section className="detail-panel">{selectedEvent ? <><div className="detail-header"><div><span className="eyebrow">Event payload</span><h2>{selectedEvent.topicName}</h2><div className="metadata"><span>{selectedEvent.tenantId}</span><i /><span>{formatDate(selectedEvent.receivedAt)}</span></div></div><button className="secondary-button" onClick={() => copy()}>{copied ? <Check size={16} /> : <Copy size={16} />}{copied ? 'Copied' : 'Copy JSON'}</button></div><div className="code-wrap"><div className="code-toolbar"><span><span className="file-dot" /> payload.json</span><span>{formattedPayload.split('\n').length} lines</span></div><pre><code>{formattedPayload}</code></pre></div></> : <div className="empty-detail">{status === 'disconnected' ? <CircleAlert size={36} /> : <Radio size={36} />}<h2>{status === 'disconnected' ? 'Server is offline' : 'No events yet'}</h2><p>Successfully published webhook messages will appear here.</p></div>}</section></section>
}

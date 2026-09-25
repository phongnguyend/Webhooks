import { useEffect, useMemo, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import LoginPage from './pages/LoginPage'
import ConfigurationPage from './pages/ConfigurationPage'
import EventsPage from './pages/EventsPage'
import UsersPage from './pages/UsersPage'
import AppHeader from './components/AppHeader'
import ProfileDialog from './components/ProfileDialog'
import TenantDialog from './components/TenantDialog'
import TopicDialog from './components/TopicDialog'
import TestPayloadDialog from './components/TestPayloadDialog'
import { X } from 'lucide-react'
import { API_URL } from './config'
import { microsoftEnabled, cancelPendingMicrosoftSignIn, startMicrosoftSignIn, completeMicrosoftSignIn } from './services/microsoftAuth'
import { startGoogleSignIn, completeGoogleSignIn } from './services/googleAuth'
import { decodeJwtPayload } from './utils/jwt'

const TOKEN_KEY = 'webhook-router-access-token'

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

// One exchange per redirect, including React StrictMode's repeated effect setup.
const initialSession = (async () => {
  sessionStorage.removeItem('webhook-router-token') // Discard legacy Google bearer sessions.
  return await completeMicrosoftSignIn(TOKEN_KEY)
    ?? await completeGoogleSignIn(TOKEN_KEY)
    ?? { token: sessionStorage.getItem(TOKEN_KEY), error: '' }
})()

function parsePayload(payload) {
  if (typeof payload !== 'string') return payload
  try { return JSON.parse(payload) } catch { return payload }
}


function eventId(event) { return event.id || `${event.receivedAt}-${event.tenantId}-${event.topicName}` }

export default function App() {
  const [accessToken, setAccessToken] = useState(null)
  const [initializingSession, setInitializingSession] = useState(true)
  const [user, setUser] = useState(null)
  const [authLoading, setAuthLoading] = useState(true)
  const [loginError, setLoginError] = useState('')
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
    let active = true
    initialSession.then(({ token, error, linked }) => {
      if (!active) return
      setAccessToken(token)
      setLoginError(error)
      if (error && token) setNotice({ kind: 'error', text: error })
      if (linked) setNotice({ kind: 'success', text: 'Microsoft account connected.' })
      setInitializingSession(false)
    })
    return () => { active = false }
  }, [])

  useEffect(() => {
    if (initializingSession) return
    let active = true
    if (!accessToken) { setUser(null); setAuthLoading(false); return }
    setAuthLoading(true)
    api('/api/auth/me')
      .then((profile) => { if (active) setUser(profile) })
      .catch(() => { if (active) { sessionStorage.removeItem(TOKEN_KEY); setAccessToken(null); setUser(null) } })
      .finally(() => { if (active) setAuthLoading(false) })
    return () => { active = false }
  }, [accessToken, initializingSession])

  useEffect(() => {
    if (!accessToken) return
    const expire = () => {
      sessionStorage.removeItem(TOKEN_KEY)
      window.dispatchEvent(new Event('webhook-router-auth-expired'))
      setLoginError('Your session expired. Please sign in again.')
    }
    try {
      const expiresAt = Number(decodeJwtPayload(accessToken).exp) * 1000
      if (!Number.isFinite(expiresAt)) { expire(); return }
      const timer = window.setTimeout(expire, Math.max(0, expiresAt - Date.now()))
      return () => window.clearTimeout(timer)
    } catch { expire() }
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

  function signInGoogle() {
    setLoginError('')
    cancelPendingMicrosoftSignIn()
    try { startGoogleSignIn() }
    catch (error) { setLoginError(error.message) }
  }

  async function signInMicrosoft(link = false) {
    setLoginError('')
    try { await startMicrosoftSignIn(link ? user.id : null) }
    catch (error) { if (link) showError(error); else setLoginError(error.message) }
  }

  function logout() {
    setView('manage')
    setDialog(null)
    sessionStorage.removeItem(TOKEN_KEY)
    setAccessToken(null)
    setUser(null)
    setTenants([])
    setTopics([])
    setEvents([])
  }

  if (authLoading || !user) return <LoginPage loading={authLoading} error={loginError} onSignIn={signInGoogle} onMicrosoftSignIn={() => signInMicrosoft()} microsoftEnabled={microsoftEnabled} />

  return (
    <main className="app-shell">
      <AppHeader view={view} onViewChange={setView} eventCount={events.length} user={user} displayName={userDisplayName} connectionStatus={connectionStatus} onProfile={() => setDialog({ type: 'profile' })} onSignOut={logout} />

      {notice && <div className={`toast ${notice.kind}`}><span>{notice.text}</span><button onClick={() => setNotice(null)} aria-label="Dismiss"><X size={15} /></button></div>}

      {view === 'users' && user.roles?.includes('Global Admin') ? <UsersPage api={api} currentUser={user} /> : view === 'manage' ? (
        <ConfigurationPage currentUser={user} tenants={tenants} selectedTenantId={selectedTenantId} setSelectedTenantId={setSelectedTenantId} selectedTenant={selectedTenant} topics={topics} loading={loading} copied={copied} copy={copy} setDialog={setDialog} toggleTenant={toggleTenant} deleteTenant={deleteTenant} toggleTopic={toggleTopic} deleteTopic={deleteTopic} />
      ) : (
        <EventsPage events={events} filteredEvents={filteredEvents} selectedEvent={selectedEvent} selectedEventId={selectedEventId} setSelectedEventId={setSelectedEventId} query={query} setQuery={setQuery} formattedPayload={formattedPayload} copied={copied} copy={copy} status={connectionStatus} />
      )}

      {dialog?.type === 'tenant' && <TenantDialog item={dialog.item} onClose={() => setDialog(null)} onSave={saveTenant} />}
      {dialog?.type === 'topic' && <TopicDialog item={dialog.item} onClose={() => setDialog(null)} onSave={saveTopic} />}
      {dialog?.type === 'test' && <TestPayloadDialog tenantId={selectedTenantId} topic={dialog.item} onClose={() => setDialog(null)} onSend={sendTestPayload} />}
      {dialog?.type === 'profile' && <ProfileDialog user={user} onClose={() => setDialog(null)} onSave={saveProfile} onSignOut={logout} onConnectMicrosoft={microsoftEnabled ? () => signInMicrosoft(true) : null} />}
    </main>
  )
}

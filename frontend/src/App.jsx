import { useEffect, useMemo, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import { Braces, Check, ChevronRight, CircleAlert, Clock3, Copy, Radio, RotateCw, Search, Webhook } from 'lucide-react'

const API_URL = (import.meta.env.VITE_API_URL || 'http://localhost:5229').replace(/\/$/, '')

function parsePayload(payload) {
  if (typeof payload !== 'string') return payload
  try {
    return JSON.parse(payload)
  } catch {
    return payload
  }
}

function formatDate(value) {
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? 'Unknown time'
    : new Intl.DateTimeFormat(undefined, {
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
      }).format(date)
}

function eventId(event) {
  return event.id || `${event.receivedAt}-${event.tenantId}-${event.topicName}`
}

export default function App() {
  const [events, setEvents] = useState([])
  const [selectedId, setSelectedId] = useState(null)
  const [status, setStatus] = useState('connecting')
  const [query, setQuery] = useState('')
  const [copied, setCopied] = useState(false)

  useEffect(() => {
    let active = true
    let connection

    async function connect() {
      try {
        const response = await fetch(`${API_URL}/`)
        if (response.ok) {
          const history = await response.json()
          if (active) {
            setEvents(history)
            setSelectedId((current) => current || (history[0] ? eventId(history[0]) : null))
          }
        }

        connection = new signalR.HubConnectionBuilder()
          .withUrl(`${API_URL}/hubs/events`)
          .withAutomaticReconnect()
          .configureLogging(signalR.LogLevel.Warning)
          .build()

        connection.on('WebhookReceived', (event) => {
          if (!active) return
          setEvents((current) => [event, ...current])
          setSelectedId((current) => current || eventId(event))
        })
        connection.onreconnecting(() => active && setStatus('reconnecting'))
        connection.onreconnected(() => active && setStatus('connected'))
        connection.onclose(() => active && setStatus('disconnected'))

        await connection.start()
        if (active) setStatus('connected')
      } catch (error) {
        console.error('Unable to connect to the webhook server.', error)
        if (active) setStatus('disconnected')
      }
    }

    connect()
    return () => {
      active = false
      connection?.stop()
    }
  }, [])

  const indexedEvents = useMemo(
    () => events.map((event) => ({ ...event, _id: eventId(event) })),
    [events],
  )
  const filteredEvents = useMemo(() => {
    const normalized = query.trim().toLowerCase()
    if (!normalized) return indexedEvents
    return indexedEvents.filter((event) =>
      [event.tenantId, event.topicName, event.payload].some((value) =>
        String(value || '').toLowerCase().includes(normalized),
      ),
    )
  }, [indexedEvents, query])
  const selectedEvent = indexedEvents.find((event) => event._id === selectedId) || indexedEvents[0]
  const formattedPayload = selectedEvent
    ? JSON.stringify(parsePayload(selectedEvent.payload), null, 2)
    : ''

  async function copyPayload() {
    if (!formattedPayload) return
    await navigator.clipboard.writeText(formattedPayload)
    setCopied(true)
    window.setTimeout(() => setCopied(false), 1600)
  }

  return (
    <main className="app-shell">
      <header className="topbar">
        <div className="brand">
          <span className="brand-mark"><Webhook size={21} strokeWidth={2.2} /></span>
          <div>
            <h1>Webhook Inspector</h1>
            <p>Live event stream</p>
          </div>
        </div>
        <div className={`connection-pill ${status}`}>
          <span className="pulse-dot" />
          {status === 'connected' ? 'Live' : status === 'connecting' ? 'Connecting' : status === 'reconnecting' ? 'Reconnecting' : 'Offline'}
        </div>
      </header>

      <section className="workspace">
        <aside className="event-panel">
          <div className="panel-heading">
            <div>
              <span className="eyebrow">Incoming</span>
              <h2>Events <span>{events.length}</span></h2>
            </div>
            <Radio size={20} aria-hidden="true" />
          </div>

          <label className="search-field">
            <Search size={17} aria-hidden="true" />
            <span className="sr-only">Search events</span>
            <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Filter events…" />
          </label>

          <div className="event-list" role="list">
            {filteredEvents.map((event) => (
              <button
                className={`event-card ${selectedEvent?._id === event._id ? 'selected' : ''}`}
                key={event._id}
                onClick={() => setSelectedId(event._id)}
                type="button"
                role="listitem"
              >
                <span className="event-icon"><Braces size={17} /></span>
                <span className="event-copy">
                  <strong>{event.topicName || 'Untitled event'}</strong>
                  <span className="tenant">{event.tenantId || 'No tenant'}</span>
                  <span className="timestamp"><Clock3 size={12} /> {formatDate(event.receivedAt)}</span>
                </span>
                <ChevronRight className="chevron" size={17} />
              </button>
            ))}

            {filteredEvents.length === 0 && (
              <div className="empty-list">
                <Radio size={24} />
                <strong>{events.length ? 'No matching events' : 'Waiting for events'}</strong>
                <span>{events.length ? 'Try a different filter.' : 'New webhooks will appear here instantly.'}</span>
              </div>
            )}
          </div>
        </aside>

        <section className="detail-panel">
          {selectedEvent ? (
            <>
              <div className="detail-header">
                <div>
                  <span className="eyebrow">Event payload</span>
                  <h2>{selectedEvent.topicName || 'Untitled event'}</h2>
                  <div className="metadata">
                    <span>{selectedEvent.tenantId || 'No tenant'}</span>
                    <i />
                    <span>{formatDate(selectedEvent.receivedAt)}</span>
                  </div>
                </div>
                <button className="copy-button" onClick={copyPayload} type="button">
                  {copied ? <Check size={16} /> : <Copy size={16} />}
                  {copied ? 'Copied' : 'Copy JSON'}
                </button>
              </div>
              <div className="code-wrap">
                <div className="code-toolbar">
                  <span><span className="file-dot" /> payload.json</span>
                  <span>{formattedPayload.split('\n').length} lines</span>
                </div>
                <pre><code>{formattedPayload}</code></pre>
              </div>
            </>
          ) : (
            <div className="empty-detail">
              {status === 'disconnected' ? <CircleAlert size={36} /> : <RotateCw size={36} />}
              <h2>{status === 'disconnected' ? 'Server is offline' : 'No event selected'}</h2>
              <p>{status === 'disconnected' ? `Start the API at ${API_URL} to receive events.` : 'Incoming webhook payloads will be displayed here.'}</p>
            </div>
          )}
        </section>
      </section>
    </main>
  )
}

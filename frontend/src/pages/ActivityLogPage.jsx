import { useEffect, useState } from 'react'
import { History, RefreshCw, Search, ChevronLeft, ChevronRight } from 'lucide-react'
import SearchableSelect from '../components/SearchableSelect'
import { formatDateTime as formatActivityTime } from '../utils/dateTime'
import './users.css'
import './activity-log.css'

const events = {
  TenantCreated: 'Tenant created', TenantUpdated: 'Tenant updated', TenantEnabled: 'Tenant enabled', TenantDisabled: 'Tenant disabled', TenantDeleted: 'Tenant deleted',
  TopicCreated: 'Topic created', TopicUpdated: 'Topic updated', TopicEnabled: 'Topic enabled', TopicDisabled: 'Topic disabled', TopicDeleted: 'Topic deleted',
  LoginSucceeded: 'Login succeeded', LoginFailed: 'Login failed', UserLockedOut: 'User locked out',
  PasswordChanged: 'Password changed', AccountEnabled: 'Account enabled', AccountDisabled: 'Account disabled',
  PasswordAuthenticationEnabled: 'Password authentication enabled', PasswordAuthenticationDisabled: 'Password authentication disabled',
}

const categories = {
  Authentication: ['LoginSucceeded', 'LoginFailed', 'UserLockedOut'],
  Users: ['PasswordChanged', 'AccountEnabled', 'AccountDisabled', 'PasswordAuthenticationEnabled', 'PasswordAuthenticationDisabled'],
  Tenants: ['TenantCreated', 'TenantUpdated', 'TenantEnabled', 'TenantDisabled', 'TenantDeleted'],
  Topics: ['TopicCreated', 'TopicUpdated', 'TopicEnabled', 'TopicDisabled', 'TopicDeleted'],
}

function ActivityMetadata({ value }) {
  if (!value) return '—'
  let formatted
  try {
    const parsed = JSON.parse(value)
    if (parsed === null || (typeof parsed === 'object' && Object.keys(parsed).length === 0)) return '—'
    formatted = JSON.stringify(parsed, null, 2)
  } catch { formatted = value }
  return <details><summary>View metadata</summary><pre className="activity-metadata">{formatted}</pre></details>
}

export default function ActivityLogPage({ api, initialUser = null }) {
  const [result, setResult] = useState({ items: [], total: 0 })
  const [page, setPage] = useState(1)
  const [eventType, setEventType] = useState('')
  const [category, setCategory] = useState('')
  const [input, setInput] = useState(initialUser?.id || '')
  const [search, setSearch] = useState(initialUser?.id || '')
  const [fromInput, setFromInput] = useState('')
  const [toInput, setToInput] = useState('')
  const [range, setRange] = useState({ from: '', to: '' })
  const [rangeError, setRangeError] = useState('')
  const [refresh, setRefresh] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  useEffect(() => {
    let active = true
    setLoading(true)
    setError('')
    const query = new URLSearchParams({ page: String(page), pageSize: '25', eventType, category, search })
    if (range.from) query.set('from', range.from)
    if (range.to) query.set('to', range.to)
    if (initialUser && search === initialUser.id) {
      query.delete('search')
      query.set('userId', initialUser.id)
    }
    api(`/api/activity-logs?${query}`).then(value => { if (active) setResult(value) })
      .catch(failure => { if (active) setError(failure.message) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [api, page, eventType, category, search, range, refresh, initialUser])

  function applyFilters(event) {
    event.preventDefault()
    const from = fromInput ? new Date(fromInput) : null
    const to = toInput ? new Date(toInput) : null
    if ((from && !Number.isFinite(from.getTime())) || (to && !Number.isFinite(to.getTime()))) {
      setRangeError('Enter a valid date and time.'); return
    }
    if (from && to && from > to) {
      setRangeError('From must be earlier than or equal to To.'); return
    }
    setRangeError('')
    setRange({ from: from?.toISOString() || '', to: to?.toISOString() || '' })
    setPage(1); setSearch(input.trim()); setRefresh(value => value + 1)
  }

  return <section className="users-workspace activity-workspace">
    <div className="content-heading"><div><span className="eyebrow">Administration</span><h2><History size={22} aria-hidden="true" /> Activity log</h2><p>Activity across application entities. Times are shown in your local timezone.</p></div><button className="secondary-button" disabled={loading} onClick={() => setRefresh(value => value + 1)}><RefreshCw size={15} aria-hidden="true" /> Refresh</button></div>
    {initialUser && search === initialUser.id && <p>Activities involving {initialUser.email || initialUser.username}, as the affected user or acting user.</p>}
    <form className="activity-filters" onSubmit={applyFilters}>
      <SearchableSelect label="Category" value={category} onChange={value => { setCategory(value); setEventType(''); setPage(1) }} options={[{ value: '', label: 'All categories' }, ...Object.keys(categories).map(value => ({ value, label: value }))]} />
      <SearchableSelect key={category} label="Event" value={eventType} onChange={value => { setEventType(value); setPage(1) }} options={[{ value: '', label: 'All events' }, ...Object.entries(categories).filter(([name]) => !category || name === category).flatMap(([group, values]) => values.map(value => ({ value, label: events[value], group })))]} />
      <label className="form-field">Search entities or users<input maxLength={256} value={input} onChange={event => setInput(event.target.value)} placeholder="Entity name, ID, or acting user" /></label>
      <label className="form-field">From (local time)<input type="datetime-local" step="1" value={fromInput} onChange={event => { setFromInput(event.target.value); setRangeError('') }} /></label>
      <label className="form-field">To (local time)<input type="datetime-local" step="1" value={toInput} onChange={event => { setToInput(event.target.value); setRangeError('') }} /></label>
      <div className="activity-filter-actions">
        <button className="secondary-button" type="button" disabled={!fromInput && !toInput && !range.from && !range.to} onClick={() => { setFromInput(''); setToInput(''); setRange({ from: '', to: '' }); setRangeError(''); setPage(1) }}>Clear dates</button>
        <button className="primary-button" type="submit"><Search size={15} aria-hidden="true" /> Search</button>
      </div>
    </form>
    {rangeError && <p className="form-error" role="alert">{rangeError}</p>}
    {error && <p className="form-error" role="alert">{error}</p>}
    {loading ? <p role="status">Loading activity…</p> : !error && <>
      <div className="users-table-wrap"><table className="users-table"><thead><tr><th>Time</th><th>Event</th><th>Entity</th><th>Performed by</th><th>Additional information</th></tr></thead><tbody>
        {result.items.map(item => <tr key={item.id}><td><time dateTime={item.occurredAt}>{formatActivityTime(item.occurredAt)}</time></td><td>{events[item.eventType] || item.eventType}</td><td><small className="activity-user-id">{item.entityType}</small><span>{item.entityName || 'Unknown entity'}</span>{item.entityId && <small className="activity-user-id">{item.entityId}</small>}</td><td><span>{item.actorUsername || 'Unauthenticated / system'}</span>{item.actorUserId && <small className="activity-user-id">{item.actorUserId}</small>}</td><td><ActivityMetadata value={item.metadata} /></td></tr>)}
        {!result.items.length && <tr><td colSpan={5}>No activity found.</td></tr>}
      </tbody></table></div>
      <div className="activity-pagination"><span>{result.total} entries · Page {page} of {Math.max(1, Math.ceil(result.total / 25))}</span><div className="header-actions"><button className="secondary-button" disabled={page === 1} onClick={() => setPage(value => value - 1)}><ChevronLeft size={15} aria-hidden="true" /> Previous</button><button className="secondary-button" disabled={page * 25 >= result.total} onClick={() => setPage(value => value + 1)}>Next <ChevronRight size={15} aria-hidden="true" /></button></div></div>
    </>}
  </section>
}

import { useEffect, useState } from 'react'
import { Search, Users } from 'lucide-react'
import './users.css'

export default function UsersPanel({ api, currentUser }) {
  const [users, setUsers] = useState([])
  const [query, setQuery] = useState('')
  const [loading, setLoading] = useState(true)
  const [busyId, setBusyId] = useState(null)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [selected, setSelected] = useState(null)

  useEffect(() => {
    let active = true
    api('/api/users').then((result) => { if (active) setUsers(result) })
      .catch((failure) => { if (active) setError(failure.message) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [api])

  async function toggle(user) {
    if (!window.confirm(`${user.isEnabled ? 'Disable' : 'Enable'} ${user.email}?`)) return
    setBusyId(user.id)
    setError('')
    setNotice('')
    try {
      await api(`/api/users/${user.id}/enabled`, { method: 'PATCH', body: JSON.stringify({ isEnabled: !user.isEnabled }) })
      const updated = await api('/api/users')
      setUsers(updated)
      setSelected((current) => current ? updated.find((item) => item.id === current.id) || null : null)
      setNotice(`Account ${user.isEnabled ? 'disabled' : 'enabled'}.`)
    } catch (failure) { setError(failure.message) }
    finally { setBusyId(null) }
  }

  const filtered = users.filter((user) => [user.email, user.firstName, user.lastName, user.phoneNumber, ...user.roles].some((value) => value?.toLowerCase().includes(query.trim().toLowerCase())))
  return <section className="users-workspace">
    <div className="content-heading"><div><span className="eyebrow">Administration</span><h2><Users size={22} /> Users</h2><p>Manage access to Webhook Router.</p></div><span>{users.length} accounts</span></div>
    <label className="search-field"><Search size={17} /><span className="sr-only">Search users</span><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search name, email, phone, or role" /></label>
    {error && <p className="form-error" role="alert">{error}</p>}
    {notice && <p role="status">{notice}</p>}
    {loading ? <p role="status">Loading users…</p> : <div className="users-table-wrap"><table className="users-table"><thead><tr><th>Name</th><th>Email</th><th>Roles</th><th>Status</th><th>Actions</th></tr></thead><tbody>
      {filtered.map((user) => <tr key={user.id}><td>{[user.firstName, user.lastName].filter(Boolean).join(' ') || '—'}{user.id === currentUser.id && ' (you)'}</td><td>{user.email}</td><td>{user.roles.join(', ')}</td><td><span className={`status-badge ${user.isEnabled ? 'enabled' : ''}`}>{user.isEnabled ? 'Enabled' : 'Disabled'}</span></td><td><div className="header-actions"><button className="secondary-button" onClick={() => setSelected(user)}>View</button><button className="secondary-button" disabled={busyId !== null || user.id === currentUser.id} title={user.id === currentUser.id ? 'You cannot disable your own account' : undefined} onClick={() => toggle(user)}>{busyId === user.id ? 'Saving…' : user.isEnabled ? 'Disable' : 'Enable'}</button></div></td></tr>)}
      {!filtered.length && <tr><td colSpan={5}>No users found.</td></tr>}
    </tbody></table></div>}
    {selected && <div className="modal-backdrop" onMouseDown={(event) => event.target === event.currentTarget && setSelected(null)}><section className="modal" role="dialog" aria-modal="true" aria-labelledby="user-details-title" onKeyDown={(event) => event.key === 'Escape' && setSelected(null)}><div className="modal-header"><h2 id="user-details-title">User details</h2><button autoFocus onClick={() => setSelected(null)} aria-label="Close">✕</button></div><dl className="modal-body user-details">{[['Name', [selected.firstName, selected.lastName].filter(Boolean).join(' ')], ['Email', selected.email], ['Username', selected.username], ['Phone', selected.phoneNumber], ['User ID', selected.id], ['Roles', selected.roles.join(', ')], ['Status', selected.isEnabled ? 'Enabled' : 'Disabled']].map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value || '—'}</dd></div>)}</dl><div className="modal-footer"><button className="primary-button" onClick={() => setSelected(null)}>Close</button></div></section></div>}
  </section>
}

import { useEffect, useState } from 'react'
import { Search, Users, UserPlus, Eye, Pencil, KeyRound, UserCheck, UserX, History } from 'lucide-react'
import './users.css'
import UserEditor from '../components/UserEditor'
import UserPasswordDialog from '../components/UserPasswordDialog'
import UserDetailsDialog from '../components/UserDetailsDialog'
import useConfirmation from '../components/useConfirmation'
import { formatDateTime } from '../utils/dateTime'

function lockoutStatus(user, now) {
  if (!user.lockoutEnabled) return 'Not enabled'
  return user.lockoutEnd && Date.parse(user.lockoutEnd) > now ? 'Locked out' : 'Not locked'
}

function lockoutTime(value) {
  return formatDateTime(value, 'None')
}

export default function UsersPage({ api, currentUser, onViewActivities }) {
  const { confirm, confirmationDialog } = useConfirmation()
  const [users, setUsers] = useState([])
  const [query, setQuery] = useState('')
  const [loading, setLoading] = useState(true)
  const [busyId, setBusyId] = useState(null)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [selected, setSelected] = useState(null)
  const [editor, setEditor] = useState(null)
  const [passwordUser, setPasswordUser] = useState(null)
  const [now, setNow] = useState(Date.now)

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [])

  async function saved() {
    setEditor(null)
    setPasswordUser(null)
    setError('')
    setNotice('User saved.')
    try { setUsers(await api('/api/users')) }
    catch (failure) { setError(`User was saved, but the list could not refresh: ${failure.message}`) }
  }

  useEffect(() => {
    let active = true
    api('/api/users').then((result) => { if (active) setUsers(result) })
      .catch((failure) => { if (active) setError(failure.message) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [api])

  async function toggle(user) {
    if (!await confirm({ title: user.isEnabled ? 'Disable user' : 'Enable user', message: user.isEnabled ? `Disable ${user.email}? They will no longer be able to access the application.` : `Enable ${user.email} and restore their access?`, confirmLabel: user.isEnabled ? 'Disable user' : 'Enable user', destructive: user.isEnabled })) return
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
    <div className="content-heading"><div><span className="eyebrow">Administration</span><h2><Users size={22} /> Users</h2><p>Manage access to Webhook Router.</p></div><div className="header-actions"><span>{users.length} accounts</span><button className="primary-button" disabled={busyId !== null} onClick={() => setEditor({ user: null })}><UserPlus size={15} aria-hidden="true" /> Create user</button></div></div>
    <label className="search-field"><Search size={17} /><span className="sr-only">Search users</span><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search name, email, phone, or role" /></label>
    {error && <p className="form-error" role="alert">{error}</p>}
    {notice && <p role="status">{notice}</p>}
    {loading ? <p role="status">Loading users…</p> : <div className="users-table-wrap"><table className="users-table"><thead><tr><th>Name</th><th>Email</th><th>Roles</th><th>Status</th><th>Password authentication</th><th>Lockout</th><th>Actions</th></tr></thead><tbody>
      {filtered.map((user) => <tr key={user.id}><td>{[user.firstName, user.lastName].filter(Boolean).join(' ') || '—'}{user.id === currentUser.id && ' (you)'}</td><td>{user.email}</td><td>{user.roles.join(', ')}</td><td><span className={`status-badge ${user.isEnabled ? 'enabled' : ''}`}>{user.isEnabled ? 'Enabled' : 'Disabled'}</span></td><td><span className={`status-badge ${user.allowPasswordAuthentication ? 'enabled' : ''}`}>{user.allowPasswordAuthentication ? 'Enabled' : 'Disabled'}</span></td><td><div className="user-lockout"><span className={`status-badge ${lockoutStatus(user, now) === 'Locked out' ? 'locked-out' : ''}`}>{lockoutStatus(user, now)}</span>{lockoutStatus(user, now) === 'Locked out' && <small>Until {lockoutTime(user.lockoutEnd)}</small>}<small>Failed attempts: {user.accessFailedCount ?? 0}</small></div></td><td><div className="header-actions"><button className="secondary-button" onClick={() => setSelected(user)}><Eye size={15} aria-hidden="true" /> View</button><button className="secondary-button" onClick={() => onViewActivities(user)}><History size={15} aria-hidden="true" /> View activities</button><button className="secondary-button" disabled={busyId !== null} onClick={() => setEditor({ user })}><Pencil size={15} aria-hidden="true" /> Edit</button><button className="secondary-button" disabled={busyId !== null} onClick={() => setPasswordUser(user)}><KeyRound size={15} aria-hidden="true" /> Password authentication</button><button className="secondary-button" disabled={busyId !== null || user.id === currentUser.id} title={user.id === currentUser.id ? 'You cannot disable your own account' : undefined} onClick={() => toggle(user)}>{user.isEnabled ? <UserX size={15} aria-hidden="true" /> : <UserCheck size={15} aria-hidden="true" />}{busyId === user.id ? 'Saving…' : user.isEnabled ? 'Disable' : 'Enable'}</button></div></td></tr>)}
      {!filtered.length && <tr><td colSpan={7}>No users found.</td></tr>}
    </tbody></table></div>}
    {selected && <UserDetailsDialog user={selected} lockoutStatus={lockoutStatus(selected, now)} lockoutEnd={lockoutTime(selected.lockoutEnd)} onClose={() => setSelected(null)} />}
    {passwordUser && <UserPasswordDialog user={passwordUser} api={api} onSaved={saved} onClose={() => setPasswordUser(null)} />}
    {editor && <UserEditor user={editor.user} currentUser={currentUser} api={api} onSaved={saved} onClose={() => setEditor(null)} />}
    {confirmationDialog}
  </section>
}

import { useState } from 'react'

export default function UserEditor({ user, currentUser, api, onSaved, onClose }) {
  const [form, setForm] = useState({ email: user?.email || '', firstName: user?.firstName || '', lastName: user?.lastName || '', phoneNumber: user?.phoneNumber || '', isEnabled: user?.isEnabled ?? true, roles: user?.roles || ['User'] })
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const isSelf = user?.id === currentUser.id
  function field(name, value) { setForm((previous) => ({ ...previous, [name]: value })) }
  async function submit(event) {
    event.preventDefault()
    if (!form.roles.length) { setError('Select at least one role.'); return }
    setSaving(true)
    setError('')
    try {
      await api(user ? `/api/users/${user.id}` : '/api/users', { method: user ? 'PUT' : 'POST', body: JSON.stringify(form) })
    } catch (failure) { setError(failure.message); setSaving(false); return }
    onSaved()
  }
  return <div className="modal-backdrop" onMouseDown={(event) => !saving && event.target === event.currentTarget && onClose()}>
    <form className="modal" role="dialog" aria-modal="true" aria-labelledby="user-editor-title" onSubmit={submit} onKeyDown={(event) => event.key === 'Escape' && !saving && onClose()}>
      <div className="modal-header"><h2 id="user-editor-title">{user ? 'Edit user' : 'Create user'}</h2><button type="button" className="icon-button" disabled={saving} onClick={onClose} aria-label="Close">×</button></div>
      <div className="modal-body">
        <p className="user-editor-help">The user first signs in with Google using this email, then can connect Microsoft from their profile. No password or invitation email is sent.</p>
        <label className="form-field">Email<input autoFocus type="email" required maxLength={256} value={form.email} readOnly={Boolean(user)} disabled={saving} onChange={(event) => field('email', event.target.value)} /></label>
        {user && <p className="user-editor-help">Email cannot be changed after the user is created.</p>}
        <label className="form-field">First name<input maxLength={100} value={form.firstName} disabled={saving} onChange={(event) => field('firstName', event.target.value)} /></label>
        <label className="form-field">Last name<input maxLength={100} value={form.lastName} disabled={saving} onChange={(event) => field('lastName', event.target.value)} /></label>
        <label className="form-field">Phone<input type="tel" maxLength={50} value={form.phoneNumber} disabled={saving} onChange={(event) => field('phoneNumber', event.target.value)} /></label>
        <fieldset className="user-role-options" disabled={saving}><legend>Roles</legend>{['User', 'Global Admin'].map((role) => <label key={role}><input type="checkbox" checked={form.roles.includes(role)} disabled={isSelf && role === 'Global Admin'} onChange={(event) => field('roles', event.target.checked ? [...form.roles, role] : form.roles.filter((item) => item !== role))} />{role}</label>)}</fieldset>
        <label className="user-enabled-option"><input type="checkbox" checked={form.isEnabled} disabled={saving || isSelf} onChange={(event) => field('isEnabled', event.target.checked)} /> Account enabled</label>
        {isSelf && <p className="user-editor-help">You cannot disable yourself or remove your own Global Admin role.</p>}
        {error && <p className="form-error" role="alert">{error}</p>}
      </div>
      <div className="modal-footer"><button type="button" className="secondary-button" disabled={saving} onClick={onClose}>Cancel</button><button className="primary-button" disabled={saving}>{saving ? 'Saving…' : user ? 'Save changes' : 'Create user'}</button></div>
    </form>
  </div>
}

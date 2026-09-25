import { useState } from 'react'
import { KeyRound, Save, X } from 'lucide-react'

export default function UserPasswordDialog({ user, api, onSaved, onClose }) {
  const [enabled, setEnabled] = useState(user.allowPasswordAuthentication)
  const [password, setPassword] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  async function submit(event) {
    event.preventDefault()
    setSaving(true)
    setError('')
    try {
      await api(`/api/users/${user.id}/password-authentication`, {
        method: 'PUT', body: JSON.stringify({ allowPasswordAuthentication: enabled, password: enabled ? password : null }),
      })
    } catch (failure) { setError(failure.message); setSaving(false); return }
    onSaved()
  }

  return <div className="modal-backdrop" onMouseDown={(event) => !saving && event.target === event.currentTarget && onClose()}>
    <form className="modal" role="dialog" aria-modal="true" aria-labelledby="password-dialog-title" onSubmit={submit} onKeyDown={(event) => event.key === 'Escape' && !saving && onClose()}>
      <div className="modal-header"><h2 className="user-dialog-title" id="password-dialog-title"><KeyRound size={19} aria-hidden="true" />Password authentication</h2><button type="button" disabled={saving} onClick={onClose} aria-label="Close"><X size={18} aria-hidden="true" /></button></div>
      <div className="modal-body">
        <p className="user-editor-help">Manage password access for {user.email}.</p>
        <label className="user-enabled-option"><input autoFocus type="checkbox" checked={enabled} disabled={saving} onChange={(event) => { setEnabled(event.target.checked); setPassword('') }} /> Enable password authentication</label>
        {enabled && <>
          <label className="form-field">{user.hasPassword ? 'New password' : 'Initial password'}<input type="password" autoComplete="new-password" minLength={12} maxLength={1024} required={!user.hasPassword} value={password} disabled={saving} onChange={(event) => setPassword(event.target.value)} /></label>
          <p className="user-editor-help">Use at least 12 characters with uppercase, lowercase, a number and a symbol. {user.hasPassword && 'Leave blank to keep the current password. '}Enabling this option approves password sign-in without email verification. Share credentials securely.</p>
        </>}
        <p className="user-editor-help">Changing the password or this setting signs the user out of existing sessions. Profile details and roles are not changed.</p>
        {error && <p className="form-error" role="alert">{error}</p>}
      </div>
      <div className="modal-footer"><button type="button" className="secondary-button" disabled={saving} onClick={onClose}><X size={15} aria-hidden="true" /> Cancel</button><button className="primary-button" disabled={saving}><Save size={15} aria-hidden="true" />{saving ? 'Saving…' : 'Save changes'}</button></div>
    </form>
  </div>
}

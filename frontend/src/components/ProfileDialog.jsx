import { useState } from 'react'
import { Check, Copy, X } from 'lucide-react'
import Field from './Field'

export default function ProfileDialog({ user, onClose, onSave, onSignOut, onConnectMicrosoft }) {
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
        <div className="profile-summary"><span>{(user.firstName || user.email).slice(0, 1).toUpperCase()}</span><div><strong>{displayName}</strong><small>Webhook Router account</small></div></div>
        <div className="field-row"><Field label="First name"><input autoFocus maxLength="100" value={form.firstName} onChange={(event) => setForm({ ...form, firstName: event.target.value })} placeholder="First name" /></Field><Field label="Last name"><input maxLength="100" value={form.lastName} onChange={(event) => setForm({ ...form, lastName: event.target.value })} placeholder="Last name" /></Field></div>
        <Field label="Email" hint="Email cannot be changed after account creation."><div className="topic-input-with-copy"><input readOnly value={user.email} /><button type="button" onClick={() => copyProfileField('email', user.email)} title="Copy email" aria-label="Copy email">{copiedField === 'email' ? <Check size={15} /> : <Copy size={15} />}</button></div></Field>
        <Field label="Phone number" hint="Changing the number resets its verification status."><div className="topic-input-with-copy"><input type="tel" maxLength="50" value={form.phoneNumber} onChange={(event) => setForm({ ...form, phoneNumber: event.target.value })} placeholder="+1 555 010 2000" /><button type="button" onClick={() => copyProfileField('phone', form.phoneNumber)} disabled={!form.phoneNumber} title="Copy phone number" aria-label="Copy phone number">{copiedField === 'phone' ? <Check size={15} /> : <Copy size={15} />}</button></div></Field>
        <Field label="Application user ID" hint="Database-generated ID used for tenant ownership.">
          <div className="topic-input-with-copy"><input readOnly value={user.id} /><button type="button" onClick={() => copyProfileField('id', user.id)} title="Copy user ID" aria-label="Copy user ID">{copiedField === 'id' ? <Check size={15} /> : <Copy size={15} />}</button></div>
        </Field>
        {onConnectMicrosoft && <button type="button" className="secondary-button" disabled={saving} onClick={onConnectMicrosoft}>Connect Microsoft account</button>}
        {error && <p className="form-error" role="alert">{error}</p>}
      </div>
      <div className="modal-footer profile-footer"><button type="button" className="danger-profile-button" onClick={onSignOut}>Sign out</button><div><button type="button" className="secondary-button" onClick={onClose}>Cancel</button><button type="submit" className="primary-button" disabled={saving}>{saving ? 'Saving…' : 'Save changes'}</button></div></div>
    </form>
  </div>
}

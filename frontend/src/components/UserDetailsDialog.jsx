import { useEffect, useRef, useState } from 'react'
import { Check, Copy, UserRound, X } from 'lucide-react'

export default function UserDetailsDialog({ user, lockoutStatus, lockoutEnd, onClose }) {
  const [copiedField, setCopiedField] = useState('')
  const [copyError, setCopyError] = useState('')
  const copyTimer = useRef(null)
  useEffect(() => () => window.clearTimeout(copyTimer.current), [])

  async function copyField(label, value) {
    if (!value) return
    window.clearTimeout(copyTimer.current)
    setCopiedField('')
    setCopyError('')
    try {
      await navigator.clipboard.writeText(value)
      setCopiedField(label)
      copyTimer.current = window.setTimeout(() => setCopiedField(''), 1600)
    } catch {
      setCopyError(`Unable to copy ${label.toLowerCase()}. Please select and copy the text manually.`)
    }
  }

  const sections = [
    ['Profile', [
      ['Name', [user.firstName, user.lastName].filter(Boolean).join(' ')],
      ['Phone', user.phoneNumber],
      ['Email', user.email],
      ['Username', user.username],
      ['User ID', user.id, true],
    ]],
    ['Access', [
      ['Roles', user.roles.join(', ')],
      ['Account status', user.isEnabled ? 'Enabled' : 'Disabled'],
      ['Password authentication', user.allowPasswordAuthentication ? 'Enabled' : 'Disabled'],
      ['Password configured', user.hasPassword ? 'Yes' : 'No'],
    ]],
    ['Lockout', [
      ['Lockout enabled', user.lockoutEnabled ? 'Yes' : 'No'],
      ['Lockout status', lockoutStatus],
      ['Failed login attempts', String(user.accessFailedCount ?? 0)],
      ['Lockout end (local time)', lockoutEnd],
    ]],
  ]

  return <div className="modal-backdrop" onMouseDown={(event) => event.target === event.currentTarget && onClose()}>
    <section className="modal user-details-modal" role="dialog" aria-modal="true" aria-labelledby="user-details-title" onKeyDown={(event) => event.key === 'Escape' && onClose()}>
      <div className="modal-header"><h2 className="user-dialog-title" id="user-details-title"><UserRound size={19} aria-hidden="true" />User details</h2><button autoFocus onClick={onClose} aria-label="Close"><X size={18} aria-hidden="true" /></button></div>
      <div className="user-details-content">
        {sections.map(([title, fields]) => <section className="user-details-section" key={title} aria-label={title}>
          <h3>{title}</h3>
          <dl className="user-details user-details-grid">
            {fields.map(([label, value, wide]) => <div key={label} className={wide ? 'user-details-wide' : undefined}>
              <dt>{label}</dt>
              <dd className={title === 'Profile' ? 'user-details-copy-value' : undefined}>
                <span>{value || '—'}</span>
                {title === 'Profile' && <button type="button" className="user-details-copy" disabled={!value} onClick={() => copyField(label, value)} aria-label={`Copy ${label.toLowerCase()}`} title={copiedField === label ? 'Copied' : `Copy ${label.toLowerCase()}`}>
                  {copiedField === label ? <Check size={14} /> : <Copy size={14} />}
                </button>}
              </dd>
            </div>)}
          </dl>
        </section>)}
        <span className="sr-only" role="status">{copiedField ? `${copiedField} copied` : ''}</span>
        {copyError && <p className="form-error" role="alert">{copyError}</p>}
      </div>
      <div className="modal-footer"><button className="primary-button" onClick={onClose}><X size={15} aria-hidden="true" /> Close</button></div>
    </section>
  </div>
}

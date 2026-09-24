import './record-timestamps.css'

const formatter = new Intl.DateTimeFormat(undefined, {
  year: 'numeric', month: 'short', day: 'numeric',
  hour: '2-digit', minute: '2-digit', second: '2-digit', timeZoneName: 'short',
})

function Timestamp({ value }) {
  const date = value ? new Date(value) : null
  if (!date || Number.isNaN(date.getTime())) return <span>Not available</span>
  return <time dateTime={date.toISOString()} title={date.toISOString()}>{formatter.format(date)}</time>
}

function AuditUser({ user }) {
  if (!user) return <span className="audit-user">By: Not recorded</span>
  const name = [user.firstName, user.lastName].filter(Boolean).join(' ')
  return <span className="audit-user" title={`User ID: ${user.id}`}>
    By: {name || user.email || user.id}
    {name && user.email && <span className="audit-email">{user.email}</span>}
  </span>
}

export default function RecordTimestamps({ createdAt, updatedAt, createdByUser, updatedByUser }) {
  return <dl className="record-timestamps">
    <div><dt>Created</dt><dd><Timestamp value={createdAt} /><AuditUser user={createdByUser} /></dd></div>
    <div><dt>Last updated</dt><dd><Timestamp value={updatedAt} /><AuditUser user={updatedByUser} /></dd></div>
  </dl>
}

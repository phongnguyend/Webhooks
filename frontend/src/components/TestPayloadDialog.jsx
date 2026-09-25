import { useState } from 'react'
import { Send } from 'lucide-react'
import Dialog from './Dialog'
import Field from './Field'
import { API_URL } from '../config'

export default function TestPayloadDialog({ tenantId, topic, onClose, onSend }) {
  const [payload, setPayload] = useState('{\n  "message": "Hello from Webhook Router"\n}')
  const [error, setError] = useState('')
  const [sending, setSending] = useState(false)

  async function submit() {
    try {
      JSON.parse(payload)
    } catch {
      setError('Enter a valid JSON payload.')
      return
    }

    setError('')
    setSending(true)
    try { await onSend(payload) } finally { setSending(false) }
  }

  return <Dialog title={`Test ${topic.name}`} icon={Send} submitIcon={Send} onClose={onClose} onSubmit={submit} submitLabel={sending ? 'Sending…' : 'Send payload'} submitDisabled={sending}>
    <div className="test-route"><span>POST</span><code>{API_URL}/tenants/{tenantId}/topics/{topic.key}</code></div>
    <Field label="JSON payload" hint={`This uses the real webhook route and publishes to the configured Azure Service Bus ${(topic.serviceBusEntityType || 'Topic').toLowerCase()}.`}>
      <textarea autoFocus value={payload} onChange={(event) => { setPayload(event.target.value); setError('') }} spellCheck="false" />
    </Field>
    {error && <p className="form-error" role="alert">{error}</p>}
  </Dialog>
}

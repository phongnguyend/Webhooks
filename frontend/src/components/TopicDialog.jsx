import { useState } from 'react'
import { Check, Copy, Database, Pencil, Plus, Save } from 'lucide-react'
import Dialog from './Dialog'
import Field from './Field'
import Toggle from './Toggle'

const emptyTopic = {
  key: '', name: '', isEnabled: true, isSharePointWebhook: false,
  useManagedIdentity: true, fullyQualifiedNamespace: '', serviceBusConnectionString: '', serviceBusEntityName: '', serviceBusEntityType: 'Topic',
}

export default function TopicDialog({ item, onClose, onSave }) {
  const [form, setForm] = useState(item ? {
    key: item.key, name: item.name, isEnabled: item.isEnabled, isSharePointWebhook: item.isSharePointWebhook,
    useManagedIdentity: item.useManagedIdentity, fullyQualifiedNamespace: item.fullyQualifiedNamespace || '',
    serviceBusConnectionString: '', serviceBusEntityName: item.serviceBusEntityName, serviceBusEntityType: item.serviceBusEntityType || 'Topic',
  } : emptyTopic)
  const [entityCopied, setEntityCopied] = useState(false)

  async function copyServiceBusEntity() {
    if (!form.serviceBusEntityName) return
    await navigator.clipboard.writeText(form.serviceBusEntityName)
    setEntityCopied(true)
    window.setTimeout(() => setEntityCopied(false), 1600)
  }

  return <Dialog title={item ? 'Edit topic route' : 'Add topic route'} icon={item ? Pencil : Database} submitIcon={item ? Save : Plus} onClose={onClose} onSubmit={() => onSave(form)}>
    <div className="field-row"><Field label="Display name"><input required maxLength="200" autoFocus value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="Order events" /></Field><Field label="Route key"><input required maxLength="100" pattern="[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*" value={form.key} onChange={(e) => setForm({ ...form, key: e.target.value.toLowerCase() })} placeholder="orders" /></Field></div>
    <fieldset className="auth-fieldset"><legend>Authentication</legend><div className="auth-options">
      <button type="button" className={form.useManagedIdentity ? 'active' : ''} onClick={() => setForm({ ...form, useManagedIdentity: true, serviceBusConnectionString: '' })}><strong>Managed Identity</strong><small>Use this application's Azure identity</small></button>
      <button type="button" className={!form.useManagedIdentity ? 'active' : ''} onClick={() => setForm({ ...form, useManagedIdentity: false, fullyQualifiedNamespace: '' })}><strong>Connection String</strong><small>Use a namespace access key</small></button>
    </div></fieldset>
    {form.useManagedIdentity ? <Field label="Fully qualified namespace" hint="Example: my-namespace.servicebus.windows.net"><input required maxLength="300" value={form.fullyQualifiedNamespace} onChange={(e) => setForm({ ...form, fullyQualifiedNamespace: e.target.value })} placeholder="my-namespace.servicebus.windows.net" /></Field> : <Field label="Connection string" hint={item?.hasServiceBusConnection ? 'Leave blank to keep the stored connection string.' : 'The secret is stored but never returned by the API.'}><input type="password" required={!item?.hasServiceBusConnection} maxLength="2000" value={form.serviceBusConnectionString} onChange={(e) => setForm({ ...form, serviceBusConnectionString: e.target.value })} placeholder={item?.hasServiceBusConnection ? 'Connection string stored — enter to replace' : 'Endpoint=sb://…'} autoComplete="new-password" /></Field>}
    <fieldset className="auth-fieldset"><legend>Destination type</legend><div className="auth-options">
      <button type="button" className={form.serviceBusEntityType === 'Topic' ? 'active' : ''} onClick={() => setForm({ ...form, serviceBusEntityType: 'Topic' })}><strong>Topic</strong><small>Publish to a Service Bus topic</small></button>
      <button type="button" className={form.serviceBusEntityType === 'Queue' ? 'active' : ''} onClick={() => setForm({ ...form, serviceBusEntityType: 'Queue' })}><strong>Queue</strong><small>Send to a Service Bus queue</small></button>
    </div></fieldset>
    <Field label={`Azure Service Bus ${form.serviceBusEntityType.toLowerCase()}`} hint={`The ${form.serviceBusEntityType.toLowerCase()} must already exist in the selected namespace.`}>
      <div className="topic-input-with-copy">
        <input required maxLength="260" value={form.serviceBusEntityName} onChange={(e) => setForm({ ...form, serviceBusEntityName: e.target.value })} placeholder={form.serviceBusEntityType === 'Queue' ? 'incoming-orders' : 'order-created'} />
        <button type="button" onClick={copyServiceBusEntity} disabled={!form.serviceBusEntityName} title={`Copy Azure Service Bus ${form.serviceBusEntityType.toLowerCase()}`} aria-label={`Copy Azure Service Bus ${form.serviceBusEntityType.toLowerCase()}`}>
          {entityCopied ? <Check size={15} /> : <Copy size={15} />}
        </button>
      </div>
    </Field>
    <Toggle checked={form.isSharePointWebhook} onChange={(value) => setForm({ ...form, isSharePointWebhook: value })} label="SharePoint webhook" text="Echo SharePoint validation tokens without publishing them to Service Bus." />
    <Toggle checked={form.isEnabled} onChange={(value) => setForm({ ...form, isEnabled: value })} label="Topic route enabled" text="Disabled routes reject incoming webhooks." />
  </Dialog>
}

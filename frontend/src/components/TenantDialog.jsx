import { useState } from 'react'
import { Layers3, Pencil, Plus, Save } from 'lucide-react'
import Dialog from './Dialog'
import Field from './Field'
import Toggle from './Toggle'

const emptyTenant = { name: '', isEnabled: true }

export default function TenantDialog({ item, onClose, onSave }) {
  const [form, setForm] = useState(item ? { name: item.name, isEnabled: item.isEnabled } : emptyTenant)
  return <Dialog title={item ? 'Edit tenant' : 'Create tenant'} icon={item ? Pencil : Layers3} submitIcon={item ? Save : Plus} onClose={onClose} onSubmit={() => onSave(form)}>
    <Field label="Tenant name"><input required maxLength="200" autoFocus value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="Acme Corporation" /></Field>
    <Toggle checked={form.isEnabled} onChange={(value) => setForm({ ...form, isEnabled: value })} label="Tenant enabled" text="Disabled tenants reject all incoming webhooks." />
  </Dialog>
}

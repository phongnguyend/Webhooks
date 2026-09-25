import { Save, X } from 'lucide-react'

export default function Dialog({ title, icon: Icon, onClose, onSubmit, submitLabel = 'Save', submitIcon: SubmitIcon = Save, submitDisabled = false, children }) {
  return <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}><form className="modal" onSubmit={(e) => { e.preventDefault(); onSubmit() }}><div className="modal-header"><h2 className="dialog-icon-title">{Icon && <Icon size={19} aria-hidden="true" />}{title}</h2><button type="button" onClick={onClose} aria-label="Close"><X size={19} aria-hidden="true" /></button></div><div className="modal-body">{children}</div><div className="modal-footer"><button type="button" className="secondary-button" onClick={onClose}><X size={15} aria-hidden="true" /> Cancel</button><button className="primary-button" type="submit" disabled={submitDisabled}><SubmitIcon size={15} aria-hidden="true" />{submitLabel}</button></div></form></div>
}

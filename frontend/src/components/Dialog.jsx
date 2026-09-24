import { X } from 'lucide-react'

export default function Dialog({ title, onClose, onSubmit, submitLabel = 'Save', submitDisabled = false, children }) {
  return <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}><form className="modal" onSubmit={(e) => { e.preventDefault(); onSubmit() }}><div className="modal-header"><h2>{title}</h2><button type="button" onClick={onClose} aria-label="Close"><X size={19} /></button></div><div className="modal-body">{children}</div><div className="modal-footer"><button type="button" className="secondary-button" onClick={onClose}>Cancel</button><button className="primary-button" type="submit" disabled={submitDisabled}>{submitLabel}</button></div></form></div>
}

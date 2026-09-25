import { useEffect, useId, useRef } from 'react'
import { AlertTriangle, Check, X } from 'lucide-react'

export default function ConfirmationDialog({ title, message, confirmLabel = 'Confirm', destructive = false, onConfirm, onCancel }) {
  const titleId = useId()
  const messageId = useId()
  const container = useRef(null)
  const cancel = useRef(null)

  useEffect(() => {
    const previous = document.activeElement
    cancel.current?.focus()
    return () => { if (previous?.isConnected) previous.focus() }
  }, [])

  function onKeyDown(event) {
    if (event.key === 'Escape') { event.preventDefault(); onCancel(); return }
    if (event.key !== 'Tab') return
    const buttons = [...container.current.querySelectorAll('button:not(:disabled)')]
    const first = buttons[0], last = buttons[buttons.length - 1]
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus() }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() }
  }

  return <div className="modal-backdrop confirmation-backdrop" onMouseDown={(event) => event.target === event.currentTarget && onCancel()}>
    <section ref={container} className="modal confirmation-modal" role="alertdialog" aria-modal="true" aria-labelledby={titleId} aria-describedby={messageId} onKeyDown={onKeyDown}>
      <div className="modal-header"><h2 className="dialog-icon-title" id={titleId}><AlertTriangle size={19} aria-hidden="true" />{title}</h2><button type="button" onClick={onCancel} aria-label="Close"><X size={19} aria-hidden="true" /></button></div>
      <div className="modal-body"><p className="confirmation-message" id={messageId}>{message}</p></div>
      <div className="modal-footer"><button ref={cancel} type="button" className="secondary-button" onClick={onCancel}><X size={15} aria-hidden="true" /> Cancel</button><button type="button" className={`primary-button ${destructive ? 'confirmation-danger' : ''}`} onClick={onConfirm}><Check size={15} aria-hidden="true" />{confirmLabel}</button></div>
    </section>
  </div>
}

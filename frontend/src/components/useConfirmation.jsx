import { useCallback, useEffect, useRef, useState } from 'react'
import ConfirmationDialog from './ConfirmationDialog'

export default function useConfirmation() {
  const [options, setOptions] = useState(null)
  const pending = useRef(null)
  useEffect(() => () => { pending.current?.(false); pending.current = null }, [])
  const confirm = useCallback((settings) => new Promise((resolve) => {
    pending.current?.(false)
    pending.current = resolve
    setOptions(settings)
  }), [])
  function settle(value) {
    const resolve = pending.current
    pending.current = null
    setOptions(null)
    resolve?.(value)
  }
  return { confirm, confirmationDialog: options && <ConfirmationDialog {...options} onConfirm={() => settle(true)} onCancel={() => settle(false)} /> }
}

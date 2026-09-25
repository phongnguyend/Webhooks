import { useId, useRef, useState } from 'react'
import { ChevronDown } from 'lucide-react'
import './searchable-select.css'

export default function SearchableSelect({ label, value, options, onChange }) {
  const id = useId()
  const input = useRef(null)
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(0)
  const selected = options.find(option => option.value === value)
  const filtered = options.filter(option => `${option.label} ${option.group || ''}`.toLowerCase().includes(query.trim().toLowerCase()))
  const activeIndex = Math.min(active, filtered.length - 1)
  const groups = [...new Set(filtered.map(option => option.group || ''))]
  function show() { setQuery(''); setActive(0); setOpen(true) }
  function choose(option) { onChange(option.value); setOpen(false); setQuery(''); input.current?.focus() }
  return <div className="form-field searchable-select" onBlur={event => {
    if (!event.currentTarget.contains(event.relatedTarget)) setOpen(false)
  }}>
    <label htmlFor={id}>{label}</label>
    <div className="searchable-select-control">
      <input ref={input} id={id} role="combobox" aria-autocomplete="list" aria-expanded={open}
        aria-controls={`${id}-list`} aria-activedescendant={open && activeIndex >= 0 ? `${id}-option-${activeIndex}` : undefined}
        autoComplete="off" value={open ? query : selected?.label || ''} placeholder={selected?.label || 'Type to search'}
        onClick={() => { if (!open) show() }}
        onChange={event => { setOpen(true); setQuery(event.target.value); setActive(0) }}
        onKeyDown={event => {
          if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault()
            if (!open) show()
            else setActive(Math.max(0, Math.min(filtered.length - 1, activeIndex + (event.key === 'ArrowDown' ? 1 : -1))))
          } else if (event.key === 'Enter' && open) {
            event.preventDefault()
            if (filtered[activeIndex]) choose(filtered[activeIndex])
          } else if (event.key === 'Escape' && open) {
            event.preventDefault(); event.stopPropagation(); setOpen(false)
          } else if (event.key === 'Tab') setOpen(false)
        }} />
      <ChevronDown size={15} aria-hidden="true" />
    </div>
    {open && <div id={`${id}-list`} role="listbox" aria-label={label} className="searchable-select-options">
      {groups.map(group => <div key={group} role={group ? 'group' : undefined} aria-label={group || undefined}>
        {group && <div className="searchable-select-group">{group}</div>}
        {filtered.filter(option => (option.group || '') === group).map(option => {
          const index = filtered.indexOf(option)
          return <div key={option.value} id={`${id}-option-${index}`} role="option" aria-selected={option.value === value}
            className={index === activeIndex ? 'active' : ''}
            ref={element => { if (element && index === activeIndex) element.scrollIntoView({ block: 'nearest' }) }}
            onMouseDown={event => event.preventDefault()} onClick={() => choose(option)}>{option.label}</div>
        })}
      </div>)}
      {!filtered.length && <div className="searchable-select-empty" role="status">No matches found.</div>}
    </div>}
  </div>
}

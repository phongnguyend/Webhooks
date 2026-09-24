import { useEffect, useState } from 'react'
import { Monitor, Moon, Sun } from 'lucide-react'

const modes = [['system', 'System', Monitor], ['light', 'Light', Sun], ['dark', 'Dark', Moon]]

export default function ThemeSwitcher() {
  const [mode, setMode] = useState(() => document.documentElement.dataset.themePreference || 'system')

  useEffect(() => {
    const media = window.matchMedia('(prefers-color-scheme: dark)')
    const apply = () => {
      const resolved = mode === 'system' ? (media.matches ? 'dark' : 'light') : mode
      document.documentElement.dataset.theme = resolved
      document.documentElement.dataset.themePreference = mode
      document.documentElement.style.colorScheme = resolved
      document.querySelector('meta[name="theme-color"]')?.setAttribute('content', resolved === 'dark' ? '#121b18' : '#eef1ef')
    }
    apply()
    media.addEventListener('change', apply)
    return () => media.removeEventListener('change', apply)
  }, [mode])

  useEffect(() => {
    const sync = (event) => {
      if (event.key === 'webhook-router-theme' || event.key === null)
        setMode(modes.some(([value]) => value === event.newValue) ? event.newValue : 'system')
    }
    window.addEventListener('storage', sync)
    return () => window.removeEventListener('storage', sync)
  }, [])

  function select(value) {
    setMode(value)
    try { localStorage.setItem('webhook-router-theme', value) } catch { /* Theme still works when storage is unavailable. */ }
  }

  return <div className="theme-switcher" role="group" aria-label="Appearance">
    {modes.map(([value, label, Icon]) => <button key={value} type="button" aria-label={`${label} theme`} title={`${label} theme`} aria-pressed={mode === value} onClick={() => select(value)}><Icon size={15} /><span>{label}</span></button>)}
  </div>
}

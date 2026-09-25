// Use browser-local time with an explicit, date-specific offset (including DST).
export function formatDateTime(value, fallback = 'Not available') {
  if (!value) return fallback
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return fallback
  const pad = number => String(number).padStart(2, '0')
  const offset = -date.getTimezoneOffset()
  const minutes = Math.abs(offset) % 60
  const zone = `${offset >= 0 ? '+' : '-'}${pad(Math.floor(Math.abs(offset) / 60))}:${pad(minutes)}`
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())} ${zone}`
}

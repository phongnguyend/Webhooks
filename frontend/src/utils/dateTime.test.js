import { test } from 'node:test'
import assert from 'node:assert/strict'
import { formatDateTime } from './dateTime.js'

test('consistent local date format with date-specific timezone offsets', () => {
  const previous = process.env.TZ
  try {
    process.env.TZ = 'Asia/Bangkok'
    assert.equal(formatDateTime('2026-09-25T08:23:12Z'), '2026-09-25 15:23:12 +07:00')
    process.env.TZ = 'America/New_York'
    assert.equal(formatDateTime('2026-01-25T08:23:12Z'), '2026-01-25 03:23:12 -05:00')
    assert.equal(formatDateTime('2026-07-25T08:23:12Z'), '2026-07-25 04:23:12 -04:00')
    process.env.TZ = 'Asia/Kathmandu'
    assert.equal(formatDateTime('2026-09-25T08:23:12Z'), '2026-09-25 14:08:12 +05:45')
    process.env.TZ = 'UTC'
    assert.equal(formatDateTime('2026-09-25T08:23:12Z'), '2026-09-25 08:23:12 +00:00')
    assert.equal(formatDateTime(null), 'Not available')
    assert.equal(formatDateTime('invalid', 'None'), 'None')
  } finally {
    if (previous === undefined) delete process.env.TZ
    else process.env.TZ = previous
  }
})

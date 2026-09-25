// Decode claims for UI state only; authentication is always validated by the API.
export function decodeJwtPayload(token) {
  const encoded = token.split('.')[1]?.replaceAll('-', '+').replaceAll('_', '/')
  if (!encoded) throw new Error('Invalid token payload.')
  const padded = encoded.padEnd(Math.ceil(encoded.length / 4) * 4, '=')
  const bytes = Uint8Array.from(atob(padded), (character) => character.charCodeAt(0))
  return JSON.parse(new TextDecoder().decode(bytes))
}

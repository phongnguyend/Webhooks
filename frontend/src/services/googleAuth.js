import { API_URL } from '../config'
import { decodeJwtPayload } from '../utils/jwt'

const GOOGLE_CLIENT_ID = import.meta.env.VITE_GOOGLE_CLIENT_ID || ''
const GOOGLE_REDIRECT_URI = import.meta.env.VITE_GOOGLE_REDIRECT_URI
  || (typeof window === 'undefined' ? '' : `${window.location.origin}${window.location.pathname}`)
const GOOGLE_STATE_KEY = 'webhook-router-google-state'
const GOOGLE_NONCE_KEY = 'webhook-router-google-nonce'

export function cancelPendingGoogleSignIn() {
  sessionStorage.removeItem(GOOGLE_STATE_KEY)
  sessionStorage.removeItem(GOOGLE_NONCE_KEY)
}

function randomUrlToken() {
  const bytes = new Uint8Array(32)
  crypto.getRandomValues(bytes)
  return btoa(String.fromCharCode(...bytes)).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '')
}

function consumeGoogleRedirect() {
  if (typeof window === 'undefined' || !window.location.hash) return { token: null, error: '' }
  const response = new URLSearchParams(window.location.hash.slice(1))
  const token = response.get('id_token')
  const oauthError = response.get('error')
  if (!token && !oauthError) return { token: null, error: '' }

  window.history.replaceState({}, document.title, `${window.location.pathname}${window.location.search}`)
  const expectedState = sessionStorage.getItem(GOOGLE_STATE_KEY)
  const expectedNonce = sessionStorage.getItem(GOOGLE_NONCE_KEY)
  sessionStorage.removeItem(GOOGLE_STATE_KEY)
  sessionStorage.removeItem(GOOGLE_NONCE_KEY)

  if (oauthError) return { token: null, error: response.get('error_description') || 'Google sign-in was cancelled.' }
  if (!expectedState || response.get('state') !== expectedState) return { token: null, error: 'Google sign-in state validation failed.' }

  try {
    if (!expectedNonce || decodeJwtPayload(token).nonce !== expectedNonce)
      return { token: null, error: 'Google sign-in nonce validation failed.' }
    return { token, error: '' }
  } catch (error) {
    return { token: null, error: error.message || 'Google returned an invalid ID token.' }
  }
}

export function startGoogleSignIn() {
  if (!GOOGLE_CLIENT_ID) {
    throw new Error('Set VITE_GOOGLE_CLIENT_ID to enable Google sign-in.')
  }

  const state = randomUrlToken()
  const nonce = randomUrlToken()
  sessionStorage.setItem(GOOGLE_STATE_KEY, state)
  sessionStorage.setItem(GOOGLE_NONCE_KEY, nonce)
  const authorizeUrl = new URL('https://accounts.google.com/o/oauth2/v2/auth')
  authorizeUrl.search = new URLSearchParams({
    client_id: GOOGLE_CLIENT_ID,
    redirect_uri: GOOGLE_REDIRECT_URI,
    response_type: 'id_token',
    response_mode: 'fragment',
    scope: 'openid email profile',
    prompt: 'select_account',
    state,
    nonce,
  }).toString()
  window.location.assign(authorizeUrl)
}

// Called once by the application's shared session bootstrap.
export async function completeGoogleSignIn(tokenKey) {
  const redirect = consumeGoogleRedirect()
  if (redirect.error) {
    sessionStorage.removeItem(tokenKey)
    return { token: null, error: redirect.error }
  }
  if (!redirect.token) return null
  sessionStorage.removeItem(tokenKey)
  try {
    const response = await fetch(`${API_URL}/api/auth/exchange/google`, {
      method: 'POST', headers: { Authorization: `Bearer ${redirect.token}` },
    })
    if (!response.ok) throw new Error('Unable to sign in. Your account may be disabled or the Google sign-in expired.')
    const session = await response.json()
    if (!session.accessToken || session.tokenType !== 'Bearer') throw new Error('Invalid application session response.')
    sessionStorage.setItem(tokenKey, session.accessToken)
    return { token: session.accessToken, error: '' }
  } catch (error) { return { token: null, error: error.message || 'Unable to sign in.' } }
}

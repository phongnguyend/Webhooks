import { API_URL } from '../config'
import { cancelPendingGoogleSignIn } from './googleAuth'

const clientId = import.meta.env.VITE_MICROSOFT_CLIENT_ID || ''
const tenantId = import.meta.env.VITE_MICROSOFT_TENANT_ID || ''
const operationKey = 'webhook-router-microsoft-operation'
export const microsoftEnabled = Boolean(clientId && tenantId)
export const cancelPendingMicrosoftSignIn = () => sessionStorage.removeItem(operationKey)
let clientPromise

async function getClient() {
  if (!microsoftEnabled) throw new Error('Microsoft sign-in is not configured.')
  if (!clientPromise) clientPromise = (async () => {
    const { PublicClientApplication } = await import('@azure/msal-browser')
    const client = new PublicClientApplication({
      auth: {
        clientId,
        authority: `https://login.microsoftonline.com/${tenantId}`,
        redirectUri: import.meta.env.VITE_MICROSOFT_REDIRECT_URI || `${window.location.origin}${window.location.pathname}`,
      },
      cache: { cacheLocation: 'sessionStorage' },
    })
    await client.initialize()
    return client
  })()
  return clientPromise
}

export async function startMicrosoftSignIn(userId = null) {
  const client = await getClient()
  cancelPendingGoogleSignIn()
  sessionStorage.setItem(operationKey, JSON.stringify({ userId }))
  try { await client.loginRedirect({ scopes: ['openid', 'profile', 'email'], prompt: 'select_account' }) }
  catch (error) { cancelPendingMicrosoftSignIn(); throw error }
}

export async function completeMicrosoftSignIn(tokenKey) {
  const pending = sessionStorage.getItem(operationKey)
  if (!pending) return null
  let client
  let linking = false
  try {
    const { userId } = JSON.parse(pending)
    linking = Boolean(userId)
    client = await getClient()
    const result = await client.handleRedirectPromise({ navigateToLoginRequestUrl: false })
    if (!result?.idToken) throw new Error('Microsoft sign-in did not complete. Please try again.')
    const headers = { Authorization: `Bearer ${result.idToken}` }
    if (linking) {
      const token = sessionStorage.getItem(tokenKey)
      const encoded = token?.split('.')[1]?.replaceAll('-', '+').replaceAll('_', '/')
      if (!encoded || JSON.parse(atob(encoded.padEnd(Math.ceil(encoded.length / 4) * 4, '='))).sub !== userId)
        throw new Error('Your application session changed. Sign in again before connecting Microsoft.')
      headers.Authorization = `Bearer ${token}`
      headers['X-Microsoft-Id-Token'] = result.idToken
    }
    const response = await fetch(`${API_URL}/api/auth/${linking ? 'link' : 'exchange'}/microsoft`, { method: 'POST', headers })
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}))
      throw new Error(problem.error || 'Microsoft sign-in failed. Check your account status and Microsoft configuration.')
    }
    const session = await response.json()
    if (!session.accessToken || session.tokenType !== 'Bearer') throw new Error('Invalid application session response.')
    sessionStorage.setItem(tokenKey, session.accessToken)
    return { token: session.accessToken, error: '', linked: linking }
  } catch (error) {
    if (!linking) sessionStorage.removeItem(tokenKey)
    return { token: linking ? sessionStorage.getItem(tokenKey) : null, error: error.message || 'Microsoft sign-in failed.' }
  } finally {
    cancelPendingMicrosoftSignIn()
    if (client) await client.clearCache().catch(() => {})
  }
}

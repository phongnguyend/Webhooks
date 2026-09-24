import { Webhook } from 'lucide-react'
import ThemeSwitcher from '../components/ThemeSwitcher'
import GoogleLogo from '../components/GoogleLogo'

export default function LoginPage({ loading, error, onSignIn }) {
  if (loading) return <div className="auth-screen"><div className="auth-theme"><ThemeSwitcher /></div><div className="auth-card"><span className="brand-mark"><Webhook size={24} /></span><p>Loading Webhook Router…</p></div></div>
  return <div className="auth-screen"><div className="auth-theme"><ThemeSwitcher /></div><div className="auth-card"><span className="brand-mark auth-logo"><Webhook size={26} /></span><span className="eyebrow">Webhook Router</span><h1>Route webhooks with confidence.</h1><p>Sign in to manage your private tenants, topic routes, and live event stream.</p><button type="button" className="google-button" onClick={onSignIn}><GoogleLogo /> Continue with Google</button>{error && <p className="login-error">{error}</p>}</div></div>
}

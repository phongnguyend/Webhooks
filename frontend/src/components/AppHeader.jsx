import { Eye, Settings2, Users, Webhook } from 'lucide-react'
import ThemeSwitcher from './ThemeSwitcher'

export default function AppHeader({ view, onViewChange, eventCount, user, displayName, connectionStatus, onProfile, onSignOut }) {
  return (
<header className="topbar">
        <div className="brand"><span className="brand-mark"><Webhook size={21} /></span><div><h1>Webhook Router</h1><p>Tenant routing console</p></div></div>
        <nav className="tabs" aria-label="Primary navigation">
          <button className={view === 'manage' ? 'active' : ''} onClick={() => onViewChange('manage')}><Settings2 size={16} /> Configuration</button>
          <button className={view === 'events' ? 'active' : ''} onClick={() => onViewChange('events')}><Eye size={16} /> Events <span>{eventCount}</span></button>
          {user.roles?.includes('Global Admin') && <button className={view === 'users' ? 'active' : ''} onClick={() => onViewChange('users')}><Users size={16} /> Users</button>}
        </nav>
        <div className="account-area"><ThemeSwitcher /><div className={`connection-pill ${connectionStatus}`}><span className="pulse-dot" />{connectionStatus === 'connected' ? 'Live' : connectionStatus}</div><div className="user-menu"><button className="profile-trigger" onClick={onProfile} title={user.email}><span>{displayName.slice(0, 1).toUpperCase()}</span><small>{displayName}</small></button><button className="signout-button" onClick={onSignOut}>Sign out</button></div></div>
      </header>
  )
}

export default function Status({ enabled }) { return <span className={`status-badge ${enabled ? 'enabled' : ''}`}>{enabled ? 'Enabled' : 'Disabled'}</span> }

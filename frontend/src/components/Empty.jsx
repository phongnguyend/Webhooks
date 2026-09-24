export default function Empty({ icon: Icon, title, text }) { return <div className="empty-state"><Icon size={29} /><strong>{title}</strong><span>{text}</span></div> }

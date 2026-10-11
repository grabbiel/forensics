import { Bell } from '@phosphor-icons/react'
import { Link } from 'react-router'
import { badgeText, bellLabel } from '../features/notifications/badge'
import { useReadOnArrival } from '../features/notifications/readOnArrival'
import { useUnreadCount } from '../features/notifications/useUnreadCount'

/**
 * A link to /notifications with the unread count. A link, not a popover, so it deep-links, works the same at phone
 * width, and is simple to test. The badge is aria-hidden because the link's name already says the count.
 */
export function NotificationBell() {
  useReadOnArrival()
  const count = useUnreadCount()
  const badge = badgeText(count)
  return (
    <Link to="/notifications" className="icon-button bell" aria-label={bellLabel(count)} title="Notificaciones">
      <Bell size={18} aria-hidden="true" />
      {badge && (
        <span className="bell__badge" aria-hidden="true">
          {badge}
        </span>
      )}
    </Link>
  )
}

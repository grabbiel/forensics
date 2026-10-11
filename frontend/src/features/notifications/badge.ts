/** The bell badge's text: hidden at zero or before the first answer (null), capped at "99+". */
export function badgeText(count: number | undefined): string | null {
  if (!count) return null
  return count > 99 ? '99+' : String(count)
}

/** The bell link's accessible name, which carries the count: "Notificaciones, 3 sin leer", or "Notificaciones". */
export function bellLabel(count: number | undefined): string {
  return count ? `Notificaciones, ${count} sin leer` : 'Notificaciones'
}

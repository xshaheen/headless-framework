import { format as timeagoFormat } from 'timeago.js'

export function formatDateTime(utcDateString: string | null | undefined): string {
  if (!utcDateString) return ''

  let iso = utcDateString.trim()
  if (!iso.endsWith('Z')) {
    iso = iso.replace(' ', 'T') + 'Z'
  }

  const dateObj = new Date(iso)

  const dd = String(dateObj.getDate()).padStart(2, '0')
  const MM = String(dateObj.getMonth() + 1).padStart(2, '0')
  const yyyy = dateObj.getFullYear()
  const hh = String(dateObj.getHours()).padStart(2, '0')
  const mm = String(dateObj.getMinutes()).padStart(2, '0')
  const ss = String(dateObj.getSeconds()).padStart(2, '0')

  return `${dd}.${MM}.${yyyy} ${hh}:${mm}:${ss}`
}

export function timeAgo(utcDateString: string | null | undefined): string {
  if (!utcDateString) return ''

  let iso = utcDateString.trim()
  if (!iso.endsWith('Z')) {
    iso = iso.replace(' ', 'T') + 'Z'
  }

  return timeagoFormat(iso)
}

import { format as timeagoFormat } from 'timeago.js'

const TIME_ZONE_SUFFIX = /(Z|[+-]\d{2}:?\d{2})$/i

function parseDateTime(dateTimeString: string | null | undefined): Date | null {
  if (!dateTimeString) return null

  let iso = dateTimeString.trim().replace(' ', 'T')
  if (!TIME_ZONE_SUFFIX.test(iso)) {
    iso += 'Z'
  }

  const date = new Date(iso)
  return Number.isNaN(date.getTime()) ? null : date
}

export function formatDateTime(dateTimeString: string | null | undefined): string {
  const dateObj = parseDateTime(dateTimeString)
  if (!dateObj) return ''

  const dd = String(dateObj.getDate()).padStart(2, '0')
  const MM = String(dateObj.getMonth() + 1).padStart(2, '0')
  const yyyy = dateObj.getFullYear()
  const hh = String(dateObj.getHours()).padStart(2, '0')
  const mm = String(dateObj.getMinutes()).padStart(2, '0')
  const ss = String(dateObj.getSeconds()).padStart(2, '0')

  return `${dd}.${MM}.${yyyy} ${hh}:${mm}:${ss}`
}

export function timeAgo(dateTimeString: string | null | undefined): string {
  const dateObj = parseDateTime(dateTimeString)
  return dateObj ? timeagoFormat(dateObj) : ''
}

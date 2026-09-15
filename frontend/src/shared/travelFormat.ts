export function formatClock(value: string) {
  return new Intl.DateTimeFormat('tr-TR', { hour: '2-digit', minute: '2-digit' }).format(new Date(value))
}

export function formatMinutes(minutes: number) {
  return `${Math.floor(minutes / 60)} sa ${minutes % 60} dk`
}

export function formatFlightDate(value: string) {
  return new Intl.DateTimeFormat('tr-TR', { day: 'numeric', month: 'short' }).format(new Date(value))
}

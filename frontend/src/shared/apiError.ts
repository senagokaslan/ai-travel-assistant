export type ApiErrorPayload = { code?: string; message?: string; action?: string; traceId?: string }

export function apiErrorMessage(payload: unknown, response: Response, fallback: string) {
  const error = payload && typeof payload === 'object' ? payload as ApiErrorPayload : {}
  const message = typeof error.message === 'string' && error.message.trim() ? error.message.trim() : fallback
  const action = typeof error.action === 'string' && error.action.trim() ? ` ${error.action.trim()}` : ''
  const traceId = typeof error.traceId === 'string' ? error.traceId : response.headers.get('X-Trace-Id')
  const reference = response.status >= 500 && traceId ? ` Takip kodu: ${traceId.slice(0, 8)}.` : ''
  return `${message}${action}${reference}`
}

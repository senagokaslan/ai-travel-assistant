import type { ReactNode } from 'react'

export type FeedbackTone = 'loading' | 'empty' | 'warning' | 'error' | 'success'

const toneIcon: Record<FeedbackTone, string> = {
  loading: '…',
  empty: '—',
  warning: '!',
  error: '!',
  success: '✓',
}

export function FeedbackState({
  tone,
  title,
  message,
  actionLabel,
  onAction,
  actionDisabled = false,
  children,
}: {
  tone: FeedbackTone
  title: string
  message: string
  actionLabel?: string
  onAction?: () => void
  actionDisabled?: boolean
  children?: ReactNode
}) {
  return (
    <div className={`feedback-state ${tone}`} role={tone === 'error' ? 'alert' : 'status'} aria-live="polite">
      <span className="feedback-icon" aria-hidden="true">{toneIcon[tone]}</span>
      <div className="feedback-copy">
        <strong>{title}</strong>
        <p>{message}</p>
        {children}
      </div>
      {tone === 'loading' && <span className="feedback-spinner" aria-label="Yükleniyor" />}
      {actionLabel && onAction && tone !== 'loading' && (
        <button type="button" className="feedback-action" onClick={onAction} disabled={actionDisabled}>
          {actionLabel}
        </button>
      )}
    </div>
  )
}

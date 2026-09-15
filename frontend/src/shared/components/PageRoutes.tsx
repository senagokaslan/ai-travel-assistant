import type { ReactNode } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../../features/auth/useAuth'

export function ProtectedRoute({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  const location = useLocation()
  if (!user) return <Navigate to={`/login?returnTo=${encodeURIComponent(location.pathname)}`} replace />
  return <>{children}</>
}
export function PageHeading({ label, title, description }: { label: string; title: string; description: string }) {
  return <header className="page-heading"><span className="section-label">{label}</span><h1>{title}</h1><p>{description}</p></header>
}

export function NotFoundPage() {
  const navigate = useNavigate()
  return <main className="page-frame not-found-page"><div className="not-found-code">404</div><PageHeading label="YOLUN DIŞINA ÇIKTIK" title="Bu sayfayı bulamadık" description="Adres değişmiş veya aradığın sayfa bu demoda yer almıyor olabilir." /><button className="primary-action" type="button" onClick={() => navigate('/')}>Ana sayfaya dön</button></main>
}


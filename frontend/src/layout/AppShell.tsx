import { useCallback, useEffect, useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import projectIdentity from '../../../content/project-identity.json'
import { useAuth } from '../features/auth/useAuth'
import { Icon } from '../shared/components/Icon'

type HealthState = 'loading' | 'healthy' | 'unhealthy'
type HealthResponse = { status: 'healthy' | 'unhealthy' }

function useHealth() {
  const [state, setState] = useState<HealthState>('loading')

  const refresh = useCallback(async () => {
    setState('loading')
    try {
      const response = await fetch('/api/health')
      const data = await response.json() as HealthResponse
      setState(response.ok && data.status === 'healthy' ? 'healthy' : 'unhealthy')
    } catch {
      setState('unhealthy')
    }
  }, [])

  useEffect(() => {
    const timer = window.setTimeout(() => void refresh(), 0)
    return () => window.clearTimeout(timer)
  }, [refresh])

  return state
}
export function ScrollToTop() {
  const { pathname } = useLocation()

  useEffect(() => {
    window.scrollTo({ top: 0, left: 0 })
  }, [pathname])

  return null
}

export function Brand() {
  return (
    <Link className="brand" to="/" aria-label="Ana sayfa">
      <span className="brand-mark" aria-hidden="true">
        <Icon name="compass" size={20} />
      </span>
      <span className="brand-copy"><strong>Bağımsız</strong><small>Seyahat Asistanı</small></span>
    </Link>
  )
}

export function AppShell() {
  const { user, signOut } = useAuth()
  const health = useHealth()
  const location = useLocation()
  const navigate = useNavigate()

  return (
    <div className="app-shell">
      <header className="topbar">
        <div className="topbar-inner">
          <Brand />
          <nav className="main-nav" aria-label="Ana menü">
            <NavLink to="/hotels"><Icon name="hotel" size={15} /><span>Oteller</span></NavLink>
            <NavLink to="/flights"><Icon name="plane" size={15} /><span>Uçuşlar</span></NavLink>
            <NavLink to="/chat"><Icon name="chat" size={15} /><span>Seyahat sohbeti</span></NavLink>
            {user && <NavLink to="/bookings"><Icon name="calendar" size={15} /><span>Kayıtlarım</span></NavLink>}
            {user?.role === 'admin' && <NavLink to="/admin">Yönetim</NavLink>}
          </nav>
          <div className="topbar-actions">
            {user ? (
              <>
                <NavLink className="profile-link" to="/profile" aria-label="Profili aç">
                  <span>{user.name.slice(0, 1).toLocaleUpperCase('tr-TR')}</span>
                  <b>{user.name.split(' ')[0]}</b>
                </NavLink>
                <button className="quiet-button" type="button" onClick={() => void signOut().finally(() => navigate('/', { replace: true }))}>Çıkış</button>
              </>
            ) : (
              <Link className="login-link" to="/login">Giriş yap</Link>
            )}
          </div>
        </div>
      </header>
      {health === 'unhealthy' && (
        <div className="service-alert" role="alert">
          <span aria-hidden="true"><Icon name="info" size={16} /></span>
          <p><strong>Arama servisine şu anda ulaşılamıyor.</strong> Arama yaparken sorun yaşarsanız yerel API ve veritabanı bağlantısını kontrol edin.</p>
        </div>
      )}
      <Outlet />
      {location.pathname !== '/chat' && <SiteFooter />}
    </div>
  )
}

function SiteFooter() {
  return (
    <footer className="site-footer">
      <div className="footer-inner">
        <div><Brand /><p>Yerel örnek verilerle seyahat seçeneklerini ara ve karşılaştır.</p></div>
        <nav aria-label="Alt menü"><strong>Keşfet</strong><Link to="/hotels">Otel ara</Link><Link to="/flights">Uçuş ara</Link><Link to="/chat">Seyahat sohbeti</Link></nav>
        <div className="footer-scope"><strong>Demo kapsamı</strong><p>Gerçek rezervasyon ve ödeme yapılmaz. Sonuçlar canlı sağlayıcı verisi değildir.</p></div>
      </div>
      <div className="footer-bottom"><span>© 2026 {projectIdentity.name}</span><span>Eğitim amaçlı yerel proje</span></div>
    </footer>
  )
}

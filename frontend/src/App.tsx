import { useCallback, useEffect, useState, type FormEvent, type ReactNode } from 'react'
import {
  BrowserRouter,
  Link,
  Navigate,
  NavLink,
  Outlet,
  Route,
  Routes,
  useLocation,
  useNavigate,
} from 'react-router-dom'
import projectIdentity from '../../content/project-identity.json'
import { AuthProvider } from './auth/AuthContext'
import { useAuth } from './auth/useAuth'
import { FeedbackState, type FeedbackTone } from './components/FeedbackState'
import './App.css'

type HealthState = 'loading' | 'healthy' | 'unhealthy'
type HealthResponse = {
  status: 'healthy' | 'unhealthy'
  database: { status: string; name?: string; message?: string }
}

function useHealth() {
  const [health, setHealth] = useState<HealthResponse | null>(null)
  const [state, setState] = useState<HealthState>('loading')

  const refresh = useCallback(async () => {
    setState('loading')
    try {
      const response = await fetch('/api/health')
      setHealth((await response.json()) as HealthResponse)
      setState(response.ok ? 'healthy' : 'unhealthy')
    } catch {
      setHealth(null)
      setState('unhealthy')
    }
  }, [])

  useEffect(() => {
    const timer = window.setTimeout(() => void refresh(), 0)
    return () => window.clearTimeout(timer)
  }, [refresh])

  return { health, state, refresh }
}

function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route element={<AppShell />}>
            <Route index element={<HomePage />} />
            <Route path="hotels" element={<SearchPage kind="hotel" />} />
            <Route path="flights" element={<SearchPage kind="flight" />} />
            <Route path="chat" element={<ChatPage />} />
            <Route path="bookings" element={<ProtectedRoute><BookingsPage /></ProtectedRoute>} />
            <Route path="profile" element={<ProtectedRoute><ProfilePage /></ProtectedRoute>} />
            <Route path="admin" element={<AdminRoute />} />
            <Route path="*" element={<NotFoundPage />} />
          </Route>
          <Route path="/login" element={<LoginPage />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  )
}

function AppShell() {
  const { user, signOut } = useAuth()
  const { state } = useHealth()

  return (
    <main>
      <header className="topbar">
        <Link className="brand" to="/" aria-label="Ana sayfa">
          <span className="brand-mark" aria-hidden="true">BA</span>
          <span>Bağımsız Seyahat Asistanı</span>
        </Link>
        <nav className="main-nav" aria-label="Ana menü">
          <NavLink to="/hotels">Oteller</NavLink>
          <NavLink to="/flights">Uçuşlar</NavLink>
          <NavLink to="/chat">Sohbet</NavLink>
          {user && <NavLink to="/bookings">Rezervasyonlarım</NavLink>}
          {user && <NavLink to="/profile">Profil</NavLink>}
          {user?.role === 'admin' && <NavLink to="/admin">Yönetim</NavLink>}
        </nav>
        <div className="topbar-actions">
          <span className={`system-pill ${state}`}>
            <span className="status-dot" aria-hidden="true" />
            {state === 'healthy' ? 'Sistem hazır' : state === 'loading' ? 'Kontrol ediliyor' : 'Kurulum bekleniyor'}
          </span>
          {user ? (
            <button className="account-button" type="button" onClick={signOut} title="Demo oturumunu kapat">
              {user.name} · Çıkış
            </button>
          ) : (
            <Link className="login-link" to="/login">Giriş yap</Link>
          )}
        </div>
      </header>
      <Outlet />
      <footer>
        <span>Bağımsız AI Destekli Seyahat Asistanı</span>
        <span>Yerel geliştirme başlangıcı</span>
      </footer>
    </main>
  )
}

function HomePage() {
  const { health, state, refresh } = useHealth()

  return (
    <>
      <section className="hero" id="top">
        <div className="hero-copy">
          <p className="eyebrow">AI DESTEKLİ SEYAHAT PLANLAMA DENEYİMİ</p>
          <h1>{projectIdentity.name}</h1>
          <p className="lead">{projectIdentity.shortDescription}</p>
          <div className="notice" role="note">
            <span className="notice-icon" aria-hidden="true">i</span>
            <p>{projectIdentity.serviceDisclaimer}</p>
          </div>
          <div className="actions">
            <Link className="primary-action" to="/hotels">Otel aramaya başla</Link>
            <Link className="secondary-action" to="/chat">Sohbetle keşfet</Link>
          </div>
        </div>
        <div className="route-card" aria-label="Örnek seyahat araması">
          <div className="route-card-head"><span>Örnek rota</span><span className="demo-badge">DEMO</span></div>
          <div className="route"><div><strong>IST</strong><span>İstanbul</span></div><div className="route-line" aria-hidden="true"><span>✦</span></div><div className="route-end"><strong>AYT</strong><span>Antalya</span></div></div>
          <dl className="trip-details"><div><dt>Tarih</dt><dd>18 Haziran</dd></div><div><dt>Yolcu</dt><dd>2 yetişkin</dd></div><div><dt>Tür</dt><dd>Gidiş-dönüş</dd></div></dl>
          <p className="sample-note">Gösterilen rota ve bilgiler yalnızca örnektir.</p>
        </div>
      </section>
      <section className="status-section" id="system-status">
        <div className="section-heading"><div><p className="eyebrow">YEREL GELİŞTİRME ORTAMI</p><h2>Üç parça, tek çalışan başlangıç</h2></div><button type="button" onClick={() => void refresh()} disabled={state === 'loading'}>Yeniden kontrol et</button></div>
        <div className="status-grid" aria-live="polite">
          <StatusCard title="React arayüz" detail="Vite geliştirme sunucusu · 5173" state="healthy" number="01" />
          <StatusCard title=".NET API" detail="ASP.NET Core Web API · 5080" state={state === 'loading' ? 'loading' : health ? 'healthy' : 'unhealthy'} number="02" />
          <StatusCard title="PostgreSQL" detail={health?.database.status === 'healthy' ? `${health.database.name} veritabanına bağlı` : health?.database.message ?? 'API bağlantısı bekleniyor'} state={state} number="03" />
        </div>
      </section>
    </>
  )
}

function SearchPage({ kind }: { kind: 'hotel' | 'flight' }) {
  const hotel = kind === 'hotel'
  const [query, setQuery] = useState('')
  const [feedback, setFeedback] = useState<{ tone: FeedbackTone; title: string; message: string } | null>(null)
  const [results, setResults] = useState<string[]>([])
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [airportHints, setAirportHints] = useState<Array<{ code: string; name: string; city: string }>>([])
  useEffect(() => { if (!hotel && query.trim().length >= 2) { fetch(`/api/travel/airports?q=${encodeURIComponent(query)}`).then(async r => r.ok ? setAirportHints(await r.json()) : setAirportHints([])).catch(() => setAirportHints([])) } else setAirportHints([]) }, [hotel, query])

  const submitSearch = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (isSubmitting) return
    if (!query.trim()) {
      setFeedback({ tone: 'error', title: 'Arama bilgisi eksik', message: hotel ? 'Devam etmek için bir şehir veya bölge yazın.' : 'Devam etmek için kalkış noktası yazın.' })
      return
    }
    setIsSubmitting(true)
    setFeedback({ tone: 'loading', title: 'Örnek sonuçlar hazırlanıyor', message: 'Arama kriterleriniz kontrol ediliyor.' })
    window.setTimeout(async () => {
      const normalized = query.trim().toLocaleLowerCase('tr-TR')
      setIsSubmitting(false)
      if (hotel) { try { const response = await fetch(`/api/hotels?q=${encodeURIComponent(query.trim())}`); if (response.ok) { const data = await response.json() as Array<{ name: string }>; setResults(data.map(item => item.name)); setFeedback(data.length ? { tone: 'success', title: 'Otel sonuçları hazır', message: 'Aktif katalog kayıtları listelendi.' } : { tone: 'empty', title: 'Sonuç bulunamadı', message: 'Bu kriterlerle eşleşen aktif otel yok.' }); return } } catch { /* demo fallback below */ } }
      if (normalized.includes('hata')) {
        setResults([])
        setFeedback({ tone: 'error', title: 'Arama tamamlanamadı', message: 'Bağlantı kurulamadı. Ayarlarınızı kontrol edip tekrar deneyin.' })
      } else if (normalized.includes('boş') || normalized.includes('yok')) {
        setResults([])
        setFeedback({ tone: 'empty', title: 'Sonuç bulunamadı', message: 'Bu kriterlerle eşleşen örnek kayıt yok. Farklı bir şehir veya tarih deneyin.' })
      } else {
        setResults(hotel ? ['Galata Meydan Otel', 'Kalepark Konaklama'] : ['Anadolu 204', 'Akdeniz 318'])
        setFeedback({ tone: 'success', title: 'Örnek sonuçlar hazır', message: 'Sonuçlar yalnızca proje içindeki örnek verilerden oluşturuldu.' })
      }
    }, 650)
  }

  const resetSearch = () => { setQuery(''); setResults([]); setFeedback(null) }
  const retrySearch = () => submitSearch({ preventDefault: () => undefined } as FormEvent<HTMLFormElement>)

  return (
    <PageFrame eyebrow={hotel ? 'KONAKLAMA ARAMA' : 'UÇUŞ ARAMA'} title={hotel ? 'Size uygun bir otel bulun' : 'Rotanıza uygun uçuşları keşfedin'} description={hotel ? 'Tarih, konum ve kişi sayısıyla örnek otel seçeneklerini karşılaştırın.' : 'Kalkış, varış ve tarihe göre örnek uçuş seçeneklerini inceleyin.'}>
      <form className="search-panel" onSubmit={submitSearch} aria-busy={isSubmitting}>
        <div className="form-grid"><label>{hotel ? 'Nereye?' : 'Nereden?'}<input value={query} onChange={(event) => setQuery(event.target.value)} placeholder={hotel ? 'Şehir veya bölge' : 'Şehir / havaalanı'} />{airportHints.length > 0 && <span className="airport-hints">{airportHints.map(item => <button type="button" key={item.code} onClick={() => { setQuery(`${item.city} (${item.code})`); setAirportHints([]) }}><strong>{item.code}</strong> {item.name}</button>)}</span>}</label><label>{hotel ? 'Giriş tarihi' : 'Gidiş tarihi'}<input type="date" /></label><label>{hotel ? 'Gece' : 'Yolcu'}<input type="number" min="1" defaultValue="2" /></label></div>
        <button type="submit" className="primary-action form-button" disabled={isSubmitting}>{isSubmitting ? 'Hazırlanıyor…' : 'Örnek sonuçları getir'}</button>
        <p className="form-note">Bu A03 başlangıç ekranı yalnızca yönlendirme ve sayfa akışını gösterir. Arama kuralları ilgili aşamalarda eklenecektir.</p>
        <p className="demo-hint">Durumları denemek için arama alanına “boş” veya “hata” yazabilirsiniz.</p>
        {feedback && <FeedbackState {...feedback} actionLabel={feedback.tone === 'error' ? 'Tekrar dene' : feedback.tone === 'success' ? 'Yeni arama' : undefined} onAction={feedback.tone === 'success' ? resetSearch : feedback.tone === 'error' ? retrySearch : undefined} actionDisabled={isSubmitting} />}
        {results.length > 0 && <div className="result-list">{results.map((result, index) => <article className="result-card" key={result}><span>0{index + 1}</span><div><strong>{result}</strong><p>{hotel ? 'Örnek konaklama · müsaitlik simülasyonu' : 'Örnek sefer · uçuş seçeneği simülasyonu'}</p></div><b>›</b></article>)}</div>}
      </form>
    </PageFrame>
  )
}

function ChatPage() {
  const [message, setMessage] = useState('')
  const [sending, setSending] = useState(false)
  const [feedback, setFeedback] = useState<{ tone: FeedbackTone; title: string; message: string } | null>(null)
  const sendMessage = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (sending) return
    if (!message.trim()) { setFeedback({ tone: 'error', title: 'Mesaj boş', message: 'Arama isteğinizi yazıp tekrar deneyin.' }); return }
    setSending(true)
    setFeedback({ tone: 'loading', title: 'Sohbet yanıtı hazırlanıyor', message: 'İsteğiniz örnek arama akışına aktarılıyor.' })
    window.setTimeout(() => { setSending(false); setMessage(''); setFeedback({ tone: 'success', title: 'İstek alındı', message: 'Eksik bilgileri tamamlamak için bir sonraki soru hazırlandı.' }) }, 650)
  }
  return <PageFrame eyebrow="SOHBETLE ARAMA" title="İsteğinizi doğal cümlelerle anlatın" description="Eksik bilgileri soran sohbet akışı, otel ve uçuş aramalarına yönlendirir."><div className="chat-preview"><div className="chat-message assistant">Merhaba! Otel mi yoksa uçuş mu aramak istersiniz?</div><div className="chat-message user">İstanbul'dan Antalya'ya iki kişi için örnek bir uçuş arıyorum.</div><div className="chat-message assistant">Tarih bilgisini de ekleyin; ardından örnek seçenekleri göstereyim.</div>{feedback && <FeedbackState {...feedback} /> }<form className="chat-input" onSubmit={sendMessage} aria-busy={sending}><input value={message} onChange={(event) => setMessage(event.target.value)} placeholder="Mesajınızı yazın..." aria-label="Mesajınızı yazın" /><button type="submit" className="primary-action" disabled={sending}>{sending ? 'Gönderiliyor…' : 'Gönder'}</button></form></div></PageFrame>
}

function BookingsPage() {
  const navigate = useNavigate(); const [count, setCount] = useState<number | null>(null); const token = sessionStorage.getItem('travel-assistant-demo-session')
  useEffect(() => { fetch('/api/bookings', { headers: { Authorization: `Bearer ${token ?? ''}` } }).then(async response => { if (response.ok) setCount((await response.json()).length) }).catch(() => setCount(0)) }, [token])
  return <PageFrame eyebrow="KAYITLARIM" title="Rezervasyon simülasyonlarınız" description="Oluşturduğunuz eğitim amaçlı kayıtları burada görebilirsiniz.">{count === null ? <FeedbackState tone="loading" title="Kayıtlar yükleniyor" message="Kişisel kayıtlarınız getiriliyor." /> : <FeedbackState tone="empty" title={count ? `${count} simülasyon kaydı` : 'Henüz simülasyon kaydı yok'} message="Bu liste yalnızca sizin hesabınıza bağlı kayıtları gösterir." actionLabel="Otel aramaya git" onAction={() => navigate('/hotels')} />}</PageFrame>
}

function ProfilePage() {
  const { user } = useAuth(); const [name, setName] = useState(user?.name ?? ''); const [phone, setPhone] = useState(''); const [currency, setCurrency] = useState('TRY'); const [feedback, setFeedback] = useState<{ tone: FeedbackTone; title: string; message: string } | null>(null); const [saving, setSaving] = useState(false)
  useEffect(() => { const token = sessionStorage.getItem('travel-assistant-demo-session'); fetch('/api/profile', { headers: { Authorization: `Bearer ${token ?? ''}` } }).then(async r => { if (r.ok) { const data = await r.json(); setName(data.name); setPhone(data.phone); setCurrency(data.currency) } }).catch(() => undefined) }, [])
  const save = async (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); if (saving) return; setSaving(true); setFeedback({ tone: 'loading', title: 'Profil güncelleniyor', message: 'Bilgileriniz kaydediliyor.' }); try { const token = sessionStorage.getItem('travel-assistant-demo-session'); const response = await fetch('/api/profile', { method: 'PATCH', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token ?? ''}` }, body: JSON.stringify({ name, phone, currency }) }); const data = await response.json(); setFeedback(response.ok ? { tone: 'success', title: 'Profil güncellendi', message: data.message } : { tone: 'error', title: 'Profil güncellenemedi', message: data.message ?? 'Tekrar deneyin.' }) } catch { setFeedback({ tone: 'error', title: 'Bağlantı kurulamadı', message: 'Bağlantınızı kontrol edip tekrar deneyin.' }) } finally { setSaving(false) } }
  return <PageFrame eyebrow="HESABIM" title="Profil ve tercihlerin" description="Adınızı, telefonunuzu ve tercih ettiğiniz para birimini yönetin."><form className="profile-card profile-form" onSubmit={save}><span className="profile-avatar">{name.slice(0, 1).toUpperCase()}</span><div className="profile-fields"><label>Ad soyad<input value={name} onChange={e => setName(e.target.value)} required /></label><label>E-posta<input value={user?.email ?? ''} readOnly /></label><label>Telefon<input value={phone} onChange={e => setPhone(e.target.value)} placeholder="+90 5xx xxx xx xx" /></label><label>Para birimi<select value={currency} onChange={e => setCurrency(e.target.value)}><option value="TRY">TRY — Türk lirası</option><option value="EUR">EUR — Euro</option><option value="USD">USD — Amerikan doları</option><option value="GBP">GBP — İngiliz sterlini</option></select></label><button className="primary-action" type="submit" disabled={saving}>{saving ? 'Kaydediliyor…' : 'Profili kaydet'}</button>{feedback && <FeedbackState {...feedback} />}</div></form></PageFrame>
}

function AdminRoute() {
  const { user } = useAuth()
  const navigate = useNavigate()
  if (!user) return <Navigate to="/login?returnTo=%2Fadmin" replace />
  if (user.role !== 'admin') return <PageFrame eyebrow="YETKİ GEREKLİ" title="Bu alana erişim yok" description="Yönetim bağlantısı yalnızca admin yetkili hesaplarda görünür."><FeedbackState tone="error" title="Yetkiniz bulunmuyor" message="Demo admin oturumu ile giriş yaparak örnek yönetim ekranını açabilirsiniz." actionLabel="Ana sayfaya dön" onAction={() => navigate('/')} /></PageFrame>
  return <PageFrame eyebrow="YÖNETİM" title="Seyahat verilerini yönet" description="Örnek otel, oda, uçuş, fiyat ve müsaitlik verilerinin yönetim alanı."><div className="admin-grid"><div><strong>Otel ve odalar</strong><span>Örnek katalog kayıtları</span></div><div><strong>Uçuşlar</strong><span>Sefer ve koltuk bilgileri</span></div><div><strong>Fiyat ve müsaitlik</strong><span>Günlük örnek veriler</span></div></div></PageFrame>
}

function LoginPage() {
  const { user, signIn, register, loading, error, clearError } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const returnTo = new URLSearchParams(location.search).get('returnTo') || '/'
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')

  useEffect(() => { if (user) navigate(returnTo, { replace: true }) }, [navigate, returnTo, user])

  const submit = async (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); clearError(); if (mode === 'register' && password !== confirm) return; const ok = mode === 'login' ? await signIn(email, password) : await register(name, email, password); if (ok) navigate(returnTo, { replace: true }) }
  return <main className="login-page"><div className="login-card"><Link className="brand" to="/"><span className="brand-mark">BA</span><span>Bağımsız Seyahat Asistanı</span></Link><p className="eyebrow">HESAP {mode === 'login' ? 'GİRİŞİ' : 'KAYDI'}</p><h1>{mode === 'login' ? 'Yolculuğunuza başlayın' : 'Yeni hesabınızı oluşturun'}</h1><p>{mode === 'login' ? 'Rezervasyon simülasyonlarınızı hesabınıza bağlamak için giriş yapın.' : 'Ad, e-posta ve güçlü bir parola ile hesabınızı oluşturun.'}</p><div className="notice compact" role="note"><span className="notice-icon" aria-hidden="true">i</span><p>{projectIdentity.serviceDisclaimer}</p></div><form className="auth-form" onSubmit={submit} noValidate>{mode === 'register' && <label>Ad soyad<input value={name} onChange={event => setName(event.target.value)} autoComplete="name" required minLength={2} /></label>}<label>E-posta<input type="email" value={email} onChange={event => setEmail(event.target.value)} autoComplete="email" required /></label><label>Parola<input type="password" value={password} onChange={event => setPassword(event.target.value)} autoComplete={mode === 'login' ? 'current-password' : 'new-password'} required /><small>En az 8 karakter; büyük harf, küçük harf ve rakam içermeli.</small></label>{mode === 'register' && <label>Parola tekrarı<input type="password" value={confirm} onChange={event => setConfirm(event.target.value)} autoComplete="new-password" required /></label>}{error && <FeedbackState tone="error" title="İşlem tamamlanamadı" message={error} />}{loading && <FeedbackState tone="loading" title="İşleniyor" message="Lütfen bekleyin." />}<button type="submit" className="primary-action" disabled={loading}>{mode === 'login' ? 'Giriş yap' : 'Hesap oluştur'}</button></form><button className="back-link auth-switch" type="button" onClick={() => { setMode(mode === 'login' ? 'register' : 'login'); clearError() }}>{mode === 'login' ? 'Yeni hesap oluştur' : 'Zaten hesabım var'}</button><Link className="back-link" to="/">Ana sayfaya dön</Link></div></main>
}

function ProtectedRoute({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  const location = useLocation()
  if (!user) return <Navigate to={`/login?returnTo=${encodeURIComponent(location.pathname)}`} replace />
  return <>{children}</>
}

function PageFrame({ eyebrow, title, description, children }: { eyebrow: string; title: string; description: string; children: ReactNode }) {
  return <section className="page-frame"><div className="page-heading"><p className="eyebrow">{eyebrow}</p><h1>{title}</h1><p className="lead">{description}</p></div>{children}</section>
}

function NotFoundPage() {
  const navigate = useNavigate()
  return <PageFrame eyebrow="404" title="Bu sayfayı bulamadık" description="Adres geçersiz veya sayfa henüz hazır değil."><FeedbackState tone="empty" title="Ana sayfaya dönün" message="Menüden uygulamanın kullanılabilir bölümlerinden birini seçebilirsiniz." actionLabel="Ana sayfaya dön" onAction={() => navigate('/')} /></PageFrame>
}

function StatusCard({ title, detail, state, number }: { title: string; detail: string; state: HealthState; number: string }) {
  const label = state === 'healthy' ? 'Hazır' : state === 'loading' ? 'Kontrol ediliyor' : 'Bağlantı yok'
  return <article className={`status-card ${state}`}><div className="card-number">{number}</div><div className="card-content"><h3>{title}</h3><p>{detail}</p></div><span className="card-status"><span className="status-dot" aria-hidden="true" />{label}</span></article>
}

export default App

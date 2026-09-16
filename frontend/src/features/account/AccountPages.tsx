import { useEffect, useState, type FormEvent } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import projectIdentity from '../../../../content/project-identity.json'
import { useAuth } from '../auth/useAuth'
import { Brand } from '../../layout/AppShell'
import { FeedbackState, type FeedbackTone } from '../../shared/components/FeedbackState'
import { PageHeading } from '../../shared/components/PageRoutes'

export function ProfilePage() {
  const { user } = useAuth()
  const [name, setName] = useState(user?.name ?? '')
  const [phone, setPhone] = useState('')
  const [currency, setCurrency] = useState('TRY')
  const [feedback, setFeedback] = useState<{ tone: FeedbackTone; title: string; message: string } | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    const token = sessionStorage.getItem('travel-assistant-demo-session')
    fetch('/api/profile', { headers: { Authorization: `Bearer ${token ?? ''}` } }).then(async response => {
      if (response.ok) { const data = await response.json(); setName(data.name); setPhone(data.phone); setCurrency(data.currency) }
    }).catch(() => undefined)
  }, [])

  const save = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (saving) return
    setSaving(true)
    setFeedback(null)
    try {
      const token = sessionStorage.getItem('travel-assistant-demo-session')
      const response = await fetch('/api/profile', { method: 'PATCH', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token ?? ''}` }, body: JSON.stringify({ name, phone, currency }) })
      const data = await response.json()
      setFeedback(response.ok ? { tone: 'success', title: 'Profil güncellendi', message: data.message } : { tone: 'error', title: 'Profil güncellenemedi', message: data.message ?? 'Bilgileri kontrol edip yeniden deneyin.' })
    } catch {
      setFeedback({ tone: 'error', title: 'Bağlantı kurulamadı', message: 'Profil servisine ulaşılamadı. Biraz sonra tekrar deneyin.' })
    } finally { setSaving(false) }
  }

  return <main className="page-frame profile-page"><PageHeading label="HESABIM" title="Profil ve tercihlerin" description="Temel hesap bilgilerini ve gösterilecek para birimini buradan yönet." /><form className="profile-card" onSubmit={save}><div className="profile-card-heading"><span className="profile-avatar">{name.slice(0, 1).toLocaleUpperCase('tr-TR')}</span><div><strong>{name || 'Profil bilgileri'}</strong><small>{user?.email}</small></div></div><div className="profile-fields"><label className="form-field"><span>Ad soyad</span><input value={name} onChange={event => setName(event.target.value)} required /></label><label className="form-field"><span>E-posta</span><input value={user?.email ?? ''} readOnly /></label><label className="form-field"><span>Telefon</span><input value={phone} onChange={event => setPhone(event.target.value)} placeholder="+90 5xx xxx xx xx" /></label><label className="form-field"><span>Para birimi</span><select value={currency} onChange={event => setCurrency(event.target.value)}><option value="TRY">TRY — Türk lirası</option><option value="EUR">EUR — Euro</option><option value="USD">USD — Amerikan doları</option><option value="GBP">GBP — İngiliz sterlini</option></select></label></div><div className="profile-actions"><p>Değişiklikler yalnızca bu yerel proje hesabında tutulur.</p><button className="primary-action" type="submit" disabled={saving}>{saving ? 'Kaydediliyor…' : 'Değişiklikleri kaydet'}</button></div>{feedback && <FeedbackState {...feedback} />}</form></main>
}

export function AdminRoute() {
  const { user } = useAuth()
  const navigate = useNavigate()
  const [name, setName] = useState('')
  const [district, setDistrict] = useState('')
  const [cityId, setCityId] = useState('')
  const [cities, setCities] = useState<Array<{ id: string; name: string }>>([])
  const [feedback, setFeedback] = useState<{ tone: FeedbackTone; title: string; message: string } | null>(null)

  useEffect(() => { fetch('/api/travel/cities').then(async response => response.ok ? setCities(await response.json()) : undefined).catch(() => undefined) }, [])
  if (!user) return <Navigate to="/login?returnTo=%2Fadmin" replace />
  if (user.role !== 'admin') return <main className="page-frame"><PageHeading label="YETKİ GEREKLİ" title="Bu alana erişim yok" description="Yönetim alanı yalnızca admin rolüne sahip hesaplarda kullanılabilir." /><FeedbackState tone="error" title="Yetkin bulunmuyor" message="Mevcut hesabınla seyahat aramalarına devam edebilirsin." actionLabel="Ana sayfaya dön" onAction={() => navigate('/')} /></main>

  const addHotel = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setFeedback(null)
    try {
      const token = sessionStorage.getItem('travel-assistant-demo-session')
      const response = await fetch('/api/admin/hotels', { method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token ?? ''}` }, body: JSON.stringify({ name, cityId, district, stars: 3, rating: 4, description: 'Admin tarafından eklenen otel.' }) })
      const data = await response.json()
      setFeedback(response.ok ? { tone: 'success', title: 'Otel kataloğa eklendi', message: data.message } : { tone: 'error', title: 'İşlem tamamlanamadı', message: data.message ?? 'Bilgileri kontrol edin.' })
      if (response.ok) { setName(''); setDistrict(''); setCityId('') }
    } catch { setFeedback({ tone: 'error', title: 'Bağlantı kurulamadı', message: 'Yönetim servisine ulaşılamadı.' }) }
  }

  return <main className="page-frame admin-page"><PageHeading label="YÖNETİM" title="Örnek seyahat verilerini yönet" description="Katalog içeriğini ve simülasyon verilerini kontrollü bir alanda düzenle." /><div className="admin-layout"><form className="admin-form" onSubmit={addHotel}><div><span className="section-label">YENİ KAYIT</span><h2>Otel ekle</h2></div><label className="form-field"><span>Otel adı</span><input required value={name} onChange={event => setName(event.target.value)} /></label><label className="form-field"><span>Bölge</span><input required value={district} onChange={event => setDistrict(event.target.value)} /></label><label className="form-field"><span>Şehir</span><select required value={cityId} onChange={event => setCityId(event.target.value)}><option value="">Şehir seçin</option>{cities.map(city => <option key={city.id} value={city.id}>{city.name}</option>)}</select></label><button className="primary-action" type="submit">Kataloğa ekle</button>{feedback && <FeedbackState {...feedback} />}</form><aside className="admin-summary"><h2>Yönetim kapsamı</h2><div><strong>Otel kataloğu</strong><span>Örnek otel ve konum kayıtları</span></div><div><strong>Uçuş verileri</strong><span>Sefer, fiyat ve örnek koltuk bilgileri</span></div><div><strong>Müsaitlik</strong><span>Aktiflik ve kontenjan güncellemeleri</span></div></aside></div></main>
}

export function LoginPage() {
  const { user, signIn, register, loading, error, clearError } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const returnTo = new URLSearchParams(location.search).get('returnTo') || '/'
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [localError, setLocalError] = useState('')

  useEffect(() => { if (user) navigate(returnTo, { replace: true }) }, [navigate, returnTo, user])

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    clearError()
    setLocalError('')
    if (mode === 'register' && password !== confirm) { setLocalError('Parolalar birbiriyle eşleşmiyor.'); return }
    const ok = mode === 'login' ? await signIn(email, password) : await register(name, email, password)
    if (ok) navigate(returnTo, { replace: true })
  }

  const switchMode = (next: 'login' | 'register') => { setMode(next); setLocalError(''); clearError() }

  return <main className="login-page" data-testid="login-page"><section className="auth-context"><Brand /><div><span className="section-label">SEYAHAT PLANLARIN SENİNLE KALSIN</span><h1>{projectIdentity.name}</h1><p>{projectIdentity.shortDescription}</p></div><div className="auth-benefits"><span><i>✓</i> Simülasyon kayıtlarını hesabına bağla</span><span><i>✓</i> Profil ve para birimi tercihlerini koru</span></div><div className="auth-disclaimer" role="note"><span aria-hidden="true">i</span><p>{projectIdentity.serviceDisclaimer}</p></div></section><section className="auth-card"><Link className="back-home" to="/">← Ana sayfaya dön</Link><div className="auth-tabs" role="tablist" aria-label="Hesap işlemi"><button type="button" role="tab" aria-selected={mode === 'login'} onClick={() => switchMode('login')}>Giriş yap</button><button type="button" role="tab" aria-selected={mode === 'register'} onClick={() => switchMode('register')}>Hesap oluştur</button></div><div className="auth-heading"><span className="section-label">{mode === 'login' ? 'TEKRAR HOŞ GELDİN' : 'YENİ HESAP'}</span><h2>{mode === 'login' ? 'Planlarına devam et' : 'Hesabını oluştur'}</h2><p>{mode === 'login' ? 'E-posta ve parolanla yerel demo hesabına giriş yap.' : 'Bilgilerin yalnızca yerel proje veritabanında tutulur.'}</p></div><form className="auth-form" onSubmit={submit} noValidate>{mode === 'register' && <label className="form-field"><span>Ad soyad</span><input value={name} onChange={event => setName(event.target.value)} autoComplete="name" required minLength={2} /></label>}<label className="form-field"><span>E-posta</span><input type="email" value={email} onChange={event => setEmail(event.target.value)} autoComplete="email" required /></label><label className="form-field"><span>Parola</span><input type="password" value={password} onChange={event => setPassword(event.target.value)} autoComplete={mode === 'login' ? 'current-password' : 'new-password'} required />{mode === 'register' && <small>En az 8 karakter; büyük harf, küçük harf ve rakam içermeli.</small>}</label>{mode === 'register' && <label className="form-field"><span>Parola tekrarı</span><input type="password" value={confirm} onChange={event => setConfirm(event.target.value)} autoComplete="new-password" required /></label>}{(error || localError) && <FeedbackState tone="error" title="İşlem tamamlanamadı" message={localError || error || ''} />}{loading && <FeedbackState tone="loading" title="Bilgiler kontrol ediliyor" message="Lütfen kısa bir süre bekleyin." />}<button type="submit" className="primary-action auth-submit" disabled={loading}>{loading ? 'Kontrol ediliyor…' : mode === 'login' ? 'Giriş yap' : 'Hesap oluştur'} <span aria-hidden="true">→</span></button></form></section></main>
}


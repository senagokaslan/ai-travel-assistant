import { useCallback, useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { FeedbackState } from '../../shared/components/FeedbackState'
import { Icon } from '../../shared/components/Icon'
import { PageHeading } from '../../shared/components/PageRoutes'

type BookingListItem = {
  id: string; referenceCode: string; kind: 'hotel' | 'flight'; title: string; status: string; totalPrice: number | null; currency: string | null
  startDate: string | null; endDate: string | null; createdAt: string; confirmedAt: string | null; cancelledAt: string | null; canCancel: boolean; reason: string | null
}
type JourneyPart = { label: string; airline: string | null; flightNumber: string | null; departureAt: string | null; arrivalAt: string | null; stops: number; fareName: string | null; baggage: string | null }
type Traveler = { type: 'adult' | 'child' | 'infant'; firstName: string; lastName: string; age: number | null; accompanyingAdultIndex: number | null }
type BookingDetail = Omit<BookingListItem, 'startDate' | 'endDate'> & {
  travel: { startDate: string | null; endDate: string | null; adults: number; children: number; infants: number; rooms: number | null; origin: string | null; destination: string | null; tripType: string | null; journeys: JourneyPart[] }
  travelers: Traveler[]
  contact: { name: string; email: string; phone: string } | null
}

const STATUS_LABELS: Record<string, string> = { simulated: 'Onaylandı', cancelled: 'İptal edildi' }
const TRAVELER_LABELS: Record<string, string> = { adult: 'Yetişkin', child: 'Çocuk', infant: 'Bebek' }
const sessionHeaders = () => ({ Authorization: `Bearer ${sessionStorage.getItem('travel-assistant-demo-session') ?? ''}` })

function formatDate(value: string | null, includeTime = false) {
  if (!value) return '—'
  const date = new Date(value.length === 10 ? `${value}T12:00:00` : value)
  if (Number.isNaN(date.getTime())) return '—'
  return new Intl.DateTimeFormat('tr-TR', includeTime ? { dateStyle: 'medium', timeStyle: 'short' } : { dateStyle: 'medium' }).format(date)
}

function price(total: number | null, currency: string | null) {
  return total === null ? '—' : `${total.toLocaleString('tr-TR')} ${currency ?? ''}`.trim()
}

async function fetchBooking(id: string) {
  const response = await fetch(`/api/bookings/${id}`, { headers: sessionHeaders() })
  if (response.status === 404) return { state: 'missing' as const, booking: null }
  if (!response.ok) return { state: 'error' as const, booking: null }
  return { state: 'ready' as const, booking: await response.json() as BookingDetail }
}

export function BookingsPage() {
  const navigate = useNavigate()
  const [state, setState] = useState<'loading' | 'ready' | 'error'>('loading')
  const [bookings, setBookings] = useState<BookingListItem[]>([])

  useEffect(() => {
    fetch('/api/bookings', { headers: sessionHeaders() })
      .then(async response => { if (!response.ok) throw new Error(); return response.json() as Promise<BookingListItem[]> })
      .then(data => { setBookings(data); setState('ready') })
      .catch(() => setState('error'))
  }, [])

  return <main className="page-frame bookings-page">
    <PageHeading label="KAYITLARIM" title="Rezervasyonların" description="Güncel ve geçmiş rezervasyonlarını incele; uygun kayıtları güvenli biçimde iptal et." />
    {state === 'loading' && <FeedbackState tone="loading" title="Rezervasyonlar getiriliyor" message="Hesabına bağlı kayıtlar kontrol ediliyor." />}
    {state === 'error' && <FeedbackState tone="error" title="Rezervasyonlar alınamadı" message="Oturum veya API bağlantısını kontrol edip yeniden deneyin." />}
    {state === 'ready' && bookings.length === 0 && <FeedbackState tone="empty" title="Henüz rezervasyonun yok" message="Otel veya uçuş aramasından seçimini onayladığında kaydın burada görünecek." actionLabel="Otel ara" onAction={() => navigate('/hotels')} />}
    {state === 'ready' && bookings.length > 0 && <section className="booking-history-shell">
      <header><div><small>TOPLAM KAYIT</small><strong>{bookings.length}</strong></div><p>Liste yalnızca giriş yaptığın hesaba ait rezervasyonları içerir.</p></header>
      <div className="booking-history-list">{bookings.map(booking => <article className="booking-history-card" key={booking.id}>
        <div className={`booking-kind-icon ${booking.kind}`}><Icon name={booking.kind === 'hotel' ? 'hotel' : 'plane'} size={20} /></div>
        <div className="booking-history-main"><small>{booking.referenceCode}</small><h2>{booking.title}</h2><p>{formatDate(booking.startDate)}{booking.endDate ? ` → ${formatDate(booking.endDate)}` : ''}</p></div>
        <div className="booking-history-meta"><span className={`booking-status ${booking.status}`}>{STATUS_LABELS[booking.status] ?? booking.status}</span><strong>{price(booking.totalPrice, booking.currency)}</strong><small>{booking.canCancel ? 'İptale uygun' : booking.reason}</small></div>
        <Link className="secondary-action" to={`/bookings/${booking.id}`}>Ayrıntıyı gör</Link>
      </article>)}</div>
    </section>}
  </main>
}

export function BookingDetailPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const [state, setState] = useState<'loading' | 'ready' | 'missing' | 'error'>('loading')
  const [booking, setBooking] = useState<BookingDetail | null>(null)
  const [confirmingCancel, setConfirmingCancel] = useState(false)
  const [cancelling, setCancelling] = useState(false)
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'error'; title: string; message: string } | null>(null)

  const load = useCallback(async () => {
    if (!id) return
    try {
      const result = await fetchBooking(id)
      setBooking(result.booking)
      setState(result.state)
    } catch { setState('error') }
  }, [id])

  useEffect(() => {
    if (!id) return
    let active = true
    void fetchBooking(id)
      .then(result => { if (active) { setBooking(result.booking); setState(result.state) } })
      .catch(() => { if (active) setState('error') })
    return () => { active = false }
  }, [id])

  const cancel = async () => {
    if (!id || cancelling) return
    setCancelling(true)
    setFeedback(null)
    try {
      const response = await fetch(`/api/bookings/${id}/cancel`, { method: 'POST', headers: { ...sessionHeaders(), 'Content-Type': 'application/json' }, body: JSON.stringify({ confirmed: true }) })
      const payload = await response.json().catch(() => ({}))
      if (!response.ok) {
        setFeedback({ tone: 'error', title: 'İptal uygulanmadı', message: payload.message ?? 'Rezervasyon iptal edilemedi.' })
        await load()
        return
      }
      setFeedback({ tone: 'success', title: 'Rezervasyon iptal edildi', message: payload.message })
      setConfirmingCancel(false)
      await load()
    } catch { setFeedback({ tone: 'error', title: 'Bağlantı kurulamadı', message: 'İptalin uygulanıp uygulanmadığını görmek için sayfayı yenileyin. Aynı iptal stokları ikinci kez değiştirmez.' }) }
    finally { setCancelling(false) }
  }

  if (!id) return <main className="page-frame"><FeedbackState tone="empty" title="Rezervasyon bağlantısı geçersiz" message="Rezervasyon kimliği bulunamadı." actionLabel="Rezervasyonlarıma dön" onAction={() => navigate('/bookings')} /></main>
  if (state === 'loading') return <main className="page-frame"><FeedbackState tone="loading" title="Rezervasyon getiriliyor" message="Kayıt ve durum bilgisi kontrol ediliyor." /></main>
  if (state === 'missing') return <main className="page-frame"><FeedbackState tone="empty" title="Rezervasyon bulunamadı" message="Kayıt mevcut değil veya başka bir hesaba ait." actionLabel="Rezervasyonlarıma dön" onAction={() => navigate('/bookings')} /></main>
  if (state === 'error' || !booking) return <main className="page-frame"><FeedbackState tone="error" title="Ayrıntı alınamadı" message="API bağlantısını kontrol edip yeniden deneyin." actionLabel="Rezervasyonlarıma dön" onAction={() => navigate('/bookings')} /></main>

  const totalPeople = booking.travel.adults + booking.travel.children + booking.travel.infants
  return <main className="page-frame booking-detail-page">
    <Link className="detail-back-link" to="/bookings">← Rezervasyonlarıma dön</Link>
    <header className="booking-detail-heading"><div className={`booking-kind-icon ${booking.kind}`}><Icon name={booking.kind === 'hotel' ? 'hotel' : 'plane'} size={24} /></div><div><span className="section-label">{booking.referenceCode}</span><h1>{booking.title}</h1><p>{booking.kind === 'hotel' ? 'Otel rezervasyonu' : 'Uçuş rezervasyonu'} · {formatDate(booking.createdAt, true)}</p></div><span className={`booking-status ${booking.status}`}>{STATUS_LABELS[booking.status] ?? booking.status}</span></header>
    {feedback && <FeedbackState {...feedback} />}
    <div className="booking-detail-grid"><div className="booking-detail-main">
      <section className="booking-detail-section"><h2>Seyahat bilgileri</h2><div className="booking-detail-facts">
        <div><small>Başlangıç</small><strong>{formatDate(booking.travel.startDate)}</strong></div><div><small>Bitiş / dönüş</small><strong>{formatDate(booking.travel.endDate)}</strong></div>
        {booking.kind === 'hotel' ? <><div><small>Oda</small><strong>{booking.travel.rooms ?? '—'}</strong></div><div><small>Misafir</small><strong>{totalPeople} kişi</strong></div></> : <><div><small>Rota</small><strong>{booking.travel.origin} → {booking.travel.destination}</strong></div><div><small>Yolcular</small><strong>{totalPeople} kişi</strong></div></>}
      </div>{booking.travel.journeys.map(journey => <div className="booking-journey-row" key={journey.label}><span>{journey.label}</span><strong>{journey.airline} · {journey.flightNumber}</strong><small>{formatDate(journey.departureAt, true)} → {formatDate(journey.arrivalAt, true)} · {journey.stops ? `${journey.stops} aktarma` : 'Aktarmasız'}</small><b>{journey.fareName} · {journey.baggage}</b></div>)}</section>
      <section className="booking-detail-section"><h2>Kişiler</h2><div className="booking-detail-people">{booking.travelers.map((traveler, index) => <div key={`${traveler.firstName}-${traveler.lastName}-${index}`}><span>{TRAVELER_LABELS[traveler.type] ?? traveler.type} {index + 1}</span><strong>{traveler.firstName} {traveler.lastName}</strong><small>{traveler.age === null ? 'Yaş bilgisi gerekmiyor' : `${traveler.age} yaş`}</small></div>)}</div>{booking.contact && <div className="booking-detail-contact"><span>İletişim kişisi</span><strong>{booking.contact.name}</strong><small>{booking.contact.email} · {booking.contact.phone}</small></div>}</section>
    </div><aside className="booking-cancel-card"><small>TOPLAM TUTAR</small><strong>{price(booking.totalPrice, booking.currency)}</strong><p>Durum: {STATUS_LABELS[booking.status] ?? booking.status}</p>{booking.cancelledAt && <small>İptal: {formatDate(booking.cancelledAt, true)}</small>}
      {booking.canCancel && !confirmingCancel && <button className="danger-action" type="button" onClick={() => setConfirmingCancel(true)}>Rezervasyonu iptal et</button>}
      {booking.canCancel && confirmingCancel && <div className="booking-cancel-confirm" role="alertdialog" aria-label="Rezervasyon iptal onayı"><strong>İptali onaylıyor musun?</strong><p>Rezervasyon iptal edilecek ve ayrılan stok geri yüklenecek.</p><button className="danger-action" type="button" disabled={cancelling} onClick={() => void cancel()}>{cancelling ? 'İptal ediliyor…' : 'Evet, iptal et'}</button><button className="secondary-action" type="button" disabled={cancelling} onClick={() => setConfirmingCancel(false)}>Vazgeç</button></div>}
      {!booking.canCancel && <div className="booking-cancel-unavailable"><strong>İptal kullanılamıyor</strong><p>{booking.reason}</p></div>}
    </aside></div>
  </main>
}

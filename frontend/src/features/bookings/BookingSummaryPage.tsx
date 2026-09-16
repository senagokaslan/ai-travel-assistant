import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { FeedbackState } from '../../shared/components/FeedbackState'
import { Icon } from '../../shared/components/Icon'
import { formatClock, formatFlightDate, formatMinutes } from '../../shared/travelFormat'
import type { FlightItinerary } from '../flights/flightTypes'
import { BookingDetailsForm } from './BookingDetailsForm'

type RoomNight = { date: string; price: number }
type RoomLine = { roomId: string; name: string; quantity: number; nightlyTotal: number; lineTotal: number; nights: RoomNight[] }
type HotelSummary = {
  kind: 'hotel'; title: string; confirmationRequired: boolean; priceChanged: boolean; quotedTotal?: number; quotedCurrency?: string; totalPrice: number; currency: string; searchUrl: string
  stay: { id: string; name: string; city: string; district: string; stars: number; checkIn: string; checkOut: string; nights: number; rooms: number; adults: number; children: number; childAges: number[]; option: { key: string; rooms: RoomLine[] } }
  priceBreakdown: RoomLine[]; conditions: { cancellation: string; board: string[] }
}
type FlightSummary = {
  kind: 'flight'; title: string; confirmationRequired: boolean; priceChanged: boolean; quotedTotal?: number; quotedCurrency?: string; totalPrice: number; currency: string; searchUrl: string
  passengers: { adults: number; children: number; infants: number; travelerCount: number; seatedPassengers: number }
  journey: { outbound: FlightItinerary; inbound: FlightItinerary | null }
  priceBreakdown: { outboundPerTraveler: number; inboundPerTraveler?: number; perTraveler: number; travelerCount: number; calculation: string }
  conditions: { baggage: string[]; changeAndCancellation: string[] }
}
type BookingSummary = HotelSummary | FlightSummary

const BOARD_LABELS: Record<string, string> = { room: 'Sadece oda', breakfast: 'Kahvaltı dahil', half: 'Yarım pansiyon', all: 'Her şey dahil' }

function formatStayDate(value: string) {
  return new Intl.DateTimeFormat('tr-TR', { day: 'numeric', month: 'short', year: 'numeric' }).format(new Date(`${value}T12:00:00`))
}

function FlightBlock({ label, itinerary }: { label: string; itinerary: FlightItinerary }) {
  return <section className="booking-flight-block"><header><div><small>{label}</small><strong>{itinerary.airline} · {itinerary.flightNumber}</strong><span>{itinerary.fare.name}</span></div><b>{itinerary.segments[0].from} → {itinerary.segments.at(-1)?.to}</b></header><div>{itinerary.segments.map(segment => <p key={segment.id}><strong>{segment.from}</strong><span>{formatFlightDate(segment.departureAt)} {formatClock(segment.departureAt)}</span><i>→</i><strong>{segment.to}</strong><span>{formatFlightDate(segment.arrivalAt)} {formatClock(segment.arrivalAt)}</span><small>{formatMinutes(segment.durationMinutes)}</small></p>)}</div></section>
}

function requestFromParams(params: URLSearchParams) {
  const kind = params.get('kind')
  if (kind === 'hotel') return {
    kind,
    endpoint: '/api/bookings/summary/hotel',
    fallback: '/hotels',
    body: {
      hotelId: params.get('hotelId'), optionKey: params.get('optionKey'), checkIn: params.get('checkIn'), checkOut: params.get('checkOut'),
      rooms: Number(params.get('rooms')), adults: Number(params.get('adults')), children: Number(params.get('children')),
      childAges: (params.get('childAges') ?? '').split(',').filter(Boolean).map(Number), quotedTotal: Number(params.get('quotedTotal')) || null, quotedCurrency: params.get('quotedCurrency'),
    },
  }
  if (kind === 'flight') return {
    kind,
    endpoint: '/api/bookings/summary/flight',
    fallback: '/flights',
    body: {
      outboundFareId: params.get('outboundFareId'), inboundFareId: params.get('inboundFareId') || null, from: params.get('from'), to: params.get('to'),
      departureDate: params.get('departureDate'), returnDate: params.get('returnDate') || null, tripType: params.get('tripType'),
      adults: Number(params.get('adults')), children: Number(params.get('children')), infants: Number(params.get('infants')), quotedTotal: Number(params.get('quotedTotal')) || null, quotedCurrency: params.get('quotedCurrency'),
    },
  }
  return null
}

export function BookingSummaryPage() {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const [state, setState] = useState<'loading' | 'ready' | 'error' | 'gone'>('loading')
  const [summary, setSummary] = useState<BookingSummary | null>(null)
  const [error, setError] = useState('')
  const [resultQuery, setResultQuery] = useState('')
  const query = params.toString()
  const approvalKey = `travel-booking-approved:${query}`
  const draftKey = `travel-booking-draft:${query}`
  const rawBookingAttempt = params.get('bookingAttempt')
  const bookingAttempt = rawBookingAttempt && /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(rawBookingAttempt) ? rawBookingAttempt : undefined
  const [approval, setApproval] = useState(() => ({ key: approvalKey, value: sessionStorage.getItem(approvalKey) ?? '' }))
  const approvedPrice = approval.key === approvalKey ? approval.value : sessionStorage.getItem(approvalKey) ?? ''
  const request = useMemo(() => requestFromParams(new URLSearchParams(query)), [query])

  useEffect(() => {
    if (!request) return
    const controller = new AbortController()
    fetch(request.endpoint, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request.body), signal: controller.signal })
      .then(async response => {
        const payload = await response.json().catch(() => ({}))
        if (response.status === 409) { setError(payload.message ?? 'Seçilen ürün artık kullanılamıyor.'); setState('gone'); setResultQuery(query); return null }
        if (!response.ok) throw new Error(payload.message ?? 'Rezervasyon özeti oluşturulamadı.')
        return payload as BookingSummary
      })
      .then(data => { if (data) { setSummary(data); setState('ready'); setResultQuery(query) } })
      .catch(fetchError => { if (fetchError.name !== 'AbortError') { setError(fetchError instanceof Error ? fetchError.message : 'Rezervasyon özeti oluşturulamadı.'); setState('error'); setResultQuery(query) } })
    return () => controller.abort()
  }, [query, request])

  const fallback = request?.fallback ?? '/'
  const visibleState = request ? resultQuery === query ? state : 'loading' : 'error'
  const visibleError = request ? error : 'Rezervasyon özeti bağlantısı eksik veya geçersiz.'
  const currentApproval = summary === null ? '' : `${summary.totalPrice}|${summary.currency}`
  const confirmed = summary !== null && approvedPrice === currentApproval
  return <main className="page-frame booking-summary-page" data-testid="booking-summary-page">
    <button className="detail-back-link booking-back-button" type="button" onClick={() => navigate(-1)}>← Seçime geri dön</button>
    <header className="compact-page-heading"><div><span className="section-label">REZERVASYON</span><h1>Seçimini kontrol et</h1></div><p>Kişisel bilgilerini girmeden önce ürün, tarih, yolcu, fiyat ve koşulları doğrula.</p></header>
    {visibleState === 'loading' && <FeedbackState tone="loading" title="Güncel bilgiler kontrol ediliyor" message="Fiyat ve müsaitlik doğrudan katalogdan yeniden okunuyor." />}
    {visibleState === 'gone' && <FeedbackState tone="empty" title="Seçim artık kullanılamıyor" message={visibleError} actionLabel="Güncel sonuçlara dön" onAction={() => navigate(fallback)} />}
    {visibleState === 'error' && <FeedbackState tone="error" title="Özet oluşturulamadı" message={visibleError} actionLabel="Aramaya dön" onAction={() => navigate(fallback)} />}
    {visibleState === 'ready' && summary && <>
      {summary.priceChanged && <FeedbackState tone="warning" title="Fiyat güncellendi" message={`Önceki ${summary.quotedTotal?.toLocaleString('tr-TR')} ${summary.quotedCurrency ?? summary.currency} yerine güncel toplam ${summary.totalPrice.toLocaleString('tr-TR')} ${summary.currency}. Onay verirsen güncel fiyat geçerli olacak.`} />}
      <section className="booking-review-grid">
        <div className="booking-review-main">
          <div className="booking-product-heading"><span><Icon name={summary.kind === 'hotel' ? 'hotel' : 'plane'} size={20} /></span><div><small>{summary.kind === 'hotel' ? 'OTEL SEÇİMİ' : 'UÇUŞ SEÇİMİ'}</small><h2>{summary.title}</h2></div><b>GÜNCEL</b></div>
          {summary.kind === 'hotel' ? <>
            <div className="booking-facts"><div><small>Giriş</small><strong>{formatStayDate(summary.stay.checkIn)}</strong></div><div><small>Çıkış</small><strong>{formatStayDate(summary.stay.checkOut)}</strong></div><div><small>Konaklama</small><strong>{summary.stay.nights} gece · {summary.stay.rooms} oda</strong></div><div><small>Misafirler</small><strong>{summary.stay.adults} yetişkin · {summary.stay.children} çocuk</strong></div></div>
            <section className="booking-section"><h3>Seçilen odalar ve fiyat oluşumu</h3>{summary.priceBreakdown.map(room => <div className="booking-price-line" key={room.roomId}><span><strong>{room.quantity} × {room.name}</strong><small>{room.nights.map(night => `${formatStayDate(night.date)}: ${night.price.toLocaleString('tr-TR')} TL`).join(' · ')}</small></span><b>{room.lineTotal.toLocaleString('tr-TR')} TL</b></div>)}</section>
            <section className="booking-section"><h3>Önemli koşullar</h3><p><b>İptal:</b> {summary.conditions.cancellation}</p><p><b>Pansiyon:</b> {summary.conditions.board.map(board => BOARD_LABELS[board] ?? board).join(' · ')}</p></section>
          </> : <>
            <div className="booking-facts"><div><small>Yolcular</small><strong>{summary.passengers.adults} yetişkin · {summary.passengers.children} çocuk · {summary.passengers.infants} bebek</strong></div><div><small>Koltuk gereken</small><strong>{summary.passengers.seatedPassengers} yolcu</strong></div><div><small>Kişi başı</small><strong>{summary.priceBreakdown.perTraveler.toLocaleString('tr-TR')} {summary.currency}</strong></div><div><small>Hesaplama</small><strong>{summary.priceBreakdown.calculation}</strong></div></div>
            <FlightBlock label="Gidiş" itinerary={summary.journey.outbound} />{summary.journey.inbound && <FlightBlock label="Dönüş" itinerary={summary.journey.inbound} />}
            <section className="booking-section"><h3>Bagaj ve bilet koşulları</h3>{summary.conditions.baggage.map(item => <p key={item}><b>Bagaj:</b> {item}</p>)}{summary.conditions.changeAndCancellation.map(item => <p key={item}><b>İade/değişiklik:</b> {item}</p>)}</section>
          </>}
        </div>
        <aside className="booking-confirm-card"><small>GÜNCEL TOPLAM</small><strong>{summary.totalPrice.toLocaleString('tr-TR')} {summary.currency}</strong><p>Fiyat ve müsaitlik bu sayfa açılırken yeniden doğrulandı.</p><button className="primary-action" type="button" disabled={confirmed} onClick={() => { sessionStorage.setItem(approvalKey, currentApproval); setApproval({ key: approvalKey, value: currentApproval }) }}>{confirmed ? 'Seçim onaylandı' : 'Özeti açıkça onayla'}</button><button className="secondary-action" type="button" onClick={() => navigate(-1)}>Seçime geri dön</button><Link to={summary.searchUrl}>Arama sonuçlarına dön</Link><small>Bu onay rezervasyon oluşturmaz. Kişisel bilgi ve kesin kayıt sonraki adımda yapılır.</small></aside>
      </section>
      {confirmed && request && <BookingDetailsForm key={draftKey} kind={summary.kind} adults={summary.kind === 'hotel' ? summary.stay.adults : summary.passengers.adults} childCount={summary.kind === 'hotel' ? summary.stay.children : summary.passengers.children} infants={summary.kind === 'hotel' ? 0 : summary.passengers.infants} childAges={summary.kind === 'hotel' ? summary.stay.childAges : []} selection={{ ...request.body, quotedTotal: summary.totalPrice, quotedCurrency: summary.currency }} draftKey={draftKey} searchUrl={summary.searchUrl} attemptKey={bookingAttempt} />}
    </>}
  </main>
}

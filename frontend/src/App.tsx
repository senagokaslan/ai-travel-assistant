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
import { HotelDetailPage, HotelResultsPage, HotelSearchPage } from './components/HotelSearch'
import { Icon } from './components/Icon'
import './App.css'

type HealthState = 'loading' | 'healthy' | 'unhealthy'
type HealthResponse = { status: 'healthy' | 'unhealthy' }
type Airport = { code: string; name: string; city: string; country: string }
type TravelCity = { id: string; name: string }
type FlightFare = {
  id: string
  name: string
  price: number
  currency: string
  baggage: string
  changePolicy: string
}
type FlightSegment = {
  id: string
  order: number
  from: string
  fromAirport: string
  to: string
  toAirport: string
  departureAt: string
  arrivalAt: string
  durationMinutes: number
  seatsAvailable: number
}
type FlightItinerary = {
  id: string
  flightNumber: string
  airline: string
  airlineCode: string
  departureAt: string
  arrivalAt: string
  durationMinutes: number
  stops: number
  seatsAvailable: number
  fare: FlightFare
  segments: FlightSegment[]
}
type FlightJourney = {
  id: string
  tripType: 'one-way' | 'round-trip'
  pricePerTraveler: number
  totalPrice: number
  travelerCount: number
  currency: string
  seatsAvailable: number
  outbound: FlightItinerary
  inbound: FlightItinerary | null
}

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

function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <ScrollToTop />
        <Routes>
          <Route element={<AppShell />}>
            <Route index element={<HomePage />} />
            <Route path="hotels" element={<HotelSearchPage />} />
            <Route path="hotels/results" element={<HotelResultsPage />} />
            <Route path="hotels/:id" element={<HotelDetailPage />} />
            <Route path="flights" element={<FlightSearchPage />} />
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

function ScrollToTop() {
  const { pathname } = useLocation()

  useEffect(() => {
    window.scrollTo({ top: 0, left: 0 })
  }, [pathname])

  return null
}

function Brand() {
  return (
    <Link className="brand" to="/" aria-label="Ana sayfa">
      <span className="brand-mark" aria-hidden="true">
        <Icon name="compass" size={20} />
      </span>
      <span className="brand-copy"><strong>Bağımsız</strong><small>Seyahat Asistanı</small></span>
    </Link>
  )
}

function AppShell() {
  const { user, signOut } = useAuth()
  const health = useHealth()
  const location = useLocation()

  return (
    <div className="app-shell">
      <header className="topbar">
        <div className="topbar-inner">
          <Brand />
          <nav className="main-nav" aria-label="Ana menü">
            <NavLink to="/hotels"><Icon name="hotel" size={15} /><span>Oteller</span></NavLink>
            <NavLink to="/flights"><Icon name="plane" size={15} /><span>Uçuşlar</span></NavLink>
            <NavLink to="/chat"><Icon name="chat" size={15} /><span>Planlama örneği</span></NavLink>
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
                <button className="quiet-button" type="button" onClick={() => void signOut()}>Çıkış</button>
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
        <nav aria-label="Alt menü"><strong>Keşfet</strong><Link to="/hotels">Otel ara</Link><Link to="/flights">Uçuş ara</Link><Link to="/chat">Sohbet örneği</Link></nav>
        <div className="footer-scope"><strong>Demo kapsamı</strong><p>Gerçek rezervasyon ve ödeme yapılmaz. Sonuçlar canlı sağlayıcı verisi değildir.</p></div>
      </div>
      <div className="footer-bottom"><span>© 2026 {projectIdentity.name}</span><span>Eğitim amaçlı yerel proje</span></div>
    </footer>
  )
}

function HomePage() {
  const [cities, setCities] = useState<TravelCity[] | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    fetch('/api/travel/cities', { signal: controller.signal })
      .then(async response => response.ok ? setCities(await response.json() as TravelCity[]) : setCities([]))
      .catch(error => { if (error.name !== 'AbortError') setCities([]) })
    return () => controller.abort()
  }, [])

  return (
    <main className="home-page" data-testid="home-page">
      <section className="home-start page-container" aria-labelledby="home-title">
        <div className="home-start-heading">
          <div><span className="section-label">YEREL SEYAHAT KATALOĞU</span><h1 id="home-title">Seyahatini planlamaya başla</h1></div>
          <p>Otel veya uçuş ölçütlerini girerek örnek seçenekleri karşılaştır; istersen planını nasıl tarif edeceğini sohbet önizlemesinde gör.</p>
        </div>
        <nav className="start-options" aria-label="Arama türü">
          <Link to="/hotels"><Icon name="hotel" size={22} /><span><strong>Otel ara</strong><small>Konum, tarih ve misafir seç</small></span><Icon name="arrow" size={18} /></Link>
          <Link to="/flights"><Icon name="plane" size={22} /><span><strong>Uçuş ara</strong><small>Rota, tarih ve yolcu seç</small></span><Icon name="arrow" size={18} /></Link>
          <Link to="/chat"><Icon name="chat" size={22} /><span><strong>Planlama örneği</strong><small>Sohbet akışını incele</small></span><Icon name="arrow" size={18} /></Link>
        </nav>
        <div className="home-scope" role="note"><Icon name="info" size={18} /><p><strong>Eğitim amaçlı planlama aracı.</strong> {projectIdentity.serviceDisclaimer}</p></div>
      </section>

      {cities && cities.length > 0 && <section className="destination-section page-container" aria-labelledby="destinations-title">
        <div className="content-heading"><div><span className="section-label">MEVCUT KATALOG</span><h2 id="destinations-title">Desteklenen şehirler</h2></div><p>Veritabanındaki şehirlerden biriyle otel aramasına başla.</p></div>
        <div className="destination-list">{cities.map(city => <Link key={city.id} to={`/hotels?q=${encodeURIComponent(city.name)}`}><Icon name="map-pin" size={18} /><span><strong>{city.name}</strong><small>Otel seçeneklerini ara</small></span><Icon name="arrow" size={16} /></Link>)}</div>
      </section>
      }
    </main>
  )
}

function localToday() {
  const now = new Date()
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`
}

function AirportField({ id, label, value, selectedCode, onChange, onSelect, error }: { id: string; label: string; value: string; selectedCode: string; onChange: (value: string) => void; onSelect: (airport: Airport) => void; error?: string }) {
  const [suggestions, setSuggestions] = useState<Airport[]>([])
  const [searchedTerm, setSearchedTerm] = useState('')
  const [lookupState, setLookupState] = useState<'idle' | 'loading' | 'ready' | 'error'>('idle')

  useEffect(() => {
    const term = value.trim()
    if (term.length < 2 || selectedCode) {
      return
    }
    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      setLookupState('loading')
      fetch(`/api/travel/airports?q=${encodeURIComponent(term)}`, { signal: controller.signal })
        .then(async response => {
          if (!response.ok) throw new Error('airport-lookup')
          setSuggestions(await response.json() as Airport[])
          setSearchedTerm(term)
          setLookupState('ready')
        })
        .catch(error => {
          if (error.name === 'AbortError') return
          setSuggestions([])
          setSearchedTerm(term)
          setLookupState('error')
        })
    }, 180)
    return () => { window.clearTimeout(timer); controller.abort() }
  }, [selectedCode, value])

  const listId = `${id}-suggestions`
  const helperId = error ? `${id}-error` : selectedCode ? `${id}-selection` : undefined

  return (
    <div className="form-field airport-field">
      <label htmlFor={id}>{label}<i>*</i></label>
      <input id={id} value={value} onChange={event => { onChange(event.target.value); setSuggestions([]); setSearchedTerm(''); setLookupState('idle') }} onKeyDown={event => { if (event.key === 'Escape') setSuggestions([]) }} placeholder="Şehir, havaalanı veya IATA kodu" autoComplete="off" role="combobox" aria-autocomplete="list" aria-controls={listId} aria-expanded={!selectedCode && suggestions.length > 0} aria-invalid={Boolean(error)} aria-describedby={helperId} />
      {!selectedCode && suggestions.length > 0 && <span id={listId} className="suggestion-popover" role="listbox">{suggestions.map(item => <button type="button" role="option" aria-selected="false" key={item.code} onClick={() => { onSelect(item); setSuggestions([]); setLookupState('idle') }}><b>{item.code}</b><span>{item.city}<small>{item.name} · {item.country}</small></span></button>)}</span>}
      {lookupState === 'loading' && <small className="airport-hint">Havaalanları aranıyor…</small>}
      {!selectedCode && lookupState === 'ready' && searchedTerm === value.trim() && suggestions.length === 0 && <small className="airport-empty">Bu adla eşleşen aktif havaalanı bulunamadı.</small>}
      {!selectedCode && lookupState === 'error' && searchedTerm === value.trim() && <small className="field-error">Havaalanı listesi alınamadı. Tekrar deneyin.</small>}
      {selectedCode && <small id={`${id}-selection`} className="airport-selection">Seçilen havaalanı: <b>{selectedCode}</b></small>}
      {error && <small id={`${id}-error`} className="field-error">{error}</small>}
    </div>
  )
}

function formatClock(value: string) {
  return new Intl.DateTimeFormat('tr-TR', { hour: '2-digit', minute: '2-digit' }).format(new Date(value))
}

function formatDuration(start: string, end: string) {
  const minutes = Math.max(0, Math.round((new Date(end).getTime() - new Date(start).getTime()) / 60_000))
  return `${Math.floor(minutes / 60)} sa ${minutes % 60} dk`
}

function formatMinutes(minutes: number) {
  return `${Math.floor(minutes / 60)} sa ${minutes % 60} dk`
}

function formatFlightDate(value: string) {
  return new Intl.DateTimeFormat('tr-TR', { day: 'numeric', month: 'short' }).format(new Date(value))
}

function FlightItineraryView({ label, itinerary }: { label: string; itinerary: FlightItinerary }) {
  return (
    <section className="itinerary-block">
      <header>
        <div><span>{label}</span><strong>{itinerary.airline}</strong><small>{itinerary.flightNumber} · {itinerary.fare.name}</small></div>
        <div className="itinerary-route-summary"><strong>{formatClock(itinerary.departureAt)}</strong><span>{itinerary.segments[0].from}</span><i /><small>{formatMinutes(itinerary.durationMinutes)} · {itinerary.stops === 0 ? 'Direkt' : `${itinerary.stops} aktarma`}</small><strong>{formatClock(itinerary.arrivalAt)}</strong><span>{itinerary.segments.at(-1)?.to}</span></div>
      </header>
      <div className="segment-list">
        {itinerary.segments.map((segment, index) => {
          const nextSegment = itinerary.segments[index + 1]
          return <div className="segment-group" key={segment.id}>
            <div className="segment-row"><b>{segment.from}</b><span>{formatFlightDate(segment.departureAt)} · {formatClock(segment.departureAt)}</span><i>→</i><b>{segment.to}</b><span>{formatFlightDate(segment.arrivalAt)} · {formatClock(segment.arrivalAt)}</span><small>{formatMinutes(segment.durationMinutes)}</small></div>
            {nextSegment && <div className="connection-row">{segment.to} havaalanında {formatDuration(segment.arrivalAt, nextSegment.departureAt)} aktarma</div>}
          </div>
        })}
      </div>
      <footer><span>{itinerary.fare.baggage}</span><span>{itinerary.fare.changePolicy}</span><strong>{itinerary.seatsAvailable} koltuk kaldı</strong></footer>
    </section>
  )
}

function journeyDuration(journey: FlightJourney) {
  return journey.outbound.durationMinutes + (journey.inbound?.durationMinutes ?? 0)
}

function journeyAirlines(journey: FlightJourney) {
  return Array.from(new Set([journey.outbound.airline, journey.inbound?.airline].filter(Boolean) as string[]))
}

function journeyHasCheckedBag(journey: FlightJourney) {
  return [journey.outbound, journey.inbound].filter(Boolean).some(item => /(?:\+|1[5-9]|[2-9]\d)\s*kg/i.test(item!.fare.baggage))
}

function FlightDetailPanel({ journey, adults, childCount, infants, summary, validating, error, onClose, onContinue }: { journey: FlightJourney; adults: number; childCount: number; infants: number; summary: FlightJourney | null; validating: boolean; error: string; onClose: () => void; onContinue: () => void }) {
  return (
    <section className="flight-detail-panel" aria-labelledby="flight-detail-title">
      <header className="flight-detail-heading"><div><span className="section-label">SEÇİLEN BİLET</span><h2 id="flight-detail-title">Uçuş ayrıntıları</h2></div><button type="button" className="quiet-button" onClick={onClose}>Kapat</button></header>
      <FlightItineraryView label="Gidiş" itinerary={journey.outbound} />
      {journey.inbound && <FlightItineraryView label="Dönüş" itinerary={journey.inbound} />}
      <div className="fare-condition-grid">
        <div><small>Bilet sınıfı</small><strong>{journey.outbound.fare.name}{journey.inbound && journey.inbound.fare.name !== journey.outbound.fare.name ? ` / ${journey.inbound.fare.name}` : ''}</strong></div>
        <div><small>Bagaj hakkı</small><strong>{journey.outbound.fare.baggage}{journey.inbound && ` · Dönüş: ${journey.inbound.fare.baggage}`}</strong></div>
        <div><small>Değişiklik koşulu</small><strong>{journey.outbound.fare.changePolicy}{journey.inbound && ` · Dönüş: ${journey.inbound.fare.changePolicy}`}</strong></div>
        <div><small>Kalan kapasite</small><strong>{journey.seatsAvailable} koltuk</strong></div>
      </div>
      {!summary && <div className="detail-price-action"><div><small>{journey.travelerCount} yolcu · kişi başı</small><span>{journey.pricePerTraveler.toLocaleString('tr-TR')} {journey.currency}</span><strong>{journey.totalPrice.toLocaleString('tr-TR')} {journey.currency}</strong></div><button className="primary-action" type="button" disabled={validating} onClick={onContinue}>{validating ? 'Yeniden kontrol ediliyor…' : 'Rezervasyon özetine geç'}</button></div>}
      {error && <FeedbackState tone="error" title="Bilet yeniden doğrulanamadı" message={error} />}
      {summary && <div className="flight-booking-summary"><header><Icon name="calendar" size={18} /><div><span className="section-label">REZERVASYON ÖZETİ</span><h3>Seçimin hazır</h3></div></header><dl><div><dt>Rota</dt><dd>{summary.outbound.segments[0].from} → {summary.outbound.segments.at(-1)?.to}{summary.inbound ? ' → ' + summary.inbound.segments.at(-1)?.to : ''}</dd></div><div><dt>Yolcular</dt><dd>{adults} yetişkin · {childCount} çocuk · {infants} bebek</dd></div><div><dt>Kişi başı</dt><dd>{summary.pricePerTraveler.toLocaleString('tr-TR')} {summary.currency}</dd></div><div><dt>Genel toplam</dt><dd>{summary.totalPrice.toLocaleString('tr-TR')} {summary.currency}</dd></div></dl><p>Fiyat ve koltuk durumu az önce yeniden doğrulandı. Bu eğitim projesinde gerçek ödeme yapılmaz.</p></div>}
    </section>
  )
}

function FlightSearchPage() {
  const [tripType, setTripType] = useState<'one-way' | 'round-trip'>('one-way')
  const [origin, setOrigin] = useState('')
  const [originAirport, setOriginAirport] = useState<Airport | null>(null)
  const [destination, setDestination] = useState('')
  const [destinationAirport, setDestinationAirport] = useState<Airport | null>(null)
  const [departureDate, setDepartureDate] = useState('')
  const [returnDate, setReturnDate] = useState('')
  const [adults, setAdults] = useState('1')
  const [children, setChildren] = useState('0')
  const [infants, setInfants] = useState('0')
  const [errors, setErrors] = useState<Record<string, string>>({})

  const clearError = (key: string) => {
    setErrors(current => {
      const next = { ...current }
      delete next[key]
      return next
    })
  }
  const [state, setState] = useState<'idle' | 'loading' | 'ready' | 'error'>('idle')
  const [results, setResults] = useState<FlightJourney[]>([])
  const [searchError, setSearchError] = useState('')
  const [sortBy, setSortBy] = useState<'price' | 'duration' | 'departure' | 'stops'>('price')
  const [currencyFilter, setCurrencyFilter] = useState('all')
  const [airlineFilter, setAirlineFilter] = useState('all')
  const [stopsFilter, setStopsFilter] = useState<'all' | 'direct' | 'one'>('all')
  const [baggageFilter, setBaggageFilter] = useState<'all' | 'checked' | 'cabin'>('all')
  const [departureFilter, setDepartureFilter] = useState<'all' | 'morning' | 'afternoon' | 'evening'>('all')
  const [maxDuration, setMaxDuration] = useState('')
  const [maxPrice, setMaxPrice] = useState('')
  const [selectedJourney, setSelectedJourney] = useState<FlightJourney | null>(null)
  const [summaryJourney, setSummaryJourney] = useState<FlightJourney | null>(null)
  const [selectionState, setSelectionState] = useState<'idle' | 'loading'>('idle')
  const [selectionError, setSelectionError] = useState('')

  const invalidateResults = () => {
    setState('idle')
    setResults([])
    setSearchError('')
    setSelectedJourney(null)
    setSummaryJourney(null)
  }

  const buildSearchParams = () => {
    const params = new URLSearchParams({ from: originAirport?.code ?? '', to: destinationAirport?.code ?? '', date: departureDate, tripType, adults, children, infants, passengers: String(Number(adults) + Number(children)) })
    if (tripType === 'round-trip') params.set('returnDate', returnDate)
    return params
  }

  const resetFilters = () => {
    setCurrencyFilter('all'); setAirlineFilter('all'); setStopsFilter('all'); setBaggageFilter('all')
    setDepartureFilter('all'); setMaxDuration(''); setMaxPrice(''); setSortBy('price')
  }

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const from = originAirport?.code ?? ''
    const to = destinationAirport?.code ?? ''
    const nextErrors: Record<string, string> = {}
    if (!from) nextErrors.origin = 'Listeden bir kalkış havaalanı seçin.'
    if (!to) nextErrors.destination = 'Listeden bir varış havaalanı seçin.'
    if (from && to && from === to) nextErrors.destination = 'Varış havaalanı kalkıştan farklı olmalı.'
    if (!departureDate) nextErrors.departureDate = 'Gidiş tarihini seçin.'
    else if (departureDate < localToday()) nextErrors.departureDate = 'Geçmiş tarih için arama yapılamaz.'
    if (tripType === 'round-trip' && !returnDate) nextErrors.returnDate = 'Dönüş tarihini seçin.'
    else if (tripType === 'round-trip' && departureDate && returnDate < departureDate) nextErrors.returnDate = 'Dönüş tarihi gidiş tarihinden önce olamaz.'
    const adultCount = Number(adults)
    const childCount = Number(children)
    const infantCount = Number(infants)
    if (!Number.isInteger(adultCount) || adultCount < 1 || adultCount > 9) nextErrors.adults = 'Yetişkin sayısı 1–9 arasında olmalı.'
    if (!Number.isInteger(childCount) || childCount < 0 || childCount > 8) nextErrors.children = 'Çocuk sayısı 0–8 arasında olmalı.'
    if (!Number.isInteger(infantCount) || infantCount < 0 || infantCount > 9) nextErrors.infants = 'Bebek sayısı 0–9 arasında olmalı.'
    else if (infantCount > adultCount) nextErrors.infants = 'Bebek sayısı yetişkin sayısını aşamaz.'
    if (adultCount + childCount + infantCount > 20) nextErrors.passengers = 'Toplam yolcu sayısı 20’yi aşamaz.'
    setErrors(nextErrors)
    if (Object.keys(nextErrors).length) return

    setState('loading')
    setResults([])
    setSearchError('')
    try {
      const params = buildSearchParams()
      const response = await fetch(`/api/flights?${params}`)
      const payload: unknown = await response.json().catch(() => null)
      if (!response.ok) {
        const apiMessage = payload && typeof payload === 'object' && 'message' in payload && typeof payload.message === 'string' ? payload.message : ''
        throw new Error(apiMessage || 'Uçuş araması tamamlanamadı.')
      }
      if (!Array.isArray(payload)) throw new Error('Uçuş araması tamamlanamadı.')
      const data = payload as FlightJourney[]
      setResults([...data].sort((a, b) => a.totalPrice - b.totalPrice))
      resetFilters()
      setSelectedJourney(null)
      setSummaryJourney(null)
      setState('ready')
    } catch (error) {
      setSearchError(error instanceof Error && error.message !== 'Failed to fetch' ? error.message : 'Arama servisine ulaşılamadı. API ve veritabanı bağlantısını kontrol edip yeniden deneyin.')
      setState('error')
    }
  }

  const revalidateSelection = async () => {
    if (!selectedJourney) return
    setSelectionState('loading')
    setSelectionError('')
    setSummaryJourney(null)
    try {
      const response = await fetch(`/api/flights?${buildSearchParams()}`)
      const payload: unknown = await response.json().catch(() => null)
      if (!response.ok || !Array.isArray(payload)) throw new Error('Bilet seçeneği yeniden kontrol edilemedi.')
      const freshJourney = (payload as FlightJourney[]).find(item => item.id === selectedJourney.id)
      if (!freshJourney) throw new Error('Bu seçenek satışa kapanmış veya yeterli koltuğu kalmamış. Sonuçları yenileyin.')
      setSelectedJourney(freshJourney)
      setSummaryJourney(freshJourney)
    } catch (error) {
      setSelectionError(error instanceof Error ? error.message : 'Bilet seçeneği yeniden kontrol edilemedi.')
    } finally {
      setSelectionState('idle')
    }
  }

  const currencies = Array.from(new Set(results.map(item => item.currency))).sort()
  const airlines = Array.from(new Set(results.flatMap(journeyAirlines))).sort((a, b) => a.localeCompare(b, 'tr'))
  const filteredResults = results
    .filter(item => currencyFilter === 'all' || item.currency === currencyFilter)
    .filter(item => airlineFilter === 'all' || journeyAirlines(item).includes(airlineFilter))
    .filter(item => stopsFilter === 'all' || (stopsFilter === 'direct' ? item.outbound.stops === 0 && (!item.inbound || item.inbound.stops === 0) : item.outbound.stops <= 1 && (!item.inbound || item.inbound.stops <= 1)))
    .filter(item => baggageFilter === 'all' || (baggageFilter === 'checked' ? journeyHasCheckedBag(item) : !journeyHasCheckedBag(item)))
    .filter(item => !maxDuration || journeyDuration(item) <= Number(maxDuration))
    .filter(item => !maxPrice || currencyFilter === 'all' || item.totalPrice <= Number(maxPrice))
    .filter(item => {
      if (departureFilter === 'all') return true
      const hour = new Date(item.outbound.departureAt).getHours()
      return departureFilter === 'morning' ? hour < 12 : departureFilter === 'afternoon' ? hour < 18 && hour >= 12 : hour >= 18
    })
    .sort((a, b) => {
      let comparison = 0
      if (sortBy === 'price') comparison = a.currency === b.currency ? a.totalPrice - b.totalPrice : a.currency.localeCompare(b.currency)
      if (sortBy === 'duration') comparison = journeyDuration(a) - journeyDuration(b)
      if (sortBy === 'departure') comparison = new Date(a.outbound.departureAt).getTime() - new Date(b.outbound.departureAt).getTime()
      if (sortBy === 'stops') comparison = a.outbound.stops + (a.inbound?.stops ?? 0) - b.outbound.stops - (b.inbound?.stops ?? 0)
      return comparison || journeyDuration(a) - journeyDuration(b) || a.id.localeCompare(b.id)
    })

  return (
    <main className="page-frame flight-page" data-testid="flight-page">
      <header className="compact-page-heading"><div><span className="section-label">UÇUŞLAR</span><h1>Uçuş ara</h1></div><p>Yerel katalogdaki örnek seferleri rota, saat ve fiyat bilgileriyle karşılaştır.</p></header>
      <form className="flight-search-form search-workbench" onSubmit={submit} noValidate aria-busy={state === 'loading'}>
        <div className="workbench-heading"><div><Icon name="plane" size={19} /><h2>Uçuş bilgileri</h2></div><small><i>*</i> Zorunlu alan</small></div>
        <div className="trip-type-toggle" role="radiogroup" aria-label="Yolculuk türü"><button type="button" role="radio" aria-checked={tripType === 'one-way'} className={tripType === 'one-way' ? 'selected' : ''} onClick={() => { setTripType('one-way'); setReturnDate(''); clearError('returnDate'); invalidateResults() }}>Tek yön</button><button type="button" role="radio" aria-checked={tripType === 'round-trip'} className={tripType === 'round-trip' ? 'selected' : ''} onClick={() => { setTripType('round-trip'); invalidateResults() }}>Gidiş dönüş</button></div>
        {Object.keys(errors).length > 0 && <FeedbackState tone="error" title="Arama bilgilerini kontrol edin" message="İşaretli alanları düzelttikten sonra tekrar deneyin." />}
        <div className="flight-form-grid">
          <AirportField id="flight-origin" label="Nereden" value={origin} selectedCode={originAirport?.code ?? ''} onChange={value => { setOrigin(value); setOriginAirport(null); clearError('origin'); invalidateResults() }} onSelect={airport => { setOrigin(`${airport.city} — ${airport.name} (${airport.code})`); setOriginAirport(airport); clearError('origin'); invalidateResults() }} error={errors.origin} />
          <button className="swap-route" type="button" aria-label="Kalkış ve varışı değiştir" onClick={() => { setOrigin(destination); setDestination(origin); setOriginAirport(destinationAirport); setDestinationAirport(originAirport); clearError('origin'); clearError('destination'); invalidateResults() }}><Icon name="swap" size={18} /></button>
          <AirportField id="flight-destination" label="Nereye" value={destination} selectedCode={destinationAirport?.code ?? ''} onChange={value => { setDestination(value); setDestinationAirport(null); clearError('destination'); invalidateResults() }} onSelect={airport => { setDestination(`${airport.city} — ${airport.name} (${airport.code})`); setDestinationAirport(airport); clearError('destination'); invalidateResults() }} error={errors.destination} />
          <label className="form-field"><span>Gidiş tarihi<i>*</i></span><input type="date" min={localToday()} value={departureDate} onChange={event => { const nextDate = event.target.value; setDepartureDate(nextDate); if (returnDate && returnDate < nextDate) setReturnDate(''); clearError('departureDate'); clearError('returnDate'); invalidateResults() }} aria-invalid={Boolean(errors.departureDate)} />{errors.departureDate && <small className="field-error">{errors.departureDate}</small>}</label>
          {tripType === 'round-trip' && <label className="form-field"><span>Dönüş tarihi<i>*</i></span><input type="date" min={departureDate || localToday()} value={returnDate} onChange={event => { setReturnDate(event.target.value); clearError('returnDate'); invalidateResults() }} aria-invalid={Boolean(errors.returnDate)} />{errors.returnDate && <small className="field-error">{errors.returnDate}</small>}</label>}
        </div>
        <div className="flight-passenger-row">
          <div className="passenger-heading"><strong>Yolcular</strong><small>Bebekler yetişkin kucağında seyahat eder.</small></div>
          <label className="form-field"><span>Yetişkin<i>*</i></span><select value={adults} onChange={event => { setAdults(event.target.value); clearError('adults'); clearError('infants'); clearError('passengers'); invalidateResults() }}>{Array.from({ length: 9 }, (_, index) => <option key={index + 1} value={index + 1}>{index + 1} yetişkin</option>)}</select>{errors.adults && <small className="field-error">{errors.adults}</small>}</label>
          <label className="form-field"><span>Çocuk (2–11)</span><select value={children} onChange={event => { setChildren(event.target.value); clearError('children'); clearError('passengers'); invalidateResults() }}>{Array.from({ length: 9 }, (_, index) => <option key={index} value={index}>{index} çocuk</option>)}</select>{errors.children && <small className="field-error">{errors.children}</small>}</label>
          <label className="form-field"><span>Bebek (0–1)</span><select value={infants} onChange={event => { setInfants(event.target.value); clearError('infants'); clearError('passengers'); invalidateResults() }}>{Array.from({ length: 10 }, (_, index) => <option key={index} value={index}>{index} bebek</option>)}</select>{errors.infants && <small className="field-error">{errors.infants}</small>}</label>
          <button className="primary-action flight-submit" type="submit" disabled={state === 'loading'}><Icon name="search" size={17} />{state === 'loading' ? 'Aranıyor…' : 'Uçuş ara'}</button>
        </div>
        {errors.passengers && <small className="field-error passenger-total-error">{errors.passengers}</small>}
      </form>

      <section className="flight-results" aria-live="polite">
        {state === 'idle' && <div className="results-placeholder"><Icon name="search" size={24} /><div><h2>Arama ölçütlerini tamamla</h2><p>Kalkış, varış ve tarihi seçtiğinde sonuçlar formun hemen altında listelenecek.</p></div><span>Canlı sağlayıcı verisi kullanılmaz.</span></div>}
        {state === 'loading' && <LoadingCards label="Uygun uçuşlar aranıyor" />}
        {state === 'error' && <FeedbackState tone="error" title="Uçuşlar getirilemedi" message={searchError} />}
        {state === 'ready' && results.length === 0 && <div className="results-placeholder empty-result"><Icon name="info" size={24} /><div><h2>Bu rota için sefer bulunamadı</h2><p>Yukarıdaki formdan kalkış veya varış havaalanını ya da tarihi değiştirip yeniden arayabilirsin.</p></div></div>}
        {state === 'ready' && results.length > 0 && <>
          <div className="result-toolbar"><div><strong>{filteredResults.length} / {results.length} yolculuk seçeneği</strong><span>{originAirport?.code} → {destinationAirport?.code} · {departureDate}{tripType === 'round-trip' ? ` · dönüş ${returnDate}` : ' · tek yön'} · {Number(adults) + Number(children) + Number(infants)} yolcu</span></div><label>Sırala<select value={sortBy} onChange={event => setSortBy(event.target.value as typeof sortBy)}><option value="price">En düşük toplam</option><option value="duration">En kısa yolculuk</option><option value="departure">En erken kalkış</option><option value="stops">En az aktarma</option></select></label></div>
          <section className="flight-filter-panel" aria-label="Uçuş filtreleri">
            <header><div><Icon name="filter" size={17} /><strong>Filtreler</strong></div><button type="button" onClick={resetFilters}>Tümünü temizle</button></header>
            <label><span>Para birimi</span><select value={currencyFilter} onChange={event => { setCurrencyFilter(event.target.value); setMaxPrice('') }}><option value="all">Tümü</option>{currencies.map(currency => <option key={currency} value={currency}>{currency}</option>)}</select></label>
            <label><span>Havayolu</span><select value={airlineFilter} onChange={event => setAirlineFilter(event.target.value)}><option value="all">Tümü</option>{airlines.map(airline => <option key={airline} value={airline}>{airline}</option>)}</select></label>
            <label><span>Aktarma</span><select value={stopsFilter} onChange={event => setStopsFilter(event.target.value as typeof stopsFilter)}><option value="all">Tümü</option><option value="direct">Yalnızca direkt</option><option value="one">En fazla 1 aktarma</option></select></label>
            <label><span>Bagaj</span><select value={baggageFilter} onChange={event => setBaggageFilter(event.target.value as typeof baggageFilter)}><option value="all">Tümü</option><option value="checked">Kayıtlı bagaj dahil</option><option value="cabin">Yalnız kabin/el bagajı</option></select></label>
            <label><span>Kalkış zamanı</span><select value={departureFilter} onChange={event => setDepartureFilter(event.target.value as typeof departureFilter)}><option value="all">Tümü</option><option value="morning">Sabah (00–12)</option><option value="afternoon">Öğleden sonra (12–18)</option><option value="evening">Akşam (18–24)</option></select></label>
            <label><span>Azami toplam süre</span><select value={maxDuration} onChange={event => setMaxDuration(event.target.value)}><option value="">Sınırsız</option><option value="120">2 saat</option><option value="240">4 saat</option><option value="480">8 saat</option></select></label>
            <label><span>Azami toplam fiyat</span><input type="number" min="0" step="100" value={maxPrice} disabled={currencyFilter === 'all'} placeholder={currencyFilter === 'all' ? 'Önce para birimi seç' : currencyFilter} onChange={event => setMaxPrice(event.target.value)} /></label>
          </section>
          {selectedJourney && <FlightDetailPanel journey={selectedJourney} adults={Number(adults)} childCount={Number(children)} infants={Number(infants)} summary={summaryJourney} validating={selectionState === 'loading'} error={selectionError} onClose={() => { setSelectedJourney(null); setSummaryJourney(null); setSelectionError('') }} onContinue={() => void revalidateSelection()} />}
          {filteredResults.length === 0 && <FeedbackState tone="empty" title="Filtrelerle eşleşen uçuş yok" message="Bir veya daha fazla filtreyi gevşeterek yeniden deneyin." />}
          <div className="flight-list">{filteredResults.map(result => <article className="journey-option-card" key={result.id}><header className="journey-option-heading"><div><span>{result.tripType === 'round-trip' ? 'Gidiş dönüş' : 'Tek yön'}</span><strong>{result.outbound.segments[0].from} → {result.outbound.segments.at(-1)?.to}</strong></div><div><small>{result.travelerCount} yolcu toplamı</small><strong>{result.totalPrice.toLocaleString('tr-TR')} {result.currency}</strong><span>{result.pricePerTraveler.toLocaleString('tr-TR')} {result.currency} / kişi · {result.seatsAvailable} koltuk</span></div></header><FlightItineraryView label="Gidiş" itinerary={result.outbound} />{result.inbound && <FlightItineraryView label="Dönüş" itinerary={result.inbound} />}<footer className="journey-card-action"><span>{journeyAirlines(result).join(' · ')} · {formatMinutes(journeyDuration(result))}</span><button className="secondary-action" type="button" onClick={() => { setSelectedJourney(result); setSummaryJourney(null); setSelectionError('') }}>Ayrıntıları gör</button></footer></article>)}</div>
        </>}
      </section>
    </main>
  )
}

function LoadingCards({ label }: { label: string }) {
  return <div className="loading-results" aria-label={label}><div className="loading-label"><span className="spinner" aria-hidden="true" />{label}</div>{[1, 2, 3].map(item => <div className="skeleton-card" key={item}><span /><div><i /><i /><i /></div><b /></div>)}</div>
}

function ChatPage() {
  return (
    <main className="chat-page" data-testid="chat-page">
      <section className="chat-workspace" aria-label="Sohbet planlama önizlemesi">
        <header className="chat-workspace-header"><div><span className="assistant-avatar"><Icon name="chat" size={18} /></span><div><h1>Seyahat planlama örneği</h1><p>Sohbet API’si bağlı değil · etkileşimsiz önizleme</p></div></div><span className="preview-status">ÖRNEK AKIŞ</span></header>
        <div className="message-list">
          <div className="chat-context-note"><Icon name="info" size={18} /><p>Bu konuşma yalnızca asistanın hangi bilgileri isteyeceğini gösterir. Yazılan mesajlar gönderilmez veya kaydedilmez.</p></div>
          <div className="chat-message assistant"><small>Seyahat yardımcısı</small><p>Nasıl bir seyahat planlıyorsun: konaklama mı, uçuş mu?</p></div>
          <div className="chat-message user"><small>Örnek kullanıcı</small><p>İstanbul’dan Antalya’ya iki kişi için uçuş arıyorum.</p></div>
          <div className="chat-message assistant"><small>Seyahat yardımcısı</small><p>Hangi tarihte uçmak istersin? Tarihi aldıktan sonra örnek katalogdaki seferler aranabilir.</p></div>
        </div>
        <div className="chat-shortcuts"><span>Aramaya doğrudan devam et:</span><Link to="/hotels"><Icon name="hotel" size={15} />Otel formu</Link><Link to="/flights"><Icon name="plane" size={15} />Uçuş formu</Link></div>
        <div className="chat-composer" aria-disabled="true"><input disabled value="" placeholder="Canlı sohbet bu sürümde kullanılamıyor" aria-label="Sohbet mesajı" /><button disabled type="button" aria-label="Mesaj gönder"><Icon name="arrow" size={18} /></button></div>
      </section>
    </main>
  )
}

function BookingsPage() {
  const navigate = useNavigate()
  const [count, setCount] = useState<number | null>(null)
  const [failed, setFailed] = useState(false)
  const token = sessionStorage.getItem('travel-assistant-demo-session')

  useEffect(() => {
    fetch('/api/bookings', { headers: { Authorization: `Bearer ${token ?? ''}` } })
      .then(async response => { if (!response.ok) throw new Error(); setCount((await response.json() as unknown[]).length) })
      .catch(() => setFailed(true))
  }, [token])

  return <main className="page-frame bookings-page"><PageHeading label="KAYITLARIM" title="Rezervasyon simülasyonların" description="Hesabına bağlı eğitim amaçlı simülasyon kayıtlarının genel durumunu burada takip et." />{failed ? <FeedbackState tone="error" title="Kayıt bilgisi alınamadı" message="Oturum veya API bağlantısını kontrol edip yeniden deneyin." /> : count === null ? <FeedbackState tone="loading" title="Kayıt sayısı getiriliyor" message="Hesabınızdaki simülasyonlar kontrol ediliyor." /> : <section className="booking-summary"><div className="count-panel"><small>TOPLAM KAYIT</small><strong>{count}</strong><span>rezervasyon simülasyonu</span></div><div><h2>{count ? 'Kayıtların hesabına bağlı' : 'Henüz bir simülasyon kaydın yok'}</h2><p>Bu görünüm şu anda yalnızca kayıt sayısını gösteriyor; ayrıntılı bir rezervasyon listesi sunulmuyor.</p><button className="primary-action" type="button" onClick={() => navigate('/hotels')}>Yeni otel araması</button></div></section>}</main>
}

function ProfilePage() {
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

function AdminRoute() {
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

function ProtectedRoute({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  const location = useLocation()
  if (!user) return <Navigate to={`/login?returnTo=${encodeURIComponent(location.pathname)}`} replace />
  return <>{children}</>
}

function PageHeading({ label, title, description }: { label: string; title: string; description: string }) {
  return <header className="page-heading"><span className="section-label">{label}</span><h1>{title}</h1><p>{description}</p></header>
}

function NotFoundPage() {
  const navigate = useNavigate()
  return <main className="page-frame not-found-page"><div className="not-found-code">404</div><PageHeading label="YOLUN DIŞINA ÇIKTIK" title="Bu sayfayı bulamadık" description="Adres değişmiş veya aradığın sayfa bu demoda yer almıyor olabilir." /><button className="primary-action" type="button" onClick={() => navigate('/')}>Ana sayfaya dön</button></main>
}

export default App

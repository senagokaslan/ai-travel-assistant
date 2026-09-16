import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { FeedbackState } from '../../shared/components/FeedbackState'
import { apiErrorMessage } from '../../shared/apiError'
import { Icon } from '../../shared/components/Icon'
import { formatClock, formatFlightDate, formatMinutes } from '../../shared/travelFormat'
import type { Airport, FlightItinerary, FlightJourney } from './flightTypes'

function localToday() {
  const now = new Date()
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`
}

function AirportField({ id, label, value, selectedCode, onChange, onSelect, error }: { id: string; label: string; value: string; selectedCode: string; onChange: (value: string) => void; onSelect: (airport: Airport) => void; error?: string }) {
  const [suggestions, setSuggestions] = useState<Airport[]>([])
  const [searchedTerm, setSearchedTerm] = useState('')
  const [lookupState, setLookupState] = useState<'idle' | 'loading' | 'ready' | 'error'>('idle')
  const [activeIndex, setActiveIndex] = useState(-1)

  useEffect(() => {
    const term = value.trim()
    if (term.length < 2 || selectedCode) return
    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      setLookupState('loading')
      fetch(`/api/travel/airports?q=${encodeURIComponent(term)}`, { signal: controller.signal })
        .then(async response => {
          if (!response.ok) throw new Error('airport-lookup')
          setSuggestions(await response.json() as Airport[])
          setActiveIndex(-1)
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
  const choose = (airport: Airport) => {
    onSelect(airport)
    setSuggestions([])
    setActiveIndex(-1)
    setLookupState('idle')
  }
  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Escape') { setSuggestions([]); setActiveIndex(-1); return }
    if (!suggestions.length || selectedCode) return
    if (event.key === 'ArrowDown') { event.preventDefault(); setActiveIndex(index => (index + 1) % suggestions.length) }
    if (event.key === 'ArrowUp') { event.preventDefault(); setActiveIndex(index => index <= 0 ? suggestions.length - 1 : index - 1) }
    if (event.key === 'Enter' && activeIndex >= 0) { event.preventDefault(); choose(suggestions[activeIndex]) }
  }

  return (
    <div className="form-field airport-field">
      <label htmlFor={id}>{label}<i>*</i></label>
      <input id={id} value={value} onChange={event => { onChange(event.target.value); setSuggestions([]); setActiveIndex(-1); setSearchedTerm(''); setLookupState('idle') }} onKeyDown={onKeyDown} placeholder="Şehir, havaalanı veya IATA kodu" autoComplete="off" role="combobox" aria-autocomplete="list" aria-controls={listId} aria-activedescendant={activeIndex >= 0 ? `${id}-option-${activeIndex}` : undefined} aria-expanded={!selectedCode && suggestions.length > 0} aria-invalid={Boolean(error)} aria-describedby={helperId} aria-busy={lookupState === 'loading'} />
      {!selectedCode && suggestions.length > 0 && <span id={listId} className="suggestion-popover" role="listbox">{suggestions.map((item, index) => <button id={`${id}-option-${index}`} type="button" role="option" aria-selected={activeIndex === index} key={item.code} onMouseEnter={() => setActiveIndex(index)} onClick={() => choose(item)}><b>{item.code}</b><span>{item.city}<small>{item.name} · {item.country}</small></span></button>)}</span>}
      {lookupState === 'loading' && <small className="airport-hint">Havaalanları aranıyor…</small>}
      {!selectedCode && lookupState === 'ready' && searchedTerm === value.trim() && suggestions.length === 0 && <small className="airport-empty">Bu adla eşleşen aktif havaalanı bulunamadı.</small>}
      {!selectedCode && lookupState === 'error' && searchedTerm === value.trim() && <small className="field-error">Havaalanı listesi alınamadı. Tekrar deneyin.</small>}
      {selectedCode && <small id={`${id}-selection`} className="airport-selection">Seçilen havaalanı: <b>{selectedCode}</b></small>}
      {error && <small id={`${id}-error`} className="field-error">{error}</small>}
    </div>
  )
}

function formatDuration(start: string, end: string) {
  const minutes = Math.max(0, Math.round((new Date(end).getTime() - new Date(start).getTime()) / 60_000))
  return `${Math.floor(minutes / 60)} sa ${minutes % 60} dk`
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

function FlightDetailPanel({ journey, validating, error, onClose, onContinue }: { journey: FlightJourney; validating: boolean; error: string; onClose: () => void; onContinue: () => void }) {
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
      <div className="detail-price-action"><div><small>{journey.travelerCount} yolcu · kişi başı</small><span>{journey.pricePerTraveler.toLocaleString('tr-TR')} {journey.currency}</span><strong>{journey.totalPrice.toLocaleString('tr-TR')} {journey.currency}</strong></div><button className="primary-action" type="button" disabled={validating} onClick={onContinue}>{validating ? 'Yeniden kontrol ediliyor…' : 'Rezervasyon özetine geç'}</button></div>
      {error && <FeedbackState tone="error" title="Bilet yeniden doğrulanamadı" message={error} />}
    </section>
  )
}

export function FlightSearchPage() {
  const location = useLocation()
  const navigate = useNavigate()
  const [initialQuery] = useState(() => new URLSearchParams(location.search))
  const initialOrigin = initialQuery.get('from')?.toUpperCase() ?? ''
  const initialDestination = initialQuery.get('to')?.toUpperCase() ?? ''
  const [tripType, setTripType] = useState<'one-way' | 'round-trip'>(initialQuery.get('tripType') === 'round-trip' ? 'round-trip' : 'one-way')
  const [origin, setOrigin] = useState(initialOrigin)
  const [originAirport, setOriginAirport] = useState<Airport | null>(initialOrigin ? { code: initialOrigin, name: initialOrigin, city: '', country: '' } : null)
  const [destination, setDestination] = useState(initialDestination)
  const [destinationAirport, setDestinationAirport] = useState<Airport | null>(initialDestination ? { code: initialDestination, name: initialDestination, city: '', country: '' } : null)
  const [departureDate, setDepartureDate] = useState(initialQuery.get('date') ?? '')
  const [returnDate, setReturnDate] = useState(initialQuery.get('returnDate') ?? '')
  const [adults, setAdults] = useState(initialQuery.get('adults') ?? '1')
  const [children, setChildren] = useState(initialQuery.get('children') ?? '0')
  const [infants, setInfants] = useState(initialQuery.get('infants') ?? '0')
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
  const [selectionState, setSelectionState] = useState<'idle' | 'loading'>('idle')
  const [selectionError, setSelectionError] = useState('')
  const formRef = useRef<HTMLFormElement>(null)

  useEffect(() => {
    if (!initialOrigin || !initialDestination || !initialQuery.get('date')) return
    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      const params = new URLSearchParams(initialQuery)
      params.set('passengers', String(Number(initialQuery.get('adults') ?? 1) + Number(initialQuery.get('children') ?? 0)))
      setState('loading')
      fetch(`/api/flights?${params}`, { signal: controller.signal })
        .then(async response => { if (!response.ok) throw new Error(); return await response.json() as FlightJourney[] })
        .then(data => { setResults(data); setState('ready') })
        .catch(error => { if (error.name !== 'AbortError') { setSearchError('Sohbetten aktarılan uçuş araması açılamadı.'); setState('error') } })
    }, 0)
    return () => { window.clearTimeout(timer); controller.abort() }
  }, [initialDestination, initialOrigin, initialQuery])

  const invalidateResults = () => {
    setState('idle')
    setResults([])
    setSearchError('')
    setSelectedJourney(null)
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
    if (Object.keys(nextErrors).length) {
      window.requestAnimationFrame(() => formRef.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
      return
    }

    setState('loading')
    setResults([])
    setSearchError('')
    try {
      const params = buildSearchParams()
      const response = await fetch(`/api/flights?${params}`)
      const payload: unknown = await response.json().catch(() => null)
      if (!response.ok) {
        throw new Error(apiErrorMessage(payload, response, 'Uçuş araması tamamlanamadı. Formu kontrol edip tekrar deneyin.'))
      }
      if (!Array.isArray(payload)) throw new Error('Uçuş araması tamamlanamadı.')
      const data = payload as FlightJourney[]
      setResults([...data].sort((a, b) => a.totalPrice - b.totalPrice))
      resetFilters()
      setSelectedJourney(null)
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
    try {
      const response = await fetch(`/api/flights?${buildSearchParams()}`)
      const payload: unknown = await response.json().catch(() => null)
      if (!response.ok) throw new Error(apiErrorMessage(payload, response, 'Bilet seçeneği yeniden kontrol edilemedi. Sonuçları yenileyin.'))
      if (!Array.isArray(payload)) throw new Error('Bilet seçeneği yeniden kontrol edilemedi. Sonuçları yenileyin.')
      const freshJourney = (payload as FlightJourney[]).find(item => item.id === selectedJourney.id)
      if (!freshJourney) throw new Error('Bu seçenek satışa kapanmış veya yeterli koltuğu kalmamış. Sonuçları yenileyin.')
      setSelectedJourney(freshJourney)
      const params = new URLSearchParams({
        kind: 'flight', outboundFareId: freshJourney.outbound.fare.id, from: originAirport?.code ?? '', to: destinationAirport?.code ?? '',
        departureDate, tripType, adults, children, infants, quotedTotal: String(selectedJourney.totalPrice), quotedCurrency: selectedJourney.currency, bookingAttempt: crypto.randomUUID(),
      })
      if (freshJourney.inbound) params.set('inboundFareId', freshJourney.inbound.fare.id)
      if (tripType === 'round-trip') params.set('returnDate', returnDate)
      navigate(`/booking/summary?${params}`)
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
      <form ref={formRef} className="flight-search-form search-workbench" onSubmit={submit} noValidate aria-busy={state === 'loading'}>
        <div className="workbench-heading"><div><Icon name="plane" size={19} /><h2>Uçuş bilgileri</h2></div><small><i>*</i> Zorunlu alan</small></div>
        <div className="trip-type-toggle" role="radiogroup" aria-label="Yolculuk türü"><button type="button" role="radio" aria-checked={tripType === 'one-way'} className={tripType === 'one-way' ? 'selected' : ''} onClick={() => { setTripType('one-way'); setReturnDate(''); clearError('returnDate'); invalidateResults() }}>Tek yön</button><button type="button" role="radio" aria-checked={tripType === 'round-trip'} className={tripType === 'round-trip' ? 'selected' : ''} onClick={() => { setTripType('round-trip'); invalidateResults() }}>Gidiş dönüş</button></div>
        {Object.keys(errors).length > 0 && <FeedbackState tone="error" title="Arama bilgilerini kontrol edin" message="İşaretli alanları düzelttikten sonra tekrar deneyin." />}
        <div className="flight-form-grid">
          <AirportField id="flight-origin" label="Nereden" value={origin} selectedCode={originAirport?.code ?? ''} onChange={value => { setOrigin(value); setOriginAirport(null); clearError('origin'); invalidateResults() }} onSelect={airport => { setOrigin(`${airport.city} — ${airport.name} (${airport.code})`); setOriginAirport(airport); clearError('origin'); invalidateResults() }} error={errors.origin} />
          <button className="swap-route" type="button" aria-label="Kalkış ve varışı değiştir" onClick={() => { setOrigin(destination); setDestination(origin); setOriginAirport(destinationAirport); setDestinationAirport(originAirport); clearError('origin'); clearError('destination'); invalidateResults() }}><Icon name="swap" size={18} /></button>
          <AirportField id="flight-destination" label="Nereye" value={destination} selectedCode={destinationAirport?.code ?? ''} onChange={value => { setDestination(value); setDestinationAirport(null); clearError('destination'); invalidateResults() }} onSelect={airport => { setDestination(`${airport.city} — ${airport.name} (${airport.code})`); setDestinationAirport(airport); clearError('destination'); invalidateResults() }} error={errors.destination} />
          <label className="form-field"><span>Gidiş tarihi<i>*</i></span><input id="flight-departure-date" type="date" min={localToday()} value={departureDate} onChange={event => { const nextDate = event.target.value; setDepartureDate(nextDate); if (returnDate && returnDate < nextDate) setReturnDate(''); clearError('departureDate'); clearError('returnDate'); invalidateResults() }} aria-invalid={Boolean(errors.departureDate)} aria-describedby={errors.departureDate ? 'flight-departure-date-error' : undefined} />{errors.departureDate && <small id="flight-departure-date-error" className="field-error">{errors.departureDate}</small>}</label>
          {tripType === 'round-trip' && <label className="form-field"><span>Dönüş tarihi<i>*</i></span><input id="flight-return-date" type="date" min={departureDate || localToday()} value={returnDate} onChange={event => { setReturnDate(event.target.value); clearError('returnDate'); invalidateResults() }} aria-invalid={Boolean(errors.returnDate)} aria-describedby={errors.returnDate ? 'flight-return-date-error' : undefined} />{errors.returnDate && <small id="flight-return-date-error" className="field-error">{errors.returnDate}</small>}</label>}
        </div>
        <div className="flight-passenger-row">
          <div className="passenger-heading"><strong>Yolcular</strong><small>Bebekler yetişkin kucağında seyahat eder.</small></div>
          <label className="form-field"><span>Yetişkin<i>*</i></span><select id="flight-adults" value={adults} onChange={event => { setAdults(event.target.value); clearError('adults'); clearError('infants'); clearError('passengers'); invalidateResults() }} aria-invalid={Boolean(errors.adults)} aria-describedby={errors.adults ? 'flight-adults-error' : undefined}>{Array.from({ length: 9 }, (_, index) => <option key={index + 1} value={index + 1}>{index + 1} yetişkin</option>)}</select>{errors.adults && <small id="flight-adults-error" className="field-error">{errors.adults}</small>}</label>
          <label className="form-field"><span>Çocuk (2–11)</span><select id="flight-children" value={children} onChange={event => { setChildren(event.target.value); clearError('children'); clearError('passengers'); invalidateResults() }} aria-invalid={Boolean(errors.children)} aria-describedby={errors.children ? 'flight-children-error' : undefined}>{Array.from({ length: 9 }, (_, index) => <option key={index} value={index}>{index} çocuk</option>)}</select>{errors.children && <small id="flight-children-error" className="field-error">{errors.children}</small>}</label>
          <label className="form-field"><span>Bebek (0–1)</span><select id="flight-infants" value={infants} onChange={event => { setInfants(event.target.value); clearError('infants'); clearError('passengers'); invalidateResults() }} aria-invalid={Boolean(errors.infants)} aria-describedby={errors.infants ? 'flight-infants-error' : undefined}>{Array.from({ length: 10 }, (_, index) => <option key={index} value={index}>{index} bebek</option>)}</select>{errors.infants && <small id="flight-infants-error" className="field-error">{errors.infants}</small>}</label>
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
          {selectedJourney && <FlightDetailPanel journey={selectedJourney} validating={selectionState === 'loading'} error={selectionError} onClose={() => { setSelectedJourney(null); setSelectionError('') }} onContinue={() => void revalidateSelection()} />}
          {filteredResults.length === 0 && <FeedbackState tone="empty" title="Filtrelerle eşleşen uçuş yok" message="Bir veya daha fazla filtreyi gevşeterek yeniden deneyin." />}
          <div className="flight-list">{filteredResults.map(result => <article className="journey-option-card" key={result.id}><header className="journey-option-heading"><div><span>{result.tripType === 'round-trip' ? 'Gidiş dönüş' : 'Tek yön'}</span><strong>{result.outbound.segments[0].from} → {result.outbound.segments.at(-1)?.to}</strong></div><div><small>{result.travelerCount} yolcu toplamı</small><strong>{result.totalPrice.toLocaleString('tr-TR')} {result.currency}</strong><span>{result.pricePerTraveler.toLocaleString('tr-TR')} {result.currency} / kişi · {result.seatsAvailable} koltuk</span></div></header><FlightItineraryView label="Gidiş" itinerary={result.outbound} />{result.inbound && <FlightItineraryView label="Dönüş" itinerary={result.inbound} />}<footer className="journey-card-action"><span>{journeyAirlines(result).join(' · ')} · {formatMinutes(journeyDuration(result))}</span><button className="secondary-action" type="button" onClick={() => { setSelectedJourney(result); setSelectionError('') }}>Ayrıntıları gör</button></footer></article>)}</div>
        </>}
      </section>
    </main>
  )
}

function LoadingCards({ label }: { label: string }) {
  return <div className="loading-results" role="status" aria-live="polite" aria-label={label}><div className="loading-label"><span className="spinner" aria-hidden="true" />{label}</div>{[1, 2, 3].map(item => <div className="skeleton-card" key={item}><span /><div><i /><i /><i /></div><b /></div>)}</div>
}

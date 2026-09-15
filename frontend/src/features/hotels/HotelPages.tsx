import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { FeedbackState } from '../../shared/components/FeedbackState'
import { Icon } from '../../shared/components/Icon'

type SearchForm = {
  location: string
  checkIn: string
  checkOut: string
  rooms: string
  adults: string
  childCount: string
  childAges: string[]
  stars: string
  rating: string
  minPrice: string
  maxPrice: string
  board: string
  features: string[]
}

type LocationOption = { key: string; label: string; city: string; district?: string; type: 'city' | 'hotel' }
type HotelNightPrice = { date: string; price: number; available: number }
type HotelRoomSelection = { roomId: string; name: string; quantity: number; capacity: number; features: string[]; nightlyTotal: number; lineTotal: number; nights: HotelNightPrice[] }
type HotelRoomOption = { key: string; totalPrice: number; totalCapacity: number; rooms: HotelRoomSelection[] }
type HotelResult = {
  id: string
  name: string
  city: string
  district: string
  stars: number
  rating: number
  description: string
  rooms: string[]
  features: string[]
  totalPrice: number
  options: HotelRoomOption[]
  boardTypes: string[]
}

const FEATURE_OPTIONS = ['Wi-Fi', 'Kahvaltı', 'Havuz', 'Otopark']
const BOARD_LABELS: Record<string, string> = { room: 'Sadece oda', breakfast: 'Kahvaltı dahil', half: 'Yarım pansiyon', all: 'Her şey dahil' }
const MAX_NIGHTS = 30

function localToday() {
  const now = new Date()
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`
}

function addDays(value: string, days: number) {
  const date = new Date(`${value}T12:00:00`)
  date.setDate(date.getDate() + days)
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

function daysBetween(start: string, end: string) {
  return Math.round((new Date(`${end}T12:00:00`).getTime() - new Date(`${start}T12:00:00`).getTime()) / 86_400_000)
}

function formatDate(value: string) {
  if (!value) return '—'
  return new Intl.DateTimeFormat('tr-TR', { day: 'numeric', month: 'short', year: 'numeric' }).format(new Date(`${value}T12:00:00`))
}

function formatStayDate(value: string) {
  return new Intl.DateTimeFormat('tr-TR', { day: 'numeric', month: 'short' }).format(new Date(`${value}T12:00:00`))
}

function normalize(value: string) {
  return value.trim().toLocaleLowerCase('tr-TR')
}

function formFromParams(params: URLSearchParams): SearchForm {
  const childCount = params.get('children') ?? '0'
  const ages = (params.get('childAges') ?? '').split(',').filter(Boolean)
  return {
    location: params.get('q') ?? '', checkIn: params.get('checkIn') ?? '', checkOut: params.get('checkOut') ?? '',
    rooms: params.get('rooms') ?? '1', adults: params.get('adults') ?? '2', childCount,
    childAges: Array.from({ length: Number(childCount) || 0 }, (_, index) => ages[index] ?? ''),
    stars: params.get('stars') ?? '', rating: params.get('rating') ?? '', minPrice: params.get('minPrice') ?? '', maxPrice: params.get('maxPrice') ?? '',
    board: params.get('board') ?? '', features: (params.get('features') ?? '').split(',').filter(Boolean),
  }
}

function toParams(form: SearchForm) {
  const params = new URLSearchParams({
    q: form.location.trim(), checkIn: form.checkIn, checkOut: form.checkOut,
    rooms: form.rooms, adults: form.adults, children: form.childCount,
  })
  if (form.childAges.length) params.set('childAges', form.childAges.join(','))
  if (form.stars) params.set('stars', form.stars)
  if (form.rating) params.set('rating', form.rating)
  if (form.minPrice) params.set('minPrice', form.minPrice)
  if (form.maxPrice) params.set('maxPrice', form.maxPrice)
  if (form.board) params.set('board', form.board)
  if (form.features.length) params.set('features', form.features.join(','))
  return params
}

function detailParams(form: SearchForm, option: HotelRoomOption) {
  const params = toParams(form)
  params.set('option', option.key)
  params.set('quotedTotal', String(option.totalPrice))
  return params
}

function validate(form: SearchForm) {
  const errors: Record<string, string> = {}
  const rooms = Number(form.rooms)
  const adults = Number(form.adults)
  const children = Number(form.childCount)
  if (!form.location.trim()) errors.location = 'Şehir veya otel adı yazın.'
  if (!form.checkIn) errors.checkIn = 'Giriş tarihini seçin.'
  else if (form.checkIn < localToday()) errors.checkIn = 'Giriş tarihi geçmişte olamaz.'
  if (!form.checkOut) errors.checkOut = 'Çıkış tarihini seçin.'
  else if (form.checkIn && form.checkOut <= form.checkIn) errors.checkOut = 'Çıkış tarihi giriş tarihinden sonra olmalı.'
  else if (form.checkIn && daysBetween(form.checkIn, form.checkOut) > MAX_NIGHTS) errors.checkOut = `Konaklama en fazla ${MAX_NIGHTS} gece olabilir.`
  if (!Number.isInteger(rooms) || rooms < 1 || rooms > 8) errors.rooms = 'Oda sayısı 1–8 arasında olmalı.'
  if (!Number.isInteger(adults) || adults < 1 || adults > 20) errors.adults = 'Yetişkin sayısı 1–20 arasında olmalı.'
  else if (Number.isInteger(rooms) && adults < rooms) errors.adults = 'Her oda için en az bir yetişkin olmalı.'
  if (!Number.isInteger(children) || children < 0 || children > 8) errors.childCount = 'Çocuk sayısı 0–8 arasında olmalı.'
  if (form.childAges.length !== children || form.childAges.some(age => age === '')) errors.childAges = 'Her çocuk için yaş seçin.'
  else if (form.childAges.some(age => Number(age) < 0 || Number(age) > 17)) errors.childAges = 'Çocuk yaşları 0–17 arasında olmalı.'
  const min = form.minPrice === '' ? null : Number(form.minPrice)
  const max = form.maxPrice === '' ? null : Number(form.maxPrice)
  if (min !== null && (Number.isNaN(min) || min < 0)) errors.minPrice = 'Geçerli bir en düşük fiyat yazın.'
  if (max !== null && (Number.isNaN(max) || max <= 0)) errors.maxPrice = 'Geçerli bir en yüksek fiyat yazın.'
  if (min !== null && max !== null && min > max) errors.maxPrice = 'En yüksek fiyat, en düşük fiyattan az olamaz.'
  if (form.rating && (Number(form.rating) < 0 || Number(form.rating) > 5)) errors.rating = 'Puan 0–5 arasında olmalı.'
  return errors
}

function ErrorText({ message }: { message?: string }) {
  return message ? <small className="field-error">{message}</small> : null
}

function ResultsFilters({ form, onUpdate, onToggleFeature, onClear }: { form: SearchForm; onUpdate: (key: string, value: string) => void; onToggleFeature: (feature: string) => void; onClear: () => void }) {
  return <div className="results-filter-panel">
    <div className="filter-panel-heading"><div><Icon name="filter" size={17} /><h2>Filtreler</h2></div><button type="button" onClick={onClear}>Temizle</button></div>
    <label className="form-field"><span>En az yıldız</span><select value={form.stars} onChange={event => onUpdate('stars', event.target.value)}><option value="">Tümü</option><option value="3">3 yıldız ve üzeri</option><option value="4">4 yıldız ve üzeri</option><option value="5">5 yıldız</option></select></label>
    <label className="form-field"><span>En az misafir puanı</span><select value={form.rating} onChange={event => onUpdate('rating', event.target.value)}><option value="">Tümü</option><option value="4">4,0 ve üzeri</option><option value="4.5">4,5 ve üzeri</option><option value="4.8">4,8 ve üzeri</option></select></label>
    <fieldset className="filter-group"><legend>Toplam örnek fiyat</legend><div className="price-filter-row"><label><span>En düşük</span><input type="number" min="0" step="100" value={form.minPrice} onChange={event => onUpdate('minPrice', event.target.value)} placeholder="0 TL" /></label><label><span>En yüksek</span><input type="number" min="1" step="100" value={form.maxPrice} onChange={event => onUpdate('maxPrice', event.target.value)} placeholder="Sınır yok" /></label></div></fieldset>
    <label className="form-field"><span>Pansiyon</span><select value={form.board} onChange={event => onUpdate('board', event.target.value)}><option value="">Tümü</option><option value="room">Sadece oda</option><option value="breakfast">Kahvaltı dahil</option><option value="half">Yarım pansiyon</option><option value="all">Her şey dahil</option></select></label>
    <fieldset className="filter-group"><legend>Özellikler</legend><div className="filter-checks">{FEATURE_OPTIONS.map(feature => <label key={feature}><input type="checkbox" checked={form.features.includes(feature)} onChange={() => onToggleFeature(feature)} /><span>{feature}</span></label>)}</div></fieldset>
  </div>
}

export function HotelSearchPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const [form, setForm] = useState<SearchForm>(() => formFromParams(searchParams))
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [checking, setChecking] = useState(false)
  const [suggestions, setSuggestions] = useState<LocationOption[]>([])
  const [showFilters, setShowFilters] = useState(Boolean(form.stars || form.rating || form.minPrice || form.maxPrice || form.board || form.features.length))
  const activeFilterCount = [form.stars, form.rating, form.minPrice, form.maxPrice, form.board, ...form.features].filter(Boolean).length

  useEffect(() => {
    const term = form.location.trim()
    if (term.length < 2) return
    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      fetch(`/api/hotels/locations?q=${encodeURIComponent(term)}`, { signal: controller.signal })
        .then(async response => response.ok ? setSuggestions(await response.json() as LocationOption[]) : setSuggestions([]))
        .catch(() => undefined)
    }, 180)
    return () => { window.clearTimeout(timer); controller.abort() }
  }, [form.location])

  const update = (key: keyof SearchForm, value: string | string[]) => {
    setForm(current => ({ ...current, [key]: value }))
    setErrors(current => { const next = { ...current }; delete next[key]; return next })
  }

  const updateChildCount = (value: string) => {
    const count = Number(value)
    setForm(current => ({ ...current, childCount: value, childAges: Array.from({ length: count }, (_, index) => current.childAges[index] ?? '') }))
    setErrors(current => { const next = { ...current }; delete next.childCount; delete next.childAges; return next })
  }

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const nextErrors = validate(form)
    setErrors(nextErrors)
    if (Object.keys(nextErrors).length) return
    setChecking(true)
    try {
      const response = await fetch(`/api/hotels/locations?q=${encodeURIComponent(form.location.trim())}`)
      if (!response.ok) throw new Error('catalog')
      const options = await response.json() as LocationOption[]
      const term = normalize(form.location)
      const supported = options.some(option => [option.label, option.city, option.district ?? ''].some(value => normalize(value) === term))
      if (!supported) {
        setErrors({ location: 'Bu şehir veya otel katalogda bulunmuyor. Önerilerden birini seçin.' })
        return
      }
      navigate(`/hotels/results?${toParams(form).toString()}`)
    } catch {
      setErrors({ form: 'Konum kataloğuna ulaşılamadı. API bağlantısını kontrol edip tekrar deneyin.' })
    } finally { setChecking(false) }
  }

  return (
    <main className="page-frame hotel-search-page" data-testid="hotel-search-page">
      <header className="compact-page-heading"><div><span className="section-label">OTELLER</span><h1>Konaklama ara</h1></div><p>Şehir, tarih ve misafir bilgilerini gir; tüm gecelerde uygun olan yerel katalog seçeneklerini karşılaştır.</p></header>
      <form className="search-card hotel-search-form" onSubmit={submit} noValidate aria-busy={checking}>
        <div className="search-card-heading"><div><span className="section-label">KONAKLAMA BİLGİLERİ</span><h2>Nerede kalmak istersin?</h2></div><span><i>*</i> Zorunlu alan</span></div>
        {Object.keys(errors).length > 0 && <FeedbackState tone="error" title="Arama bilgilerini kontrol edin" message={errors.form ?? 'İşaretli alanları düzelttikten sonra yeniden arayın.'} />}
        <div className="hotel-form-grid">
          <label className="form-field location-field"><span>Şehir veya otel<i>*</i></span><input value={form.location} onChange={event => { update('location', event.target.value); if (event.target.value.trim().length < 2) setSuggestions([]) }} placeholder="Örn. İstanbul veya Galata Meydan Otel" autoComplete="off" required aria-invalid={Boolean(errors.location)} />
            {suggestions.length > 0 && <span className="suggestion-popover location-suggestions">{suggestions.map(option => <button type="button" key={option.key} onClick={() => { update('location', option.label); setSuggestions([]) }}><b>{option.type === 'city' ? 'Şehir' : 'Otel'}</b><span>{option.label}<small>{option.type === 'hotel' ? `${option.city}${option.district ? ` · ${option.district}` : ''}` : 'Tüm katalog seçenekleri'}</small></span></button>)}</span>}
            <ErrorText message={errors.location} />
          </label>
          <label className="form-field"><span>Giriş tarihi<i>*</i></span><input type="date" min={localToday()} value={form.checkIn} onChange={event => update('checkIn', event.target.value)} required aria-invalid={Boolean(errors.checkIn)} /><ErrorText message={errors.checkIn} /></label>
          <label className="form-field"><span>Çıkış tarihi<i>*</i></span><input type="date" min={form.checkIn ? addDays(form.checkIn, 1) : localToday()} max={form.checkIn ? addDays(form.checkIn, MAX_NIGHTS) : undefined} value={form.checkOut} onChange={event => update('checkOut', event.target.value)} required aria-invalid={Boolean(errors.checkOut)} /><ErrorText message={errors.checkOut} /></label>
        </div>
        <div className="occupancy-grid" aria-label="Misafir bilgileri">
          <div className="occupancy-title"><span className="section-label">MİSAFİRLER</span><p>Her oda için en az bir yetişkin seçin.</p></div>
          <label className="form-field"><span>Oda<i>*</i></span><select value={form.rooms} onChange={event => update('rooms', event.target.value)} aria-invalid={Boolean(errors.rooms)}>{Array.from({ length: 8 }, (_, index) => <option key={index + 1} value={index + 1}>{index + 1} oda</option>)}</select><ErrorText message={errors.rooms} /></label>
          <label className="form-field"><span>Yetişkin<i>*</i></span><select value={form.adults} onChange={event => update('adults', event.target.value)} aria-invalid={Boolean(errors.adults)}>{Array.from({ length: 20 }, (_, index) => <option key={index + 1} value={index + 1}>{index + 1} yetişkin</option>)}</select><ErrorText message={errors.adults} /></label>
          <label className="form-field"><span>Çocuk<i>*</i></span><select value={form.childCount} onChange={event => updateChildCount(event.target.value)} aria-invalid={Boolean(errors.childCount)}>{Array.from({ length: 9 }, (_, index) => <option key={index} value={index}>{index} çocuk</option>)}</select><ErrorText message={errors.childCount} /></label>
        </div>
        {form.childAges.length > 0 && <fieldset className="child-ages"><legend>Çocuk yaşları <i>*</i></legend><p>Konaklama başlangıcındaki yaşları seçin.</p><div>{form.childAges.map((age, index) => <label className="form-field" key={index}><span>{index + 1}. çocuk</span><select value={age} onChange={event => { const ages = [...form.childAges]; ages[index] = event.target.value; update('childAges', ages) }} aria-label={`${index + 1}. çocuğun yaşı`}><option value="">Yaş seçin</option>{Array.from({ length: 18 }, (_, ageValue) => <option key={ageValue} value={ageValue}>{ageValue} yaş</option>)}</select></label>)}</div><ErrorText message={errors.childAges} /></fieldset>}

        <button className="filter-toggle" type="button" aria-expanded={showFilters} onClick={() => setShowFilters(value => !value)}><span><b>İsteğe bağlı filtreler</b><small>Yıldız, toplam fiyat, pansiyon ve özellik</small></span>{activeFilterCount > 0 && <em>{activeFilterCount} seçili</em>}<i aria-hidden="true">{showFilters ? '−' : '+'}</i></button>
        {showFilters && <div className="optional-filters">
          <label className="form-field"><span>En az yıldız</span><select value={form.stars} onChange={event => update('stars', event.target.value)}><option value="">Fark etmez</option><option value="3">3 yıldız ve üzeri</option><option value="4">4 yıldız ve üzeri</option><option value="5">5 yıldız</option></select></label>
          <label className="form-field"><span>En az puan</span><select value={form.rating} onChange={event => update('rating', event.target.value)}><option value="">Fark etmez</option><option value="4">4,0 ve üzeri</option><option value="4.5">4,5 ve üzeri</option><option value="4.8">4,8 ve üzeri</option></select></label>
          <label className="form-field"><span>En düşük toplam</span><input type="number" min="0" step="100" value={form.minPrice} onChange={event => update('minPrice', event.target.value)} placeholder="TL" aria-invalid={Boolean(errors.minPrice)} /><ErrorText message={errors.minPrice} /></label>
          <label className="form-field"><span>En yüksek toplam</span><input type="number" min="1" step="100" value={form.maxPrice} onChange={event => update('maxPrice', event.target.value)} placeholder="TL" aria-invalid={Boolean(errors.maxPrice)} /><ErrorText message={errors.maxPrice} /></label>
          <label className="form-field"><span>Pansiyon</span><select value={form.board} onChange={event => update('board', event.target.value)}><option value="">Fark etmez</option><option value="room">Sadece oda</option><option value="breakfast">Kahvaltı dahil</option><option value="half">Yarım pansiyon</option><option value="all">Her şey dahil</option></select></label>
          <fieldset className="feature-filter"><legend>Özellikler</legend>{FEATURE_OPTIONS.map(feature => <label key={feature}><input type="checkbox" checked={form.features.includes(feature)} onChange={() => update('features', form.features.includes(feature) ? form.features.filter(item => item !== feature) : [...form.features, feature])} /><span>{feature}</span></label>)}</fieldset>
        </div>}
        <div className="search-action-row"><p>Bugün giriş yapabilirsiniz. En fazla {MAX_NIGHTS} gecelik arama desteklenir.</p><button type="submit" className="primary-action" disabled={checking}>{checking ? 'Konum kontrol ediliyor…' : 'Otelleri ara'} <span aria-hidden="true">→</span></button></div>
      </form>
    </main>
  )
}

function HotelLoadingCards() {
  return <div className="loading-results" aria-label="Uygun oteller aranıyor"><div className="loading-label"><span className="spinner" aria-hidden="true" />Uygun oteller aranıyor</div>{[1, 2, 3].map(item => <div className="skeleton-card hotel-skeleton" key={item}><span /><div><i /><i /><i /></div><b /></div>)}</div>
}

export function HotelResultsPage() {
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const form = useMemo(() => formFromParams(params), [params])
  const errors = useMemo(() => validate(form), [form])
  const invalidSearch = Object.keys(errors).length > 0
  const [results, setResults] = useState<HotelResult[]>([])
  const [state, setState] = useState<'loading' | 'ready' | 'error'>('loading')
  const [sort, setSort] = useState<'rating' | 'price-asc' | 'price-desc'>('rating')
  const nights = form.checkIn && form.checkOut ? daysBetween(form.checkIn, form.checkOut) : 0

  useEffect(() => {
    if (invalidSearch) return
    const controller = new AbortController()
    const apiParams = new URLSearchParams({ q: form.location, checkIn: form.checkIn, checkOut: form.checkOut, adults: form.adults, rooms: form.rooms, children: form.childCount, childAges: form.childAges.join(',') })
    fetch(`/api/hotels?${apiParams}`, { signal: controller.signal })
      .then(async response => { if (!response.ok) throw new Error(); return await response.json() as HotelResult[] })
      .then(data => {
        const min = form.minPrice ? Number(form.minPrice) : 0
        const max = form.maxPrice ? Number(form.maxPrice) : Number.POSITIVE_INFINITY
        const wantedStars = form.stars ? Number(form.stars) : 0
        const wantedRating = form.rating ? Number(form.rating) : 0
        const filtered = data.filter(hotel => hotel.stars >= wantedStars && hotel.rating >= wantedRating && (!form.board || hotel.boardTypes.includes(form.board))).map(hotel => {
          const options = hotel.options.filter(option => option.totalPrice >= min && option.totalPrice <= max)
            .filter(option => form.features.every(feature => option.rooms.some(room => room.features.includes(feature))))
            .slice(0, 8)
          return { ...hotel, options, totalPrice: options[0]?.totalPrice ?? hotel.totalPrice }
        }).filter(hotel => hotel.options.length > 0)
        setResults(filtered)
        setState('ready')
      }).catch(error => { if (error.name !== 'AbortError') setState('error') })
    return () => controller.abort()
  }, [form, invalidSearch])

  const searchQuery = toParams(form).toString()
  const visibleState = invalidSearch ? 'error' : state
  const displayResults = useMemo(() => [...results].sort((a, b) => sort === 'price-asc' ? a.totalPrice - b.totalPrice : sort === 'price-desc' ? b.totalPrice - a.totalPrice : b.rating - a.rating), [results, sort])
  const activeCriteria = [
    ...(form.stars ? [{ id: 'stars', label: `${form.stars}+ yıldız` }] : []),
    ...(form.rating ? [{ id: 'rating', label: `${form.rating.replace('.', ',')}+ puan` }] : []),
    ...(form.minPrice ? [{ id: 'minPrice', label: `${form.minPrice} TL’den yüksek` }] : []),
    ...(form.maxPrice ? [{ id: 'maxPrice', label: `${form.maxPrice} TL’ye kadar` }] : []),
    ...(form.board ? [{ id: 'board', label: BOARD_LABELS[form.board] }] : []),
    ...form.features.map(feature => ({ id: `feature:${feature}`, label: feature })),
  ]

  const updateFilter = (key: string, value: string) => {
    const next = new URLSearchParams(params)
    if (value) next.set(key, value); else next.delete(key)
    setParams(next, { replace: true })
  }

  const toggleFeature = (feature: string) => {
    const nextFeatures = form.features.includes(feature) ? form.features.filter(item => item !== feature) : [...form.features, feature]
    updateFilter('features', nextFeatures.join(','))
  }

  const clearFilters = () => {
    const next = new URLSearchParams(params)
    ;['stars', 'rating', 'minPrice', 'maxPrice', 'board', 'features'].forEach(key => next.delete(key))
    setParams(next, { replace: true })
  }

  const removeCriterion = (id: string) => id.startsWith('feature:') ? toggleFeature(id.slice(8)) : updateFilter(id, '')

  return <main className="page-frame hotel-results-page" data-testid="hotel-results-page">
    <section className="results-search-bar">
      <div className="results-query"><Icon name="map-pin" size={18} /><span><small>Konum</small><strong>{form.location || 'Arama sonuçları'}</strong></span></div>
      <div><Icon name="calendar" size={18} /><span><small>Tarih</small><strong>{formatDate(form.checkIn)} – {formatDate(form.checkOut)}</strong></span></div>
      <div><Icon name="users" size={18} /><span><small>Misafir</small><strong>{form.adults} yetişkin{Number(form.childCount) ? `, ${form.childCount} çocuk` : ''} · {form.rooms} oda</strong></span></div>
      <Link className="primary-action" to={`/hotels?${searchQuery}`}>Düzenle</Link>
    </section>
    <details className="mobile-filter-drawer"><summary><Icon name="filter" size={17} />Filtreler{activeCriteria.length > 0 && <span>{activeCriteria.length}</span>}</summary><ResultsFilters form={form} onUpdate={updateFilter} onToggleFeature={toggleFeature} onClear={clearFilters} /></details>
    <div className="hotel-results-layout">
      <aside className="results-filter-sidebar"><ResultsFilters form={form} onUpdate={updateFilter} onToggleFeature={toggleFeature} onClear={clearFilters} /></aside>
      <section className="results-main" aria-live="polite">
        <div className="result-toolbar"><div><strong>{visibleState === 'ready' ? `${results.length} otel bulundu` : 'Otel sonuçları'}</strong><span>Fiyatlar {nights} gecelik örnek toplamdır.</span></div><label>Sırala<select value={sort} onChange={event => setSort(event.target.value as typeof sort)}><option value="rating">Puana göre</option><option value="price-asc">Fiyat: düşükten yükseğe</option><option value="price-desc">Fiyat: yüksekten düşüğe</option></select></label></div>
        {activeCriteria.length > 0 && <div className="active-filter-strip" aria-label="Etkin filtreler">{activeCriteria.map(item => <button type="button" key={item.id} onClick={() => removeCriterion(item.id)}>{item.label}<span aria-hidden="true">×</span></button>)}</div>}
        {visibleState === 'loading' && <HotelLoadingCards />}
        {visibleState === 'error' && <FeedbackState tone="error" title="Sonuçlar gösterilemedi" message={invalidSearch ? 'Arama bilgileri geçersiz veya eksik. Formu açıp bilgileri düzeltin.' : 'Otel kataloğuna ulaşılamadı. API bağlantısını kontrol edip tekrar deneyin.'} actionLabel="Aramayı düzenle" onAction={() => navigate(`/hotels?${searchQuery}`)} />}
        {visibleState === 'ready' && results.length === 0 && <FeedbackState tone="empty" title="Bu ölçütlerle otel bulunamadı" message="Tarihleri, misafir sayısını veya filtreleri değiştirerek yeniden arayın." actionLabel={activeCriteria.length ? 'Filtreleri temizle' : 'Aramayı düzenle'} onAction={activeCriteria.length ? clearFilters : () => navigate(`/hotels?${searchQuery}`)} />}
        {visibleState === 'ready' && results.length > 0 && <div className="hotel-result-list">{displayResults.map(hotel => <article key={hotel.id} className="hotel-result-card availability-result-card"><div className="hotel-result-marker"><span><Icon name="hotel" size={24} /></span><small>{hotel.city}</small></div><div className="hotel-result-copy"><div className="hotel-title-row"><div><span className="stars" aria-label={`${hotel.stars} yıldız`}>{'★'.repeat(hotel.stars)}</span><h2>{hotel.name}</h2><p><Icon name="map-pin" size={14} />{hotel.district}, {hotel.city}</p></div><span className="rating-badge"><strong>{hotel.rating.toLocaleString('tr-TR')}</strong><small>5 üzerinden</small></span></div><p className="hotel-description">{hotel.description}</p><div className="hotel-tags board-tags">{hotel.boardTypes.map(board => <span key={board}>{BOARD_LABELS[board] ?? board}</span>)}</div><div className="availability-badge"><span aria-hidden="true">✓</span> Girişten çıkışa kadar her gece müsait</div><div className="room-options">{hotel.options.map((option, optionIndex) => <section className="room-option" key={option.key}><header><div><b>Oda seçeneği {optionIndex + 1}</b><span>{form.rooms} oda · toplam {option.totalCapacity} kişi kapasitesi</span></div><strong>{option.totalPrice.toLocaleString('tr-TR')} TL<small>konaklama toplamı</small></strong></header><div className="room-option-lines">{option.rooms.map(room => <details key={room.roomId}><summary><span><b>{room.quantity} × {room.name}</b><small>Oda başına {room.capacity} kişi · {room.features.join(', ') || 'Standart özellikler'}</small></span><strong>{room.lineTotal.toLocaleString('tr-TR')} TL</strong></summary><div className="nightly-breakdown"><p>Her gece ayrı ayrı doğrulandı</p>{room.nights.map(night => <span key={night.date}><time dateTime={night.date}>{formatStayDate(night.date)}</time><b>{night.price.toLocaleString('tr-TR')} TL / oda</b><small>{night.available} oda müsait</small></span>)}</div></details>)}</div></section>)}</div></div><div className="hotel-price"><small>En düşük toplam</small><strong>{hotel.totalPrice.toLocaleString('tr-TR')} TL</strong><span>{nights} gece · {form.rooms} oda</span><Link className="detail-link" to={`/hotels/${hotel.id}?${detailParams(form, hotel.options[0])}`}>Ayrıntıları gör <span aria-hidden="true">→</span></Link><p>Gerçek rezervasyon ve ödeme içermez.</p></div></article>)}</div>}
      </section>
    </div>
  </main>
}

export function HotelDetailPage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const form = useMemo(() => formFromParams(params), [params])
  const errors = useMemo(() => validate(form), [form])
  const [hotel, setHotel] = useState<HotelResult | null>(null)
  const [state, setState] = useState<'loading' | 'ready' | 'gone' | 'error'>('loading')
  const searchQuery = toParams(form).toString()
  const selectedKey = params.get('option')
  const quotedTotal = Number(params.get('quotedTotal'))
  const visibleState = Object.keys(errors).length > 0 ? 'error' : state

  useEffect(() => {
    if (Object.keys(errors).length > 0) return
    const controller = new AbortController()
    const apiParams = new URLSearchParams({ checkIn: form.checkIn, checkOut: form.checkOut, adults: form.adults, rooms: form.rooms, children: form.childCount, childAges: form.childAges.join(',') })
    fetch(`/api/hotels/${encodeURIComponent(id)}/availability?${apiParams}`, { signal: controller.signal })
      .then(async response => {
        if (response.status === 404) { setState('gone'); return null }
        if (!response.ok) throw new Error('availability')
        return await response.json() as HotelResult
      })
      .then(data => { if (data) { setHotel(data); setState('ready') } })
      .catch(error => { if (error.name !== 'AbortError') setState('error') })
    return () => controller.abort()
  }, [errors, form, id])

  const selectedOption = hotel?.options.find(option => option.key === selectedKey) ?? hotel?.options[0]
  const selectionChanged = Boolean(hotel && selectedKey && !hotel.options.some(option => option.key === selectedKey))
  const priceChanged = Boolean(selectedOption && Number.isFinite(quotedTotal) && quotedTotal > 0 && selectedOption.totalPrice !== quotedTotal)
  const selectOption = (option: HotelRoomOption) => {
    const next = new URLSearchParams(params)
    next.set('option', option.key)
    next.set('quotedTotal', String(option.totalPrice))
    setParams(next, { replace: true })
  }

  return <main className="page-frame hotel-detail-page" data-testid="hotel-detail-page">
    <Link className="detail-back-link" to={`/hotels/results?${searchQuery}`}>← Sonuçlara dön</Link>
    {visibleState === 'loading' && <HotelLoadingCards />}
    {visibleState === 'gone' && <FeedbackState tone="empty" title="Bu otel artık kullanılamıyor" message="Otel kaldırılmış veya seçtiğiniz tarihlerdeki son uygun oda artık müsait değil. Güncel sonuçlara dönebilirsiniz." actionLabel="Güncel sonuçları göster" onAction={() => navigate(`/hotels/results?${searchQuery}`)} />}
    {visibleState === 'error' && <FeedbackState tone="error" title="Otel ayrıntısı gösterilemedi" message={Object.keys(errors).length ? 'Arama bilgileri eksik veya geçersiz. Aramayı yeniden düzenleyin.' : 'Güncel müsaitlik bilgisi alınamadı. Lütfen tekrar deneyin.'} actionLabel="Aramayı düzenle" onAction={() => navigate(`/hotels?${searchQuery}`)} />}
    {visibleState === 'ready' && hotel && selectedOption && <>
      <section className="detail-hero">
        <div><span className="stars" aria-label={`${hotel.stars} yıldız`}>{'★'.repeat(hotel.stars)}</span><h1>{hotel.name}</h1><p><Icon name="map-pin" size={15} />{hotel.district}, {hotel.city}</p></div>
        <span className="rating-badge"><strong>{hotel.rating.toLocaleString('tr-TR')}</strong><small>5 üzerinden</small></span>
      </section>
      <section className="detail-stay-summary"><div><small>Giriş</small><strong>{formatDate(form.checkIn)}</strong></div><div><small>Çıkış</small><strong>{formatDate(form.checkOut)}</strong></div><div><small>Konaklama</small><strong>{daysBetween(form.checkIn, form.checkOut)} gece · {form.rooms} oda</strong></div><div><small>Misafir</small><strong>{form.adults} yetişkin{Number(form.childCount) ? ` · ${form.childCount} çocuk` : ''}</strong></div></section>
      {(selectionChanged || priceChanged) && <FeedbackState tone="warning" title="Müsaitlik bilgisi güncellendi" message={selectionChanged ? 'Seçtiğiniz oda dağılımı artık yok; en uygun güncel seçenek gösteriliyor.' : `Arama sonucundaki ${quotedTotal.toLocaleString('tr-TR')} TL fiyat yerine güncel toplam ${selectedOption.totalPrice.toLocaleString('tr-TR')} TL.`} />}
      <div className="detail-layout">
        <section className="detail-main-card"><h2>Oda seçenekleri</h2><p>{hotel.description}</p><div className="hotel-tags board-tags">{hotel.boardTypes.map(board => <span key={board}>{BOARD_LABELS[board] ?? board}</span>)}{hotel.features.map(feature => <span key={feature}>{feature}</span>)}</div>
          <div className="detail-option-list">{hotel.options.map((option, index) => <button type="button" className={option.key === selectedOption.key ? 'detail-option selected' : 'detail-option'} key={option.key} onClick={() => selectOption(option)}><span><b>Oda seçeneği {index + 1}</b><small>{option.rooms.map(room => `${room.quantity} × ${room.name}`).join(' + ')} · {option.totalCapacity} kişi kapasitesi</small></span><strong>{option.totalPrice.toLocaleString('tr-TR')} TL</strong></button>)}</div>
          <div className="selected-room-details">{selectedOption.rooms.map(room => <section key={room.roomId}><header><div><h3>{room.quantity} × {room.name}</h3><p>Oda başına {room.capacity} kişi · {room.features.join(', ') || 'Standart özellikler'}</p></div><strong>{room.lineTotal.toLocaleString('tr-TR')} TL</strong></header><div className="nightly-breakdown"><p>Her gece için fiyat ve kalan oda</p>{room.nights.map(night => <span key={night.date}><time dateTime={night.date}>{formatStayDate(night.date)}</time><b>{night.price.toLocaleString('tr-TR')} TL / oda</b><small>{night.available} oda müsait</small></span>)}</div></section>)}</div>
        </section>
        <aside className="detail-total-card"><span>Güncel konaklama toplamı</span><strong>{selectedOption.totalPrice.toLocaleString('tr-TR')} TL</strong><p>{daysBetween(form.checkIn, form.checkOut)} gece · {form.rooms} oda</p><small>Fiyat, seçilen tarihlerdeki tüm geceler yeniden kontrol edilerek hesaplandı.</small><Link className="primary-action" to={`/booking/summary?${new URLSearchParams({ kind: 'hotel', hotelId: hotel.id, optionKey: selectedOption.key, checkIn: form.checkIn, checkOut: form.checkOut, rooms: form.rooms, adults: form.adults, children: form.childCount, childAges: form.childAges.join(','), quotedTotal: String(selectedOption.totalPrice) })}`}>Rezervasyon özetine geç</Link></aside>
      </div>
    </>}
  </main>
}

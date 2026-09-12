import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { FeedbackState } from './FeedbackState'

type SearchForm = {
  location: string
  checkIn: string
  checkOut: string
  rooms: string
  adults: string
  childCount: string
  childAges: string[]
  stars: string
  minPrice: string
  maxPrice: string
  board: string
  features: string[]
}

type LocationOption = { key: string; label: string; city: string; district?: string; type: 'city' | 'hotel' }
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
}

const FEATURE_OPTIONS = ['Wi-Fi', 'Kahvaltı', 'Havuz', 'Otopark']
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
    stars: params.get('stars') ?? '', minPrice: params.get('minPrice') ?? '', maxPrice: params.get('maxPrice') ?? '',
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
  if (form.minPrice) params.set('minPrice', form.minPrice)
  if (form.maxPrice) params.set('maxPrice', form.maxPrice)
  if (form.board) params.set('board', form.board)
  if (form.features.length) params.set('features', form.features.join(','))
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
  if (Number.isInteger(rooms) && Number.isInteger(adults) && Number.isInteger(children) && adults + children > rooms * 4) errors.guests = 'Bir oda için en fazla 4 misafir seçilebilir. Oda sayısını artırın.'
  const min = form.minPrice === '' ? null : Number(form.minPrice)
  const max = form.maxPrice === '' ? null : Number(form.maxPrice)
  if (min !== null && (Number.isNaN(min) || min < 0)) errors.minPrice = 'Geçerli bir en düşük fiyat yazın.'
  if (max !== null && (Number.isNaN(max) || max <= 0)) errors.maxPrice = 'Geçerli bir en yüksek fiyat yazın.'
  if (min !== null && max !== null && min > max) errors.maxPrice = 'En yüksek fiyat, en düşük fiyattan az olamaz.'
  return errors
}

function ErrorText({ message }: { message?: string }) {
  return message ? <small className="field-error">{message}</small> : null
}

export function HotelSearchPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const [form, setForm] = useState<SearchForm>(() => formFromParams(searchParams))
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [checking, setChecking] = useState(false)
  const [suggestions, setSuggestions] = useState<LocationOption[]>([])
  const [showFilters, setShowFilters] = useState(Boolean(form.stars || form.minPrice || form.maxPrice || form.board || form.features.length))

  useEffect(() => {
    const term = form.location.trim()
    if (term.length < 2) return
    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      fetch(`/api/hotels/locations?q=${encodeURIComponent(term)}`, { signal: controller.signal })
        .then(async response => response.ok ? setSuggestions(await response.json() as LocationOption[]) : setSuggestions([]))
        .catch(() => undefined)
    }, 220)
    return () => { window.clearTimeout(timer); controller.abort() }
  }, [form.location])

  const update = (key: keyof SearchForm, value: string | string[]) => {
    setForm(current => ({ ...current, [key]: value }))
    setErrors(current => { const next = { ...current }; delete next[key]; delete next.guests; return next })
  }

  const updateChildCount = (value: string) => {
    const count = Number(value)
    setForm(current => ({ ...current, childCount: value, childAges: Array.from({ length: count }, (_, index) => current.childAges[index] ?? '') }))
    setErrors(current => { const next = { ...current }; delete next.childCount; delete next.childAges; delete next.guests; return next })
  }

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const nextErrors = validate(form)
    if (Object.keys(nextErrors).length) { setErrors(nextErrors); return }
    setChecking(true)
    try {
      const response = await fetch(`/api/hotels/locations?q=${encodeURIComponent(form.location.trim())}`)
      if (!response.ok) throw new Error('catalog')
      const options = await response.json() as LocationOption[]
      const term = normalize(form.location)
      const supported = options.some(option => [option.label, option.city, option.district ?? ''].some(value => normalize(value) === term))
      if (!supported) {
        setErrors({ location: 'Bu şehir veya otel şu anda desteklenmiyor. Listeden bir seçenek belirleyin.' })
        return
      }
      navigate(`/hotels/results?${toParams(form).toString()}`)
    } catch {
      setErrors({ form: 'Konumlar doğrulanamadı. Bağlantıyı kontrol edip tekrar deneyin.' })
    } finally {
      setChecking(false)
    }
  }

  return (
    <section className="page-frame hotel-search-page">
      <div className="page-heading"><p className="eyebrow">KONAKLAMA ARAMA</p><h1>Doğru oteli, tüm ayrıntılarıyla arayın</h1><p className="lead">Konaklama bilgilerinizi girin; yalnızca ölçütlerinize uyan katalog seçeneklerini gösterelim.</p></div>
      <form className="search-panel hotel-search-form" onSubmit={submit} noValidate aria-busy={checking}>
        <div className="form-legend"><strong>Konaklama bilgileri</strong><span><i aria-hidden="true">*</i> Zorunlu alan</span></div>
        {(Object.keys(errors).length > 0) && <FeedbackState tone="error" title="Bilgileri kontrol edin" message={errors.form ?? 'İşaretli alanları düzelttikten sonra yeniden arayın.'} />}
        <div className="hotel-form-grid">
          <label className="location-field"><span>Şehir veya otel <i>*</i></span><input value={form.location} onChange={event => { update('location', event.target.value); if (event.target.value.trim().length < 2) setSuggestions([]) }} placeholder="Örn. İstanbul veya Galata Meydan Otel" autoComplete="off" required aria-invalid={Boolean(errors.location)} />
            {suggestions.length > 0 && <span className="location-suggestions">{suggestions.map(option => <button type="button" key={option.key} onClick={() => { update('location', option.label); setSuggestions([]) }}><b>{option.type === 'city' ? 'Şehir' : 'Otel'}</b><span>{option.label}</span>{option.type === 'hotel' && <small>{option.city}{option.district ? ` · ${option.district}` : ''}</small>}</button>)}</span>}
            <ErrorText message={errors.location} />
          </label>
          <label><span>Giriş tarihi <i>*</i></span><input type="date" min={localToday()} value={form.checkIn} onChange={event => update('checkIn', event.target.value)} required aria-invalid={Boolean(errors.checkIn)} /><ErrorText message={errors.checkIn} /></label>
          <label><span>Çıkış tarihi <i>*</i></span><input type="date" min={form.checkIn ? addDays(form.checkIn, 1) : localToday()} max={form.checkIn ? addDays(form.checkIn, MAX_NIGHTS) : undefined} value={form.checkOut} onChange={event => update('checkOut', event.target.value)} required aria-invalid={Boolean(errors.checkOut)} /><ErrorText message={errors.checkOut} /></label>
          <label><span>Oda <i>*</i></span><select value={form.rooms} onChange={event => update('rooms', event.target.value)} aria-invalid={Boolean(errors.rooms)}>{Array.from({ length: 8 }, (_, i) => <option key={i + 1} value={i + 1}>{i + 1} oda</option>)}</select><ErrorText message={errors.rooms} /></label>
          <label><span>Yetişkin <i>*</i></span><select value={form.adults} onChange={event => update('adults', event.target.value)} aria-invalid={Boolean(errors.adults)}>{Array.from({ length: 20 }, (_, i) => <option key={i + 1} value={i + 1}>{i + 1} yetişkin</option>)}</select><ErrorText message={errors.adults} /></label>
          <label><span>Çocuk <i>*</i></span><select value={form.childCount} onChange={event => updateChildCount(event.target.value)} aria-invalid={Boolean(errors.childCount)}>{Array.from({ length: 9 }, (_, i) => <option key={i} value={i}>{i} çocuk</option>)}</select><ErrorText message={errors.childCount} /></label>
        </div>
        {form.childAges.length > 0 && <fieldset className="child-ages"><legend>Çocuk yaşları <i>*</i></legend><p>Konaklama başlangıcındaki yaşları seçin.</p><div>{form.childAges.map((age, index) => <label key={index}><span>{index + 1}. çocuk</span><select value={age} onChange={event => { const ages = [...form.childAges]; ages[index] = event.target.value; update('childAges', ages) }} aria-label={`${index + 1}. çocuğun yaşı`}><option value="">Yaş seçin</option>{Array.from({ length: 18 }, (_, i) => <option key={i} value={i}>{i} yaş</option>)}</select></label>)}</div><ErrorText message={errors.childAges ?? errors.guests} /></fieldset>}
        {!form.childAges.length && <ErrorText message={errors.guests} />}
        <button className="filter-toggle" type="button" aria-expanded={showFilters} onClick={() => setShowFilters(value => !value)}><span>İsteğe bağlı filtreler</span><small>Yıldız, fiyat, pansiyon ve özellik</small><b>{showFilters ? '−' : '+'}</b></button>
        {showFilters && <div className="optional-filters">
          <label><span>En az yıldız</span><select value={form.stars} onChange={event => update('stars', event.target.value)}><option value="">Fark etmez</option><option value="3">3 yıldız ve üzeri</option><option value="4">4 yıldız ve üzeri</option><option value="5">5 yıldız</option></select></label>
          <label><span>En düşük toplam fiyat</span><input type="number" min="0" step="100" value={form.minPrice} onChange={event => update('minPrice', event.target.value)} placeholder="₺" aria-invalid={Boolean(errors.minPrice)} /><ErrorText message={errors.minPrice} /></label>
          <label><span>En yüksek toplam fiyat</span><input type="number" min="1" step="100" value={form.maxPrice} onChange={event => update('maxPrice', event.target.value)} placeholder="₺" aria-invalid={Boolean(errors.maxPrice)} /><ErrorText message={errors.maxPrice} /></label>
          <label><span>Pansiyon</span><select value={form.board} onChange={event => update('board', event.target.value)}><option value="">Fark etmez</option><option value="room">Sadece oda</option><option value="breakfast">Kahvaltı dahil</option><option value="half">Yarım pansiyon</option><option value="all">Her şey dahil</option></select></label>
          <fieldset className="feature-filter"><legend>Özellikler</legend>{FEATURE_OPTIONS.map(feature => <label key={feature}><input type="checkbox" checked={form.features.includes(feature)} onChange={() => update('features', form.features.includes(feature) ? form.features.filter(item => item !== feature) : [...form.features, feature])} /><span>{feature}</span></label>)}</fieldset>
        </div>}
        <div className="search-submit-row"><p>Bugün giriş yapabilirsiniz. En fazla {MAX_NIGHTS} gecelik arama desteklenir.</p><button type="submit" className="primary-action" disabled={checking}>{checking ? 'Kontrol ediliyor…' : 'Otelleri ara'} <span aria-hidden="true">→</span></button></div>
      </form>
    </section>
  )
}

export function HotelResultsPage() {
  const [params] = useSearchParams()
  const form = useMemo(() => formFromParams(params), [params])
  const errors = useMemo(() => validate(form), [form])
  const invalidSearch = Object.keys(errors).length > 0
  const [results, setResults] = useState<HotelResult[]>([])
  const [state, setState] = useState<'loading' | 'ready' | 'error'>('loading')
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
        const filtered = data.filter(hotel => hotel.stars >= wantedStars && hotel.totalPrice >= min && hotel.totalPrice <= max)
          .filter(hotel => form.features.every(feature => hotel.features.includes(feature)))
          .filter(hotel => !form.board || (form.board === 'breakfast' ? hotel.features.includes('Kahvaltı') : form.board === 'room' ? !hotel.features.includes('Kahvaltı') : false))
        setResults(filtered); setState('ready')
      }).catch(error => { if (error.name !== 'AbortError') setState('error') })
    return () => controller.abort()
  }, [form, invalidSearch])

  const searchQuery = toParams(form).toString()
  const visibleState = invalidSearch ? 'error' : state
  return <section className="page-frame hotel-results-page">
    <div className="results-heading"><div><p className="eyebrow">OTEL SONUÇLARI</p><h1>{form.location || 'Arama sonuçları'}</h1><p>{form.checkIn} → {form.checkOut} · {nights} gece · {form.rooms} oda · {form.adults} yetişkin{Number(form.childCount) ? ` · ${form.childCount} çocuk (${form.childAges.join(', ')} yaş)` : ''}</p></div><Link className="secondary-action modify-search" to={`/hotels?${searchQuery}`}>Aramayı değiştir</Link></div>
    <div className="criteria-strip"><span>{form.stars ? `${form.stars}+ yıldız` : 'Tüm yıldızlar'}</span><span>{form.minPrice || form.maxPrice ? `${form.minPrice || '0'}–${form.maxPrice || '∞'} TL` : 'Fiyat sınırı yok'}</span><span>{form.board ? ({ room: 'Sadece oda', breakfast: 'Kahvaltı dahil', half: 'Yarım pansiyon', all: 'Her şey dahil' } as Record<string, string>)[form.board] : 'Tüm pansiyonlar'}</span>{form.features.map(feature => <span key={feature}>{feature}</span>)}</div>
    {visibleState === 'loading' && <FeedbackState tone="loading" title="Uygun oteller aranıyor" message="Tarih, kapasite ve filtreleriniz katalogda kontrol ediliyor." />}
    {visibleState === 'error' && <FeedbackState tone="error" title="Sonuçlar gösterilemedi" message={invalidSearch ? 'Arama bilgileri geçersiz veya eksik. Formu açıp bilgileri düzeltin.' : 'Katalog bağlantısı kurulamadı. Biraz sonra tekrar deneyin.'} actionLabel="Aramayı düzenle" onAction={() => window.location.assign(`/hotels?${searchQuery}`)} />}
    {visibleState === 'ready' && results.length === 0 && <FeedbackState tone="empty" title="Bu ölçütlerle otel bulunamadı" message="Tarihleri, kişi sayısını veya isteğe bağlı filtreleri değiştirerek yeniden arayın." />}
    {visibleState === 'ready' && results.length > 0 && <><div className="result-count"><strong>{results.length} otel bulundu</strong><span>Toplam konaklama fiyatına göre gösteriliyor</span></div><div className="hotel-result-list">{results.map(hotel => <article key={hotel.id} className="hotel-result-card"><div className="hotel-result-visual"><span>{hotel.city.slice(0, 2).toLocaleUpperCase('tr-TR')}</span></div><div className="hotel-result-copy"><div className="hotel-result-title"><div><span className="stars" aria-label={`${hotel.stars} yıldız`}>{'★'.repeat(hotel.stars)}</span><h2>{hotel.name}</h2><p>{hotel.district}, {hotel.city} · {hotel.rating}/5 puan</p></div><strong>{hotel.totalPrice.toLocaleString('tr-TR')} TL<small>{nights} gece · toplam</small></strong></div><p>{hotel.description}</p><div className="hotel-tags">{hotel.rooms.map(room => <span key={room}>{room}</span>)}{hotel.features.map(feature => <span key={feature}>{feature}</span>)}</div></div></article>)}</div></>}
  </section>
}

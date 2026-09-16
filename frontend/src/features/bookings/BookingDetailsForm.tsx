import { useMemo, useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { FeedbackState } from '../../shared/components/FeedbackState'
import { useAuth } from '../auth/useAuth'

type TravelerType = 'adult' | 'child' | 'infant'
type Traveler = { type: TravelerType; firstName: string; lastName: string; age: number | null; accompanyingAdultIndex: number | null }
type Contact = { adultIndex: number; email: string; phone: string }
type FieldErrors = Record<string, string>
type ValidatedDetails = { requestKey: string; travelers: Traveler[]; contact: Contact & { name: string }; message: string }
type ConfirmedBooking = { id: string; referenceCode: string; title: string; totalPrice: number; currency: string; message: string }
type PriceChange = { currentTotal: number; currency: string; message: string }

const TYPE_LABELS: Record<TravelerType, string> = { adult: 'Yetişkin', child: 'Çocuk', infant: 'Bebek' }
const NAME_PATTERN = /^\p{L}[\p{L}\p{M} '-]{0,48}[\p{L}\p{M}]$/u
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

function initialTravelers(adults: number, children: number, infants: number, childAges: number[]) {
  return [
    ...Array.from({ length: adults }, (): Traveler => ({ type: 'adult', firstName: '', lastName: '', age: null, accompanyingAdultIndex: null })),
    ...Array.from({ length: children }, (_, index): Traveler => ({ type: 'child', firstName: '', lastName: '', age: childAges[index] ?? null, accompanyingAdultIndex: null })),
    ...Array.from({ length: infants }, (): Traveler => ({ type: 'infant', firstName: '', lastName: '', age: 0, accompanyingAdultIndex: 0 })),
  ]
}

function normalizedName(value: string) {
  return value.trim().replace(/\s+/g, ' ')
}

function loadDraft(key: string) {
  try { return JSON.parse(sessionStorage.getItem(key) ?? 'null') as ValidatedDetails | null } catch { return null }
}

function loadConfirmed(key: string) {
  try { return JSON.parse(sessionStorage.getItem(key) ?? 'null') as ConfirmedBooking | null } catch { return null }
}

function validate(travelers: Traveler[], contact: Contact, kind: 'hotel' | 'flight', adults: number) {
  const errors: FieldErrors = {}
  const identities = new Set<string>()
  travelers.forEach((traveler, index) => {
    const firstName = normalizedName(traveler.firstName)
    const lastName = normalizedName(traveler.lastName)
    if (!NAME_PATTERN.test(firstName)) errors[`travelers.${index}.firstName`] = 'Geçerli bir ad yazın.'
    if (!NAME_PATTERN.test(lastName)) errors[`travelers.${index}.lastName`] = 'Geçerli bir soyad yazın.'
    if (traveler.type === 'child' && kind === 'flight' && (traveler.age === null || traveler.age < 2 || traveler.age > 11)) errors[`travelers.${index}.age`] = 'Yaş 2–11 arasında olmalı.'
    if (traveler.type === 'infant' && (traveler.age === null || traveler.age < 0 || traveler.age > 1)) errors[`travelers.${index}.age`] = 'Yaş 0–1 arasında olmalı.'
    const identity = `${firstName.toLocaleLowerCase('tr-TR')}|${lastName.toLocaleLowerCase('tr-TR')}|${traveler.age ?? 'adult'}`
    if (firstName && lastName && identities.has(identity)) errors[`travelers.${index}.firstName`] = 'Aynı kişi daha önce eklendi.'
    identities.add(identity)
  })
  const linkedAdults = new Set<number>()
  travelers.forEach((traveler, index) => {
    if (traveler.type !== 'infant') return
    if (traveler.accompanyingAdultIndex === null || traveler.accompanyingAdultIndex < 0 || traveler.accompanyingAdultIndex >= adults) errors[`travelers.${index}.accompanyingAdultIndex`] = 'Bir yetişkin seçin.'
    else if (linkedAdults.has(traveler.accompanyingAdultIndex)) errors[`travelers.${index}.accompanyingAdultIndex`] = 'Bu yetişkin başka bir bebekle eşleştirildi.'
    else linkedAdults.add(traveler.accompanyingAdultIndex)
  })
  if (contact.adultIndex < 0 || contact.adultIndex >= adults) errors['contact.adultIndex'] = 'İletişim kişisi olarak bir yetişkin seçin.'
  if (!EMAIL_PATTERN.test(contact.email.trim()) || contact.email.trim().length > 254) errors['contact.email'] = 'Geçerli bir e-posta adresi yazın.'
  const phoneDigits = contact.phone.replace(/\D/g, '')
  if (!/^[+\d\s().-]+$/.test(contact.phone.trim()) || phoneDigits.length < 10 || phoneDigits.length > 15) errors['contact.phone'] = 'Ülke koduyla birlikte geçerli bir telefon yazın.'
  return errors
}

export function BookingDetailsForm({ kind, adults, childCount, infants, childAges, selection, draftKey, searchUrl, attemptKey }: { kind: 'hotel' | 'flight'; adults: number; childCount: number; infants: number; childAges: number[]; selection: unknown; draftKey: string; searchUrl: string; attemptKey?: string }) {
  const navigate = useNavigate()
  const location = useLocation()
  const { user } = useAuth()
  const [initialDraft] = useState(() => loadDraft(draftKey))
  const [requestKey] = useState(() => initialDraft?.requestKey ?? attemptKey ?? crypto.randomUUID())
  const [travelers, setTravelers] = useState(() => initialDraft?.travelers ?? initialTravelers(adults, childCount, infants, childAges))
  const [contact, setContact] = useState<Contact>(() => initialDraft?.contact ?? { adultIndex: 0, email: '', phone: '' })
  const [errors, setErrors] = useState<FieldErrors>({})
  const [state, setState] = useState<'editing' | 'saving' | 'done'>(() => initialDraft ? 'done' : 'editing')
  const [serverError, setServerError] = useState('')
  const [validated, setValidated] = useState<ValidatedDetails | null>(initialDraft)
  const [confirming, setConfirming] = useState(false)
  const [confirmationError, setConfirmationError] = useState('')
  const [priceChange, setPriceChange] = useState<PriceChange | null>(null)
  const [acceptedTotal, setAcceptedTotal] = useState(() => Number((selection as { quotedTotal?: number }).quotedTotal ?? 0))
  const [acceptedCurrency, setAcceptedCurrency] = useState(() => String((selection as { quotedCurrency?: string }).quotedCurrency ?? ''))
  const confirmationKey = `${draftKey}:confirmed`
  const [confirmedBooking, setConfirmedBooking] = useState<ConfirmedBooking | null>(() => loadConfirmed(confirmationKey))
  const adultOptions = useMemo(() => travelers.slice(0, adults), [travelers, adults])

  const updateTraveler = (index: number, patch: Partial<Traveler>, field: string) => {
    setTravelers(current => current.map((traveler, travelerIndex) => travelerIndex === index ? { ...traveler, ...patch } : traveler))
    setErrors(current => { const next = { ...current }; delete next[`travelers.${index}.${field}`]; return next })
    setServerError('')
  }

  const updateContact = (patch: Partial<Contact>, field: string) => {
    setContact(current => ({ ...current, ...patch }))
    setErrors(current => { const next = { ...current }; delete next[`contact.${field}`]; return next })
    setServerError('')
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    const nextErrors = validate(travelers, contact, kind, adults)
    setErrors(nextErrors)
    if (Object.keys(nextErrors).length) return
    setState('saving')
    setServerError('')
    try {
      const response = await fetch(`/api/bookings/details/${kind}/validate`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ selection, travelers, contact }),
      })
      const payload = await response.json().catch(() => ({}))
      if (!response.ok) {
        if (payload.field) setErrors({ [payload.field]: payload.message })
        throw new Error(payload.message ?? 'Kişi bilgileri doğrulanamadı.')
      }
      const saved = { ...(payload as Omit<ValidatedDetails, 'requestKey'>), requestKey }
      setTravelers(saved.travelers)
      setContact(saved.contact)
      setValidated(saved)
      sessionStorage.setItem(draftKey, JSON.stringify(saved))
      setState('done')
    } catch (error) {
      setServerError(error instanceof Error && error.message !== 'Failed to fetch' ? error.message : 'API bağlantısı kurulamadı. Bilgiler kaydedilmedi.')
      setState('editing')
    }
  }

  const confirmBooking = async () => {
    if (!validated) return
    if (!user) {
      navigate(`/login?returnTo=${encodeURIComponent(location.pathname + location.search)}`)
      return
    }
    setConfirming(true)
    setConfirmationError('')
    setPriceChange(null)
    try {
      const token = sessionStorage.getItem('travel-assistant-demo-session')
      const currentSelection = { ...(selection as Record<string, unknown>), quotedTotal: acceptedTotal, quotedCurrency: acceptedCurrency }
      const response = await fetch(`/api/bookings/confirm/${kind}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token ?? ''}` },
        body: JSON.stringify({ requestKey, details: { selection: currentSelection, travelers, contact } }),
      })
      const payload = await response.json().catch(() => ({}))
      if (response.status === 401) { navigate(`/login?returnTo=${encodeURIComponent(location.pathname + location.search)}`); return }
      if (response.status === 409 && payload.code === 'price_changed') { setPriceChange(payload as PriceChange); return }
      if (!response.ok) throw new Error(payload.message ?? 'Son fiyat ve müsaitlik kontrolü tamamlanamadı.')
      const confirmed = payload as ConfirmedBooking
      setConfirmedBooking(confirmed)
      sessionStorage.setItem(confirmationKey, JSON.stringify(confirmed))
    } catch (error) {
      setConfirmationError(error instanceof Error && error.message !== 'Failed to fetch' ? error.message : 'API bağlantısı kurulamadı. Rezervasyon oluşturulmadı.')
    } finally {
      setConfirming(false)
    }
  }

  if (confirmedBooking) return <section className="booking-details-complete booking-confirmed-result" aria-live="polite">
    <FeedbackState tone="success" title="Rezervasyon simülasyonu oluşturuldu" message={confirmedBooking.message} />
    <div><span>Rezervasyon kodu</span><strong>{confirmedBooking.referenceCode}</strong><small>{confirmedBooking.title}</small></div>
    <div><span>Onaylanan toplam</span><strong>{confirmedBooking.totalPrice.toLocaleString('tr-TR')} {confirmedBooking.currency}</strong><small>Fiyat ve stok transaction içinde son kez doğrulandı.</small></div>
    <div><span>Rezervasyondaki kişiler</span><strong>{travelers.length} kişi</strong><small>{validated?.contact.name ?? 'İletişim kişisi kaydedildi'}</small></div>
    <Link className="primary-action" to="/bookings">Rezervasyonlarıma git</Link>
  </section>

  if (state === 'done' && validated) return <section className="booking-details-complete" aria-live="polite">
    <FeedbackState tone="success" title="Kişi ve iletişim bilgileri eklendi" message={validated.message} />
    <div className="booking-people-review"><h3>Rezervasyon özeti · kişiler</h3>{validated.travelers.map((traveler, index) => <div key={`${traveler.firstName}-${traveler.lastName}-${index}`}><span>{TYPE_LABELS[traveler.type]} {index + 1}</span><strong>{traveler.firstName} {traveler.lastName}</strong>{traveler.age !== null && <small>{traveler.age} yaş</small>}</div>)}</div>
    <div className="booking-contact-review"><span>İletişim kişisi</span><strong>{validated.contact.name}</strong><small>{validated.contact.email} · {validated.contact.phone}</small></div>
    <p className="privacy-note">Kimlik, pasaport veya ödeme bilgisi alınmadı. Henüz rezervasyon oluşturulmadı.</p>
    {priceChange && <div className="booking-price-change"><strong>Fiyat değişti</strong><p>{priceChange.message}</p><span>Yeni toplam: {priceChange.currentTotal.toLocaleString('tr-TR')} {priceChange.currency}</span><button className="secondary-action" type="button" onClick={() => { setAcceptedTotal(priceChange.currentTotal); setAcceptedCurrency(priceChange.currency); setPriceChange(null) }}>Yeni fiyatı kabul et</button></div>}
    {!priceChange && (acceptedTotal !== Number((selection as { quotedTotal?: number }).quotedTotal ?? 0) || acceptedCurrency !== String((selection as { quotedCurrency?: string }).quotedCurrency ?? '')) && <div className="booking-accepted-price"><span>Kabul edilen güncel toplam</span><strong>{acceptedTotal.toLocaleString('tr-TR')} {acceptedCurrency}</strong></div>}
    {confirmationError && <><FeedbackState tone="error" title="Rezervasyon oluşturulmadı" message={confirmationError} /><Link className="secondary-action booking-recovery-link" to={searchUrl}>Güncel sonuçlara dön</Link></>}
    <div className="booking-final-actions"><button className="secondary-action" type="button" disabled={confirming} onClick={() => { setConfirmationError(''); setPriceChange(null); setState('editing') }}>Bilgileri düzenle</button><button className="primary-action" type="button" disabled={confirming || Boolean(priceChange)} onClick={() => void confirmBooking()}>{confirming ? 'Fiyat ve stok kontrol ediliyor…' : user ? 'Son kontrolü yap ve rezervasyonu onayla' : 'Giriş yap ve son onaya geç'}</button></div>
    <small className="booking-final-note">Son onayda güncel fiyat, oda veya koltuk sayısı yeniden sorgulanır. Değişiklik varsa işlem otomatik durur.</small>
  </section>

  return <form className="booking-details-form" onSubmit={submit} noValidate>
    <header><span className="section-label">MİSAFİR VE İLETİŞİM</span><h2>Rezervasyonda yer alacak kişileri ekle</h2><p>Aramadaki {adults + childCount + infants} kişi için yalnızca eğitim akışında gereken temel bilgileri gir.</p></header>
    <div className="booking-traveler-list">{travelers.map((traveler, index) => {
      const typeNumber = travelers.slice(0, index + 1).filter(item => item.type === traveler.type).length
      return <fieldset className="booking-traveler-card" key={`${traveler.type}-${index}`}><legend>{TYPE_LABELS[traveler.type]} {typeNumber}</legend><div className="booking-person-fields">
        <label className="form-field"><span>Ad<i>*</i></span><input value={traveler.firstName} autoComplete="off" maxLength={50} onChange={event => updateTraveler(index, { firstName: event.target.value }, 'firstName')} />{errors[`travelers.${index}.firstName`] && <small className="field-error">{errors[`travelers.${index}.firstName`]}</small>}</label>
        <label className="form-field"><span>Soyad<i>*</i></span><input value={traveler.lastName} autoComplete="off" maxLength={50} onChange={event => updateTraveler(index, { lastName: event.target.value }, 'lastName')} />{errors[`travelers.${index}.lastName`] && <small className="field-error">{errors[`travelers.${index}.lastName`]}</small>}</label>
        {traveler.type === 'child' && kind === 'hotel' && <label className="form-field"><span>Aramadaki yaş</span><input value={`${traveler.age} yaş`} readOnly /></label>}
        {traveler.type === 'child' && kind === 'flight' && <label className="form-field"><span>Yaş<i>*</i></span><select value={traveler.age ?? ''} onChange={event => updateTraveler(index, { age: Number(event.target.value) }, 'age')}><option value="" disabled>Yaş seçin</option>{Array.from({ length: 10 }, (_, age) => age + 2).map(age => <option value={age} key={age}>{age}</option>)}</select>{errors[`travelers.${index}.age`] && <small className="field-error">{errors[`travelers.${index}.age`]}</small>}</label>}
        {traveler.type === 'infant' && <><label className="form-field"><span>Yaş<i>*</i></span><select value={traveler.age ?? 0} onChange={event => updateTraveler(index, { age: Number(event.target.value) }, 'age')}><option value="0">0 yaş</option><option value="1">1 yaş</option></select>{errors[`travelers.${index}.age`] && <small className="field-error">{errors[`travelers.${index}.age`]}</small>}</label><label className="form-field"><span>Eşlik eden yetişkin<i>*</i></span><select value={traveler.accompanyingAdultIndex ?? ''} onChange={event => updateTraveler(index, { accompanyingAdultIndex: Number(event.target.value) }, 'accompanyingAdultIndex')}>{adultOptions.map((adult, adultIndex) => <option value={adultIndex} key={adultIndex}>{normalizedName(`${adult.firstName} ${adult.lastName}`) || `Yetişkin ${adultIndex + 1}`}</option>)}</select>{errors[`travelers.${index}.accompanyingAdultIndex`] && <small className="field-error">{errors[`travelers.${index}.accompanyingAdultIndex`]}</small>}</label></>}
      </div></fieldset>
    })}</div>
    <fieldset className="booking-contact-card"><legend>İletişim bilgileri</legend><p>Bilgilendirmeler için yetişkinlerden birini iletişim kişisi seç.</p><div className="booking-contact-fields">
      <label className="form-field"><span>İletişim kişisi<i>*</i></span><select value={contact.adultIndex} onChange={event => updateContact({ adultIndex: Number(event.target.value) }, 'adultIndex')}>{adultOptions.map((adult, index) => <option value={index} key={index}>{normalizedName(`${adult.firstName} ${adult.lastName}`) || `Yetişkin ${index + 1}`}</option>)}</select>{errors['contact.adultIndex'] && <small className="field-error">{errors['contact.adultIndex']}</small>}</label>
      <label className="form-field"><span>E-posta<i>*</i></span><input type="email" value={contact.email} autoComplete="email" maxLength={254} placeholder="ornek@eposta.com" onChange={event => updateContact({ email: event.target.value }, 'email')} />{errors['contact.email'] && <small className="field-error">{errors['contact.email']}</small>}</label>
      <label className="form-field"><span>Telefon<i>*</i></span><input type="tel" value={contact.phone} autoComplete="tel" maxLength={24} placeholder="+90 5xx xxx xx xx" onChange={event => updateContact({ phone: event.target.value }, 'phone')} />{errors['contact.phone'] && <small className="field-error">{errors['contact.phone']}</small>}</label>
    </div></fieldset>
    <div className="booking-details-actions"><p>Kimlik numarası, pasaport veya ödeme bilgisi istenmez.</p><button className="primary-action" type="submit" disabled={state === 'saving'}>{state === 'saving' ? 'Bilgiler doğrulanıyor…' : 'Kişileri özete ekle'}</button></div>
    {serverError && <FeedbackState tone="error" title="Bilgiler eklenemedi" message={serverError} />}
  </form>
}

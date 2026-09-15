import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import { Icon } from '../../shared/components/Icon'
import { formatClock, formatFlightDate, formatMinutes } from '../../shared/travelFormat'

type ChatConversation = { id: string; title: string; createdAt: string; updatedAt: string }
type ChatResult = {
  kind: 'hotel' | 'flight'; id?: string; name?: string; city?: string; district?: string; stars?: number; rating?: number
  flightNumber?: string; airline?: string; from?: string; to?: string; departureAt?: string; arrivalAt?: string
  durationMinutes?: number; stops?: number; baggage?: string; totalPrice: number; currency: string; detailUrl?: string
}
type ChatMetadata = { intent?: 'hotel' | 'flight' | 'both'; classification?: 'hotel' | 'flight' | 'both' | 'ambiguous' | 'out-of-scope' | 'continuation'; confidence?: 'low' | 'medium' | 'high'; understood?: Record<string, string>; missing?: string[]; results?: ChatResult[]; searchUrl?: string; appliedChange?: string; assistantMode?: 'ai' | 'fallback'; fallbackUrl?: string }
type ChatMessage = { id: string; role: 'user' | 'assistant'; content: string; metadata?: ChatMetadata; createdAt: string; pending?: boolean }

function chatHeaders(json = false) {
  const headers: Record<string, string> = { Authorization: `Bearer ${sessionStorage.getItem('travel-assistant-demo-session') ?? ''}` }
  if (json) headers['Content-Type'] = 'application/json'
  return headers
}
function ChatResultCards({ metadata }: { metadata?: ChatMetadata }) {
  if (!metadata?.results?.length) return null
  return <div className="chat-result-list">{metadata.results.map((result, index) => <article className="chat-result-card" key={`${result.kind}-${result.id ?? result.flightNumber}-${index}`}>
    <header><span><Icon name={result.kind === 'hotel' ? 'hotel' : 'plane'} size={16} />{result.kind === 'hotel' ? `${result.stars ?? 0} yıldız` : result.stops ? `${result.stops} aktarma` : 'Direkt'}</span><strong>{result.totalPrice.toLocaleString('tr-TR')} {result.currency}</strong></header>
    {result.kind === 'hotel' ? <><h3>{result.name}</h3><p>{result.district}, {result.city} · {result.rating?.toLocaleString('tr-TR')}/5</p></> : <><h3>{result.airline}</h3><p>{result.flightNumber} · {result.from} → {result.to}</p><small>{result.departureAt ? `${formatFlightDate(result.departureAt)} ${formatClock(result.departureAt)}` : ''} · {formatMinutes(result.durationMinutes ?? 0)}</small><small>{result.baggage}</small></>}
    <Link to={result.detailUrl ?? metadata.searchUrl ?? (result.kind === 'hotel' ? '/hotels' : '/flights')}>{result.kind === 'hotel' ? 'Ayrıntıları gör' : 'Ayrıntı ve rezervasyona geç'} <span aria-hidden="true">→</span></Link>
  </article>)}</div>
}

function ChatMessageView({ message }: { message: ChatMessage }) {
  const understood = Object.entries(message.metadata?.understood ?? {})
  const routingLabel = message.metadata?.classification === 'ambiguous' ? 'İstek belirsiz' : message.metadata?.classification === 'out-of-scope' ? 'Seyahat kapsamı dışında' : message.metadata?.classification === 'both' ? 'Uçuş + otel isteği' : null
  return <div className={`chat-message ${message.role}${message.pending ? ' pending' : ''}`}>
    <small>{message.role === 'user' ? 'Sen' : 'Seyahat yardımcısı'}</small><p>{message.content}</p>
    {routingLabel && <div className={`chat-routing-state ${message.metadata?.classification}`}><Icon name="info" size={13} />{routingLabel} · arama başlatılmadı</div>}
    {message.role === 'assistant' && message.metadata?.assistantMode === 'fallback' && <div className="chat-fallback-state"><Icon name="info" size={14} /><span>AI hizmeti kullanılamıyor; güvenli sınırlı anlayıcı etkin.</span>{message.metadata.fallbackUrl && <Link to={message.metadata.fallbackUrl}>Klasik formu aç</Link>}</div>}
    {message.role === 'assistant' && message.metadata?.appliedChange && <div className="chat-applied-change"><Icon name="filter" size={14} />{message.metadata.appliedChange}</div>}
    {message.role === 'assistant' && understood.length > 0 && <div className="chat-understood" aria-label="Anlaşılan bilgiler">{understood.map(([key, value]) => <span key={key}><b>{key}</b>{value}</span>)}</div>}
    {message.role === 'assistant' && message.metadata?.missing?.map(item => <div className="chat-missing" key={item}><Icon name="info" size={14} />Eksik bilgi: <strong>{item}</strong></div>)}
    <ChatResultCards metadata={message.metadata} />
  </div>
}

export function ChatPage() {
  const { user } = useAuth()
  const [conversations, setConversations] = useState<ChatConversation[]>([])
  const [activeId, setActiveId] = useState<string | null>(null)
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [draft, setDraft] = useState('')
  const [loadingHistory, setLoadingHistory] = useState(true)
  const [sending, setSending] = useState(false)
  const [error, setError] = useState('')
  const messageListRef = useRef<HTMLDivElement>(null)

  const loadConversations = useCallback(async () => {
    const response = await fetch('/api/chat/conversations', { headers: chatHeaders() })
    if (!response.ok) throw new Error('Konuşma geçmişi alınamadı.')
    const data = await response.json() as ChatConversation[]
    setConversations(data)
    setActiveId(current => current ?? data[0]?.id ?? null)
  }, [])

  useEffect(() => { const timer = window.setTimeout(() => void loadConversations().catch(() => setError('Konuşma geçmişi yüklenemedi.')).finally(() => setLoadingHistory(false)), 0); return () => window.clearTimeout(timer) }, [loadConversations, user?.id])
  useEffect(() => {
    if (!activeId) return
    const controller = new AbortController()
    fetch(`/api/chat/conversations/${activeId}/messages`, { headers: chatHeaders(), signal: controller.signal })
      .then(async response => { if (!response.ok) throw new Error(); setMessages(await response.json() as ChatMessage[]); setError('') })
      .catch(fetchError => { if (fetchError.name !== 'AbortError') setError('Mesaj geçmişi yüklenemedi.') })
      .finally(() => setLoadingHistory(false))
    return () => controller.abort()
  }, [activeId])
  useEffect(() => { messageListRef.current?.scrollTo({ top: messageListRef.current.scrollHeight, behavior: 'smooth' }) }, [messages, sending])

  const createConversation = async (whileSending = false) => {
    if (sending && !whileSending) return null
    setError('')
    const response = await fetch('/api/chat/conversations', { method: 'POST', headers: chatHeaders(true), body: '{}' })
    if (!response.ok) { setError('Yeni konuşma başlatılamadı.'); return null }
    const conversation = await response.json() as ChatConversation
    setConversations(current => [conversation, ...current])
    setActiveId(conversation.id); setMessages([]); setDraft('')
    return conversation.id
  }

  const sendMessage = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const content = draft.trim()
    if (!content) { setError('Boş mesaj gönderilemez.'); return }
    if (content.length > 1000) { setError('Mesaj en fazla 1000 karakter olabilir.'); return }
    if (sending) return
    setSending(true); setError('')
    let conversationId = activeId
    try {
      if (!conversationId) conversationId = await createConversation(true)
      if (!conversationId) throw new Error('conversation')
      const clientMessageId = crypto.randomUUID()
      const optimistic: ChatMessage = { id: clientMessageId, role: 'user', content, createdAt: new Date().toISOString(), pending: true }
      setMessages(current => [...current, optimistic]); setDraft('')
      const response = await fetch(`/api/chat/conversations/${conversationId}/messages`, { method: 'POST', headers: chatHeaders(true), body: JSON.stringify({ content, clientMessageId }) })
      const payload = await response.json().catch(() => ({}))
      if (!response.ok) throw new Error(payload.message ?? 'Mesaj gönderilemedi.')
      setMessages(current => [...current.map(item => item.id === clientMessageId ? { ...item, id: payload.userMessageId, pending: false } : item), payload.assistant as ChatMessage])
      setConversations(current => current.map(item => item.id === conversationId ? { ...item, title: item.title === 'Yeni konuşma' ? content.slice(0, 60) : item.title, updatedAt: new Date().toISOString() } : item).sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)))
    } catch (sendError) {
      setMessages(current => current.filter(item => !item.pending))
      setDraft(content)
      setError(sendError instanceof Error && sendError.message !== 'conversation' ? sendError.message : 'Mesaj gönderilemedi. Tekrar deneyin.')
    } finally { setSending(false) }
  }

  return <main className="chat-page" data-testid="chat-page">
    <section className="chat-shell">
      <aside className="conversation-sidebar"><header><div><strong>Konuşmalar</strong><small>{user?.name}</small></div><button type="button" onClick={() => void createConversation()} disabled={sending}>+ Yeni</button></header><nav aria-label="Konuşma geçmişi">{conversations.map(conversation => <button type="button" className={activeId === conversation.id ? 'active' : ''} key={conversation.id} onClick={() => { setLoadingHistory(true); setMessages([]); setActiveId(conversation.id) }} disabled={sending}><strong>{conversation.title}</strong><small>{new Intl.DateTimeFormat('tr-TR', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }).format(new Date(conversation.updatedAt))}</small></button>)}{!loadingHistory && conversations.length === 0 && <p>Henüz kayıtlı konuşman yok.</p>}</nav></aside>
      <section className="chat-workspace" aria-label="Seyahat sohbeti">
        <header className="chat-workspace-header"><div><span className="assistant-avatar"><Icon name="chat" size={18} /></span><div><h1>Seyahat asistanı</h1><p>Otel ve uçuş aramalarını doğal cümlelerle yap</p></div></div><span className="preview-status live">AKTİF</span></header>
        <div className="message-list" ref={messageListRef} aria-live="polite">
          {!loadingHistory && messages.length === 0 && <div className="chat-empty"><span className="assistant-avatar"><Icon name="compass" size={22} /></span><h2>Nereye gitmek istiyorsun?</h2><p>“IST’den AYT’ye 14.09.2026 tarihinde 2 kişilik uçuş” veya “Antalya’da 14.09.2026–16.09.2026 için 1 oda” yazabilirsin.</p></div>}
          {messages.map(message => <ChatMessageView message={message} key={message.id} />)}
          {sending && <div className="chat-message assistant typing"><small>Seyahat yardımcısı</small><p><span /><span /><span /></p></div>}
        </div>
        <div className="chat-shortcuts"><span>Hazır formlar:</span><Link to="/hotels"><Icon name="hotel" size={15} />Otel</Link><Link to="/flights"><Icon name="plane" size={15} />Uçuş</Link></div>
        {error && <div className="chat-error" role="alert">{error}</div>}
        <form className="chat-composer" onSubmit={sendMessage}><textarea value={draft} onChange={event => setDraft(event.target.value)} maxLength={1000} disabled={sending} placeholder="Seyahatini anlat…" aria-label="Sohbet mesajı" rows={1} onKeyDown={event => { if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); event.currentTarget.form?.requestSubmit() } }} /><span>{draft.length}/1000</span><button disabled={sending || !draft.trim()} type="submit" aria-label="Mesaj gönder"><Icon name="arrow" size={18} /></button></form>
      </section>
    </section>
  </main>
}

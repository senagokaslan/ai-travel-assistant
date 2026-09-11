import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { AuthContext, type SessionUser } from './context'

const STORAGE_KEY = 'travel-assistant-demo-session'

const token = () => sessionStorage.getItem(STORAGE_KEY)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<SessionUser | null>(null)
  const [loading, setLoading] = useState(() => Boolean(token()))
  const [error, setError] = useState<string | null>(null)

  useEffect(() => { const current = token(); if (!current) return; fetch('/api/auth/me', { headers: { Authorization: `Bearer ${current}` } }).then(async response => { if (response.ok) setUser((await response.json()).user); else sessionStorage.removeItem(STORAGE_KEY) }).catch(() => undefined).finally(() => setLoading(false)) }, [])

  const request = async (url: string, body: unknown) => {
    setError(null); setLoading(true)
    try { const response = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }); const data = await response.json().catch(() => ({})); if (!response.ok) { setError(response.status === 401 ? 'E-posta veya parola hatalı.' : data.message ?? 'İşlem tamamlanamadı. Tekrar deneyin.'); return false }; if (data.token) sessionStorage.setItem(STORAGE_KEY, data.token); setUser(data.user); return true } catch { setError('Sunucuya bağlanılamadı. Bağlantınızı kontrol edip tekrar deneyin.'); return false } finally { setLoading(false) }
  }

  const value = useMemo(() => ({
    user, loading, error,
    signIn: (email: string, password: string) => request('/api/auth/login', { email: email.trim(), password }),
    register: (name: string, email: string, password: string) => request('/api/auth/register', { name: name.trim(), email: email.trim(), password }),
    signOut: async () => { const current = token(); if (current) await fetch('/api/auth/logout', { method: 'POST', headers: { Authorization: `Bearer ${current}` } }).catch(() => undefined); setUser(null); sessionStorage.removeItem(STORAGE_KEY) },
    clearError: () => setError(null),
  }), [user, loading, error])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

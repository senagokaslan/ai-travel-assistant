import { useMemo, useState, type ReactNode } from 'react'
import { AuthContext, type UserRole } from './context'

const STORAGE_KEY = 'travel-assistant-demo-session'

function readStoredUser() {
  try {
    const stored = sessionStorage.getItem(STORAGE_KEY)
    if (stored === 'admin') return { name: 'Demo Admin', role: 'admin' as const }
    if (stored === 'user') return { name: 'Demo Kullanıcı', role: 'user' as const }
  } catch {
    // Storage may be disabled; an anonymous session is still usable.
  }
  return null
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState(readStoredUser)

  const value = useMemo(() => ({
    user,
    signIn: (role: UserRole) => {
      const nextUser = role === 'admin' ? { name: 'Demo Admin', role: 'admin' as const } : { name: 'Demo Kullanıcı', role: 'user' as const }
      setUser(nextUser)
      sessionStorage.setItem(STORAGE_KEY, role)
    },
    signOut: () => {
      setUser(null)
      sessionStorage.removeItem(STORAGE_KEY)
    },
  }), [user])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

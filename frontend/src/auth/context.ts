import { createContext } from 'react'

export type UserRole = 'user' | 'admin'
export type SessionUser = { id: string; name: string; email: string; role: UserRole }
export type AuthContextValue = { user: SessionUser | null; loading: boolean; error: string | null; signIn: (email: string, password: string) => Promise<boolean>; register: (name: string, email: string, password: string) => Promise<boolean>; signOut: () => Promise<void>; clearError: () => void }
export const AuthContext = createContext<AuthContextValue | null>(null)

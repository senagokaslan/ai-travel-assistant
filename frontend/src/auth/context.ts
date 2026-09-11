import { createContext } from 'react'

export type UserRole = 'user' | 'admin'
export type SessionUser = { name: string; role: UserRole }
export type AuthContextValue = { user: SessionUser | null; signIn: (role: UserRole) => void; signOut: () => void }
export const AuthContext = createContext<AuthContextValue | null>(null)

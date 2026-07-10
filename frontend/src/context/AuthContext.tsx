import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';
import { AuthApi } from '../api/endpoints';
import { clearAuth, storeAuthResult } from '../api/client';
import type { UserRole } from '../types';

interface AuthUser {
  userId: string;
  username: string;
  role: UserRole;
}

interface AuthContextValue {
  user: AuthUser | null;
  login: (username: string, password: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

const USER_STORAGE_KEY = 'rls.user';

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(() => {
    const raw = localStorage.getItem(USER_STORAGE_KEY);
    return raw ? (JSON.parse(raw) as AuthUser) : null;
  });

  const login = async (username: string, password: string) => {
    const result = await AuthApi.login(username, password);
    storeAuthResult(result);
    const authUser: AuthUser = { userId: result.userId, username: result.username, role: result.role };
    localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(authUser));
    setUser(authUser);
  };

  const logout = () => {
    clearAuth();
    localStorage.removeItem(USER_STORAGE_KEY);
    setUser(null);
  };

  const value = useMemo(() => ({ user, login, logout }), [user]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return ctx;
}

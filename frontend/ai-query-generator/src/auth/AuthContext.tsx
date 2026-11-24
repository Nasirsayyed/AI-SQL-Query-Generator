import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { authApi } from '../api/endpoints';
import { refreshAccessToken, setSessionExpiredHandler } from '../api/client';
import type { UserProfile } from '../api/types';
import { tokenStore } from './tokenStore';
import type { PermissionCode } from './permissions';

interface AuthState {
  user: UserProfile | null;
  initializing: boolean;
  sessionExpired: boolean;
  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  hasPermission: (permission: PermissionCode) => boolean;
  hasAny: (...permissions: PermissionCode[]) => boolean;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserProfile | null>(null);
  const [initializing, setInitializing] = useState(true);
  const [sessionExpired, setSessionExpired] = useState(false);
  const queryClient = useQueryClient();

  // Restore the session from the HttpOnly refresh cookie on start-up.
  useEffect(() => {
    let cancelled = false;
    refreshAccessToken()
      .then(async (token) => {
        if (!token || cancelled) return;
        const profile = await authApi.profile();
        if (!cancelled) setUser(profile);
      })
      .catch(() => undefined)
      .finally(() => !cancelled && setInitializing(false));
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    setSessionExpiredHandler(() => {
      setUser(null);
      setSessionExpired(true);
      queryClient.clear();
    });
  }, [queryClient]);

  const login = useCallback(async (email: string, password: string) => {
    const result = await authApi.login(email, password);
    tokenStore.set(result.accessToken);
    setSessionExpired(false);
    setUser(result.user);
  }, []);

  const logout = useCallback(async () => {
    try {
      await authApi.logout();
    } finally {
      tokenStore.set(null);
      setUser(null);
      queryClient.clear();
    }
  }, [queryClient]);

  const value = useMemo<AuthState>(() => {
    const permissions = new Set(user?.permissions ?? []);
    return {
      user,
      initializing,
      sessionExpired,
      login,
      logout,
      hasPermission: (p) => permissions.has(p),
      hasAny: (...ps) => ps.some((p) => permissions.has(p)),
    };
  }, [user, initializing, sessionExpired, login, logout]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}

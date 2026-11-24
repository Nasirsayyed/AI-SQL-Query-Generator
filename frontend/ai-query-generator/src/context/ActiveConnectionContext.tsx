import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { connectionsApi } from '../api/endpoints';
import type { ConnectionSummary } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { Permissions } from '../auth/permissions';

const STORAGE_KEY = 'aiqg.activeConnectionId';

interface ActiveConnectionState {
  connections: ConnectionSummary[];
  activeConnection: ConnectionSummary | null;
  setActiveConnectionId: (id: number) => void;
  isLoading: boolean;
}

const Ctx = createContext<ActiveConnectionState>({ connections: [], activeConnection: null, setActiveConnectionId: () => undefined, isLoading: false });

function readStored(): number | null {
  try {
    const v = Number(localStorage.getItem(STORAGE_KEY));
    return Number.isFinite(v) && v > 0 ? v : null;
  } catch {
    return null;
  }
}

/** The target database the user is currently working with (persisted per browser). */
export function ActiveConnectionProvider({ children }: { children: ReactNode }) {
  const { user, hasPermission } = useAuth();
  const [selectedId, setSelectedId] = useState<number | null>(readStored);
  const { data = [], isLoading } = useQuery({
    queryKey: ['connections', 'accessible'],
    queryFn: connectionsApi.accessible,
    enabled: !!user && hasPermission(Permissions.ConnectionsView),
    staleTime: 60_000,
  });

  const usable = useMemo(() => data.filter((c) => c.isActive), [data]);
  const activeConnection = usable.find((c) => c.connectionId === selectedId) ?? usable[0] ?? null;

  useEffect(() => {
    if (activeConnection && activeConnection.connectionId !== selectedId) setSelectedId(activeConnection.connectionId);
  }, [activeConnection, selectedId]);

  const value = useMemo<ActiveConnectionState>(
    () => ({
      connections: usable,
      activeConnection,
      isLoading,
      setActiveConnectionId: (id) => {
        setSelectedId(id);
        try {
          localStorage.setItem(STORAGE_KEY, String(id));
        } catch {
          /* storage unavailable */
        }
      },
    }),
    [usable, activeConnection, isLoading],
  );

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

// eslint-disable-next-line react-refresh/only-export-components
export const useActiveConnection = () => useContext(Ctx);

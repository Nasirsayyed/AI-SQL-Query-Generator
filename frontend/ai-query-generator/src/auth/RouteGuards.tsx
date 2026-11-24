import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { Box, CircularProgress } from '@mui/material';
import { useAuth } from './AuthContext';
import type { PermissionCode } from './permissions';
import { EmptyState } from '../components/EmptyState';

export function RequireAuth({ children }: { children: ReactNode }) {
  const { user, initializing } = useAuth();
  const location = useLocation();
  if (initializing)
    return (
      <Box sx={{ display: 'grid', placeItems: 'center', height: '100vh' }}>
        <CircularProgress aria-label="Loading" />
      </Box>
    );
  if (!user) return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  return <>{children}</>;
}

/** Hides a page from users without the permission. The backend enforces the same rule independently. */
export function RequirePermission({ permission, children }: { permission: PermissionCode; children: ReactNode }) {
  const { hasPermission } = useAuth();
  if (!hasPermission(permission))
    return <EmptyState title="Access denied" description="Your role does not have permission to view this page." />;
  return <>{children}</>;
}

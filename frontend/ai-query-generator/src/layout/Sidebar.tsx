import { NavLink, useLocation } from 'react-router-dom';
import { Box, List, ListItemButton, ListItemIcon, ListItemText, Typography } from '@mui/material';
import { DatabaseZap } from 'lucide-react';
import { navItems } from './navigation';
import { useAuth } from '../auth/AuthContext';

export function Sidebar({ onNavigate }: { onNavigate?: () => void }) {
  const { hasPermission } = useAuth();
  const location = useLocation();

  return (
    <Box component="nav" aria-label="Main navigation" sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25, px: 2.5, height: 64 }}>
        <DatabaseZap size={26} aria-hidden />
        <Box>
          <Typography variant="subtitle1" sx={{ fontWeight: 700, lineHeight: 1.1 }}>
            AI SQL
          </Typography>
          <Typography variant="caption" color="text.secondary">
            Query Generator
          </Typography>
        </Box>
      </Box>
      <List sx={{ px: 1, flex: 1 }}>
        {navItems
          .filter((item) => hasPermission(item.permission))
          .map((item) => {
            const Icon = item.icon;
            const selected = item.path === '/' ? location.pathname === '/' : location.pathname.startsWith(item.path);
            return (
              <ListItemButton
                key={item.path}
                component={NavLink}
                to={item.path}
                selected={selected}
                onClick={onNavigate}
                sx={{ borderRadius: 1.5, mb: 0.25, '&.Mui-selected': { bgcolor: 'action.selected' } }}
              >
                <ListItemIcon sx={{ minWidth: 36, color: selected ? 'primary.main' : 'text.secondary' }}>
                  <Icon size={19} aria-hidden />
                </ListItemIcon>
                <ListItemText primary={item.label} slotProps={{ primary: { variant: 'body2', sx: { fontWeight: selected ? 600 : 500 } } }} />
              </ListItemButton>
            );
          })}
      </List>
      <Typography variant="caption" color="text.secondary" sx={{ px: 2.5, py: 2 }}>
        Read-only · AI output is validated server-side
      </Typography>
    </Box>
  );
}

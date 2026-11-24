import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import {
  AppBar, Avatar, Badge, Box, Divider, IconButton, InputBase, ListItemIcon, ListItemText, Menu, MenuItem, Select, Toolbar, Tooltip, Typography,
} from '@mui/material';
import { Bell, Database, LogOut, Menu as MenuIcon, Moon, Search, Sun, UserRound } from 'lucide-react';
import dayjs from 'dayjs';
import { useAuth } from '../auth/AuthContext';
import { useColorMode } from '../theme/ColorModeContext';
import { useActiveConnection } from '../context/ActiveConnectionContext';
import { dashboardApi } from '../api/endpoints';
import { Permissions } from '../auth/permissions';

export function Header({ onMenuClick }: { onMenuClick: () => void }) {
  const { user, logout, hasPermission } = useAuth();
  const { mode, toggle } = useColorMode();
  const { connections, activeConnection, setActiveConnectionId } = useActiveConnection();
  const navigate = useNavigate();
  const [userMenu, setUserMenu] = useState<HTMLElement | null>(null);
  const [notifications, setNotifications] = useState<HTMLElement | null>(null);
  const [search, setSearch] = useState('');

  const { data: activity } = useQuery({
    queryKey: ['dashboard', 'activity'],
    queryFn: dashboardApi.activity,
    enabled: hasPermission(Permissions.DashboardView),
    refetchInterval: 60_000,
  });
  const failures = activity?.latestFailures ?? [];
  const recentFailures = failures.filter((f) => dayjs(f.at).isAfter(dayjs().subtract(1, 'day')));

  const submitSearch = (e: FormEvent) => {
    e.preventDefault();
    if (search.trim()) navigate(`/schema?q=${encodeURIComponent(search.trim())}`);
  };

  return (
    <AppBar position="sticky" color="inherit" sx={{ borderBottom: 1, borderColor: 'divider', bgcolor: 'background.paper' }}>
      <Toolbar sx={{ gap: 1.5, minHeight: 64 }}>
        <IconButton edge="start" onClick={onMenuClick} sx={{ display: { md: 'none' } }} aria-label="Open navigation">
          <MenuIcon size={20} />
        </IconButton>

        {connections.length > 0 && (
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <Database size={18} aria-hidden />
            <Select
              value={activeConnection?.connectionId ?? ''}
              onChange={(e) => setActiveConnectionId(Number(e.target.value))}
              variant="standard"
              disableUnderline
              inputProps={{ 'aria-label': 'Active database connection' }}
              sx={{ fontWeight: 600, minWidth: { xs: 110, sm: 200 }, maxWidth: { xs: 140, sm: 280 } }}
            >
              {connections.map((c) => (
                <MenuItem key={c.connectionId} value={c.connectionId}>
                  {c.connectionName}
                </MenuItem>
              ))}
            </Select>
          </Box>
        )}

        {hasPermission(Permissions.SchemaView) && (
          <Box
            component="form"
            onSubmit={submitSearch}
            role="search"
            sx={{ display: { xs: 'none', sm: 'flex' }, alignItems: 'center', gap: 1, px: 1.5, py: 0.5, ml: 'auto', borderRadius: 2, bgcolor: 'action.hover', width: { sm: 220, lg: 320 } }}
          >
            <Search size={16} aria-hidden />
            <InputBase placeholder="Search tables & columns" value={search} onChange={(e) => setSearch(e.target.value)} sx={{ flex: 1, fontSize: 14 }} inputProps={{ 'aria-label': 'Search schema' }} />
          </Box>
        )}

        <Box sx={{ ml: hasPermission(Permissions.SchemaView) ? 0 : 'auto', display: 'flex', alignItems: 'center', gap: 0.5 }}>
          <Tooltip title="Notifications">
            <IconButton onClick={(e) => setNotifications(e.currentTarget)} aria-label="Notifications">
              <Badge color="error" badgeContent={recentFailures.length} max={9}>
                <Bell size={19} />
              </Badge>
            </IconButton>
          </Tooltip>
          <Tooltip title={mode === 'dark' ? 'Light mode' : 'Dark mode'}>
            <IconButton onClick={toggle} aria-label="Toggle colour theme">
              {mode === 'dark' ? <Sun size={19} /> : <Moon size={19} />}
            </IconButton>
          </Tooltip>
          <IconButton onClick={(e) => setUserMenu(e.currentTarget)} aria-label="User menu">
            <Avatar sx={{ width: 32, height: 32, fontSize: 14, bgcolor: 'primary.main' }}>
              {user?.fullName?.split(' ').map((p) => p[0]).slice(0, 2).join('')}
            </Avatar>
          </IconButton>
        </Box>

        <Menu anchorEl={notifications} open={!!notifications} onClose={() => setNotifications(null)} slotProps={{ paper: { sx: { width: 340 } } }}>
          <Typography variant="subtitle2" sx={{ px: 2, py: 1 }}>
            Recent failed executions
          </Typography>
          <Divider />
          {failures.length === 0 && <MenuItem disabled>No failures — all good.</MenuItem>}
          {failures.slice(0, 6).map((f) => (
            <MenuItem key={f.id} onClick={() => { setNotifications(null); navigate('/history?status=Failed'); }} sx={{ whiteSpace: 'normal' }}>
              <ListItemText primary={f.title} secondary={`${f.detail ?? f.status} · ${dayjs(f.at).format('MMM D, HH:mm')}`} slotProps={{ secondary: { noWrap: true } }} />
            </MenuItem>
          ))}
        </Menu>

        <Menu anchorEl={userMenu} open={!!userMenu} onClose={() => setUserMenu(null)}>
          <Box sx={{ px: 2, py: 1 }}>
            <Typography variant="subtitle2">{user?.fullName}</Typography>
            <Typography variant="caption" color="text.secondary">
              {user?.email} · {user?.roles.join(', ')}
            </Typography>
          </Box>
          <Divider />
          <MenuItem onClick={() => { setUserMenu(null); navigate('/settings?tab=account'); }}>
            <ListItemIcon><UserRound size={17} /></ListItemIcon>
            My account
          </MenuItem>
          <MenuItem onClick={() => { setUserMenu(null); void logout().then(() => navigate('/login')); }}>
            <ListItemIcon><LogOut size={17} /></ListItemIcon>
            Sign out
          </MenuItem>
        </Menu>
      </Toolbar>
    </AppBar>
  );
}

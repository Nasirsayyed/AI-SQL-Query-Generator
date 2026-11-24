import { useState } from 'react';
import { Outlet } from 'react-router-dom';
import { Box, Drawer } from '@mui/material';
import { Header } from './Header';
import { Sidebar } from './Sidebar';

const DRAWER_WIDTH = 248;

export function AppLayout() {
  const [mobileOpen, setMobileOpen] = useState(false);

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh' }}>
      <Drawer
        variant="temporary"
        open={mobileOpen}
        onClose={() => setMobileOpen(false)}
        ModalProps={{ keepMounted: true }}
        sx={{ display: { xs: 'block', md: 'none' }, '& .MuiDrawer-paper': { width: DRAWER_WIDTH } }}
      >
        <Sidebar onNavigate={() => setMobileOpen(false)} />
      </Drawer>
      <Drawer
        variant="permanent"
        sx={{ display: { xs: 'none', md: 'block' }, width: DRAWER_WIDTH, flexShrink: 0, '& .MuiDrawer-paper': { width: DRAWER_WIDTH, boxSizing: 'border-box' } }}
        open
      >
        <Sidebar />
      </Drawer>
      <Box sx={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column' }}>
        <Header onMenuClick={() => setMobileOpen(true)} />
        <Box component="main" sx={{ flex: 1, p: { xs: 2, md: 3 }, maxWidth: 1600, width: '100%', mx: 'auto' }}>
          <Outlet />
        </Box>
      </Box>
    </Box>
  );
}

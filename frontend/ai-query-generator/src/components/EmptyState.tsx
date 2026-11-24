import type { ReactNode } from 'react';
import { Box, Typography } from '@mui/material';
import { Inbox } from 'lucide-react';

export function EmptyState({ title, description, icon, action }: { title: string; description?: string; icon?: ReactNode; action?: ReactNode }) {
  return (
    <Box sx={{ textAlign: 'center', py: 6, px: 2, color: 'text.secondary' }}>
      <Box sx={{ mb: 1.5, display: 'flex', justifyContent: 'center', opacity: 0.7 }}>{icon ?? <Inbox size={36} aria-hidden />}</Box>
      <Typography variant="h6" color="text.primary">
        {title}
      </Typography>
      {description && <Typography sx={{ mt: 0.5, maxWidth: 520, mx: 'auto' }}>{description}</Typography>}
      {action && <Box sx={{ mt: 2 }}>{action}</Box>}
    </Box>
  );
}

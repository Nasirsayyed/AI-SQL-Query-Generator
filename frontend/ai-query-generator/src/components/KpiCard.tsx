import type { ReactNode } from 'react';
import { Box, Card, CardContent, Skeleton, Typography } from '@mui/material';

export function KpiCard({ label, value, icon, hint, loading, color = 'primary.main' }: {
  label: string;
  value: ReactNode;
  icon: ReactNode;
  hint?: string;
  loading?: boolean;
  color?: string;
}) {
  return (
    <Card sx={{ height: '100%' }}>
      <CardContent sx={{ display: 'flex', gap: 2, alignItems: 'center' }}>
        <Box sx={{ width: 44, height: 44, borderRadius: 2, display: 'grid', placeItems: 'center', bgcolor: 'action.hover', color, flexShrink: 0 }}>{icon}</Box>
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="body2" color="text.secondary" noWrap>
            {label}
          </Typography>
          <Typography variant="h5" component="p" sx={{ fontVariantNumeric: 'tabular-nums' }}>
            {loading ? <Skeleton width={60} /> : value}
          </Typography>
          {hint && (
            <Typography variant="caption" color="text.secondary">
              {hint}
            </Typography>
          )}
        </Box>
      </CardContent>
    </Card>
  );
}

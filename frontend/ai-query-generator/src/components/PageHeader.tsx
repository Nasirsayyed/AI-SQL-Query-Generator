import type { ReactNode } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { Box, Breadcrumbs, Link, Typography } from '@mui/material';

export function PageHeader({ title, subtitle, breadcrumbs = [], actions }: {
  title: string;
  subtitle?: string;
  breadcrumbs?: { label: string; to?: string }[];
  actions?: ReactNode;
}) {
  return (
    <Box sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'flex-end', gap: 2, mb: 3 }}>
      <Box sx={{ flex: 1, minWidth: 240 }}>
        <Breadcrumbs aria-label="breadcrumb" sx={{ mb: 0.5, fontSize: 13 }}>
          <Link component={RouterLink} to="/" underline="hover" color="inherit">
            Home
          </Link>
          {breadcrumbs.map((b) =>
            b.to ? (
              <Link key={b.label} component={RouterLink} to={b.to} underline="hover" color="inherit">
                {b.label}
              </Link>
            ) : (
              <Typography key={b.label} color="text.primary" sx={{ fontSize: 13 }}>
                {b.label}
              </Typography>
            ),
          )}
          {breadcrumbs.length === 0 && <Typography color="text.primary" sx={{ fontSize: 13 }}>{title}</Typography>}
        </Breadcrumbs>
        <Typography variant="h4" component="h1">
          {title}
        </Typography>
        {subtitle && (
          <Typography color="text.secondary" sx={{ mt: 0.5 }}>
            {subtitle}
          </Typography>
        )}
      </Box>
      {actions && <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>{actions}</Box>}
    </Box>
  );
}

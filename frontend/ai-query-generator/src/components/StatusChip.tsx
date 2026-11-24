import { Chip, type ChipProps } from '@mui/material';

const colors: Record<string, ChipProps['color']> = {
  Succeeded: 'success',
  Healthy: 'success',
  Generated: 'info',
  ClarificationRequested: 'default',
  ValidationFailed: 'warning',
  Unsafe: 'warning',
  Untested: 'default',
  Failed: 'error',
  TimedOut: 'error',
  Cancelled: 'default',
};

const labels: Record<string, string> = {
  ClarificationRequested: 'Clarification',
  ValidationFailed: 'Rejected',
  TimedOut: 'Timed out',
};

export function StatusChip({ status, size = 'small' }: { status: string; size?: ChipProps['size'] }) {
  return <Chip size={size} label={labels[status] ?? status} color={colors[status] ?? 'default'} variant="outlined" />;
}

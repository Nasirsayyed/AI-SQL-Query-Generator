import dayjs from 'dayjs';
import relativeTime from 'dayjs/plugin/relativeTime';

dayjs.extend(relativeTime);

export const formatDateTime = (value?: string | null) => (value ? dayjs(value).format('YYYY-MM-DD HH:mm') : '—');
export const fromNow = (value?: string | null) => (value ? dayjs(value).fromNow() : '—');
export const formatNumber = (value?: number | null, digits = 0) =>
  value === null || value === undefined ? '—' : value.toLocaleString(undefined, { maximumFractionDigits: digits });
export const formatDuration = (ms?: number | null) =>
  ms === null || ms === undefined ? '—' : ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(ms < 10_000 ? 2 : 1)} s`;

import { useEffect, useMemo, useRef, useState } from 'react';
import { Alert, Box, Button, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { ImageDown } from 'lucide-react';
import type { ChartSuggestion, QueryResult } from '../api/types';
import { aggregations, buildChartOption, buildSeries, chartTypes, initialChartConfig, validateChartConfig, type ChartConfig } from '../utils/chart';
import { EChart, type EChartHandle } from './EChart';
import { downloadBlob, safeFileName } from '../utils/export';
import { EmptyState } from './EmptyState';

/**
 * Charts built strictly from returned rows. The AI suggestion is only a starting point and is validated against the
 * result columns; users can change type, axes and aggregation.
 */
export function ResultChart({ result, suggestion, title, onConfigChange }: {
  result: QueryResult;
  suggestion?: ChartSuggestion | null;
  title: string;
  onConfigChange?: (config: ChartConfig | null) => void;
}) {
  const [config, setConfig] = useState<ChartConfig | null>(() => initialChartConfig(result.columns, suggestion));
  const chartRef = useRef<EChartHandle>(null);

  useEffect(() => setConfig(initialChartConfig(result.columns, suggestion)), [result, suggestion]);
  useEffect(() => onConfigChange?.(config), [config, onConfigChange]);

  const validation = useMemo(() => validateChartConfig(config, result.columns), [config, result.columns]);
  const option = useMemo(() => (config && validation.valid ? buildChartOption(config, buildSeries(result.rows, result.columns, config)) : null), [config, validation.valid, result]);

  if (result.rowCount === 0) return <EmptyState title="Nothing to chart" description="The query returned no rows." />;
  if (!config) return <EmptyState title="No chartable columns" description="A chart needs at least one numeric column and one category or date column." />;

  const set = (patch: Partial<ChartConfig>) => setConfig({ ...config, ...patch });

  const download = () => {
    const url = chartRef.current?.toDataUrl();
    if (!url) return;
    void fetch(url).then((r) => r.blob()).then((b) => downloadBlob(b, safeFileName(title, 'png')));
  };

  return (
    <Box>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ mb: 2 }}>
        <TextField select label="Chart type" value={config.chartType} onChange={(e) => set({ chartType: e.target.value as ChartConfig['chartType'] })} sx={{ minWidth: 150 }}>
          {chartTypes.map((t) => <MenuItem key={t.value} value={t.value}>{t.label}</MenuItem>)}
        </TextField>
        <TextField select label="X-axis" value={config.xAxis} onChange={(e) => set({ xAxis: e.target.value })} sx={{ minWidth: 160 }}>
          {result.columns.map((c) => <MenuItem key={c.name} value={c.name}>{c.name}</MenuItem>)}
        </TextField>
        <TextField select label="Y-axis" value={config.yAxis} onChange={(e) => set({ yAxis: e.target.value })} sx={{ minWidth: 160 }}>
          {result.columns.map((c) => <MenuItem key={c.name} value={c.name}>{c.name}</MenuItem>)}
        </TextField>
        <TextField select label="Aggregation" value={config.aggregation} onChange={(e) => set({ aggregation: e.target.value as ChartConfig['aggregation'] })} sx={{ minWidth: 130 }}>
          {aggregations.map((a) => <MenuItem key={a} value={a}>{a === 'none' ? 'None' : a.toUpperCase()}</MenuItem>)}
        </TextField>
        <Box sx={{ flex: 1 }} />
        <Button startIcon={<ImageDown size={16} />} onClick={download} disabled={!option}>Download image</Button>
      </Stack>
      {suggestion?.reason && suggestion.recommended && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 1 }}>
          Suggested ({suggestion.source}): {suggestion.reason}
        </Typography>
      )}
      {!validation.valid && <Alert severity="warning">{validation.errors.join(' ')}</Alert>}
      {option && <EChart ref={chartRef} option={option} height={420} ariaLabel={`${config.chartType} chart of ${config.yAxis} by ${config.xAxis}`} />}
    </Box>
  );
}

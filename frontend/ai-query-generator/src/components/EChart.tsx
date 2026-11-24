import { forwardRef, useEffect, useImperativeHandle, useRef } from 'react';
import { Box, useTheme } from '@mui/material';
import * as echarts from 'echarts/core';
import { BarChart, GraphChart, LineChart, PieChart, ScatterChart } from 'echarts/charts';
import { DataZoomComponent, GridComponent, LegendComponent, TitleComponent, TooltipComponent } from 'echarts/components';
import { CanvasRenderer } from 'echarts/renderers';
import type { EChartsCoreOption } from 'echarts/core';

echarts.use([BarChart, LineChart, PieChart, ScatterChart, GraphChart, GridComponent, TooltipComponent, LegendComponent, TitleComponent, DataZoomComponent, CanvasRenderer]);

export interface EChartHandle {
  /** PNG data URL of the rendered chart. */
  toDataUrl: () => string | undefined;
}

/**
 * Minimal ECharts wrapper. Options are always plain data built by the app from query results —
 * never code or configuration supplied by the AI.
 */
export const EChart = forwardRef<EChartHandle, { option: EChartsCoreOption; height?: number | string; ariaLabel: string }>(
  function EChart({ option, height = 360, ariaLabel }, ref) {
    const container = useRef<HTMLDivElement>(null);
    const chart = useRef<echarts.ECharts | null>(null);
    const theme = useTheme();
    const mode = theme.palette.mode;

    useEffect(() => {
      if (!container.current) return;
      const instance = echarts.init(container.current, mode === 'dark' ? 'dark' : undefined, { renderer: 'canvas' });
      chart.current = instance;
      const observer = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(() => instance.resize()) : null;
      observer?.observe(container.current);
      return () => {
        observer?.disconnect();
        instance.dispose();
        chart.current = null;
      };
    }, [mode]);

    useEffect(() => {
      chart.current?.setOption({ backgroundColor: 'transparent', ...option }, { notMerge: true });
    }, [option, mode]);

    useImperativeHandle(ref, () => ({
      toDataUrl: () => chart.current?.getDataURL({ type: 'png', pixelRatio: 2, backgroundColor: theme.palette.background.paper }),
    }));

    return <Box ref={container} role="img" aria-label={ariaLabel} sx={{ width: '100%', height }} />;
  },
);

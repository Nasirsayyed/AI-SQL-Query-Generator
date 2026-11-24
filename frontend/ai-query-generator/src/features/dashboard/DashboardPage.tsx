import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { Box, Card, CardContent, CardHeader, Grid, List, ListItemButton, ListItemText, Skeleton, Stack, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material';
import { Activity, CheckCircle2, Database, Sparkles, Timer, XCircle } from 'lucide-react';
import { dashboardApi } from '../../api/endpoints';
import { useAuth } from '../../auth/AuthContext';
import { Permissions } from '../../auth/permissions';
import { KpiCard } from '../../components/KpiCard';
import { PageHeader } from '../../components/PageHeader';
import { EChart } from '../../components/EChart';
import { EmptyState } from '../../components/EmptyState';
import { StatusChip } from '../../components/StatusChip';
import { ErrorAlert } from '../../components/ErrorAlert';
import { formatDuration, formatNumber, fromNow } from '../../utils/format';

function ChartCard({ title, loading, empty, children }: { title: string; loading: boolean; empty: boolean; children: React.ReactNode }) {
  return (
    <Card sx={{ height: '100%' }}>
      <CardHeader title={title} slotProps={{ title: { variant: 'h6' } }} />
      <CardContent sx={{ pt: 0 }}>
        {loading ? <Skeleton variant="rectangular" height={260} /> : empty ? <EmptyState title="No data yet" description="Run some queries to populate this chart." /> : children}
      </CardContent>
    </Card>
  );
}

export function DashboardPage() {
  const { hasPermission } = useAuth();
  const navigate = useNavigate();
  const [days, setDays] = useState(30);
  const viewAll = hasPermission(Permissions.HistoryViewAll);

  const summary = useQuery({ queryKey: ['dashboard', 'summary'], queryFn: dashboardApi.summary });
  const trends = useQuery({ queryKey: ['dashboard', 'trends', days], queryFn: () => dashboardApi.trends(days) });
  const tables = useQuery({ queryKey: ['dashboard', 'tables', days], queryFn: () => dashboardApi.topTables(days) });
  const performance = useQuery({ queryKey: ['dashboard', 'performance', days], queryFn: () => dashboardApi.performance(days) });
  const users = useQuery({ queryKey: ['dashboard', 'users', days], queryFn: () => dashboardApi.topUsers(days), enabled: viewAll });
  const categories = useQuery({ queryKey: ['dashboard', 'categories'], queryFn: dashboardApi.categories });
  const activity = useQuery({ queryKey: ['dashboard', 'activity'], queryFn: dashboardApi.activity });

  const s = summary.data;
  const trendOption = useMemo(() => ({
    tooltip: { trigger: 'axis' },
    legend: { bottom: 0 },
    grid: { left: 8, right: 16, top: 16, bottom: 40, containLabel: true },
    xAxis: { type: 'category', data: (trends.data ?? []).map((d) => d.date.slice(5)) },
    yAxis: { type: 'value', minInterval: 1 },
    series: [
      { name: 'Executions', type: 'line', smooth: true, data: (trends.data ?? []).map((d) => d.total) },
      { name: 'Generated', type: 'line', smooth: true, data: (trends.data ?? []).map((d) => d.generated) },
    ],
  }), [trends.data]);

  const outcomeOption = useMemo(() => ({
    tooltip: { trigger: 'item' },
    legend: { bottom: 0 },
    series: [{ type: 'pie', radius: ['45%', '72%'], data: [
      { name: 'Successful', value: s?.successfulQueries ?? 0, itemStyle: { color: '#2e9d62' } },
      { name: 'Failed', value: s?.failedQueries ?? 0, itemStyle: { color: '#d64545' } },
    ] }],
  }), [s]);

  const barOption = (data: { name: string; count: number }[], name: string) => ({
    tooltip: { trigger: 'axis' },
    grid: { left: 8, right: 24, top: 8, bottom: 8, containLabel: true },
    xAxis: { type: 'value', minInterval: 1 },
    yAxis: { type: 'category', inverse: true, data: data.map((d) => d.name), axisLabel: { width: 140, overflow: 'truncate' } },
    series: [{ type: 'bar', name, data: data.map((d) => d.count) }],
  });

  const perfOption = useMemo(() => ({
    tooltip: { trigger: 'axis' },
    legend: { bottom: 0 },
    grid: { left: 8, right: 16, top: 16, bottom: 40, containLabel: true },
    xAxis: { type: 'category', data: (performance.data?.daily ?? []).map((d) => d.date.slice(5)) },
    yAxis: { type: 'value', name: 'ms' },
    series: [
      { name: 'Average', type: 'bar', data: (performance.data?.daily ?? []).map((d) => d.averageMs) },
      { name: 'Max', type: 'line', data: (performance.data?.daily ?? []).map((d) => d.maxMs) },
    ],
  }), [performance.data]);

  const noTrend = (trends.data ?? []).every((d) => d.total === 0 && d.generated === 0);

  return (
    <Box>
      <PageHeader
        title="Dashboard"
        subtitle={s ? (s.isGlobalScope ? 'Activity across all users' : 'Your query activity') : undefined}
        actions={
          <ToggleButtonGroup size="small" exclusive value={days} onChange={(_, v) => v && setDays(v)} aria-label="Time range">
            <ToggleButton value={7}>7 days</ToggleButton>
            <ToggleButton value={30}>30 days</ToggleButton>
            <ToggleButton value={90}>90 days</ToggleButton>
          </ToggleButtonGroup>
        }
      />
      <ErrorAlert error={summary.error} />

      <Grid container spacing={2} sx={{ mb: 2 }}>
        {[
          { label: 'Total Queries', value: formatNumber(s?.totalQueries), icon: <Activity size={22} /> },
          { label: 'Successful Queries', value: formatNumber(s?.successfulQueries), icon: <CheckCircle2 size={22} />, color: 'success.main' },
          { label: 'Failed Queries', value: formatNumber(s?.failedQueries), icon: <XCircle size={22} />, color: 'error.main' },
          { label: 'Average Execution Time', value: formatDuration(s ? Math.round(s.averageExecutionTimeMs) : null), icon: <Timer size={22} />, color: 'warning.main' },
          { label: 'Queries Generated Today', value: formatNumber(s?.queriesGeneratedToday), icon: <Sparkles size={22} />, color: 'secondary.main' },
          { label: 'Active Connections', value: formatNumber(s?.activeConnections), icon: <Database size={22} />, color: 'info.main' },
        ].map((k) => (
          <Grid key={k.label} size={{ xs: 12, sm: 6, lg: 2 }}>
            <KpiCard {...k} loading={summary.isLoading} />
          </Grid>
        ))}
      </Grid>

      <Grid container spacing={2}>
        <Grid size={{ xs: 12, lg: 8 }}>
          <ChartCard title="Queries by day" loading={trends.isLoading} empty={noTrend}>
            <EChart option={trendOption} height={280} ariaLabel="Queries by day" />
          </ChartCard>
        </Grid>
        <Grid size={{ xs: 12, lg: 4 }}>
          <ChartCard title="Success vs failure" loading={summary.isLoading} empty={!s || s.totalQueries === 0}>
            <EChart option={outcomeOption} height={280} ariaLabel="Query success versus failure" />
          </ChartCard>
        </Grid>
        <Grid size={{ xs: 12, md: 6, lg: 4 }}>
          <ChartCard title="Most used tables" loading={tables.isLoading} empty={(tables.data ?? []).length === 0}>
            <EChart option={barOption(tables.data ?? [], 'Executions')} height={280} ariaLabel="Most frequently used tables" />
          </ChartCard>
        </Grid>
        {viewAll && (
          <Grid size={{ xs: 12, md: 6, lg: 4 }}>
            <ChartCard title="Most active users" loading={users.isLoading} empty={(users.data ?? []).length === 0}>
              <EChart option={barOption(users.data ?? [], 'Executions')} height={280} ariaLabel="Most active users" />
            </ChartCard>
          </Grid>
        )}
        <Grid size={{ xs: 12, md: 6, lg: 4 }}>
          <ChartCard title="Popular query categories" loading={categories.isLoading} empty={(categories.data ?? []).length === 0}>
            <EChart option={barOption(categories.data ?? [], 'Usage')} height={280} ariaLabel="Most popular query categories" />
          </ChartCard>
        </Grid>
        <Grid size={{ xs: 12, lg: viewAll ? 12 : 8 }}>
          <ChartCard title="Average execution time" loading={performance.isLoading} empty={(performance.data?.daily ?? []).every((d) => d.executions === 0)}>
            <EChart option={perfOption} height={280} ariaLabel="Average query execution time" />
          </ChartCard>
        </Grid>

        {[
          { title: 'Latest queries', items: activity.data?.latestQueries ?? [], onClick: () => navigate('/history') },
          { title: 'Latest failed executions', items: activity.data?.latestFailures ?? [], onClick: () => navigate('/history?status=Failed') },
          { title: 'Recent metadata refreshes', items: activity.data?.metadataRefreshes ?? [], onClick: () => hasPermission(Permissions.ConnectionsManage) && navigate('/connections') },
        ].map((panel) => (
          <Grid key={panel.title} size={{ xs: 12, md: 4 }}>
            <Card sx={{ height: '100%' }}>
              <CardHeader title={panel.title} slotProps={{ title: { variant: 'h6' } }} />
              <List dense sx={{ pt: 0 }}>
                {activity.isLoading && <Skeleton sx={{ mx: 2 }} height={120} />}
                {!activity.isLoading && panel.items.length === 0 && <Typography color="text.secondary" sx={{ px: 2, pb: 2 }}>Nothing yet.</Typography>}
                {panel.items.map((item) => (
                  <ListItemButton key={`${panel.title}-${item.id}`} onClick={panel.onClick}>
                    <ListItemText
                      primary={item.title}
                      secondary={[item.userName, item.connectionName, fromNow(item.at)].filter(Boolean).join(' · ')}
                      slotProps={{ primary: { noWrap: true }, secondary: { noWrap: true } }}
                    />
                    <Stack sx={{ ml: 1 }}><StatusChip status={item.status} /></Stack>
                  </ListItemButton>
                ))}
              </List>
            </Card>
          </Grid>
        ))}
      </Grid>
    </Box>
  );
}

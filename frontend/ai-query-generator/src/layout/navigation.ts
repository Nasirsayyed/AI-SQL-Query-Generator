import { Bot, Cable, Database, History, LayoutDashboard, Settings, Star, Users, type LucideIcon } from 'lucide-react';
import { Permissions, type PermissionCode } from '../auth/permissions';

export interface NavItem {
  label: string;
  path: string;
  icon: LucideIcon;
  permission: PermissionCode;
}

export const navItems: NavItem[] = [
  { label: 'Dashboard', path: '/', icon: LayoutDashboard, permission: Permissions.DashboardView },
  { label: 'AI SQL Assistant', path: '/assistant', icon: Bot, permission: Permissions.QueryGenerate },
  { label: 'Query History', path: '/history', icon: History, permission: Permissions.HistoryView },
  { label: 'Saved Queries', path: '/saved', icon: Star, permission: Permissions.SharedReportsView },
  { label: 'Database Connections', path: '/connections', icon: Cable, permission: Permissions.ConnectionsManage },
  { label: 'Schema Explorer', path: '/schema', icon: Database, permission: Permissions.SchemaView },
  { label: 'Users & Roles', path: '/users', icon: Users, permission: Permissions.UsersManage },
  { label: 'Settings', path: '/settings', icon: Settings, permission: Permissions.DashboardView },
];

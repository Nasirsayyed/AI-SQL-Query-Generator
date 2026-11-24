import { Navigate, Route, Routes } from 'react-router-dom';
import { AppLayout } from './layout/AppLayout';
import { RequireAuth, RequirePermission } from './auth/RouteGuards';
import { Permissions } from './auth/permissions';
import { LoginPage } from './features/auth/LoginPage';
import { DashboardPage } from './features/dashboard/DashboardPage';
import { AssistantPage } from './features/assistant/AssistantPage';
import { HistoryPage } from './features/history/HistoryPage';
import { SavedQueriesPage } from './features/saved/SavedQueriesPage';
import { ConnectionsPage } from './features/connections/ConnectionsPage';
import { SchemaExplorerPage } from './features/schema/SchemaExplorerPage';
import { UsersPage } from './features/users/UsersPage';
import { SettingsPage } from './features/settings/SettingsPage';
import { ActiveConnectionProvider } from './context/ActiveConnectionContext';

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        element={
          <RequireAuth>
            <ActiveConnectionProvider>
              <AppLayout />
            </ActiveConnectionProvider>
          </RequireAuth>
        }
      >
        <Route index element={<RequirePermission permission={Permissions.DashboardView}><DashboardPage /></RequirePermission>} />
        <Route path="assistant" element={<RequirePermission permission={Permissions.QueryGenerate}><AssistantPage /></RequirePermission>} />
        <Route path="history" element={<RequirePermission permission={Permissions.HistoryView}><HistoryPage /></RequirePermission>} />
        <Route path="saved" element={<RequirePermission permission={Permissions.SharedReportsView}><SavedQueriesPage /></RequirePermission>} />
        <Route path="connections" element={<RequirePermission permission={Permissions.ConnectionsManage}><ConnectionsPage /></RequirePermission>} />
        <Route path="schema" element={<RequirePermission permission={Permissions.SchemaView}><SchemaExplorerPage /></RequirePermission>} />
        <Route path="users" element={<RequirePermission permission={Permissions.UsersManage}><UsersPage /></RequirePermission>} />
        <Route path="settings" element={<SettingsPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}

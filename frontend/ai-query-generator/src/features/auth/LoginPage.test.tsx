import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../test/utils';
import { LoginPage } from './LoginPage';
import { ApiError } from '../../api/client';

const login = vi.fn();
let rejectWith: Error | null = null;
// A plain async function records the call on the spy and then fails, so the rejection is owned by the component.
const loginProxy = async (email: string, password: string) => {
  login(email, password);
  if (rejectWith) throw rejectWith;
};
vi.mock('../../auth/AuthContext', () => ({ useAuth: () => ({ user: null, login: loginProxy, sessionExpired: false }) }));

describe('LoginPage', () => {
  beforeEach(() => {
    login.mockReset();
    rejectWith = null;
  });

  it('validates required fields before submitting', async () => {
    renderWithProviders(<LoginPage />, { route: '/login' });
    await userEvent.click(screen.getByRole('button', { name: /sign in/i }));
    expect(await screen.findByText('Email is required')).toBeInTheDocument();
    expect(screen.getByText('Password is required')).toBeInTheDocument();
    expect(login).not.toHaveBeenCalled();
  });

  it('submits credentials', async () => {
    renderWithProviders(<LoginPage />, { route: '/login' });
    await userEvent.type(screen.getByLabelText(/email/i), 'dev@test.local');
    await userEvent.type(screen.getByLabelText(/^password/i), 'Secret-Passw0rd');
    await userEvent.click(screen.getByRole('button', { name: /sign in/i }));
    await waitFor(() => expect(login).toHaveBeenCalledWith('dev@test.local', 'Secret-Passw0rd'));
  });

  it('shows the server message on failure', async () => {
    rejectWith = new ApiError('Invalid email or password.', 401, 'INVALID_CREDENTIALS');
    renderWithProviders(<LoginPage />, { route: '/login' });
    await userEvent.type(screen.getByLabelText(/email/i), 'dev@test.local');
    await userEvent.type(screen.getByLabelText(/^password/i), 'wrong');
    await userEvent.click(screen.getByRole('button', { name: /sign in/i }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid email or password.');
  });
});

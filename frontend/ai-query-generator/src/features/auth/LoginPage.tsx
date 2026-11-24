import { useState } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { Alert, Box, Button, Card, CardContent, CircularProgress, IconButton, InputAdornment, TextField, Typography } from '@mui/material';
import { DatabaseZap, Eye, EyeOff } from 'lucide-react';
import { useFormik } from 'formik';
import * as yup from 'yup';
import { useAuth } from '../../auth/AuthContext';
import { toApiError } from '../../api/client';

const schema = yup.object({
  email: yup.string().trim().email('Enter a valid email address').required('Email is required'),
  password: yup.string().required('Password is required'),
});

export function LoginPage() {
  const { user, login, sessionExpired } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [error, setError] = useState<string | null>(null);
  const [showPassword, setShowPassword] = useState(false);
  const from = (location.state as { from?: string } | null)?.from ?? '/';

  const formik = useFormik({
    initialValues: { email: '', password: '' },
    validationSchema: schema,
    onSubmit: async (values) => {
      setError(null);
      try {
        await login(values.email.trim(), values.password);
        navigate(from, { replace: true });
      } catch (e) {
        setError(toApiError(e).message);
      }
    },
  });

  if (user) return <Navigate to={from} replace />;

  return (
    <Box sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center', p: 2, background: (t) => t.palette.mode === 'dark' ? 'radial-gradient(circle at 20% 20%, #1b2433, #0f1218)' : 'radial-gradient(circle at 20% 20%, #e3efff, #f4f6f9)' }}>
      <Card sx={{ width: '100%', maxWidth: 420 }}>
        <CardContent sx={{ p: 4 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, mb: 3 }}>
            <DatabaseZap size={32} aria-hidden />
            <Box>
              <Typography variant="h5" component="h1">AI SQL Query Generator</Typography>
              <Typography variant="body2" color="text.secondary">Sign in to continue</Typography>
            </Box>
          </Box>
          {sessionExpired && !error && <Alert severity="info" sx={{ mb: 2 }}>Your session expired. Please sign in again.</Alert>}
          {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
          <form onSubmit={formik.handleSubmit} noValidate>
            <TextField
              fullWidth
              size="medium"
              label="Email"
              name="email"
              type="email"
              autoComplete="username"
              value={formik.values.email}
              onChange={formik.handleChange}
              onBlur={formik.handleBlur}
              error={formik.touched.email && !!formik.errors.email}
              helperText={formik.touched.email && formik.errors.email}
              sx={{ mb: 2 }}
              autoFocus
            />
            <TextField
              fullWidth
              size="medium"
              label="Password"
              name="password"
              type={showPassword ? 'text' : 'password'}
              autoComplete="current-password"
              value={formik.values.password}
              onChange={formik.handleChange}
              onBlur={formik.handleBlur}
              error={formik.touched.password && !!formik.errors.password}
              helperText={formik.touched.password && formik.errors.password}
              sx={{ mb: 3 }}
              slotProps={{
                input: {
                  endAdornment: (
                    <InputAdornment position="end">
                      <IconButton onClick={() => setShowPassword(!showPassword)} aria-label={showPassword ? 'Hide password' : 'Show password'} edge="end">
                        {showPassword ? <EyeOff size={18} /> : <Eye size={18} />}
                      </IconButton>
                    </InputAdornment>
                  ),
                },
              }}
            />
            <Button type="submit" fullWidth size="large" variant="contained" disabled={formik.isSubmitting}>
              {formik.isSubmitting ? <CircularProgress size={22} color="inherit" /> : 'Sign in'}
            </Button>
          </form>
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 3, textAlign: 'center' }}>
            Accounts are created by an administrator.
          </Typography>
        </CardContent>
      </Card>
    </Box>
  );
}

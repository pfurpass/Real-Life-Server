import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';

export function Login() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      await login(username, password);
      navigate('/');
    } catch {
      setError('Anmeldung fehlgeschlagen. Benutzername oder Passwort falsch.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex min-h-screen items-center justify-center">
      <form onSubmit={submit} className="w-full max-w-sm space-y-4 rounded-lg bg-slate-900 p-8">
        <h1 className="text-xl font-bold">Real-Life-Server</h1>
        <p className="text-sm text-slate-400">Bitte melde dich an.</p>

        <div className="space-y-1">
          <label className="text-xs text-slate-500">Benutzername</label>
          <input
            className="w-full rounded bg-slate-800 px-3 py-2 text-sm outline-none focus:ring-1 focus:ring-blue-500"
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            autoFocus
          />
        </div>

        <div className="space-y-1">
          <label className="text-xs text-slate-500">Passwort</label>
          <input
            type="password"
            className="w-full rounded bg-slate-800 px-3 py-2 text-sm outline-none focus:ring-1 focus:ring-blue-500"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </div>

        {error && <p className="text-sm text-red-400">{error}</p>}

        <button
          type="submit"
          disabled={busy}
          className="w-full rounded bg-blue-600 px-3 py-2 text-sm font-medium hover:bg-blue-500 disabled:opacity-50"
        >
          {busy ? 'Anmelden…' : 'Anmelden'}
        </button>
      </form>
    </div>
  );
}

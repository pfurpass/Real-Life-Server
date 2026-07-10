import { NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';

export function Layout() {
  const { user, logout } = useAuth();

  return (
    <div className="flex min-h-screen">
      <aside className="w-56 shrink-0 border-r border-slate-800 bg-slate-900/50 p-4">
        <h1 className="mb-6 text-lg font-bold">Real-Life-Server</h1>
        <nav className="space-y-1 text-sm">
          <NavLink
            to="/"
            end
            className={({ isActive }) =>
              `block rounded px-3 py-2 ${isActive ? 'bg-slate-800 text-white' : 'text-slate-400 hover:bg-slate-800/50'}`
            }
          >
            Dashboard
          </NavLink>
        </nav>

        <div className="mt-8 border-t border-slate-800 pt-4 text-xs text-slate-500">
          <p>{user?.username}</p>
          <p className="mb-3">{user?.role}</p>
          <button onClick={logout} className="text-slate-400 hover:text-white">
            Abmelden
          </button>
        </div>
      </aside>

      <main className="flex-1 p-6">
        <Outlet />
      </main>
    </div>
  );
}

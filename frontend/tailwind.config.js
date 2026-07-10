/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  theme: {
    extend: {
      colors: {
        scene: {
          live: '#22c55e',
          degraded: '#eab308',
          reconnecting: '#f97316',
          brb: '#ef4444',
          offline: '#6b7280',
          connecting: '#3b82f6'
        }
      }
    }
  },
  plugins: []
};

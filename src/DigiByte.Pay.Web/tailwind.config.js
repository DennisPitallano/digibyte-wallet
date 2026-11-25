/** @type {import('tailwindcss').Config} */
module.exports = {
  // theme.js sets the `dark` class on <html>, the same mechanism as the wallet and the
  // shared style layer (_content/DigiByte.UI/css/ui.css).
  darkMode: 'class',
  content: [
    './**/*.razor',
    './**/*.html',
    './**/*.cs',
  ],
  theme: {
    extend: {
      fontFamily: {
        // Self-hosted by DigiByte.UI (same fonts as the wallet).
        sans: ['Inter', 'system-ui', '-apple-system', 'sans-serif'],
        mono: ['JetBrains Mono', 'ui-monospace', 'SFMono-Regular', 'Menlo', 'Consolas', 'monospace'],
      },
      // DigiByte brand blue palette — same as the wallet for visual continuity.
      colors: {
        dgb: {
          50: '#e6f0ff',
          100: '#b3d1ff',
          200: '#80b3ff',
          300: '#4d94ff',
          400: '#1a75ff',
          500: '#0066CC',
          600: '#0052a3',
          700: '#003d7a',
          800: '#002952',
          900: '#002352',
          950: '#001529',
        },
      },
    },
  },
  plugins: [],
};

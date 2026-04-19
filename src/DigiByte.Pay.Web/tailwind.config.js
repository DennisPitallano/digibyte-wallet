/** @type {import('tailwindcss').Config} */
module.exports = {
  // Align with our theme.js which sets data-theme="dark" on <html>.
  // 'selector' strategy (v3.4.1+) lets us pick any selector; we match
  // either manual (data-theme="dark") or, if nothing's set, the OS via media.
  darkMode: ['selector', '[data-theme="dark"]'],
  content: [
    './**/*.razor',
    './**/*.html',
    './**/*.cs',
  ],
  theme: {
    extend: {
      fontFamily: {
        sans: ['system-ui', '-apple-system', 'Segoe UI', 'sans-serif'],
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

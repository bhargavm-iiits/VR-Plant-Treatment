/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      // The From Field to Leaf tokens. src/index.css holds the same values as
      // custom properties; the component classes there are the main styling path.
      fontFamily: {
        display: ['"DM Serif Text"', 'Georgia', 'serif'],
        sans: ['"Instrument Sans"', 'system-ui', 'sans-serif'],
      },
      colors: {
        canvas:  '#F5F0E6',
        surface: '#FBF7EF',
        ink:     '#132620',
        muted:   '#4F5B51',
        action:  { DEFAULT: '#126456', hover: '#0E5146' },
        selection: '#DDEBE3',
        border:  '#D8D0C0',
        marker:  { DEFAULT: '#CC592C', ink: '#9A3D17' },
        caution: { DEFAULT: '#7A4717', surface: '#F6E9D5' },
        failure: '#8D362A',
      },
    },
  },
  plugins: [],
}

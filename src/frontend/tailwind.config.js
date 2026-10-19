/** @type {import('tailwindcss').Config} */
module.exports = {
  darkMode: "class",
  content: [
    "./src/**/*.{html,ts}",
  ],
  theme: {
    extend: {
      colors: {
        "primary": "#137fec",
        "background-light": "#ffffff",
        "background-dark": "#0a0a0a",
        "border-subtle": "#e5e7eb",
        "diff-added": "#ebfbee",
        "diff-added-text": "#2b8a3e",
        "diff-removed": "#fff5f5",
        "diff-removed-text": "#c92a2a",
        "diff-modified": "#e7f5ff",
        "diff-modified-text": "#1864ab",
      },
      fontFamily: {
        "display": ["Inter", "sans-serif"],
        "mono": ["JetBrains Mono", "monospace"],
      },
      borderRadius: {
        "DEFAULT": "0.25rem",
        "lg": "0.5rem",
        "xl": "0.75rem",
        "full": "9999px",
      },
      animation: {
        "shimmer": "shimmer 2s infinite linear",
      },
      keyframes: {
        shimmer: {
          "0%": { transform: "translateX(-100%)" },
          "100%": { transform: "translateX(100%)" },
        },
      },
    },
  },
  plugins: [require('@tailwindcss/typography')],
};

import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

// Em desenvolvimento, use subdomínios de *.localhost (resolvem para 127.0.0.1 sem DNS).
// O proxy mantém o Host original, que a API usa para identificar a superfície/escola.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { dedupe: ["react", "react-dom", "react-router"] },
  server: {
    host: "127.0.0.1",
    port: 5173,
    strictPort: true,
    allowedHosts: [".localhost"],
    proxy: {
      "/api": { target: "http://localhost:5200", changeOrigin: false },
    },
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    css: false,
    // Processa estas libs pelo Vite para usarem a mesma instância de React (evita cópia duplicada no Windows).
    server: { deps: { inline: [/lucide-react/, /recharts/, /react-router/, /@sinedu\/ui/, /@testing-library\/jest-dom/] } },
  },
});

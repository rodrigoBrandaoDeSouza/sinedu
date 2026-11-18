import { lazy, Suspense } from "react";

// Telas protegidas em chunks próprios: só baixadas por quem navega até elas (Direção/Pedagogia).
const PendingPage = lazy(() => import("./pending-page"));
const HealthImportPage = lazy(() => import("./health-import-page"));

const Loading = () => <div className="h-40 animate-pulse rounded-(--radius-card) border border-border bg-surface" aria-busy="true" />;

export function PendingRoute() {
  return (
    <Suspense fallback={<Loading />}>
      <PendingPage />
    </Suspense>
  );
}

export function HealthImportRoute() {
  return (
    <Suspense fallback={<Loading />}>
      <HealthImportPage />
    </Suspense>
  );
}

import { useQuery } from "@tanstack/react-query";
import { Button, Card, PageHeader, Select } from "@sinedu/ui";
import { Plus } from "lucide-react";
import { useState } from "react";
import { TenantTable } from "../components/tenant-table";
import { fetchOverview } from "../data/overview";

const statusOptions = [
  { value: "all", label: "Todas as situações" },
  { value: "ativa", label: "Ativa" },
  { value: "provisionando", label: "Provisionando" },
  { value: "suspensa", label: "Suspensa" },
];

export function TenantsPage() {
  const [status, setStatus] = useState("all");
  const { data } = useQuery({ queryKey: ["overview"], queryFn: fetchOverview });
  const tenants = (data?.tenants ?? []).filter((t) => status === "all" || t.status === status);

  return (
    <>
      <PageHeader
        title="Escolas"
        description="Cada escola tem subdomínio, banco de dados e chave de criptografia próprios."
        actions={
          <Button variant="primary">
            <Plus />
            Nova escola
          </Button>
        }
      />
      <div className="mb-4 flex flex-wrap gap-3">
        <Select label="Situação" options={statusOptions} value={status} onChange={(e) => setStatus(e.target.value)} className="w-56" />
      </div>
      <Card>
        <TenantTable tenants={tenants} />
      </Card>
    </>
  );
}

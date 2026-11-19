import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Badge, Button, Card, Dialog, EmptyState, Input, PageHeader, Select, Table, Td, Th, Tr } from "@sinedu/ui";
import { ClipboardList, Plus, Settings2 } from "lucide-react";
import { useState } from "react";
import { useNavigate, useSearchParams } from "react-router";
import {
  formatDateTime,
  occurrencesApi,
  occurrenceStatusLabels,
  occurrenceStatusTone,
  type OccurrenceStatus,
} from "../../lib/occurrences-api";
import { useHasRole } from "../../lib/session";
import { errorsOf } from "../protected/utils";

const statusOptions = [{ value: "", label: "Todas as situações" }, ...Object.entries(occurrenceStatusLabels).map(([value, label]) => ({ value, label }))];

export function OccurrencesPage() {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const director = useHasRole("DIRETOR");
  const [categoriesOpen, setCategoriesOpen] = useState(false);
  const filter = {
    status: (params.get("situacao") || undefined) as OccurrenceStatus | undefined,
    from: params.get("de") || undefined,
    to: params.get("ate") || undefined,
    page: Number(params.get("pagina") || 1),
  };
  const list = useQuery({ queryKey: ["occurrences", filter], queryFn: () => occurrencesApi.list(filter) });
  const set = (key: string, value: string) => {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value);
    else next.delete(key);
    next.delete("pagina");
    setParams(next, { replace: true });
  };

  return (
    <>
      <PageHeader
        title="Ocorrências"
        description="Registro institucional de fatos e providências. Não é diagnóstico nem rótulo do aluno."
        actions={
          <div className="flex gap-2">
            {director ? (
              <Button onClick={() => setCategoriesOpen(true)}>
                <Settings2 />
                Categorias
              </Button>
            ) : null}
            <Button variant="primary" onClick={() => navigate("/ocorrencias/nova")}>
              <Plus />
              Nova ocorrência
            </Button>
          </div>
        }
      />

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <Select label="Situação" value={filter.status ?? ""} onChange={(e) => set("situacao", e.target.value)} options={statusOptions} />
        <Input label="De" type="date" value={filter.from ?? ""} onChange={(e) => set("de", e.target.value)} className="w-40" />
        <Input label="Até" type="date" value={filter.to ?? ""} onChange={(e) => set("ate", e.target.value)} className="w-40" />
      </div>

      {list.data?.total === 0 ? (
        <EmptyState icon={ClipboardList} title="Nenhuma ocorrência" description="As ocorrências registradas aparecem aqui. Rascunhos ficam visíveis só para quem os criou." />
      ) : (
        <Card>
          <Table>
            <thead>
              <tr>
                <Th>Data do fato</Th>
                <Th>Aluno</Th>
                <Th>Turma do fato</Th>
                <Th>Categoria</Th>
                <Th>Situação</Th>
                <Th>Registro</Th>
              </tr>
            </thead>
            <tbody>
              {list.data?.items.map((o) => (
                <Tr key={o.id} className="cursor-pointer" onClick={() => navigate(`/ocorrencias/${o.id}`)}>
                  <Td className="tabular-nums text-foreground-secondary">{formatDateTime(o.occurredAt)}</Td>
                  <Td>
                    <p className="font-medium text-foreground">{o.studentName}</p>
                    {o.studentCode ? <p className="font-mono text-xs text-muted-foreground">CGM {o.studentCode}</p> : null}
                  </Td>
                  <Td>{o.classroom}</Td>
                  <Td>
                    {o.category}
                    {o.severity ? <span className="text-muted-foreground"> · {o.severity}</span> : null}
                  </Td>
                  <Td>
                    <Badge tone={occurrenceStatusTone[o.status]} icon={o.status !== "Rascunho"}>
                      {occurrenceStatusLabels[o.status]}
                    </Badge>
                  </Td>
                  <Td className="text-foreground-secondary">{o.createdByName}</Td>
                </Tr>
              ))}
            </tbody>
          </Table>
          {list.data && list.data.total > list.data.pageSize ? (
            <div className="flex items-center justify-between border-t border-border px-4 py-3 text-sm text-muted-foreground">
              <span>
                {list.data.total} ocorrências · página {list.data.page}
              </span>
              <div className="flex gap-2">
                <Button size="sm" disabled={list.data.page <= 1} onClick={() => setParams({ ...Object.fromEntries(params), pagina: String(list.data.page - 1) })}>
                  Anterior
                </Button>
                <Button
                  size="sm"
                  disabled={list.data.page * list.data.pageSize >= list.data.total}
                  onClick={() => setParams({ ...Object.fromEntries(params), pagina: String(list.data.page + 1) })}
                >
                  Próxima
                </Button>
              </div>
            </div>
          ) : null}
        </Card>
      )}

      {director ? <CategoriesDialog open={categoriesOpen} onClose={() => setCategoriesOpen(false)} /> : null}
    </>
  );
}

function CategoriesDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const client = useQueryClient();
  const categories = useQuery({ queryKey: ["occurrence-categories"], queryFn: occurrencesApi.categories, enabled: open });
  const [name, setName] = useState("");
  const [error, setError] = useState<string>();
  const refresh = () => client.invalidateQueries({ queryKey: ["occurrence-categories"] });
  const create = useMutation({
    mutationFn: () => occurrencesApi.createCategory(name.trim()),
    onSuccess: () => {
      setName("");
      setError(undefined);
      refresh();
    },
    onError: (err) => setError(errorsOf(err).name ?? errorsOf(err).form),
  });
  const toggle = useMutation({ mutationFn: (c: { id: string; name: string; isActive: boolean }) => occurrencesApi.updateCategory(c.id, c.name, !c.isActive), onSuccess: refresh });
  const [renaming, setRenaming] = useState<{ id: string; name: string; isActive: boolean }>();
  const rename = useMutation({
    mutationFn: (c: { id: string; name: string; isActive: boolean }) => occurrencesApi.updateCategory(c.id, c.name.trim(), c.isActive),
    onSuccess: () => {
      setRenaming(undefined);
      setError(undefined);
      refresh();
    },
    onError: (err) => setError(errorsOf(err).name ?? errorsOf(err).form),
  });

  return (
    <Dialog open={open} onClose={onClose} title="Categorias de ocorrência" description="Renomear ou desativar uma categoria não altera as ocorrências que já a usam.">
      <div className="space-y-4">
        <ul className="divide-y divide-border">
          {categories.data?.categories.map((c) =>
            renaming?.id === c.id ? (
              <li key={c.id} className="py-2">
                <form
                  className="flex items-end gap-2"
                  onSubmit={(e) => {
                    e.preventDefault();
                    rename.mutate(renaming);
                  }}
                >
                  <Input
                    label={`Novo nome para ${c.name}`}
                    value={renaming.name}
                    onChange={(e) => setRenaming({ ...renaming, name: e.target.value })}
                    className="flex-1"
                    maxLength={80}
                    autoFocus
                  />
                  <Button type="submit" variant="primary" disabled={renaming.name.trim().length < 2 || rename.isPending}>
                    Salvar
                  </Button>
                  <Button type="button" variant="ghost" onClick={() => setRenaming(undefined)}>
                    Cancelar
                  </Button>
                </form>
              </li>
            ) : (
              <li key={c.id} className="flex items-center justify-between gap-2 py-2">
                <span className={c.isActive ? "text-foreground" : "text-muted-foreground line-through"}>{c.name}</span>
                <span className="flex gap-1">
                  <Button size="sm" variant="ghost" onClick={() => setRenaming({ id: c.id, name: c.name, isActive: c.isActive })}>
                    Renomear
                  </Button>
                  <Button size="sm" variant="ghost" onClick={() => toggle.mutate(c)}>
                    {c.isActive ? "Desativar" : "Reativar"}
                  </Button>
                </span>
              </li>
            ),
          )}
        </ul>
        <form
          className="flex items-end gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            create.mutate();
          }}
        >
          <Input label="Nova categoria" value={name} onChange={(e) => setName(e.target.value)} className="flex-1" maxLength={80} />
          <Button type="submit" disabled={name.trim().length < 2}>
            Adicionar
          </Button>
        </form>
        {error ? <Alert tone="critical" title={error} /> : null}
      </div>
    </Dialog>
  );
}

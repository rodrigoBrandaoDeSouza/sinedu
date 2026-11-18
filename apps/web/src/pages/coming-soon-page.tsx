import { Badge, EmptyState, PageHeader } from "@sinedu/ui";
import { Hammer } from "lucide-react";

export function ComingSoonPage({ title, description, backlog }: { title: string; description: string; backlog: string }) {
  return (
    <>
      <PageHeader title={title} description={description} eyebrow={<Badge tone="outline">Backlog {backlog}</Badge>} />
      <EmptyState
        icon={Hammer}
        title="Em construção"
        description={`Esta área será entregue no item ${backlog} do backlog. A navegação já está pronta para recebê-la.`}
      />
    </>
  );
}

#!/usr/bin/env bash
# Copia os backups cifrados da homologação para FORA da VPS (este computador). Rodar no Git Bash:
#   infra/hml/pull-backups.sh                 # destino padrão: ~/.sinedu/backups-hml
#   DEST=/d/backups infra/hml/pull-backups.sh
# Os arquivos chegam cifrados (age). A chave privada fica em ~/.sinedu/hml-backup.agekey — guarde uma cópia
# num cofre de senhas: sem ela, nenhum backup pode ser restaurado.
set -euo pipefail
VPS=${VPS:-bem-vps}
DEST=${DEST:-$HOME/.sinedu/backups-hml}
mkdir -p "$DEST"

for dir in $(ssh "$VPS" 'ls -1 /var/backups/sinedu-hml'); do
  [ -f "$DEST/$dir/SHA256SUMS" ] && continue
  echo "[pull] $dir"
  mkdir -p "$DEST/$dir"
  ssh "$VPS" "tar -C /var/backups/sinedu-hml/$dir -cf - ." | tar -C "$DEST/$dir" -xf -
  (cd "$DEST/$dir" && sha256sum -c SHA256SUMS --quiet) || { echo "[pull] hash divergente em $dir"; exit 1; }
done
echo "[pull] ok: $(ls -1 "$DEST" | wc -l) backup(s) em $DEST"

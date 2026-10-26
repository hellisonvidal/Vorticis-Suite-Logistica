#!/usr/bin/env bash
# Envio enxuto para o GitHub (pensado para o Claude Code gastar poucos tokens):
#   scripts/enviar.sh "mensagem do commit"      build + testes + commit + push
#   scripts/enviar.sh --so-push                  só envia os commits já feitos
#   VERIFICAR=0 scripts/enviar.sh "msg"          pula build/testes (use só para mudanças de texto já testadas)
# Imprime só o resumo (erros, totais dos testes e o resultado do push). Saída completa em /tmp/vorticis-enviar.log.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
LOG=/tmp/vorticis-enviar.log
: > "$LOG"
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 PATH="$HOME/.dotnet:$PATH"
ramo=$(git branch --show-current)
[ -n "$ramo" ] || { echo "ERRO: HEAD destacado; troque para um ramo."; exit 1; }
case "$ramo" in main|master) echo "ERRO: não envie direto para $ramo; crie um ramo (claude/...)."; exit 1;; esac

if [ "${1:-}" != "--so-push" ]; then
  msg="${1:?informe a mensagem do commit}"
  if [ "${VERIFICAR:-1}" = "1" ]; then
    if ! dotnet build Vorticis.slnx -c Release -v q >>"$LOG" 2>&1; then
      grep -E " error " "$LOG" | sed 's/^.*\/src\//src\//;s/^.*\/tests\//tests\//' | sort -u | head -15; echo "BUILD FALHOU (detalhes em $LOG)"; exit 1
    fi
    if ! dotnet test Vorticis.slnx -c Release --no-build >>"$LOG" 2>&1; then
      grep -E "^\s+Failed |Error Message" -A2 "$LOG" | head -30; grep -E "Passed!|Failed!" "$LOG"; echo "TESTES FALHARAM"; exit 1
    fi
    grep -E "Passed!|Failed!" "$LOG" | sed 's/ - .*\/\([^/]*\.dll\).*/ \1/;s/, Duration.*//'
    if [ -n "$(git status --porcelain -- db)" ]; then echo "(db/ mudou: rode também os testes SQL descritos no CLAUDE.md)"; fi
  fi
  git add -A
  if git diff --cached --quiet; then echo "Nada para commitar."; else git commit -q -m "$msg"; git log --oneline -1; fi
fi

# push com o ramo atualizado (pacote fino) e até 4 novas tentativas em falha de rede
git fetch -q origin "$ramo" 2>/dev/null || true
for espera in 0 2 4 8 16; do
  sleep "$espera"
  if git push -q -u origin "$ramo" >>"$LOG" 2>&1; then
    # clone raso só acompanha o main: cria a referência do ramo remoto para o upstream ficar registrado
    git fetch -q origin "$ramo:refs/remotes/origin/$ramo" 2>/dev/null && git branch -q -u "origin/$ramo" 2>/dev/null || true
    echo "Enviado: $ramo ($(git rev-parse --short HEAD))"; exit 0; fi
done
tail -5 "$LOG"; echo "PUSH FALHOU"; exit 1

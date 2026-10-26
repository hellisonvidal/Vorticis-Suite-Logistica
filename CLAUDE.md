# Vorticis Suite Logística — regras do projeto

C# .NET 10, Avalonia (MVVM), PostgreSQL. Produto único: `src/Vorticis.Logistica.Desktop` (Frota, Gerencial, Contábil, Fiscal e Folha num menu só); os módulos são bibliotecas sobre o casco `src/Vorticis.Desktop.Comum` (`IModuloDesktop`).

## Regra principal

**Toda função nova ou alterada deve ter o manual atualizado em `docs/manual/` (e o mapa de F1, `docs/manual/mapa.txt`, se houver) no mesmo commit.**
O manual é embutido no casco; `ManualTests` falha se existir tela registrada sem tópico no mapa. Escreva para o contador (usuário final): passo a passo, linguagem simples, sem jargão técnico.

## Regras práticas

- **Migrações imutáveis:** nunca edite um `db/NNN_*.sql` já commitado; crie outro arquivo com o próximo número livre e confira a última antes de numerar. As faixas antigas (core/contábil 001–029, fiscal 030–039 e 046–048, folha 040–045) estão esgotadas; a partir da 049 a numeração é única e sequencial para todos os módulos (a ordem de aplicação é a do número).
- **Testes obrigatórios:** `dotnet build Vorticis.slnx -c Release` e `dotnet test` em todos os projetos de `tests/` antes de commitar; toda regra nova ganha teste. Para rodar os de integração: `VORTICIS_TEST_PG="Host=localhost;Username=postgres;Password=postgres;Database=postgres"`. Ao mexer em migrações ou regras do banco, rode também os testes SQL do CI: `createdb vorticis_sql && cat db/0*.sql db/tests/*.sql | psql -d vorticis_sql -v ON_ERROR_STOP=1 -q` (deve terminar em "TODOS OS TESTES PASSARAM").
- **Interface em português do Brasil:** textos, mensagens, menus e documentação; nomes de tela e botões iguais aos do manual.
- **Permissões nos serviços**, não só no menu; regras legais (alíquotas, tabelas) em `core.parametro_legal`, nunca no código.
- **Casco comum:** telas e cadastros usados por mais de um módulo ficam em `Vorticis.Desktop.Comum`; cada tela nova se registra em `RegistrarTelas` com chave única.
- **Economia de uso:** leia o mínimo necessário (README, `docs/`, definições de menu e views), use busca (`grep`) em vez de abrir arquivos grandes e evite reescrever o que já funciona.

## Modelos e consumo (Claude Code)

`.claude/settings.json` fixa `opusplan`: **Opus só no modo de planejamento** (Shift+Tab até "plan mode") e **Sonnet na execução**; subagentes (Explore, busca) rodam em **Haiku**. Raciocínio limitado a 8 mil tokens.
- Tarefa grande ou de arquitetura (módulo novo, migração complexa): planeje em plan mode e execute fora dele.
- Correção pontual, texto do manual, testes: fique no Sonnet; `/model haiku` para tarefas mecânicas (renomear, ajustar textos).
- Não releia arquivos já lidos na sessão; filtre saídas longas (`| tail -20`, `grep`); rode só o projeto de testes afetado durante o desenvolvimento e a suíte completa antes do commit.
- Use `/compact` ao trocar de assunto e `/clear` ao terminar uma tarefa.
- **Pacotes antes de acabar o consumo:** ao perceber que os dados/tokens vão acabar, interrompa o que estiver fazendo, faça commit e push do que já funciona (build e testes verdes): o CI da branch gera o instalador `Setup-VorticisSuiteLogistica` (programa único da transportadora) e o portátil `VorticisSuiteLogistica-portatil` na aba *Actions* (guardados por 3 dias).
- Trabalhe uma etapa por vez (um módulo/uma correção por commit).

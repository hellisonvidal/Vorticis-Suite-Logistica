# 01 — Arquitetura

## 1. Decisões de stack (custo zero de licença)

| Camada | Escolha | Por quê | Alternativa considerada |
|---|---|---|---|
| Linguagem/backend | **C# / .NET 10 (LTS)** | Tipo `decimal` nativo (128 bits, sem erro de arredondamento), suporte de primeira classe a XML, XSD e **assinatura XMLDSig** (`System.Security.Cryptography.Xml`), acesso nativo ao repositório de certificados do Windows (A1/A3 ICP-Brasil), ecossistema brasileiro de DF-e maduro | Python (ótimo p/ scripts, fraco para assinatura A3 e distribuição desktop) |
| Interface | **Avalonia UI** (MIT) | Desktop nativo no Windows hoje, roda em Linux/macOS amanhã sem reescrever | WPF (só Windows), Tauri + React (dois runtimes) |
| Banco | **PostgreSQL 16+ local** | Gratuito; *constraint triggers* diferidos (validação de partidas dobradas no COMMIT), `NUMERIC` exato, JSONB, robusto para multiusuário quando migrar para servidor/LAN | SQLite (sem triggers diferidos, concorrência fraca; serve só para modo "portátil") |
| Acesso a dados | EF Core (cadastros) + **Dapper/SQL** (relatórios e SPED) | Relatórios contábeis são agregações pesadas — SQL direto é mais rápido e auditável | — |
| NF-e/NFC-e/CT-e | **DFe.NET (Zeus)** (LGPL) | Classes, schemas e assinatura prontos | ACBr (Delphi, via ACBrLib) |
| Excel | **ClosedXML** (MIT) | Importação/exportação `.xlsx` | — |
| PDF/relatórios | **QuestPDF** (licença Community gratuita até US$ 1 mi de receita/ano) | Layout em código, versionável | FastReport OpenSource |
| Instalador | Inno Setup | Um único Setup.exe com o app e os binários do PostgreSQL; o app cria (initdb), inicia e para o banco local sozinho (`ServidorPostgresEmbutido`) | — |

**Princípio:** a aplicação desktop fala com uma **camada de serviços em processo** (não um servidor HTTP) no MVP. As interfaces de serviço já são desenhadas para virar uma API REST depois, sem mudar o domínio.

## 2. Arquitetura em camadas (monólito modular)

```
┌──────────────────────────────────────────────────────────────────┐
│ Vorticis.Desktop.Comum + executáveis dos módulos (Avalonia, MVVM) │
├──────────────────────────────────────────────────────────────────┤
│ Vorticis.Application  — casos de uso (EfetivarLancamento,          │
│                        ImportarNFe, GerarECD, CalcularFolha...)  │
├──────────────┬───────────────┬──────────────┬────────────────────┤
│ Módulo       │ Módulo        │ Módulo       │ Módulo             │
│ Contábil     │ Fiscal        │ Banco/       │ Folha              │
│ (Domínio)    │ (DF-e, apur.) │ Conciliação  │ (eSocial)          │
├──────────────┴───────────────┴──────────────┴────────────────────┤
│ Vorticis.Sped  — geradores de arquivo ECD/ECF/EFD (leiaute em     │
│                 código + validação de registros)                  │
│ Vorticis.Gov   — clientes SOAP/REST: eSocial, EFD-Reinf, SEFAZ,    │
│                 ADN NFS-e; assinatura com certificado A1/A3       │
├──────────────────────────────────────────────────────────────────┤
│ Vorticis.Infra — PostgreSQL, arquivos, backup, logs, criptografia  │
└──────────────────────────────────────────────────────────────────┘
```

Regras de dependência:
- **Só o módulo Contábil grava lançamentos.** Fiscal, Banco e Folha emitem *propostas de lançamento* (`LancamentoProposto`) que passam pelo mesmo `ServicoLancamento.Efetivar()` — um único ponto de validação.
- Cada módulo é um schema PostgreSQL (`core`, `contabil`, `fiscal`, `banco`, `folha`, `gov`).
- Tabelas legais (alíquotas, faixas, tetos, códigos de tabelas do eSocial/SPED) vivem em `core.parametro_legal` com vigência — **nada de valor legal no código**.

### 2.1 Suíte de executáveis (Contábil, Fiscal, Folha)

O produto é uma **suíte de três executáveis** sobre o mesmo banco, login, empresas e configurações (`%APPDATA%\Vorticis`, `%LOCALAPPDATA%\Vorticis`):

```
Vorticis.Desktop (VorticisContabil)  Vorticis.Fiscal.Desktop (VorticisFiscal)  Vorticis.Folha.Desktop (VorticisFolha)
        │  telas contábeis                   │  telas fiscais                       │  telas da folha
        └──────────────────┬─────────────────┴──────────────────────────────────────┘
                 Vorticis.Desktop.Comum  — casco: App/bootstrap, janela principal (menu fornecido pelo módulo),
                 login, banco local, empresas, contadores, clientes/fornecedores, usuários, perfis, certificados,
                 configurações, backup, licença, sobre, controles e utilitários de tela
                           │
        Vorticis.Fiscal ── Vorticis.Folha ── (regras/serviços/repositórios de cada módulo; Folha implementada, com `ServicosFolha.De(VorticisApp)` como raiz de composição)
                           │
              Vorticis.Application / Vorticis.Domain / Vorticis.Infra   (compartilhados; Infra tem o migrador e `VorticisApp`)
```

**Ponto de extensão.** Cada executável implementa `IModuloDesktop` (`Vorticis.Desktop.Comum.Modulos`) e herda `AppBase`/`MainViewModelBase`:

| Membro | Uso |
|---|---|
| `Nome`, `NomeCurto`, `Subtitulo`, `NomeExecutavel` | Título da janela, login, tela inicial e Sobre |
| `CorDestaque`, `CorBarra` | Identidade visual do módulo (`#RRGGBB`): faixa e selo sob/ao lado dos menus, nome no login e na tela inicial, cabeçalho das janelas de função. Contábil `#1B7F5A`/`#14532D`, Fiscal `#D97706`/`#78350F`, Folha `#7E22CE`/`#3B0764`, Escritório `#1D4ED8`/`#1E3A8A`, Gerencial `#0891B2`/`#164E63`, Condomínio `#BE123C`/`#881337`. O ícone é `Assets/<NomeExecutavel>.ico` (fonte em `instalador/icone`, gerado por `gerar_icone.py` com as mesmas cores e a sigla do módulo); o instalador usa `VorticisInstalador.ico` |
| `ItensCadastros` | Itens do módulo no menu Cadastros (depois de Empresas, Contadores, Clientes, Fornecedores) |
| `Menus` | Menus do módulo, entre Cadastros e Configurações; item = `ItemMenu.Tela(titulo, chave)`, `Submenu`, `Separador` ou `EmBreve` (desabilitado) |
| `Atalhos` | Atalhos de teclado (`AtalhoTela(gesto, chave, descrição)`), listados na tela Sobre |
| `RegistrarTelas(CatalogoTelas)` | `catalogo.Instancia(nome, m => new XViewModel(m), atualizarPermissoes:, aoLimpar:, aoTrocarEmpresa:).Tela(chave, título, pode:, aoAbrir:)` |

O casco mantém os menus **Cadastros comuns**, **Configurações** e **Ajuda**; o login, a empresa ativa, as permissões, as janelas de função e as confirmações ficam em `MainViewModelBase`. Telas novas: ViewModel + View (`UserControl`) no projeto do módulo, um `DataTemplate` no `App.axaml` do módulo e o registro em `RegistrarTelas`. Telas comuns a vários módulos entram no casco (e em `TemplatesComuns`).

**Banco compartilhado.** O servidor local sobe com o primeiro módulo aberto e só é parado pelo último a fechar (marcas travadas em `%LOCALAPPDATA%\Vorticis\execucao`).

**Migrações centralizadas.** Todas ficam em `db/` (um único migrador; toda base recebe todos os esquemas, independentemente dos módulos instalados). Faixas reservadas: **001–024** núcleo e contábil (em uso), **025–029** ECD/ECF, **030–039** Fiscal, **040–049** Folha de Pagamento.

## 3. Modelo de dados

O DDL executável do núcleo e do módulo contábil está em [`db/001_core_contabil.sql`](../db/001_core_contabil.sql), com testes em [`db/tests/`](../db/tests/).

### 3.1 Núcleo e contábil (implementado em SQL)

| Tabela | Finalidade | Registro SPED correspondente |
|---|---|---|
| `core.empresa` / `core.estabelecimento` | CNPJ, NIRE, município IBGE, IE/IM | ECD 0000, 0020 |
| `core.empresa_regime` | Regime tributário **por vigência** | ECF 0010 |
| `core.participante` | Clientes, fornecedores, sócios, bancos | ECD 0150/0180 |
| `core.parametro_legal` | Alíquotas/faixas/tetos versionados (JSONB) | — |
| `core.usuario`, `core.usuario_empresa` | Acesso por perfil e empresa | ECD J930 (signatários) |
| `core.auditoria` | Trilha de alterações com hash encadeado | — |
| `contabil.exercicio` / `contabil.periodo` | Exercício social e meses abertos/fechados | ECD 0000 (DT_INI/DT_FIN) |
| `contabil.plano_referencial` | Plano referencial RFB por ano e tipo | Tabelas dinâmicas do PVA |
| `contabil.plano_contas` | Plano da empresa (sintéticas/analíticas) | ECD I050 |
| `contabil.mapeamento_referencial` | De/para conta → referencial | ECD I051 |
| `contabil.centro_custo` | Centros de custo hierárquicos | ECD I100 |
| `contabil.historico_padrao` | Históricos reutilizáveis | ECD I075 |
| `contabil.lancamento` | Cabeçalho do lançamento | ECD I200 |
| `contabil.partida` | Linhas débito/crédito | ECD I250 |
| `contabil.saldo_mensal` | Saldos consolidados por mês | ECD I155 / J100 / J150 |

### 3.2 Módulos seguintes (próximas migrações)

```sql
-- fiscal: documentos eletrônicos importados
fiscal.documento        (id, empresa_id, modelo /*55,65,57,NFSe*/, chave_acesso CHAR(44) UNIQUE,
                         tipo_operacao /*E|S*/, emitente_id, destinatario_id, data_emissao,
                         data_entrada_saida, valor_total, situacao /*autorizada|cancelada|denegada*/,
                         xml_original BYTEA /*gzip*/, sha256 CHAR(64), lancamento_id)
fiscal.documento_item   (documento_id, n_item, cod_produto, ncm, cest, cfop, unidade, qtd, vl_unit,
                         vl_total, cst_icms, vl_bc_icms, vl_icms, vl_icms_st, cst_ipi, vl_ipi,
                         cst_pis, vl_pis, cst_cofins, vl_cofins,
                         -- Reforma Tributária (LC 214/2025): grupo IBSCBS da NF-e
                         cst_ibs_cbs, c_class_trib, vl_bc_ibs_cbs, aliq_ibs_uf, aliq_ibs_mun,
                         vl_ibs, aliq_cbs, vl_cbs, vl_is)
fiscal.regra_contabilizacao (empresa_id, prioridade, filtro JSONB /*cfop, ncm, participante, cst*/,
                         conta_debito_id, conta_credito_id, centro_custo_id, historico_padrao_id)

-- banco: extratos e conciliação
banco.conta_bancaria    (id, empresa_id, banco_compe, agencia, conta, conta_contabil_id)
banco.extrato_importacao(id, conta_bancaria_id, arquivo_nome, sha256 UNIQUE, formato /*OFX|CSV|XLSX*/,
                         periodo_ini, periodo_fim, saldo_final_informado)
banco.transacao         (id, conta_bancaria_id, fitid, data, valor /*com sinal*/, memo, documento,
                         status /*PENDENTE|SUGERIDA|CONCILIADA|IGNORADA*/,
                         UNIQUE (conta_bancaria_id, fitid))
banco.regra_classificacao(id, empresa_id, prioridade, condicoes JSONB, acao JSONB, ativo,
                         origem /*USUARIO|APRENDIDA*/, acertos, erros)
banco.conciliacao       (transacao_id, lancamento_id, tipo /*AUTO|MANUAL*/, usuario_id, em)

-- folha
folha.trabalhador       (id, empresa_id, cpf, nome, data_nasc, sexo, raca_cor, grau_instr, pis_nis,
                         endereco JSONB, dependentes -> folha.dependente)
folha.vinculo           (id, trabalhador_id, matricula, categoria_esocial /*101,103,721...*/,
                         data_admissao, tipo_regime_trab, cbo, salario_base, unidade_salario,
                         jornada JSONB, sindicato_id, data_desligamento, motivo_deslig)
folha.rubrica           (id, empresa_id, codigo, descricao, tipo /*P|D|I*/,
                         natureza_esocial /*Tabela 03*/, incid_cp, incid_irrf, incid_fgts,
                         formula, conta_debito_id, conta_credito_id)
folha.periodo_folha     (id, empresa_id, competencia, tipo /*MENSAL|13º|FERIAS|RESCISAO*/, status)
folha.calculo_item      (periodo_id, vinculo_id, rubrica_id, referencia, valor, memoria_calculo JSONB)
folha.afastamento, folha.ferias, folha.rescisao, folha.sindicato_cct (pisos, adicionais por CCT)

-- gov: mensageria com órgãos
gov.evento              (id, empresa_id, sistema /*ESOCIAL|REINF|NFSE|SEFAZ*/, tipo /*S-1200, R-4020...*/,
                         id_evento, referencia_id, xml_assinado, protocolo, recibo, status, erros JSONB,
                         enviado_em, retornado_em)
```

## 4. Fluxos principais

```
XML NF-e ──► valida schema/assinatura ──► dedup por chave ──► fiscal.documento
                                                     │
                                regras de contabilização (CFOP/NCM/participante)
                                                     ▼
OFX/CSV ──► dedup por FITID ──► motor de regras ──► LancamentoProposto ──► revisão (UI)
                                                     │                    (se confiança < limiar)
Folha calculada ──► provisões/encargos ─────────────►│
                                                     ▼
                                    ServicoLancamento.Efetivar()  ◄── único ponto de gravação
                                                     ▼
                     contabil.lancamento/partida ──► saldo_mensal ──► Diário, Razão, Balancete,
                                                                      BP, DRE, DMPL/DLPA, ECD, ECF
```

## 5. Segurança e operação local

- **Usuários**: senha com PBKDF2-HMAC-SHA256 (600 mil iterações, sal aleatório, comparação em tempo constante; o nº de iterações fica no hash para permitir aumento futuro). Bloqueio após 5 falhas por 15 min; mensagens de erro não revelam se o login existe; registro de todas as tentativas (`core.registro_acesso`); banco impede remover o último administrador ativo. Perfis configuráveis (`core.perfil`, `core.perfil_permissao`) com catálogo de funções em `Permissoes.Catalogo`; o `Autorizador` consulta as permissões efetivas no banco e **todos os serviços** (lançamento, estorno, saldos iniciais, períodos, plano de contas, centros de custo, relatórios) verificam antes de executar; administração (empresas, usuários, perfis, backup) é verificada pela flag de administrador.
- **Troca de usuário**: ao sair, todas as telas são recriadas, para que nada consultado por um usuário fique visível ao próximo.
- **Consulta de CNPJ**: `IConsultaCnpj` (Application) com implementação sobre a base pública da RFB (BrasilAPI → Minha Receita). Os dados apenas pré-preenchem o formulário; a origem e a data da consulta ficam gravadas na empresa (`fonte_dados`, `dados_consultados_em`).
- **Certificado digital**: A1 (`.pfx`) cifrado com DPAPI do Windows; A3 via Windows Certificate Store (token/cartão). Nunca gravar senha do PFX em texto.
- **Conexão**: `%APPDATA%\Vorticis\vorticis.json`, senha cifrada com DPAPI (escopo do usuário do Windows).
- **Banco**: PostgreSQL escutando só em `localhost`; usuário da aplicação sem permissão de `TRUNCATE`/`DROP`; papel separado para migrações.
- **Backup**: `pg_dump` agendado diário + antes de todo fechamento de período/geração de SPED; retenção local + cópia externa (pasta sincronizada pelo usuário). Restauração testável pela própria UI.
- **LGPD**: dados de trabalhadores (CPF, saúde em atestados/ASO, dependentes) com acesso por perfil, mascaramento em telas de consulta e registro de acesso.
- **Atualização de tabelas legais**: pacote de dados assinado (JSON) separado do executável, para publicar nova tabela de INSS/IRRF ou novo plano referencial sem reinstalar.

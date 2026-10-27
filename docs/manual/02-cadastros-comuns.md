# Cadastros comuns

Estes cadastros existem em todos os módulos e têm o mesmo código em todos eles. Todos usam a barra **Incluir · Alterar · Excluir · Salvar · Cancelar**, mostram o **Nº** de cada registro e a lista dos cadastrados à direita. As datas, CNPJ, CPF e CEP são formatados enquanto você digita.

## Empresas

**Para que serve:** cadastrar as empresas atendidas. Só o administrador cria e altera.
**Onde fica:** *Cadastros › Empresas*.

### Passo a passo
1. Clique em **Incluir**.
2. Digite o **CNPJ** (aceita o novo CNPJ alfanumérico) e clique em **Buscar dados na Receita**: razão social, natureza jurídica, CNAE, endereço, início de atividade e opção pelo Simples/MEI são preenchidos. Revise os dados. Os sócios (QSA) também são incluídos.
3. Confira a **inscrição estadual** (digite só números ou ISENTO). **Buscar IE (SINTEGRA)** procura o número; o sistema sempre pergunta antes de usá-lo. Se não encontrar, grava ISENTO. **Abrir SINTEGRA** abre o portal para conferência manual.
4. Preencha o **NIRE** (11 dígitos), endereço e contatos.
5. Em **Módulos**, marque onde a empresa será usada (Contábil, Fiscal, Folha de Pagamento). A lista de empresas no topo de cada programa mostra só as empresas ativas naquele módulo.
6. Em **Escrituração**, escolha o **regime tributário** (tem histórico por ano; é definido na inclusão) e o primeiro exercício.
7. **Salvar**.

### Logo da empresa (sai em todos os relatórios)
No Vorticis Suite Logística, este cadastro se chama **Transportadoras**. O logo enviado aqui sai no cabeçalho, à esquerda do nome da empresa, em **todos os relatórios em PDF** dela: relatórios da frota, do TMS e do contábil, CT-e, faturas, livros etc.
1. Selecione a empresa na lista da direita (o logo é gravado na empresa selecionada).
2. Na aba **Dados cadastrais**, em **Logo da empresa**, clique em **Enviar logo...** e escolha a imagem no computador: arquivo **PNG** ou **JPG** de até **1 MB** (de preferência com fundo branco ou transparente).
3. A prévia aparece ao lado e o logo já vale para os próximos relatórios; não precisa clicar em Salvar.
4. Para trocar, envie outro arquivo; para tirar, clique em **Remover logo** e confirme.

Só o administrador troca ou remove o logo.

### Abas da empresa
- **Dados cadastrais**: tudo acima, inclusive o logo.
- **Sócios** e **Contadores**: iguais às telas [Sócios](#socios) e [Contadores](#contadores).
- **Contabilidade**: modelo e máscara do plano de contas, centros de custo, início da escrituração, regras dos lançamentos e contas do encerramento (veja abaixo).

### Aba Contabilidade (campos principais)
| Campo | Para que serve |
|---|---|
| Modelo do plano | 13 modelos prontos (comércio, indústria e serviços por regime, ITG 1000, entidade sem fins lucrativos, completo). "Plano próprio" não cria contas. |
| Máscara dos códigos | Ex.: `9.9.99.999`; usada na importação e na conversão do plano. |
| Usar centros de custo / máscara | Liga os centros de custo e define o formato dos códigos. |
| Início da escrituração | Data a partir da qual a empresa é escriturada no sistema. |
| Exigir nº do documento | Cada partida do lançamento precisa do número do documento (saldos iniciais, encerramento e estornos ficam isentos). |
| Bloquear lançamentos até | Recusa lançamentos com data até a data informada. |
| Usar estorno de lançamentos | Ligado (padrão): lançamentos gravados são corrigidos por estorno. Desligado: não há estorno; **Editar** altera o próprio lançamento e **Excluir** o remove (sempre com registro na Auditoria). Veja o tópico Lançamentos. |
| Plano com participantes | Clientes e fornecedores ficam dentro das contas contábeis (veja [Participantes](#clientes-e-fornecedores-participantes)). |
| Grande porte, apuração do IRPJ, regime de caixa | Informações usadas na ECD e nas apurações. |
| Contas do encerramento | Apuração do resultado, lucros e prejuízos acumulados, usadas como padrão em *Encerramento do exercício*. |

### Regras e cuidados
- **Excluir uma empresa apaga toda a escrituração dela** (lançamentos, plano, períodos, sócios, acessos). O sistema avisa e **gera um backup automático antes**, informando onde ficou.
- Não é possível trocar o plano de contas depois que existem lançamentos.
- A consulta de CNPJ usa a base pública da Receita (via BrasilAPI e Minha Receita); empresas muito recentes podem não constar. A consulta de IE permite 3 consultas por minuto.

## Parâmetros

**Para que serve:** reunir, numa tela só, as configurações do módulo para a **empresa ativa**. Cada grupo tem um texto curto de ajuda. Nada é guardado em duplicidade: o que você muda aqui é o mesmo que aparece nas telas próprias.

**Onde fica:** *Cadastros › Parâmetros* (em todos os módulos).

**Como usar:**
1. Escolha a empresa no topo da janela e abra *Cadastros › Parâmetros*.
2. Altere os campos desejados. Os botões de cada grupo (por exemplo, *Abrir Encerramento do exercício*) levam à tela completa daquela configuração.
3. Clique em **Salvar**. *Descartar alterações* volta aos valores gravados.

No Vorticis Suite Logística a tela tem uma aba para cada área do módulo e a aba *Menus*; veja [Parâmetros do programa](13-logistica.md#parametros).

Se o seu perfil não permite alterar, a tela só mostra os valores. Se você apertar Esc com alterações não salvas, o sistema pergunta antes de fechar.

### Contábil
| Grupo | O que tem |
|---|---|
| Lançamentos | Usar estorno de lançamentos e exigir número do documento nas partidas. O bloqueio por data fica em *Cadastros › Bloqueio de lançamentos*. |
| Centros de custo e contabilidade gerencial | Usar centros de custo; **Contabilidade gerencial**: ligado, aparece o menu *Relatórios › Gerenciais*; desligado, ele fica escondido. |
| Participantes | Plano de contas com participantes. |
| Tipo de contabilização | **On-line** (o lançamento sai na hora, o padrão) ou **Em lote** (os módulos geram pendências e você contabiliza em *Integrações › Exportar › Contabilização em lote*). Veja [Contabilização em lote](03-contabil.md#contabilizacao-em-lote). |
| Encerramento do exercício | Contas de apuração do resultado, de lucros e de prejuízos acumulados. |
| SPED Contábil (ECD) e ECF | Empresa de grande porte. |

### Fiscal
| Grupo | O que tem |
|---|---|
| Lançamento das notas | Informar centro de custo nos itens das notas; **Contabilizar as notas on-line** (ao gravar ou importar) ou em lote (pela função *Contabilizar notas fiscais*, que só aparece neste caso). |
| Duplicatas e baixas | Gerar duplicata à vista, dias do vencimento à vista, contas de fornecedores, clientes, juros, multa e descontos, e o histórico dos lançamentos de pagamento e recebimento. |
| EFD ICMS/IPI | Perfil, atividade, códigos de receita e dia de vencimento do ICMS (o assistente já abre preenchido). |
| EFD-Contribuições | Natureza da pessoa jurídica e atividade. |
| Contabilização e tributos | Atalhos para *Regras de contabilização*, *Contas dos tributos* e *Fechamento do período fiscal*. |

## Importar cadastros de planilha (Excel)

**Para que serve:** trazer de uma vez os cadastros que você já tem numa planilha (de outro sistema ou feita à mão), sem digitar um por um.

**Onde fica:** no próprio grupo do cadastro, no menu *Cadastros*: cada grupo tem o item **Importar de planilha (Excel)** só com os cadastros dele. No Vorticis Suite Logística, cada módulo tem o seu, com os cadastros dele: clientes e fornecedores em todos; veículos, motoristas, frotas e rotas no **Frota**; tabelas de frete no **Transporte**; produtos e regras fiscais no **Gerencial**; centros de custo e históricos padrão na **Contabilidade**. O plano de contas tem importação própria (*Integrações › Importar › Plano de contas*).

**Como usar:**
1. Escolha o cadastro no menu. Se ainda não tem planilha, clique em **Gerar planilha modelo**: o sistema cria um .xlsx com as colunas do cadastro e uma linha de exemplo.
2. Clique em **Escolher planilha...** e selecione o arquivo (Excel **.xlsx** ou **.csv**). A primeira linha precisa ser o cabeçalho, com o nome de cada coluna.
3. **Adapte o layout à sua planilha:** à esquerda aparece cada campo do cadastro e a coluna da planilha ligada a ele. O sistema já liga as colunas de nome parecido (por exemplo, *CNPJ*, *CPF/CNPJ* ou *Documento* para o CPF/CNPJ). Troque o que for preciso; *(não importar)* deixa o campo em branco. Os campos com **\*** são obrigatórios.
4. Confira a **prévia** (as primeiras linhas como vão entrar) e clique em **Importar**.
5. Cada linha é gravada com as mesmas regras da tela do cadastro (CPF/CNPJ, placa, CFOP etc. são conferidos). As linhas com erro ficam de fora e aparecem com o número da linha e o motivo; corrija a planilha e importe só elas de novo.

**Dicas:**
- O **layout fica gravado** para a empresa: na próxima planilha do mesmo cadastro, as colunas já vêm escolhidas (use **Gravar layout** para guardá-lo sem importar).
- **Código** em branco: o sistema numera a partir do maior código existente.
- Tipos, combustíveis e afins aceitam o código ou o nome que aparece na tela (ex.: *Diesel S10* ou *DIESEL_S10*; *Cavalo*; *Própria*). Sim/não aceita *S*, *Sim*, *X* ou *1*.
- Rotas: informe a UF e o município de origem e de destino (o nome ou o código IBGE); sem distância, o sistema estima como na tela de rotas.
- Cadastros já existentes (mesmo CPF/CNPJ, placa ou código) não são duplicados: a linha aparece com o aviso.

## Clientes e fornecedores (participantes)

**Para que serve:** cadastrar quem compra e quem vende. A mesma pessoa pode ser cliente e fornecedor (são menus separados).
**Onde fica:** *Cadastros › Clientes* e *Cadastros › Fornecedores*.

1. **Incluir** e digite CPF ou CNPJ (validado); **Buscar dados na Receita** preenche o restante.
2. Complete nome, inscrição estadual (ou ISENTO), endereço (o CEP preenche o endereço) e contatos.
3. Em **Contabilidade**, informe a **conta contábil** (código ou reduzido; opcional).
4. **Salvar**.

**Participante na conta:** com a opção "plano com participantes" ligada na empresa, informe a conta (ex.: Clientes) e marque **Participante**. A conta passa a exigir o participante em cada lançamento, a tela de lançamento ganha a coluna *Part.* e o balancete pode **detalhar participantes**.

## Contas bancárias

**Para que serve:** cadastrar as contas da empresa nos bancos, para indicar onde cai o dinheiro de cada forma de pagamento e qual conta contábil representa o banco.
**Onde fica:** *Cadastros › Bancos* no Contábil; *Cadastros › Contas bancárias* no Fiscal e no Gerencial. O formulário fica em cima e a lista embaixo.

1. Clique em **Incluir**; o código é sugerido.
2. Informe a **descrição** (ex.: *Banco do Brasil – movimento*), o **banco** (código de compensação com 3 dígitos: 001, 104, 237, 341...), a **agência** e a **conta** (com o dígito depois do traço, se houver).
3. Se quiser, informe a **conta contábil** do banco (precisa existir no plano de contas da empresa).
4. **Gravar**. Desmarque **Ativa** para tirar a conta das listas sem apagá-la. Conta usada numa forma de pagamento pode ser excluída: a forma fica sem conta.

## Formas de pagamento

**Para que serve:** padronizar como as notas são pagas ou recebidas. Na digitação da nota fiscal, ao escolher a forma, as duplicatas (parcelas) são geradas sozinhas.
**Onde fica:** *Cadastros › Formas de pagamento* (Fiscal e Gerencial).

1. Clique em **Incluir**: o próximo código já aparece preenchido.
2. Informe a **descrição** (ex.: *Boleto 30/60/90*) e o **meio** (dinheiro, boleto, PIX, cartão, cheque, transferência...).
3. Informe o número de **Parcelas**, o **1º vencimento (dias)** e os **Dias entre parcelas**: os **Prazos (dias)** são montados sozinhos, contados da emissão da nota (ex.: 3 parcelas, 1º vencimento em 30 dias, 30 dias entre parcelas = `30/60/90`; 1 parcela com 0 dias = à vista). Para prazos irregulares, digite-os direto em **Prazos (dias)**, separados por barra (ex.: `0/30/45`). O exemplo abaixo mostra como fica uma venda de R$ 1.000,00 hoje.
4. Escolha, se quiser, a **conta bancária** onde o dinheiro entra ou sai. **Gravar**.

As parcelas saem com valores iguais; a última recebe a diferença dos centavos. Os prazos precisam estar em ordem crescente.

A lista embaixo mostra código, descrição, meio, parcelas, intervalo, prazos, conta e situação, todas as colunas ajustadas à largura da janela.

## Sócios

**Onde fica:** *Cadastros › Empresas*, aba **Sócios** (empresa selecionada no topo).

1. **Importar quadro societário da Receita** traz os sócios da base pública. O CPF vem mascarado: complete-o.
2. Para incluir à mão: nome, CPF ou CNPJ, qualificação, **capital (%)**, data de entrada e de saída, e-mail e telefone.
3. Marque **Representante legal** em quem assina a ECD e os termos.

**Regra:** a soma da participação dos sócios ativos não pode passar de 100%.

## Contadores

**Para que serve:** vincular à empresa o contador ou escritório responsável.
**Onde fica:** *Cadastros › Contadores* (ou a aba *Contadores* da empresa).

1. Escolha **Contador** (CPF, CRC/UF) ou **Escritório contábil** (CNPJ, CRC). O cadastro é único e reaproveitado entre empresas: se já existe, escolha na lista "Já cadastrado".
2. Informe o início do vínculo e contatos (e-mail e telefone são exigidos pela ECD).
3. Marque **Responsável técnico** (apenas um vigente por empresa) e **Assina ECD/ECF** se for o signatário. Sem marcação, assina o responsável técnico. Quem assina como contador precisa ter o CRC informado.
4. Se o contador também é procurador da empresa, preencha a seção **Procuração eletrônica** (veja abaixo).

**Dica:** na troca de contabilidade, **encerre o vínculo** (campo *Fim do vínculo*); não exclua.

### Procuração eletrônica

**Para que serve:** registrar que o contador é **procurador da empresa**: ele assina a ECD/ECF no lugar do responsável (qualificação 309 – Procurador) e pode transmitir o eSocial com o certificado dele. Não existe mais um cadastro separado de procuradores: o procurador é um contador da empresa.
**Onde fica:** *Cadastros › Contadores*, seção **Procuração eletrônica**, ao **Incluir** o contador ou ao **Alterar** o vínculo dele.

1. Marque **É procurador da empresa**.
2. Informe a vigência: **desde** (obrigatório) e **até** (deixe em branco se o prazo é indeterminado).
3. Marque a **abrangência**: **ECD**, **ECF**, **EFD** e/ou **eSocial**, conforme os poderes da procuração.
4. Use a **observação** para anotar a identificação da procuração no e-CAC.
5. **Salvar**. A lista de contadores mostra a procuração na coluna *Procuração eletrônica*.

**Quem não é contador:** se o procurador não tem CRC, deixe o CRC em branco e desmarque *Responsável técnico* e *Assina ECD/ECF*; ele fica cadastrado só para a procuração.

**Regras:** o assistente da ECD/ECF e a transmissão do eSocial só aceitam o procurador com procuração **vigente hoje** e com a **abrangência** da obrigação (ex.: procuração marcada só para ECF não vale para a ECD). O certificado usado deve ser o do próprio procurador (mesmo CPF).

**Importante:** a procuração em si deve ser outorgada no e-CAC da Receita; o Vorticis apenas registra quem é o procurador, por quanto tempo e para quais obrigações.

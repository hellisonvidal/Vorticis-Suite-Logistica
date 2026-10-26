# Auditoria (ECD e ECF)

Os dois assistentes geram o arquivo do SPED já pronto para **importar e validar no PVA** (programa validador da Receita Federal). Eles têm cinco passos e, ao final, guardam o histórico dos arquivos gerados. Menu: *Integrações › Exportar*.

## ECD (SPED Contábil)

**Para que serve:** gerar a Escrituração Contábil Digital (Livro G), com assinatura digital opcional.
**Onde fica:** *Integrações › Exportar › Gerar ECD (SPED Contábil)*.

### Antes de começar
- Plano de contas **vinculado ao plano referencial** (*Integrações › Importar › Vínculo ao plano referencial*).
- Exercício **encerrado** (*Lançamentos › Fechamentos › Encerramento do exercício*).
- Contador e representante legal cadastrados, com CPF, CRC, e-mail e telefone (*Cadastros › Contadores* e *Sócios*).

### Passo a passo
1. **Dados**: escolha o exercício (ano), natureza e número do livro, nível de aglutinação, mutações do patrimônio líquido (DMPL ou DLPA), certidão do contador (opcional) e se inclui os históricos padronizados. O NIRE e o porte vêm do cadastro da empresa.
2. **Pré-requisitos**: lista o que falta, com atalho para as telas que resolvem. Use **Verificar novamente** depois de corrigir.
3. **Validação prévia**: mostra **pendências bloqueantes** (impedem gerar; ex.: CNPJ inválido, conta sem vínculo referencial, balancete que não fecha, exercício não encerrado, signatário sem dados) e **avisos** (não impedem). Corrija e clique em **Validar novamente**.
4. **Assinaturas**: escolha **Não assinar agora (assino no PVA)** ou assine com certificado A1 (arquivo .pfx), A3 (token/cartão do Windows) ou um **certificado cadastrado/padrão**. Informe a senha do certificado quando pedida. Existe a opção de assinar como **procurador**.
5. **Gerar e salvar**: **Gerar arquivo da ECD**, escolha a **pasta** e **Salvar arquivo**.

Em seguida, siga o último passo mostrado na tela: no PVA da ECD, *Escrituração › Importar*, escolha o arquivo e *Escrituração › Validar*.

### Dicas e erros comuns
- Se o PVA recusar a assinatura gerada pelo Vorticis, gere **sem assinar** e assine no próprio PVA.
- Certificados de teste (autoassinados) não passam na validação da cadeia ICP-Brasil.
- A lista **ECDs geradas** guarda data, período, número do livro, linhas, assinaturas e o hash SHA-1 de cada geração.

## ECF (Escrituração Contábil Fiscal)

**Para que serve:** gerar o arquivo da ECF do ano-calendário a partir da contabilidade e das apurações do módulo Fiscal.
**Onde fica:** *Integrações › Exportar › Gerar ECF (SPED)*.

### Antes de começar
- Regime Lucro Real ou Presumido (empresas do Simples e MEI não entregam ECF: o assistente avisa e não gera).
- Apurações trimestrais **salvas** no módulo Fiscal (Lucro Presumido ou Lucro Real com LALUR), vínculo ao plano referencial e exercício encerrado.
- Ideal ter gerado a ECD do ano.

### Passo a passo
1. **Dados**: ano-calendário. Para ECF **retificadora**, marque a opção e informe o **recibo da ECF anterior** (hashcode do recibo).
2. **Pré-requisitos**, 3. **Validação prévia** e 4. **Assinaturas**: funcionam como na ECD. Avisos comuns: divergência entre os cálculos da ECF e a apuração do Fiscal, trimestre sem apuração, regimes misturados no mesmo ano, lançamento do LALUR sem código da ECF.
5. **Gerar e salvar**: **Gerar arquivo da ECF**, escolha a pasta e **Salvar arquivo**.

No PVA da ECF: *Escrituração › Importar*, depois *Escrituração › Recuperar ECD* se o bloco C vier sem dados, e *Escrituração › Validar*.

**Transmissão por procurador:** exige a procuração eletrônica no e-CAC e o contador marcado como procurador (com vigência e abrangência ECD/ECF) em *Cadastros › Contadores*, seção **Procuração eletrônica**; o assistente lembra no passo final.

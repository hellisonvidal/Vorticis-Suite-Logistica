-- =============================================================================
-- Vorticis — Migração 052: procuração eletrônica no cadastro do contador (substitui core.procurador)
--   * O procurador deixa de ter cadastro próprio: é o contador (core.contador) vinculado à empresa
--     (core.empresa_contabilidade) marcado como "procurador da empresa", com vigência e abrangência da
--     procuração (ECD, ECF, EFD, ESOCIAL) e observação. A procuração eletrônica em si continua no e-CAC.
--   * core.contador.crc / uf_crc passam a ser opcionais: o procurador pode não ser contador (sem CRC). O CRC
--     continua obrigatório para quem é responsável técnico ou assina a ECD/ECF como contador (regra no serviço).
--   * Os dados de core.procurador são migrados: casa-se por CPF com o contador existente; se não houver, cria-se
--     o contador com os dados do procurador. Por empresa e CPF migra-se a procuração mais recente.
--   * core.procurador é removida em seguida (nenhuma outra tabela a referencia).
-- =============================================================================

ALTER TABLE core.contador
    ALTER COLUMN crc DROP NOT NULL,
    ALTER COLUMN uf_crc DROP NOT NULL,
    ADD CONSTRAINT ck_contador_crc_e_uf CHECK ((crc IS NULL) = (uf_crc IS NULL));

ALTER TABLE core.empresa_contabilidade
    ADD COLUMN procurador             BOOLEAN      NOT NULL DEFAULT FALSE,
    ADD COLUMN procuracao_inicio      DATE,
    ADD COLUMN procuracao_fim         DATE,                                  -- nulo = prazo indeterminado
    ADD COLUMN procuracao_abrangencia TEXT[]       NOT NULL DEFAULT '{}',    -- ECD | ECF | EFD | ESOCIAL
    ADD COLUMN procuracao_observacao  VARCHAR(200),                          -- ex.: número da procuração no e-CAC
    ADD CONSTRAINT ck_procuracao_contador CHECK (NOT procurador OR contador_id IS NOT NULL),
    ADD CONSTRAINT ck_procuracao_inicio CHECK (NOT procurador OR procuracao_inicio IS NOT NULL),
    ADD CONSTRAINT ck_procuracao_vigencia CHECK (procuracao_fim IS NULL OR procuracao_inicio IS NULL OR procuracao_fim >= procuracao_inicio),
    ADD CONSTRAINT ck_procuracao_abrangencia CHECK (procuracao_abrangencia <@ ARRAY['ECD', 'ECF', 'EFD', 'ESOCIAL']::TEXT[]
                                                    AND (NOT procurador OR cardinality(procuracao_abrangencia) > 0));

DO $$
DECLARE
    p   RECORD;
    cid BIGINT;
    vid BIGINT;
BEGIN
    FOR p IN SELECT DISTINCT ON (empresa_id, cpf) * FROM core.procurador ORDER BY empresa_id, cpf, vigencia_inicio DESC, id DESC
    LOOP
        SELECT id INTO cid FROM core.contador WHERE cpf = p.cpf;
        IF NOT FOUND THEN
            INSERT INTO core.contador (nome, cpf, crc, uf_crc, email, telefone)
            VALUES (p.nome, p.cpf, NULL, NULL, p.email, p.telefone) RETURNING id INTO cid;
        END IF;

        SELECT id INTO vid FROM core.empresa_contabilidade WHERE empresa_id = p.empresa_id AND contador_id = cid AND fim IS NULL;
        IF FOUND THEN
            UPDATE core.empresa_contabilidade
               SET procurador = TRUE, procuracao_inicio = p.vigencia_inicio, procuracao_fim = p.vigencia_fim,
                   procuracao_abrangencia = ARRAY['ECD', 'ECF', 'ESOCIAL'], procuracao_observacao = p.observacao
             WHERE id = vid;
        ELSE
            -- sem vínculo vigente: cria o vínculo pelo período da procuração (encerrado quando a procuração tem fim)
            INSERT INTO core.empresa_contabilidade (empresa_id, contador_id, inicio, fim, procurador, procuracao_inicio, procuracao_fim,
                                                    procuracao_abrangencia, procuracao_observacao)
            VALUES (p.empresa_id, cid, p.vigencia_inicio, p.vigencia_fim, TRUE, p.vigencia_inicio, p.vigencia_fim,
                    ARRAY['ECD', 'ECF', 'ESOCIAL'], p.observacao);
        END IF;
    END LOOP;
END $$;

DROP TABLE core.procurador;

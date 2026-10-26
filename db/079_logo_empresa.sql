-- Logo da empresa (PNG ou JPEG, até 1 MB), enviado no cadastro da empresa/transportadora e impresso no cabeçalho dos relatórios em PDF.
ALTER TABLE core.empresa ADD COLUMN IF NOT EXISTS logo bytea;
ALTER TABLE core.empresa ADD CONSTRAINT ck_empresa_logo_tamanho CHECK (logo IS NULL OR octet_length(logo) BETWEEN 8 AND 1048576);

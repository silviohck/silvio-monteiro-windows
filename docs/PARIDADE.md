# Orthck macOS ↔ Windows — Matriz objetiva de paridade

Esta matriz diferencia **código implementado**, **paridade completa** e **testes reais**, evitando marcar como pronto algo que apenas parece pronto.

| Funcionalidade | Windows: situação em código | Pendência para paridade real |
|---|---|---|
| Geração DOCX a partir de `{{CAMPOS}}` | Implementada (OpenXML) | Testar modelos reais complexos, imagens, tabelas, caixas de texto, seções |
| Placeholders distribuídos em runs | Implementada; teste automatizado | Comparar conteúdo com saída do Mac |
| Dados automáticos de paciente/local/data/idade | Implementados | Verificar aliases atuais do Mac |
| Campos de consulta (CID, conduta, afastamento) | Isolados por documento | Expandir testes de trocas parciais |
| Cadastro de pacientes/homônimos | Básico funcional | Mesclar cadastros, validar CPF/data, melhoria da busca |
| Locais de atendimento | Cadastro básico | Transferência segura de vínculos e preferências por local |
| Modelos e subpastas | Descoberta recursiva; pastas compartilhadas (`1 - APAC`) e ignoradas como no Mac | Atalhos (.lnk), arquivos soltos na pasta do local, .doc/.rtf/.odt, tipos locais, sincronização bidirecional e remoção segura |
| Histórico | Lista, abrir e colocar na fila | Filtros por paciente/local, reimpressão avançada |
| Fila de impressão | Persistente, sequencial, seleção e reenvio confirmado | Testar diferentes impressoras/versões do Word |
| Impressão pelo Word | COM por documento | Homologação em impressoras reais e validação de cópias |
| CID-10 / SIGTAP / TUSS | Importador TSV e pesquisa | Incorporar bases oficiais versionadas/filtros avançados |
| Dados do Mac | Leitura direta `dados.json` e backup | Testar importações das várias versões do Mac |
| Aparência suave | WPF tema claro | Modo escuro e opção de tamanho do texto |
| Backup/restauração | Backup ao salvar e antes de importar | Backup versionado, restauração guiada e criptografia |
| Segurança clínica/LGPD | Não envia dados para a nuvem | Controle de acesso, criptografia, auditoria e plano de backup |
| Sincronização Mac + Windows | Não implementada | Projeto separado após homologação local |

## Fluxo mínimo de homologação

1. Importar cópia anônima de dados de teste do Mac, verificar IDs, CPF e nomes repetidos.
2. Gerar 20 DOCX com modelos reais, incluindo cabeçalho/rodapé, tabela e campos divididos entre runs.
3. Gerar pacientes A/B/A seguidamente e verificar que nenhum diagnóstico permanece indevidamente.
4. Gerar 10 documentos na fila com cópias diferentes, imprimir sequencialmente e conferir páginas.
5. Desligar/desconectar impressora durante a fila; verificar estado `incerto`, sem reenvio automático.
6. Encerrar aplicativo durante envio; reabrir e conferir persistência e confirmação de reenvio.
7. Mover a pasta de modelos; garantir que nenhum DOCX ou cadastro seja removido automaticamente.
8. Validar versão Windows x64 e Word desktop real antes de uso assistencial.

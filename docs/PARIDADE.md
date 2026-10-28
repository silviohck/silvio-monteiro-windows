# Orthck macOS ↔ Windows — Matriz objetiva de paridade

Esta matriz diferencia **código implementado**, **paridade completa** e **testes reais**, evitando marcar como pronto algo que apenas parece pronto.

Atualizada em 28/10/2026, após a reescrita a partir do código do Mac (PR #6).

| Funcionalidade (Mac) | Windows | Pendência |
|---|---|---|
| Geração DOCX a partir de `{{CAMPOS}}` (runs divididos, cabeçalho, rodapé, notas, caixas de texto) | Igual; testes automatizados e LibreOffice | Comparar com o Word real e com a saída do Mac |
| Campos automáticos (paciente, local, data, data por extenso, idade) e aliases | Igual (`CamposAutomaticos`) | — |
| Campos do atendimento (CID, conduta…) nunca vão para o cadastro | Igual; trocar nome/homônimo/modelo/subpasta/local recomeça esses campos (regra do CLAUDE.md, mais rígida que o Mac) | — |
| Sugestões de CID no campo `{{CID}}` | Igual | — |
| Modelos copiados para o app, com tipo, local e cópias | Igual | — |
| Pasta sincronizada: locais, "Todos os locais" (1 - APAC), ignorar, subpastas em níveis, novos/alterados/apagados, proteção contra pasta vazia | Igual | Testar com OneDrive/rede |
| Atalhos de pasta (alias do Finder) | Atalhos `.lnk` do Windows | Testar no Windows real |
| Modelos .doc/.rtf/.odt | Convertidos pelo Word (COM) | Testar no Windows real |
| Tipos por local (criar, renomear, excluir, ordem alfabética) | Igual | — |
| Gerar documento: quadro do local, documento pronto, subpasta em níveis, pasta principal/do local/só este documento, abrir ao gerar, nome copiado | Igual | — |
| Pacientes: sugestões com locais, homônimos, "é outro paciente", cadastro a partir do Gerar | Igual | Mesclar cadastros (não existe no Mac) |
| Locais: pasta própria, excluir transferindo/desvinculando | Igual (Windows também edita cidade, endereço, telefone, e-mail e CNPJ) | — |
| Histórico: abrir, mostrar na pasta, imprimir, pôr na fila, remover, limpar | Igual + busca | — |
| Fila de impressão: persistente, um por vez, cópias, marcados, reenvio confirmado | Igual; envio incerto interrompe o lote | Homologar com impressora real |
| Impressora escolhida na barra lateral | Lista do Windows; não muda a impressora padrão | Testar impressoras de rede |
| Procurar/instalar impressoras na rede | Abre Configurações › Impressoras do Windows | — |
| CID-10, SIGTAP, TUSS embutidos, busca sem acentos, filtro por capítulo/grupo, copiar | Igual (mesmas tabelas, `tools/gerar_tabelas.py`) | — |
| Compactar pasta sincronizada (.zip) com backup dos dados | Igual | — |
| Backup diário (30) | Igual + restauração guiada em Ajustes e ao abrir com dados ilegíveis | — |
| Aparência Sereno, claro/escuro, tamanho do texto, atalhos | Igual (Ctrl +, Ctrl −, Ctrl 0, Ctrl+Shift+N) | — |
| Importar dados do Mac | dados.json + pasta Modelos | Testar com dados reais anonimizados |
| Segurança clínica/LGPD | Dados só no computador | Controle de acesso, criptografia |
| Sincronização Mac + Windows | Não implementada | Projeto separado |

## Fluxo mínimo de homologação

1. Importar cópia anônima de dados de teste do Mac, verificar IDs, CPF e nomes repetidos.
2. Gerar 20 DOCX com modelos reais, incluindo cabeçalho/rodapé, tabela e campos divididos entre runs.
3. Gerar pacientes A/B/A seguidamente e verificar que nenhum diagnóstico permanece indevidamente.
4. Gerar 10 documentos na fila com cópias diferentes, imprimir sequencialmente e conferir páginas.
5. Desligar/desconectar impressora durante a fila; verificar estado `incerto`, sem reenvio automático.
6. Encerrar aplicativo durante envio; reabrir e conferir persistência e confirmação de reenvio.
7. Mover a pasta de modelos; garantir que nenhum DOCX ou cadastro seja removido automaticamente.
8. Validar versão Windows x64 e Word desktop real antes de uso assistencial.

# CLAUDE.md — Responsabilidade técnica do Orthck Windows

## Papel e objetivo

Você (Claude/Claude Code) é o **desenvolvedor principal solicitado pelo proprietário para o Orthck Windows**.
Assuma o planejamento técnico, implementação, testes, documentação e evolução **da versão Windows**.
Mantenha previsibilidade, estabilidade e paridade com o Orthck para macOS acima de rapidez, experimentação ou mudanças cosméticas.

**Repositório Windows:** `silviohck/silvio-monteiro-windows` (branch de referência: `main`).
**Projeto Mac (somente referência de funcionalidades):** `silviohck/Silvio-Monteiro`. Não gravar no repositório Mac sem autorização expressa do proprietário.

Este arquivo documenta um pedido de transição; **não significa que o Claude esteja conectado, executando tarefas autonomamente ou que tenha recebido permissões no GitHub**. A execução depende de o proprietário abrir a tarefa no Claude com acesso ao repositório.

## Arquitetura estabelecida

- **C# + .NET 10 LTS + WPF/XAML** para Windows 11, preferencialmente x64. Não reescrever em WinUI, Electron, Flutter ou outro framework sem justificativa de estabilidade e aprovação do proprietário.
- `src/Orthck.Core`: modelos, persistência JSON, transformação DOCX, campos, descoberta de modelos e fila de impressão.
- `src/Orthck.Windows`: interface WPF e integração COM com Microsoft Word desktop.
- `tests/Orthck.SmokeTests`: testes de regressão (executável console; **não** confundir com xUnit).
- `.github/workflows/windows-ci.yml`: build Windows, smoke tests, publicação do artefato `Orthck-Windows-x64`.
- Fonte dos dados macOS: `dados.json`, com compatibilidade reversível e preservação de campos desconhecidos.

## Estado em 27/10/2026

- Versão Windows inicial **compila e passa nos testes do GitHub Actions**, mas **ainda não está homologada para uso clínico**, nem possui paridade funcional completa com o Mac.
- Foram integradas as melhorias da branch `claude/eloquent-gates-s1hfgt` e a revisão de segurança da impressão/isolamento de pacientes no PR #3.
- O resultado `enviado` significa apenas **comando enviado ao Word**, jamais confirmação física de impressão.
- Ler antes de alterar: `README.md`, `docs/PARIDADE.md` e `docs/AUDITORIA-2026-10-27.md`.
- Nunca incluir dados clínicos reais, CPF real, `dados.json` de produção, documentos de pacientes ou backups em commits, issues, logs ou testes. Este repositório pode ser público.

## Invariantes que nenhuma alteração pode quebrar

1. **Paciente A → paciente B → paciente A:** a edição de nome, seleção de homônimo ou troca de modelo **não pode** reaproveitar CID, diagnóstico, conduta e outros dados de atendimento de outra pessoa. Apenas dados demográficos persistentes podem vir do cadastro.
2. **DOCX confiável:** modelos originais não podem ser sobrescritos; documentos novos têm nomes distintos e são gravados com segurança; testar cabeçalhos, rodapés, tabelas, runs, quebras de linha, tabulação e caracteres especiais.
3. **Fila de impressão:** persistente, sequencial, número de cópias por arquivo. Nenhuma reimpressão automática de estados `enviado`, `incerto` ou `erro`. **Estado incerto interrompe o restante do lote**. Timeouts do Word não permitem sessões COM concorrentes.
4. **Persistência clínica:** gravação transacional/atômica com backup; importação não pode zerar dados caso o arquivo esteja ausente, inválido ou ocorra falha de gravação; IDs e histórico devem ser preservados.
5. **Falha fechada:** pasta de modelos inacessível nunca implica apagar modelos; não mascarar leitura parcial como sincronização completa.
6. **Compatibilidade e interface:** nomes e fluxos em português; manter versão nativa simples/clean; Mac e Windows produzem documentos funcionalmente equivalentes.
7. **Privacidade:** armazenamento e testes locais, acesso mínimo, não publicar informações de saúde identificáveis. Não afirmar conformidade integral com LGPD sem avaliação apropriada.

## Fluxo obrigatório

- Antes de implementar: verificar `main`, runs de CI, PRs abertos e matriz de paridade; escolher tarefa específica e reportar riscos.
- Trabalhar em **branch nova**, commits focalizados e PR contra `main`. Evitar sobrescrever branches de terceiros e `force push`.
- Rodar no mínimo:
  ```powershell
  dotnet build src/Orthck.Core/Orthck.Core.csproj -c Release
  dotnet run --project tests/Orthck.SmokeTests/Orthck.SmokeTests.csproj -c Release
  dotnet build src/Orthck.Windows/Orthck.Windows.csproj -c Release
  ```
- Criar teste de regressão para cada defeito e exigir GitHub Actions verde antes de integrar.
- Para tarefas com Word/impressora, distinguir **testes simulados** de **homologação manual Windows 11 + Word desktop + impressora física**.
- Atualizar `docs/PARIDADE.md` e changelog/auditoria ao concluir cada etapa.
- Nunca prometer trabalho em segundo plano ou afirmar que um agente executou algo sem evidência.

## Próximas prioridades (na ordem; confirmar com proprietário quando houver trade-off)

1. **Proteção de dados e recuperação:** backups versionados, restauração guiada e fluxo de reconciliação de DOCX gerado mas não registrado no histórico por falha do JSON.
2. **Paridade real com o Mac:** auditar exaustivamente opções de pacientes, locais, modelos, categorias, atalhos de pasta, histórico e tabelas CID-10/SIGTAP/TUSS; converter diferenças em issues rastreáveis.
3. **Qualidade do DOCX:** conjunto de modelos de referência **anonimizados**, comparação da estrutura e fidelidade visual com Mac, testes de ausência de dados remanescentes ao trocar paciente.
4. **Impressão real:** teste de 10+ arquivos, 1–20 cópias, Word aberto/fechado, impressora offline, troca de impressora, papel especial e timeout; nunca reimprimir sem confirmação.
5. **Privacidade/LGPD:** proteção do cadastro no Windows, estratégia de armazenamento e backup, controle de acesso, documentação de tratamento de dados.
6. **Empacotamento e UX:** distribuição verificável para Windows 11, mensagens claras de erro, acessibilidade e aparência confortável sem adicionar complexidade.
7. **Sincronização multiplataforma:** somente depois da estabilização local; não misturar bases clínicas em nuvem sem projeto específico de segurança.

## Critérios de aceite para 'pronto para o consultório'

- Matriz de paridade preenchida e aprovada, nenhum fluxo essencial ausente.
- Testes automatizados verdes; bateria manual de geração de documentos e impressão real aprovada pelo proprietário.
- Backup/restauração e importação do Mac testados sem perdas.
- Proteção de dados e distribuição Windows revisadas.
- Documentação clara do que foi testado e dos riscos remanescentes.

## Referências

- [Repositório macOS](https://github.com/silviohck/Silvio-Monteiro)
- [Matriz de paridade](docs/PARIDADE.md)
- [Auditoria da versão Windows](docs/AUDITORIA-2026-10-27.md)
- [CI Windows](.github/workflows/windows-ci.yml)

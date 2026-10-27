# Auditoria de segurança funcional — Orthck Windows (27/10/2026)

> **Estado:** correções propostas para revisão. A passagem no GitHub Actions confirma compilação e testes automatizados, **não** homologação clínica, impressão real nem paridade total com o Mac.

## Base integrada do Claude

Branch: `claude/eloquent-gates-s1hfgt`. Inclui compatibilidade de datas ISO-8601 com `dados.json` do Mac, pastas compartilhadas, prevenção de nomes reservados do Windows, higienização de campos DOCX, testes correspondentes, melhor controle do Word e execução única do aplicativo.

## Falhas adicionais encontradas e corrigidas

| Severidade | Defeito | Correção |
|---|---|---|
| Crítica | Edição do nome ou troca de homônimo preservava CID, diagnóstico e conduta manuais do paciente anterior (`RefreshFields(keepTyped: true)`). | Campos específicos de atendimento são redefinidos ao trocar identidade/modelo; dados permanentes vêm apenas do cadastro selecionado. |
| Alta | Timeout ou impressão com estado `incerto` podia deixar o restante do lote seguir automaticamente. | Lote para imediatamente no estado incerto; demais arquivos permanecem pendentes. |
| Alta | Status `enviado` era divulgado antes de terminar a limpeza da instância COM do Word. | O próximo envio é liberado só após fechar documento, restaurar impressora e concluir limpeza. |
| Alta | Após timeout, outra tarefa podia iniciar uma segunda automação Word enquanto a primeira permanecia travada. | Semaphore global de impressão Word; só liberado quando a tarefa COM realmente terminar, não quando expirar o timeout. |
| Alta | Importar arquivo JSON ausente tratava a origem como cadastro vazio. | Exige existência da origem antes da importação. |
| Alta | Falha na gravação do JSON importado podia deixar memória diferente dos dados em disco. | Troca da base ativa somente depois de gravar a importação com sucesso. |
| Média | Recuperação de itens `enviando` para `incerto` acontecia apenas em memória. | Grava imediatamente o estado de recuperação em disco. |
| Média | Campos de exames, justificativas e relatos clínicos extensos tinham edição limitada a linha curta. | Entradas multilinha para campos pertinentes, com rolagem. |

## Testes automatizados adicionados

- DOCX modelo intacto e arquivo de saída existente não sobrescrito.
- Campo clínico (CID e conduta) não incorporado aos dados permanentes do paciente.
- Importação inválida e importação de arquivo inexistente preservam cadastros.
- Estado de impressão interrompida é salvo como `incerto`, inclusive após outra inicialização.
- Lote interrompido por envio incerto deixa os próximos pacientes pendentes.
- Reenvio de documento incerto exige confirmação explícita.

## Riscos remanescentes e critérios de homologação

1. **Microsoft Word COM e drivers:** testar em Windows 11 com Word desktop real, modelos complexos, impressora USB/rede, papel especial, 1–20 cópias, Word aberto/fechado, impressora offline, PDF e fila bloqueada. `Enviado` significa comando ao Word, não papel impresso.
2. **Compatibilidade DOCX:** comparar visualmente geração Mac/Windows para cabeçalhos, rodapés, tabelas, caixas de texto e formatação de parágrafos; os testes automatizados não garantem igualdade visual.
3. **Paridade de modelos/cadastros:** implementação Windows ainda não reproduz todas as configurações de local, exclusão e sincronização do aplicativo Mac; consultar `docs/PARIDADE.md`.
4. **Privacidade e LGPD:** os dados locais ainda não estão protegidos por criptografia específica do aplicativo; avaliar permissões do perfil Windows, política de backup, disco criptografado e acesso por usuário antes de usar informações clínicas reais.
5. **Histórico migrado:** caminhos absolutos do macOS não são caminhos Windows; arquivos históricos precisam ser copiados/remapeados.
6. **Erros irreversíveis após gravar DOCX:** um arquivo Word salvo e um erro posterior no JSON podem deixar um documento fora do histórico. Planejar operação de reconciliação/verificação de arquivos não registrados.

**Não usar o projeto como sistema clínico de produção antes da homologação real.**

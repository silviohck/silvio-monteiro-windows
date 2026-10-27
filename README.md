# Orthck — Windows

Portabilidade independente do [Orthck para macOS](https://github.com/silviohck/Silvio-Monteiro),
priorizando **previsibilidade, estabilidade e paridade funcional**.

> **Estado atual:** primeira implementação funcional / versão de desenvolvimento. **Não certificada para uso clínico real** até passar pela compilação do Windows, testes de impressão em Word e homologação com modelos reais. Algumas funcionalidades têm paridade inicial, outras ainda dependem de conclusão; veja [Matriz de paridade](docs/PARIDADE.md).

## Tecnologias

- **C# / .NET 10 LTS / WPF**: aplicação desktop nativa Windows.
- **System.IO.Compression + System.Xml.Linq**: motor Open XML que preenche DOCX diretamente, preserva o Word como editor e não usa a interface gráfica para preencher.
- **Microsoft Word COM (late binding)**: impressão no objeto `Document`, usando uma fila persistente, serializada em STA e sem depender de `ActiveDocument`.
- **JSON compatível com `dados.json` do Mac**, gravação com arquivo temporário + substituição atômica + backup. SQLite pode ser incorporado depois de uma migração reversível e testada.
- **Testes independentes do Word**: campos divididos entre runs, pacientes consecutivos, CID, persistência e não duplicação de impressões.

## Baixar e testar

Cada atualização da `main` publica uma versão de teste em **[Releases](https://github.com/silviohck/silvio-monteiro-windows/releases/latest)** como um arquivo único, **Orthck.exe**: baixe e abra com dois cliques, sem instalar nada (o .NET vai dentro). Funciona em PCs com Windows e no Windows 11 de Macs com chip Apple (Parallels, VMware Fusion ou UTM).

Link direto da versão mais recente: https://github.com/silviohck/silvio-monteiro-windows/releases/latest/download/Orthck.exe

No primeiro uso o Windows extrai algumas bibliotecas internas para uma pasta temporária, então a primeira abertura demora alguns segundos a mais. Como o app ainda não tem assinatura digital, o Windows pode mostrar "O Windows protegeu o computador": clique em **Mais informações → Executar assim mesmo**. Para testar a fila sem papel, escolha a impressora **Microsoft Print to PDF**.

## Compilar no Windows

Instale Windows 11, Visual Studio com suporte a WPF e SDK do .NET 10; para impressão direta, tenha o **Microsoft Word desktop** instalado.

```powershell
dotnet build src/Orthck.Windows/Orthck.Windows.csproj -c Release
dotnet run --project src/Orthck.Windows/Orthck.Windows.csproj
```

Testes:

```powershell
dotnet run --project tests/Orthck.SmokeTests/Orthck.SmokeTests.csproj
```

O workflow **Windows — compilar e testar Orthck** também publica uma compilação `Orthck-Windows-x64` nos artifacts do GitHub Actions quando o projeto for publicado.

## Primeiro uso

1. Na aba **Modelos**, clique em **Escolher pasta de modelos**, indicando a pasta onde os DOCX estão organizados como `Local / Tipo / Modelo.docx` (subpastas adicionais são aceitas). Como no Mac, a pasta `1 - APAC` (e as demais de `pastasCompartilhadas` no `dados.json`) vale para todos os locais e aparece como um tipo em cada um; as `pastasIgnoradas` não viram locais.
2. Configure a pasta principal dos documentos gerados. O padrão é `Documentos/Orthck`.
3. Cadastre os locais ou importe uma cópia de `dados.json` do Mac, pelo botão **Importar dados do Mac**. A importação não remove o arquivo original e cria um backup dos dados locais anteriores.
4. Em **Gerar documento**, escolha local, tipo e modelo, preencha o paciente e gere o documento. O padrão é adicionar à **Fila de impressão**.
5. Na fila, confira os itens e clique em **Imprimir todos os pendentes**; o processamento é sequencial. **Enviado ao Word não garante que o papel saiu**.

### Cadastro e privacidade

Os dados pessoais são armazenados **somente no computador local**, em `%LOCALAPPDATA%/Orthck/dados.json`. Nunca envie esse arquivo, documentos médicos, cadastros ou logs com informações reais para um repositório público. Antes de atender pacientes, implemente acesso restrito ao Windows, backups cifrados e uma política de retenção conforme a LGPD.

## Importação CID-10 / SIGTAP / TUSS

O repositório Windows não contém ainda cópias das grandes tabelas do Mac. Para convertê-las a partir do projeto original:

```powershell
python tools/exportar_tabelas_mac.py C:\caminho\Silvio-Monteiro C:\temp\codigos
```

Em cada aba de códigos, **Importar tabela TSV…** e escolha o arquivo correspondente. O uso é offline após importar. Para paridade completa ainda serão necessários filtros por capítulos/grupos e atualização automática das competências.

## Princípios de estabilidade

1. Não apagar modelo original quando falha leitura de pasta.
2. Nunca sobrescrever um DOCX anterior; arquivos consecutivos recebem nomes distintos.
3. CID, conduta e outros campos clínicos não são reaproveitados automaticamente entre pacientes.
4. A fila é serial e persistente: não reimprime em caso de erro ou encerramento inesperado sem confirmação explícita.
5. Histórico e DOCX são preservados quando um item é removido da fila.
6. O projeto macOS **não é modificado** pela portabilidade.

## Compilar fora do Windows

O núcleo e os testes rodam em qualquer sistema com o SDK do .NET 10. O app WPF também compila (sem executar) no Linux/macOS com:

```bash
dotnet build Orthck.Windows.sln -c Release -p:EnableWindowsTargeting=true
```

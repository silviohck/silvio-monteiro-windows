using Orthck.Core;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Orthck.Windows;

public partial class MainWindow
{
    private FrameworkElement PatientPage()
    {
        var panel = Column(); panel.Children.Add(Heading("Pacientes"));
        panel.Children.Add(Secondary("O mesmo cadastro pode ser usado em diversos locais. Homônimos continuam sendo pessoas diferentes."));
        var search = Input();
        var list = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, Height = 265 };
        list.Columns.Add(new DataGridTextColumn { Header = "Nome", Binding = new System.Windows.Data.Binding("Nome"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        list.Columns.Add(new DataGridTextColumn { Header = "Nascimento", Binding = new System.Windows.Data.Binding("DataNascimento"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        list.Columns.Add(new DataGridTextColumn { Header = "CPF", Binding = new System.Windows.Data.Binding("Cpf"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        void Reload() => list.ItemsSource = _storage.Data.Pacientes.Where(p => p.Nome.Contains(search.Text, StringComparison.OrdinalIgnoreCase) || p.Cpf.Contains(search.Text)).OrderBy(p => p.Nome).ToArray();
        search.TextChanged += (_, _) => Reload();
        panel.Children.Add(Text("Buscar por nome ou CPF")); panel.Children.Add(search); panel.Children.Add(list);
        var form = Column(); var name = Input(); var cpf = Input(); var birth = Input(); var rg = Input(); var phone = Input(); var email = Input(); var address = Input(); var convenio = Input(); var observations = Input();
        void Field(string label, TextBox box) { form.Children.Add(Text(label)); form.Children.Add(box); }
        Field("Nome completo", name); Field("Data de nascimento (dd/MM/aaaa)", birth); Field("CPF", cpf); Field("RG", rg);
        Field("Telefone", phone); Field("E-mail", email); Field("Endereço", address); Field("Convênio", convenio); Field("Observações", observations);
        void SetPatient(Patient? p)
        {
            name.Text = p?.Nome ?? ""; cpf.Text = p?.Cpf ?? ""; birth.Text = p?.DataNascimento ?? "";
            rg.Text = p?.Rg ?? ""; phone.Text = p?.Telefone ?? ""; email.Text = p?.Email ?? "";
            address.Text = p?.Endereco ?? ""; convenio.Text = p?.Convenio ?? ""; observations.Text = p?.Observacoes ?? "";
        }
        list.SelectionChanged += (_, _) => SetPatient(list.SelectedItem as Patient);
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Novo cadastro", () => { list.SelectedItem = null; SetPatient(null); name.Focus(); }));
        actions.Children.Add(ActionButton("Salvar paciente", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("Informe o nome completo.");
            var p = list.SelectedItem as Patient;
            if (p == null) { p = new Patient(); _storage.Data.Pacientes.Add(p); }
            p.Nome = name.Text.Trim(); p.Cpf = cpf.Text.Trim(); p.DataNascimento = birth.Text.Trim(); p.Rg = rg.Text.Trim();
            p.Telefone = phone.Text.Trim(); p.Email = email.Text.Trim(); p.Endereco = address.Text.Trim();
            p.Convenio = convenio.Text.Trim(); p.Observacoes = observations.Text.Trim(); _storage.Save(); Reload(); list.SelectedItem = p;
        }));
        actions.Children.Add(ActionButton("Excluir cadastro…", () =>
        {
            if (list.SelectedItem is not Patient p) return;
            if (MessageBox.Show($"Excluir cadastro de {p.Nome}? Documentos existentes e histórico serão preservados.", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _storage.Data.Pacientes.RemoveAll(x => x.Id == p.Id); _storage.Save(); SetPatient(null); Reload();
        }));
        form.Children.Add(actions); panel.Children.Add(Text("Editar cadastro", 18)); panel.Children.Add(Card(form)); Reload();
        return Scroll(panel);
    }

    private FrameworkElement LocationPage()
    {
        var panel = Column(); panel.Children.Add(Heading("Locais de atendimento"));
        panel.Children.Add(Secondary("Locais próprios podem compartilhar modelos Word; os documentos são separados por clínica e categoria."));
        var list = new ListBox { Height = 172, DisplayMemberPath = "Nome" };
        void Reload() => list.ItemsSource = _storage.Data.Locais.OrderBy(x => x.Nome).ToArray();
        panel.Children.Add(list);
        var form = Column(); var name = Input(); var city = Input(); var address = Input(); var phone = Input(); var email = Input(); var cnpj = Input();
        foreach (var (label, box) in new[] { ("Nome do local", name), ("Cidade", city), ("Endereço", address), ("Telefone", phone), ("E-mail", email), ("CNPJ", cnpj) })
        { form.Children.Add(Text(label)); form.Children.Add(box); }
        void Set(ClinicLocation? c) { name.Text = c?.Nome ?? ""; city.Text = c?.Cidade ?? ""; address.Text = c?.Endereco ?? ""; phone.Text = c?.Telefone ?? ""; email.Text = c?.Email ?? ""; cnpj.Text = c?.Cnpj ?? ""; }
        list.SelectionChanged += (_, _) => Set(list.SelectedItem as ClinicLocation);
        var row = new WrapPanel();
        row.Children.Add(ActionButton("Novo local", () => { list.SelectedItem = null; Set(null); name.Focus(); }));
        row.Children.Add(ActionButton("Salvar local", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("Informe o nome do local.");
            var c = list.SelectedItem as ClinicLocation;
            if (c == null) { c = new ClinicLocation(); _storage.Data.Locais.Add(c); }
            c.Nome = name.Text.Trim(); c.Cidade = city.Text.Trim(); c.Endereco = address.Text.Trim();
            c.Telefone = phone.Text.Trim(); c.Email = email.Text.Trim(); c.Cnpj = cnpj.Text.Trim(); _storage.Save(); Reload(); list.SelectedItem = c;
        }));
        row.Children.Add(ActionButton("Excluir local…", () =>
        {
            if (list.SelectedItem is not ClinicLocation c) return;
            if (_storage.Data.Pacientes.Any(p => p.LocalID == c.Id) || _storage.Data.Modelos.Any(m => m.LocalID == c.Id))
                throw new InvalidOperationException("Este local está vinculado a pacientes/modelos. Transfira esses vínculos antes de excluir, para não perder a referência.");
            if (MessageBox.Show("Excluir este local?", "Confirmar", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            _storage.Data.Locais.RemoveAll(x => x.Id == c.Id); _storage.Save(); Reload(); Set(null);
        }));
        form.Children.Add(row); panel.Children.Add(Text("Dados do local", 18)); panel.Children.Add(Card(form)); Reload();
        return Scroll(panel);
    }

    private FrameworkElement TemplatePage()
    {
        var panel = Column(); panel.Children.Add(Heading("Modelos Word"));
        panel.Children.Add(Secondary("O Orthck lê pastas locais na estrutura Local / Tipo / Subpastas / Modelo.docx. Não remove arquivos se a pasta estiver indisponível."));
        var rootText = Text("Pasta atual: " + (string.IsNullOrWhiteSpace(_storage.Data.PastaSincronizada) ? "não definida" : _storage.Data.PastaSincronizada));
        panel.Children.Add(rootText);
        var row = new WrapPanel();
        row.Children.Add(ActionButton("Escolher pasta de modelos…", () =>
        {
            var dir = ChooseFolder("Selecione a pasta principal dos modelos do Orthck");
            if (dir.Length == 0) return;
            _storage.Data.PastaSincronizada = dir; _storage.Save(); ShowPage(4);
        }));
        row.Children.Add(ActionButton("Escolher pasta de documentos…", () =>
        {
            var dir = ChooseFolder("Selecione onde os documentos gerados serão salvos");
            if (dir.Length == 0) return;
            _storage.Data.PastaDocumentos = dir; _storage.Save(); ShowPage(4);
        }));
        panel.Children.Add(row);
        panel.Children.Add(Secondary("Destino dos documentos: " + (string.IsNullOrWhiteSpace(_storage.Data.PastaDocumentos) ? "Documentos\\Orthck" : _storage.Data.PastaDocumentos)));
        var templates = DiscoverTemplates();
        panel.Children.Add(Text($"Modelos encontrados: {templates.Count}", 17));
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, Height = 350 };
        grid.Columns.Add(new DataGridTextColumn { Header = "Local", Binding = new System.Windows.Data.Binding("Clinic"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Tipo", Binding = new System.Windows.Data.Binding("Category"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Subpasta", Binding = new System.Windows.Data.Binding("Subpath"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Modelo", Binding = new System.Windows.Data.Binding("DisplayName"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        grid.ItemsSource = templates;
        panel.Children.Add(grid);
        var controls = new WrapPanel();
        controls.Children.Add(ActionButton("Abrir modelo no Word", () => { if (grid.SelectedItem is TemplateInfo t) OpenDocument(t.FullPath); }));
        controls.Children.Add(ActionButton("Importar arquivo DOCX…", () =>
        {
            if (string.IsNullOrWhiteSpace(_storage.Data.PastaSincronizada) || !Directory.Exists(_storage.Data.PastaSincronizada))
                throw new InvalidOperationException("Primeiro escolha a pasta principal de modelos.");
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Documentos Word (*.docx)|*.docx" };
            if (dialog.ShowDialog() != true) return;
            var clinic = _storage.Data.Locais.FirstOrDefault()?.Nome ?? "Todos os locais";
            var dir = Path.Combine(_storage.Data.PastaSincronizada, PathRules.SafeComponent(clinic), "Outros");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, Path.GetFileName(dialog.FileName));
            if (File.Exists(dest)) throw new IOException("Já existe um modelo com esse nome. Renomeie antes de importar.");
            _ = DocxService.Fields(dialog.FileName); // valida antes de copiar
            File.Copy(dialog.FileName, dest);
            ShowPage(4);
        }));
        panel.Children.Add(controls);
        return Scroll(panel);
    }

    private FrameworkElement HistoryPage()
    {
        var panel = Column(); panel.Children.Add(Heading("Histórico"));
        panel.Children.Add(Secondary("Registro dos documentos produzidos. Excluir da lista não apaga o arquivo Word."));
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, Height = 440 };
        grid.Columns.Add(new DataGridTextColumn { Header = "Data", Binding = new System.Windows.Data.Binding("Data") { StringFormat = "dd/MM/yyyy HH:mm" }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Paciente", Binding = new System.Windows.Data.Binding("Paciente"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Modelo", Binding = new System.Windows.Data.Binding("Modelo"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Local", Binding = new System.Windows.Data.Binding("Local"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.ItemsSource = _storage.Data.Historico.OrderByDescending(x => x.Data).ToArray(); panel.Children.Add(grid);
        var row = new WrapPanel();
        row.Children.Add(ActionButton("Abrir no Word", () => { if (grid.SelectedItem is GeneratedDocument d) OpenDocument(d.Caminho); }));
        row.Children.Add(ActionButton("Adicionar à fila", () =>
        {
            if (grid.SelectedItem is not GeneratedDocument d) return;
            if (!File.Exists(d.Caminho)) throw new FileNotFoundException("Documento original não encontrado.", d.Caminho);
            _queue.Add(d, 1); MessageBox.Show("Documento incluído na fila de impressão.");
        }));
        row.Children.Add(ActionButton("Remover do histórico", () =>
        {
            if (grid.SelectedItem is not GeneratedDocument d) return;
            _storage.Data.Historico.RemoveAll(x => x.Id == d.Id); _storage.Save(); ShowPage(5);
        }));
        panel.Children.Add(row); return Scroll(panel);
    }

    private FrameworkElement QueuePage()
    {
        var panel = Column(); panel.Children.Add(Heading("Fila de impressão"));
        var items = _storage.Data.FilaImpressao;
        var pending = items.Where(x => x.Situacao == "pendente").ToArray();
        panel.Children.Add(Secondary($"{pending.Length} pendentes · {pending.Sum(x => x.Copias)} cópias · {items.Count(x => x.Situacao == "enviado")} enviados ao Word. O papel impresso deve ser conferido."));
        var printer = new ComboBox { MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Left };
        var choices = new List<string> { "Padrão do Windows" };
        try { choices.AddRange(System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>()); } catch { /* Sem impressora instalada */ }
        printer.ItemsSource = choices; printer.SelectedIndex = 0;
        panel.Children.Add(Text("Impressora")); panel.Children.Add(printer);
        var list = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Extended, Height = 392 };
        list.Columns.Add(new DataGridTextColumn { Header = "Paciente", Binding = new System.Windows.Data.Binding("Paciente"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        list.Columns.Add(new DataGridTextColumn { Header = "Documento", Binding = new System.Windows.Data.Binding("Modelo"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        list.Columns.Add(new DataGridTextColumn { Header = "Cópias", Binding = new System.Windows.Data.Binding("Copias"), Width = 65 });
        list.Columns.Add(new DataGridTextColumn { Header = "Situação", Binding = new System.Windows.Data.Binding("Situacao"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        list.Columns.Add(new DataGridTextColumn { Header = "Observação", Binding = new System.Windows.Data.Binding("Observacao"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        list.ItemsSource = items.ToArray(); panel.Children.Add(list);
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 10) };
        string? SelectedPrinter() => printer.SelectedIndex <= 0 ? null : printer.SelectedItem as string;
        actions.Children.Add(ActionButton("Imprimir todos os pendentes", () => _ = RunPrinting(pending.Select(x => x.Id).ToArray(), SelectedPrinter())));
        actions.Children.Add(ActionButton("Imprimir selecionados", () => _ = RunPrinting(list.SelectedItems.Cast<PrintItem>().Where(x => x.Situacao == "pendente").Select(x => x.Id).ToArray(), SelectedPrinter())));
        actions.Children.Add(ActionButton("Reenviar…", () =>
        {
            if (list.SelectedItem is not PrintItem i || i.Situacao is not ("erro" or "incerto")) return;
            if (MessageBox.Show("Confira se o documento já saiu na impressora. Reenviar pode duplicar cópias. Confirmar?", "Reenvio", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            { _queue.ConfirmRetry(i.Id); ShowPage(1); }
        }));
        actions.Children.Add(ActionButton("Remover da fila", () =>
        {
            if (list.SelectedItem is PrintItem i) { _queue.Remove(i.Id); ShowPage(1); }
        }));
        actions.Children.Add(ActionButton("Limpar enviados", () => { _queue.ClearSent(); ShowPage(1); }));
        panel.Children.Add(actions);
        panel.Children.Add(Secondary("Envio sequencial pelo Word. Status 'enviado' não é confirmação de impressão física; falhas e envios incertos nunca são reenviados automaticamente."));
        return Scroll(panel);
    }
    private async Task RunPrinting(Guid[] ids, string? printer)
    {
        if (ids.Length == 0) { MessageBox.Show("Não há documentos pendentes selecionados."); return; }
        try { await _queue.SubmitAsync(ids, printer); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Fila de impressão", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { ShowPage(1); }
    }

    private FrameworkElement CodePage(string title, string fileName)
    {
        var panel = Column(); panel.Children.Add(Heading(title));
        panel.Children.Add(Secondary("Pesquisa local de códigos; funciona sem internet após importar uma tabela em TSV (código e descrição)."));
        var file = Path.Combine(_storage.DirectoryPath, "Codigos", fileName + ".tsv");
        var search = Input(); panel.Children.Add(Text("Buscar código ou descrição")); panel.Children.Add(search);
        var list = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, Height = 440 };
        list.Columns.Add(new DataGridTextColumn { Header = "Código", Binding = new System.Windows.Data.Binding("Code"), Width = 150 });
        list.Columns.Add(new DataGridTextColumn { Header = "Descrição", Binding = new System.Windows.Data.Binding("Description"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var rows = LoadCodes(file);
        void Refresh() => list.ItemsSource = rows.Where(x => x.Code.Contains(search.Text, StringComparison.OrdinalIgnoreCase) || x.Description.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).Take(300).ToArray();
        search.TextChanged += (_, _) => Refresh(); Refresh(); panel.Children.Add(list);
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Importar tabela TSV…", () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Tabela tabulada (*.tsv;*.txt)|*.tsv;*.txt" };
            if (dialog.ShowDialog() != true) return;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.Copy(dialog.FileName, file, overwrite: true); ShowPage(Navigation.SelectedIndex);
        }));
        actions.Children.Add(ActionButton("Copiar código", () => { if (list.SelectedItem is CodeRow row) System.Windows.Clipboard.SetText(row.Code); }));
        actions.Children.Add(ActionButton("Copiar código e descrição", () => { if (list.SelectedItem is CodeRow row) System.Windows.Clipboard.SetText($"{row.Code} - {row.Description}"); }));
        panel.Children.Add(actions);
        panel.Children.Add(Secondary(rows.Count == 0 ? "Tabela ainda não importada. O repositório não publica bases de pacientes nem incorpora tabelas externas sem verificar a fonte e a versão." : $"{rows.Count} registros carregados. Os primeiros 300 resultados são exibidos para manter a interface rápida."));
        return Scroll(panel);
    }
    private sealed record CodeRow(string Code, string Description);
    private static List<CodeRow> LoadCodes(string path)
    {
        if (!File.Exists(path)) return [];
        return File.ReadLines(path).Select(l => l.Split('\t', 2))
            .Where(f => f.Length == 2 && !string.IsNullOrWhiteSpace(f[0]))
            .Select(f => new CodeRow(f[0].Trim(), f[1].Trim())).ToList();
    }
}

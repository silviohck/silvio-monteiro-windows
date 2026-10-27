using Microsoft.Win32;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Orthck.Core;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Orthck.Windows;

public partial class MainWindow : Window
{
    private readonly Storage _storage;
    private readonly PrintQueue _queue;
    /// <summary>A página da fila é recriada a cada mudança; a impressora escolhida não pode voltar à padrão sem aviso.</summary>
    private string? _chosenPrinter;
    private readonly SolidColorBrush _accent = new(Color.FromRgb(48, 97, 110));
    private readonly SolidColorBrush _muted = new(Color.FromRgb(101, 117, 125));
    private readonly SolidColorBrush _surface = new(Colors.White);
    public MainWindow(Storage storage, PrintQueue queue)
    {
        InitializeComponent();
        _storage = storage;
        _queue = queue;
        DataPath.Text = "Dados locais: " + storage.FilePath;
        _queue.Changed += () => Dispatcher.InvokeAsync(() =>
        {
            if (Navigation.SelectedIndex == 1) ShowPage(1);
            UpdateQueueBadge();
        });
        // Fechar no meio do envio deixaria um Word invisível aberto e o item em situação incerta.
        Closing += (_, e) =>
        {
            if (_queue.IsProcessing && MessageBox.Show("A fila ainda está enviando documentos ao Word. Fechar agora pode deixar a impressão incompleta. Fechar mesmo assim?",
                    "Orthck", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                e.Cancel = true;
        };
        UpdateQueueBadge();
        Navigation.SelectedIndex = 0;
    }

    /// <summary>Mostra os pendentes já ao abrir (inclusive os de uma sessão anterior).</summary>
    private void UpdateQueueBadge()
    {
        if (Navigation.Items[1] is not ListBoxItem item) return;
        var pending = _storage.Data.FilaImpressao.Count(x => x.Situacao == "pendente");
        item.Content = pending == 0 ? "Fila de impressão" : $"Fila de impressão ({pending})";
    }

    private void Navigation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Navigation.SelectedIndex >= 0) ShowPage(Navigation.SelectedIndex);
    }
    private void ShowPage(int index)
    {
        PageHost.Content = index switch
        {
            0 => GeneratePage(),
            1 => QueuePage(),
            2 => PatientPage(),
            3 => LocationPage(),
            4 => TemplatePage(),
            5 => HistoryPage(),
            6 => CodePage("CID-10", "cid10"),
            7 => CodePage("SIGTAP", "sigtap"),
            8 => CodePage("TUSS", "tuss"),
            _ => Text("Em desenvolvimento")
        };
    }
    private void ImportMac_Click(object sender, RoutedEventArgs e)
    {
        if (_queue.IsProcessing) { MessageBox.Show("Aguarde a fila de impressão terminar antes de importar.", "Orthck"); return; }
        var picker = new OpenFileDialog { Title = "Importar dados.json do Orthck Mac", Filter = "Dados do Orthck (dados.json)|dados.json|JSON (*.json)|*.json" };
        if (picker.ShowDialog() != true) return;
        if (MessageBox.Show("A importação substituirá os cadastros, histórico e configurações locais atuais. Uma cópia anterior será preservada. Continuar?",
            "Confirmar importação", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Try(() => { _storage.ImportMacData(picker.FileName); UpdateQueueBadge(); ShowPage(Navigation.SelectedIndex); MessageBox.Show("Dados importados. Escolha novamente a pasta de modelos, pois os caminhos do Mac são diferentes."); });
    }

    private StackPanel Column() => new() { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Stretch };
    private TextBlock Text(string message, double size = 14) => new() { Text = message, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black, Margin = new Thickness(0, 0, 0, 9) };
    private TextBlock Secondary(string message) => new() { Text = message, Foreground = _muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 13) };
    private TextBlock Heading(string title) => new() { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 24, Foreground = _accent, Margin = new Thickness(0, 0, 0, 9) };
    private Button ActionButton(string label, Action click)
    {
        var b = new Button { Content = label };
        b.Click += (_, _) => Try(click);
        return b;
    }
    private TextBox Input(string value = "") => new() { Text = value, MinWidth = 240 };
    private FrameworkElement Scroll(StackPanel panel) => new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private Border Card(UIElement content)
    {
        return new Border { Child = content, BorderBrush = new SolidColorBrush(Color.FromRgb(215, 223, 227)), BorderThickness = new Thickness(1), Background = _surface, CornerRadius = new CornerRadius(12), Padding = new Thickness(19), Margin = new Thickness(0, 0, 0, 15) };
    }
    private void Try(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Orthck", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void OpenDocument(string path)
    {
        if (!File.Exists(path)) { MessageBox.Show("Arquivo não encontrado: " + path); return; }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    private string ChooseFolder(string title)
    {
        using var dialog = new Forms.FolderBrowserDialog { Description = title, UseDescriptionForTitle = true, ShowNewFolderButton = true };
        return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : "";
    }

    private FrameworkElement GeneratePage()
    {
        var panel = Column();
        panel.Children.Add(Heading("Gerar documento"));
        panel.Children.Add(Secondary("Preencha o modelo Word, salve o DOCX e adicione à fila para imprimir ao final dos atendimentos."));
        var templates = DiscoverTemplates(out var scanError);
        if (scanError != null) panel.Children.Add(Secondary(scanError));
        var clinicOptions = _storage.Data.Locais.Select(x => x.Nome).Concat(templates.Select(x => x.Clinic))
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        var location = new ComboBox { ItemsSource = clinicOptions, SelectedIndex = clinicOptions.Length == 0 ? -1 : 0 };
        var category = new ComboBox();
        var model = new ComboBox { DisplayMemberPath = "DisplayName" };
        var name = Input();
        var copies = new ComboBox { ItemsSource = Enumerable.Range(1, 20).ToArray(), SelectedItem = 1, Width = 85, HorizontalAlignment = HorizontalAlignment.Left };
        var mode = new ComboBox { ItemsSource = new[] { "Adicionar à fila (recomendado)", "Imprimir agora", "Só salvar" }, SelectedIndex = 0 };
        var match = new ComboBox { Visibility = Visibility.Collapsed, DisplayMemberPath = "Display" };
        var fields = Column();
        var fieldEditors = new Dictionary<string, TextBox>();
        (string Path, DateTime Stamp, IReadOnlyList<string> Names)? fieldCache = null;
        var locationCard = Column();
        locationCard.Children.Add(Text("Local de atendimento")); locationCard.Children.Add(location);
        locationCard.Children.Add(Text("Tipo de documento")); locationCard.Children.Add(category);
        locationCard.Children.Add(Text("Modelo Word")); locationCard.Children.Add(model);
        panel.Children.Add(Card(locationCard));
        var patientCard = Column();
        patientCard.Children.Add(Text("Nome do paciente")); patientCard.Children.Add(name);
        patientCard.Children.Add(match);
        patientCard.Children.Add(Text("Campos do documento", 17)); patientCard.Children.Add(fields);
        panel.Children.Add(Card(patientCard));
        var bottom = Column();
        bottom.Children.Add(Text("Depois de gerar")); bottom.Children.Add(mode);
        bottom.Children.Add(Text("Quantidade de cópias")); bottom.Children.Add(copies);
        var actions = new WrapPanel();
        var generate = new Button { Content = "Gerar documento" };
        void ClearFields() { fields.Children.Clear(); fieldEditors.Clear(); }
        var next = ActionButton("Próximo paciente", () => { name.Clear(); model.SelectedIndex = -1; ClearFields(); });
        void RefreshTemplates()
        {
            var clin = location.SelectedItem as string;
            var items = templates.Where(x => x.AppliesTo(clin)).ToArray();
            category.ItemsSource = items.Select(x => x.Category).Distinct().OrderBy(x => x).ToArray();
            category.SelectedIndex = category.Items.Count > 0 ? 0 : -1;
        }
        void RefreshModels()
        {
            var clin = location.SelectedItem as string;
            var cat = category.SelectedItem as string;
            model.ItemsSource = templates.Where(x => x.AppliesTo(clin) && x.Category == cat).ToArray();
            model.SelectedIndex = -1;
            ClearFields();
        }
        void RefreshPatient()
        {
            var possibles = _storage.Data.Pacientes.Where(x => x.Nome.Equals(name.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            var candidates = possibles.Select(x => new PatientChoice(x, $"{x.Nome} — nasc.: {x.DataNascimento} — CPF: {x.Cpf}")).ToArray();
            match.ItemsSource = candidates;
            match.Visibility = candidates.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
            match.SelectedIndex = -1;
            // Mudança no nome inicia novo contexto; nunca herdar CID/conduta do paciente anterior.
            RefreshFields();
        }
        Patient? PatientSelected()
        {
            if (match.Visibility == Visibility.Visible) return (match.SelectedItem as PatientChoice)?.Patient;
            return _storage.Data.Pacientes.FirstOrDefault(x => x.Nome.Equals(name.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        // Campos específicos do atendimento sempre recomeçam vazios ao mudar nome/homônimo/modelo.
        void RefreshFields()
        {
            ClearFields();
            if (model.SelectedItem is not TemplateInfo template) return;
            IReadOnlyList<string> names;
            try
            {
                var stamp = File.GetLastWriteTimeUtc(template.FullPath);
                if (fieldCache?.Path != template.FullPath || fieldCache.Value.Stamp != stamp)
                    fieldCache = (template.FullPath, stamp, DocxService.Fields(template.FullPath));
                names = fieldCache.Value.Names;
            }
            catch (Exception ex) { fields.Children.Add(Secondary("Não foi possível ler o modelo: " + ex.Message)); return; }
            var clinicData = FindClinic(location.SelectedItem as string);
            var defaults = ClinicalFields.Defaults(names, PatientSelected(), clinicData, DateTimeOffset.Now);
            foreach (var field in names.Where(x => !ClinicalFields.IsPatientName(x)))
            {
                fields.Children.Add(Text(field));
                var input = Input(defaults.GetValueOrDefault(field, ""));
                var key = ClinicalFields.Key(field);
                if (new[] { "DIAGNOSTICO", "CONDUTA", "OBSERVAC", "HISTORIA", "ANAMNESE",
                            "EXAME_FISICO", "RELATORIO", "JUSTIFICATIVA", "TRATAMENTO" }
                    .Any(key.Contains))
                {
                    input.AcceptsReturn = true;
                    input.TextWrapping = TextWrapping.Wrap;
                    input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    input.MinHeight = 90;
                    input.MaxHeight = 220;
                }
                fieldEditors[field] = input;
                fields.Children.Add(input);
            }
        }
        location.SelectionChanged += (_, _) => RefreshTemplates();
        category.SelectionChanged += (_, _) => RefreshModels();
        model.SelectionChanged += (_, _) => RefreshFields();
        match.SelectionChanged += (_, _) => RefreshFields();
        name.TextChanged += (_, _) => RefreshPatient();
        generate.Click += async (_, _) =>
        {
            // Um segundo clique durante a geração/impressão criaria outro DOCX e poderia imprimir em dobro.
            if (!generate.IsEnabled) return;
            generate.IsEnabled = false;
            try
            {
                if (model.SelectedItem is not TemplateInfo template) throw new InvalidOperationException("Selecione um modelo antes de gerar.");
                if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("Informe o nome do paciente.");
                var collisions = _storage.Data.Pacientes.Where(x => x.Nome.Equals(name.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
                if (collisions.Length > 1 && match.SelectedItem is not PatientChoice)
                    throw new InvalidOperationException("Existem pacientes homônimos. Selecione o cadastro pela data de nascimento/CPF.");
                var patient = PatientSelected();
                var modelFields = DocxService.Fields(template.FullPath);
                // Modelos de pastas compartilhadas não têm local próprio: vale o local escolhido.
                var clinicName = location.SelectedItem as string ?? template.ClinicLabel;
                var clinicData = FindClinic(clinicName);
                var values = ClinicalFields.Defaults(modelFields, patient, clinicData, DateTimeOffset.Now);
                foreach (var kv in fieldEditors) values[kv.Key] = kv.Value.Text;
                foreach (var f in modelFields.Where(ClinicalFields.IsPatientName)) values[f] = name.Text.Trim();
                var root = string.IsNullOrWhiteSpace(_storage.Data.PastaDocumentos)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Orthck")
                    : _storage.Data.PastaDocumentos;
                var path = PathRules.NewDocumentPath(root, clinicName, template.Category, template.DisplayName, name.Text.Trim(), DateTime.Now);
                DocxService.Fill(template.FullPath, path, values);
                if (patient == null)
                {
                    patient = new Patient { Nome = name.Text.Trim(), LocalID = clinicData?.Id };
                    _storage.Data.Pacientes.Add(patient);
                }
                ClinicalFields.UpdatePermanentFields(patient, values);
                var doc = new GeneratedDocument { Modelo = template.DisplayName, Paciente = patient.Nome, PacienteID = patient.Id,
                    Local = clinicName, Caminho = path };
                _storage.Data.Historico.Insert(0, doc);
                _storage.Save();
                var outcome = "Somente salvo.";
                if (mode.SelectedIndex <= 1)
                {
                    var item = _queue.Add(doc, (int)(copies.SelectedItem ?? 1));
                    outcome = "Adicionado à fila.";
                    if (mode.SelectedIndex == 1)
                        outcome = await _queue.SubmitAsync([item.Id], _chosenPrinter)
                            ? $"Impressão: {item.Situacao}. {item.Observacao}"
                            : "A fila já está imprimindo; o documento ficou pendente na fila.";
                }
                MessageBox.Show($"Documento salvo:\n{path}\n\n{outcome}", "Orthck");
                name.Clear(); model.SelectedIndex = -1; ClearFields();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Orthck — Gerar documento", MessageBoxButton.OK, MessageBoxImage.Warning); }
            finally { generate.IsEnabled = true; }
        };
        actions.Children.Add(generate); actions.Children.Add(next);
        bottom.Children.Add(actions); panel.Children.Add(Card(bottom));
        RefreshTemplates();
        return Scroll(panel);
    }
    private sealed record PatientChoice(Patient Patient, string Display);
    private ClinicLocation? FindClinic(string? name) => _storage.Data.Locais.FirstOrDefault(x => TemplateCatalog.SameName(x.Nome, name));
    private IReadOnlyList<TemplateInfo> DiscoverTemplates(out string? error)
    {
        error = null;
        var root = _storage.Data.PastaSincronizada;
        if (string.IsNullOrWhiteSpace(root)) return [];
        if (!Directory.Exists(root)) { error = $"Pasta de modelos indisponível: {root}. Nenhum modelo foi removido; reconecte a pasta ou escolha outra em Modelos."; return []; }
        try { return TemplateCatalog.Scan(root, _storage.Data.PastasIgnoradas, _storage.Data.PastasCompartilhadas); }
        catch (Exception ex) { error = "Não foi possível ler a pasta de modelos: " + ex.Message; return []; }
    }
}

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Orthck.Windows;

public enum Estilo { Normal, Legenda, Apoio, Titulo, Secao, Destaque, Codigo }

/// <summary>Peças de interface repetidas em todas as telas (equivalentes às do Mac: TituloSecao, AvisoVazio, seções do Form).</summary>
public static class Ui
{
    public static readonly FontFamily Icones = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily Monoespacada = new("Cascadia Mono, Consolas");

    // Glifos (Segoe Fluent Icons / MDL2).
    public const string GDocumentoNovo = "\uE8A5", GImpressora = "\uE749", GPessoas = "\uE716", GPredio = "\uE825", GDocumento = "\uE8A5",
        GRelogio = "\uE81C", GEstetoscopio = "\uE95E", GCruz = "\uE9D9", GLista = "\uE8FD", GPasta = "\uE8B7", GPastaAberta = "\uE838",
        GSincronizar = "\uE895", GEngrenagem = "\uE713", GMais = "\uE710", GLixeira = "\uE74D", GBusca = "\uE721", GCopiar = "\uE8C8",
        GColar = "\uE77F", GPessoa = "\uE77B", GPessoaMais = "\uE8FA", GOk = "\uE73E", GOkCirculo = "\uE930", GAviso = "\uE7BA",
        GFechar = "\uE711", GFecharCirculo = "\uE711", GVoltar = "\uE72B", GProximo = "\uE72A", GAtualizar = "\uE72C", GZip = "\uF012",
        GSalvar = "\uE74E", GCampos = "\uE8FD", GTelefone = "\uE717", GNota = "\uE70B", GMundo = "\uE774", GSetaBaixo = "\uE70D",
        GTrocar = "\uE8AB", GAbrir = "\uE8E5", GImportar = "\uE896", GPaleta = "\uE790", GBackup = "\uE777";

    public static TextBlock Texto(string texto, Estilo estilo = Estilo.Normal, string? cor = null)
    {
        var t = new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap };
        Formatar(t, estilo, cor);
        return t;
    }

    public static void Formatar(TextBlock t, Estilo estilo, string? cor = null)
    {
        t.SetResourceReference(TextBlock.FontSizeProperty, estilo switch
        {
            Estilo.Legenda => Tema.TamanhoLegenda,
            Estilo.Apoio or Estilo.Codigo => Tema.TamanhoApoio,
            Estilo.Titulo => Tema.TamanhoTitulo,
            Estilo.Secao => Tema.TamanhoSecao,
            Estilo.Destaque => Tema.TamanhoDestaque,
            _ => Tema.TamanhoBase
        });
        if (estilo is Estilo.Titulo or Estilo.Secao or Estilo.Destaque) t.FontWeight = FontWeights.SemiBold;
        if (estilo == Estilo.Codigo) t.FontFamily = Monoespacada;
        t.SetResourceReference(TextBlock.ForegroundProperty, cor ?? (estilo is Estilo.Legenda or Estilo.Apoio ? Tema.TextoSecundario : Tema.TextoPrincipal));
    }

    public static TextBlock Icone(string glifo, double tamanho = 15, string cor = Tema.Destaque)
    {
        var t = new TextBlock { Text = glifo, FontFamily = Icones, FontSize = tamanho, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, cor);
        return t;
    }

    /// <summary>Título de seção com ícone, negrito e cor de destaque (TituloSecao do Mac).</summary>
    public static StackPanel TituloSecao(string texto, string glifo)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 8) };
        var i = Icone(glifo, 15);
        i.Margin = new Thickness(0, 0, 9, 0);
        p.Children.Add(i);
        p.Children.Add(Texto(texto, Estilo.Secao));
        return p;
    }

    public static Border Cartao(UIElement conteudo, string fundo = Tema.Superficie, double raio = 12, double espaco = 18)
    {
        var b = new Border { Child = conteudo, CornerRadius = new CornerRadius(raio), Padding = new Thickness(espaco), BorderThickness = new Thickness(1) };
        b.SetResourceReference(Border.BackgroundProperty, fundo);
        b.SetResourceReference(Border.BorderBrushProperty, Tema.Borda);
        return b;
    }

    /// <summary>Seção de formulário agrupado (Section do Mac): título, cartão com o conteúdo e texto de rodapé.</summary>
    public static StackPanel Secao(string? titulo, string glifo, UIElement conteudo, string? rodape = null)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
        if (titulo != null) p.Children.Add(TituloSecao(titulo, glifo));
        p.Children.Add(Cartao(conteudo));
        if (rodape != null)
        {
            var r = Texto(rodape, Estilo.Apoio);
            r.Margin = new Thickness(4, 8, 4, 0);
            p.Children.Add(r);
        }
        return p;
    }

    public static StackPanel Coluna(double espaco = 10) => new() { Orientation = Orientation.Vertical, Tag = espaco };

    /// <summary>Adiciona com espaço entre os itens.</summary>
    public static T Add<T>(this Panel painel, T elemento, double antes = -1) where T : UIElement
    {
        if (elemento is FrameworkElement fe && painel.Children.Count > 0)
        {
            var espaco = antes >= 0 ? antes : painel.Tag is double d ? d : 8;
            var m = fe.Margin;
            if (painel is StackPanel { Orientation: Orientation.Horizontal } || painel is WrapPanel)
                fe.Margin = new Thickness(m.Left + espaco, m.Top, m.Right, m.Bottom);
            else
                fe.Margin = new Thickness(m.Left, m.Top + espaco, m.Right, m.Bottom);
        }
        painel.Children.Add(elemento);
        return elemento;
    }

    public static StackPanel Linha(double espaco = 8, params UIElement[] itens)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Tag = espaco };
        foreach (var i in itens) p.Add(i);
        return p;
    }

    /// <summary>Conteúdo à esquerda e à direita, como HStack com Spacer.</summary>
    public static DockPanel Extremos(UIElement esquerda, UIElement direita)
    {
        var d = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(direita, Dock.Right);
        d.Children.Add(direita);
        if (esquerda is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        d.Children.Add(esquerda);
        return d;
    }

    /// <summary>Rótulo à esquerda e controle à direita (LabeledContent do Mac).</summary>
    public static Grid Rotulado(string rotulo, UIElement controle, double largura = 190)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(largura) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var r = Texto(rotulo);
        r.VerticalAlignment = VerticalAlignment.Center;
        r.Margin = new Thickness(0, 0, 12, 0);
        g.Children.Add(r);
        Grid.SetColumn(controle, 1);
        if (controle is FrameworkElement fe && fe.VerticalAlignment == VerticalAlignment.Stretch) fe.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(controle);
        return g;
    }

    public static Button Botao(string texto, Action acao, bool principal = false, string? glifo = null, string? dica = null)
    {
        var b = new Button { Content = ConteudoDoBotao(texto, glifo), ToolTip = dica, Padding = new Thickness(14, 7, 14, 7), MinHeight = 34 };
        if (principal) b.SetResourceReference(FrameworkElement.StyleProperty, "AccentButtonStyle");
        b.Click += (_, _) => Executar(acao);
        return b;
    }

    public static object ConteudoDoBotao(string texto, string? glifo)
    {
        if (glifo == null) return texto;
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        var i = new TextBlock { Text = glifo, FontFamily = Icones, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) };
        p.Children.Add(i);
        p.Children.Add(new TextBlock { Text = texto, VerticalAlignment = VerticalAlignment.Center });
        return p;
    }

    /// <summary>Botão só com ícone, sem borda.</summary>
    public static Button BotaoIcone(string glifo, string dica, Action acao, string cor = Tema.TextoSecundario)
    {
        var b = new Button
        {
            Content = Icone(glifo, 14, cor), ToolTip = dica, Padding = new Thickness(7), MinWidth = 0, MinHeight = 0,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center
        };
        b.Click += (_, _) => Executar(acao);
        AutomationProperties(b, dica);
        return b;
    }

    private static void AutomationProperties(DependencyObject o, string nome) => System.Windows.Automation.AutomationProperties.SetName(o, nome);

    /// <summary>Texto clicável como link (botões .link do Mac).</summary>
    public static TextBlock Link(string texto, Action acao, string? glifo = null, Estilo estilo = Estilo.Normal)
    {
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Formatar(t, estilo, Tema.Destaque);
        var h = new Hyperlink { TextDecorations = null, Cursor = Cursors.Hand };
        h.SetResourceReference(TextElement.ForegroundProperty, Tema.Destaque);
        if (glifo != null) h.Inlines.Add(new Run(glifo + "  ") { FontFamily = Icones, FontSize = 13 });
        h.Inlines.Add(new Run(texto));
        h.Click += (_, _) => Executar(acao);
        h.MouseEnter += (_, _) => h.TextDecorations = TextDecorations.Underline;
        h.MouseLeave += (_, _) => h.TextDecorations = null;
        t.Inlines.Add(h);
        return t;
    }

    public static TextBox CaixaDeTexto(string valor = "", string? dica = null, bool multilinha = false)
    {
        var t = new TextBox { Text = valor, MinHeight = 34, VerticalContentAlignment = multilinha ? VerticalAlignment.Top : VerticalAlignment.Center };
        if (dica != null) Dica(t, dica);
        if (multilinha)
        {
            t.AcceptsReturn = true; t.TextWrapping = TextWrapping.Wrap; t.MinHeight = 68; t.MaxHeight = 200;
            t.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
        return t;
    }

    /// <summary>Texto de exemplo dentro da caixa vazia (placeholder).</summary>
    public static void Dica(TextBox caixa, string dica)
    {
        System.Windows.Automation.AutomationProperties.SetHelpText(caixa, dica);
        caixa.SetValue(TextBoxDica.TextoProperty, dica);
    }

    public static ComboBox Escolha<T>(IEnumerable<(string Texto, T Valor)> opcoes, T? selecionado, Action<T> aoEscolher, double minimo = 220)
    {
        var lista = opcoes.ToList();
        var c = new ComboBox { MinWidth = minimo, MinHeight = 34, VerticalContentAlignment = VerticalAlignment.Center };
        foreach (var (texto, _) in lista) c.Items.Add(texto);
        c.SelectedIndex = lista.FindIndex(o => EqualityComparer<T>.Default.Equals(o.Valor, selecionado));
        c.SelectionChanged += (_, _) => { if (c.SelectedIndex >= 0) Executar(() => aoEscolher(lista[c.SelectedIndex].Valor)); };
        return c;
    }

    public static Border Etiqueta(string texto, string cor, string fundo)
    {
        var t = Texto(texto, Estilo.Legenda, cor);
        t.FontWeight = FontWeights.SemiBold;
        var b = new Border { Child = t, CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 9, 3), VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BackgroundProperty, fundo);
        return b;
    }

    public static Border Divisor()
    {
        var b = new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6) };
        b.SetResourceReference(Border.BackgroundProperty, Tema.Borda);
        return b;
    }

    /// <summary>Área vazia com ícone, título, mensagem e botão (AvisoVazio do Mac).</summary>
    public static FrameworkElement AvisoVazio(string titulo, string glifo, string mensagem = "", string? botao = null, Action? acao = null)
    {
        var p = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 460, Margin = new Thickness(24) };
        var icone = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(20), Child = Icone(glifo, 30), HorizontalAlignment = HorizontalAlignment.Center };
        ((TextBlock)icone.Child).HorizontalAlignment = HorizontalAlignment.Center;
        icone.SetResourceReference(Border.BackgroundProperty, Tema.DestaqueSuave);
        p.Children.Add(icone);
        var t = Texto(titulo, Estilo.Titulo);
        t.TextAlignment = TextAlignment.Center; t.Margin = new Thickness(0, 14, 0, 0);
        p.Children.Add(t);
        if (mensagem.Length > 0)
        {
            var m = Texto(mensagem, Estilo.Normal, Tema.TextoSecundario);
            m.TextAlignment = TextAlignment.Center; m.Margin = new Thickness(0, 8, 0, 0);
            p.Children.Add(m);
        }
        if (botao != null && acao != null)
        {
            var b = Botao(botao, acao, principal: true);
            b.HorizontalAlignment = HorizontalAlignment.Center; b.Margin = new Thickness(0, 16, 0, 0); b.Padding = new Thickness(18, 9, 18, 9);
            p.Children.Add(b);
        }
        var fundo = new Border { Child = p };
        fundo.SetResourceReference(Border.BackgroundProperty, Tema.Fundo);
        return fundo;
    }

    /// <summary>Página rolável com margens (o Form agrupado do Mac).</summary>
    public static ScrollViewer Pagina(UIElement conteudo, double larguraMaxima = 900)
    {
        if (conteudo is FrameworkElement fe) { fe.MaxWidth = larguraMaxima; fe.Margin = new Thickness(28, 22, 28, 28); fe.HorizontalAlignment = HorizontalAlignment.Stretch; }
        var s = new ScrollViewer { Content = conteudo, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        s.SetResourceReference(Control.BackgroundProperty, Tema.Fundo);
        return s;
    }

    private static Style? _itemDeLista;

    /// <summary>Itens de lista com seleção no tom suave do Orthck (o Fluent usaria o azul do Windows).</summary>
    public static Style ItemDeLista()
    {
        if (_itemDeLista != null) return _itemDeLista;
        var borda = new FrameworkElementFactory(typeof(Border)) { Name = "fundo" };
        borda.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        borda.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        borda.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        borda.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1));
        var conteudo = new FrameworkElementFactory(typeof(ContentPresenter));
        conteudo.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        conteudo.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        borda.AppendChild(conteudo);
        var modelo = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = borda };
        var passando = new MultiTrigger();
        passando.Conditions.Add(new Condition(UIElement.IsMouseOverProperty, true));
        passando.Conditions.Add(new Condition(UIElement.IsEnabledProperty, true));
        passando.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(Tema.Borda), "fundo"));
        modelo.Triggers.Add(passando);
        var selecionado = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selecionado.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(Tema.DestaqueSuave), "fundo"));
        modelo.Triggers.Add(selecionado);
        var estilo = new Style(typeof(ListBoxItem));
        estilo.Setters.Add(new Setter(Control.TemplateProperty, modelo));
        estilo.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        estilo.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        estilo.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
        estilo.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        _itemDeLista = estilo;
        return estilo;
    }

    // MARK: Ações e mensagens

    /// <summary>Executa mostrando a mensagem de erro em vez de fechar o app.</summary>
    public static void Executar(Action acao)
    {
        try { acao(); }
        catch (Exception ex) { Erro(ex.Message); }
    }

    public static void Erro(string mensagem) =>
        MessageBox.Show(Application.Current?.MainWindow!, mensagem, "Orthck — Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);

    public static void Informar(string mensagem) =>
        MessageBox.Show(Application.Current?.MainWindow!, mensagem, "Orthck", MessageBoxButton.OK, MessageBoxImage.Information);

    public static bool Confirmar(string pergunta, string detalhe, bool perigoso = false) =>
        MessageBox.Show(Application.Current?.MainWindow!, detalhe.Length == 0 ? pergunta : $"{pergunta}\n\n{detalhe}", "Orthck",
            MessageBoxButton.YesNo, perigoso ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    // MARK: Arquivos e pastas

    public static void Abrir(string caminho) => Executar(() =>
    {
        if (!File.Exists(caminho) && !Directory.Exists(caminho)) throw new FileNotFoundException($"Não encontrado: {caminho}");
        Process.Start(new ProcessStartInfo(caminho) { UseShellExecute = true });
    });

    /// <summary>Abre o Explorador com o arquivo selecionado ("Mostrar no Finder").</summary>
    public static void MostrarNaPasta(string caminho) => Executar(() =>
    {
        if (File.Exists(caminho)) Process.Start("explorer.exe", $"/select,\"{caminho}\"");
        else if (Directory.Exists(caminho)) Process.Start("explorer.exe", $"\"{caminho}\"");
        else throw new FileNotFoundException($"Não encontrado: {caminho}");
    });

    public static void AbrirPasta(string pasta) => Executar(() =>
    {
        Directory.CreateDirectory(pasta);
        Process.Start("explorer.exe", $"\"{pasta}\"");
    });

    public static string? EscolherPasta(string titulo)
    {
        var d = new Microsoft.Win32.OpenFolderDialog { Title = titulo, Multiselect = false };
        return d.ShowDialog(Application.Current?.MainWindow) == true ? d.FolderName : null;
    }

    public static string[] EscolherDocumentos(bool varios)
    {
        var d = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Escolher modelo", Multiselect = varios,
            Filter = "Documentos do Word (*.docx;*.doc;*.rtf;*.odt)|*.docx;*.doc;*.rtf;*.odt|Todos os arquivos|*.*"
        };
        return d.ShowDialog(Application.Current?.MainWindow) == true ? d.FileNames : [];
    }

    /// <summary>"C:\Users\silvio\Documents\…" → "Documentos\…" quando estiver na pasta do usuário.</summary>
    public static string CaminhoAbreviado(string caminho)
    {
        var documentos = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (caminho.StartsWith(documentos, StringComparison.OrdinalIgnoreCase)) return "Documentos" + caminho[documentos.Length..];
        var usuario = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (caminho.StartsWith(usuario, StringComparison.OrdinalIgnoreCase)) return "~" + caminho[usuario.Length..];
        return caminho;
    }

    public static void Copiar(string texto) => Executar(() => Clipboard.SetText(texto));
}

/// <summary>Texto de exemplo nas caixas vazias, desenhado por um adorno (o WPF não tem placeholder nativo).</summary>
public static class TextBoxDica
{
    public static readonly DependencyProperty TextoProperty = DependencyProperty.RegisterAttached(
        "Texto", typeof(string), typeof(TextBoxDica), new PropertyMetadata(null, AoMudar));

    private static void AoMudar(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox caixa) return;
        caixa.Loaded += (_, _) => Atualizar(caixa);
        caixa.TextChanged += (_, _) => Atualizar(caixa);
    }

    private static void Atualizar(TextBox caixa)
    {
        var camada = AdornerLayer.GetAdornerLayer(caixa);
        if (camada == null) return;
        foreach (var a in camada.GetAdorners(caixa) ?? []) if (a is Adorno) camada.Remove(a);
        if (caixa.Text.Length == 0 && caixa.GetValue(TextoProperty) is string dica) camada.Add(new Adorno(caixa, dica));
    }

    private sealed class Adorno(TextBox caixa, string dica) : Adorner(caixa)
    {
        protected override void OnRender(DrawingContext dc)
        {
            IsHitTestVisible = false;
            var cor = Tema.Pincel(Tema.TextoSecundario).Clone();
            cor.Opacity = 0.75;
            var texto = new FormattedText(dica, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(caixa.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), caixa.FontSize, cor,
                VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = Math.Max(10, caixa.ActualWidth - 24), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            var topo = caixa.AcceptsReturn ? caixa.Padding.Top + 6 : (caixa.ActualHeight - texto.Height) / 2;
            dc.DrawText(texto, new Point(caixa.Padding.Left + 12, Math.Max(2, topo)));
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Orthck.Windows;

/// <summary>Janela de diálogo com o visual do app (as "sheets" do Mac): conteúdo e barra de botões embaixo.</summary>
public class Dialogo : Window
{
    protected readonly StackPanel Botoes = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly ContentControl _conteudo = new();
    protected readonly DockPanel Rodape = new();

    public Dialogo(string titulo, double largura, double altura)
    {
        Title = titulo;
        Width = largura; Height = altura; MinWidth = Math.Min(largura, 420); MinHeight = Math.Min(altura, 300);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI");
        SetResourceReference(FontSizeProperty, Tema.TamanhoBase);
        SetResourceReference(BackgroundProperty, Tema.Fundo);
        try { Icon = new BitmapImage(new Uri("pack://application:,,,/Recursos/Orthck.png")); } catch { }
        var doca = new DockPanel();
        var barra = new Border { Padding = new Thickness(20, 12, 20, 12), BorderThickness = new Thickness(0, 1, 0, 0), Child = Rodape };
        barra.SetResourceReference(Border.BorderBrushProperty, Tema.Borda);
        barra.SetResourceReference(Border.BackgroundProperty, Tema.Lateral);
        DockPanel.SetDock(Botoes, Dock.Right);
        Rodape.Children.Add(Botoes);
        DockPanel.SetDock(barra, Dock.Bottom);
        doca.Children.Add(barra);
        doca.Children.Add(_conteudo);
        Content = doca;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    protected UIElement Corpo { set => _conteudo.Content = value; }

    protected Button AdicionarBotao(string texto, Action acao, bool principal = false)
    {
        var b = Ui.Botao(texto, acao, principal);
        b.Margin = new Thickness(8, 0, 0, 0);
        if (principal) b.IsDefault = true;
        Botoes.Children.Add(b);
        return b;
    }

    protected void TextoDoRodape(string texto)
    {
        var t = Ui.Texto(texto, Estilo.Apoio);
        t.VerticalAlignment = VerticalAlignment.Center;
        Rodape.Children.Add(t);
    }
}

/// <summary>Lista à esquerda (lateral) e detalhe à direita (o HSplitView das telas Pacientes, Locais e Modelos).</summary>
public sealed class ListaDetalhe : Grid
{
    private readonly ContentControl _detalhe = new();
    public StackPanel Topo { get; } = Ui.Coluna(8);
    public ListBox Lista { get; } = new() { BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent };

    public ListaDetalhe(double larguraDaLista = 310)
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(larguraDaLista), MinWidth = 240 });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var doca = new DockPanel();
        Topo.Margin = new Thickness(10, 10, 10, 6);
        DockPanel.SetDock(Topo, Dock.Top);
        doca.Children.Add(Topo);
        Lista.Margin = new Thickness(6, 0, 6, 6);
        ScrollViewer.SetHorizontalScrollBarVisibility(Lista, ScrollBarVisibility.Disabled);
        doca.Children.Add(Lista);
        var esquerda = new Border { Child = doca };
        esquerda.SetResourceReference(Border.BackgroundProperty, Tema.Lateral);
        Children.Add(esquerda);
        var divisor = new GridSplitter { Width = 1, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        divisor.SetResourceReference(BackgroundProperty, Tema.Borda);
        SetColumn(divisor, 1);
        Children.Add(divisor);
        SetColumn(_detalhe, 2);
        Children.Add(_detalhe);
    }

    public object? Detalhe { set => _detalhe.Content = value; }

    /// <summary>Título de grupo dentro da lista ("Campina Grande (12)"), não selecionável.</summary>
    public void AdicionarGrupo(string titulo)
    {
        var t = Ui.Texto(titulo, Estilo.Legenda);
        t.FontWeight = FontWeights.SemiBold;
        Lista.Items.Add(new ListBoxItem { Content = t, IsEnabled = false, Focusable = false, Padding = new Thickness(8, Lista.Items.Count == 0 ? 4 : 14, 8, 2) });
    }

    public ListBoxItem AdicionarItem(UIElement conteudo, object tag)
    {
        var item = new ListBoxItem { Content = conteudo, Tag = tag, Padding = new Thickness(10, 6, 10, 6) };
        Lista.Items.Add(item);
        return item;
    }

    public void Selecionar(object? tag)
    {
        foreach (var item in Lista.Items.OfType<ListBoxItem>())
            if (Equals(item.Tag, tag)) { item.IsSelected = true; item.BringIntoView(); return; }
    }

    /// <summary>Duas linhas: título e legenda (o VStack das linhas do Mac).</summary>
    public static StackPanel LinhaDupla(string titulo, string? legenda, UIElement? aoLado = null, string? corDaLegenda = null)
    {
        var p = Ui.Coluna(1);
        var linha = Ui.Linha(6, Ui.Texto(titulo));
        if (aoLado != null) linha.Add(aoLado);
        p.Add(linha);
        if (!string.IsNullOrEmpty(legenda)) p.Add(Ui.Texto(legenda, Estilo.Legenda, corDaLegenda));
        return p;
    }
}

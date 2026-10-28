using System.Windows;
using System.Windows.Media;

namespace Orthck.Windows;

public enum TamanhoTexto { Normal = 0, Grande = 1, MuitoGrande = 2 }
public enum Aparencia { Automatica, Clara, Escura }

/// <summary>
/// Paleta "Sereno" do Orthck para Mac (Tema.swift): cinzas minerais e azul-petróleo dessaturado, sem branco ou preto puros.
/// As cores e tamanhos ficam em recursos dinâmicos, então trocar tema ou tamanho do texto atualiza a janela na hora.
/// </summary>
public static class Tema
{
    // Chaves dos recursos (usadas com SetResourceReference).
    public const string Destaque = "Destaque", Sucesso = "Sucesso", Aviso = "Aviso", Fundo = "Fundo", Lateral = "Lateral",
        Superficie = "Superficie", Borda = "Borda", DestaqueSuave = "DestaqueSuave", TextoPrincipal = "TextoPrincipal",
        TextoSecundario = "TextoSecundario", TextoNoDestaque = "TextoNoDestaque", SucessoSuave = "SucessoSuave", AvisoSuave = "AvisoSuave";
    public const string TamanhoBase = "TamanhoBase", TamanhoLegenda = "TamanhoLegenda", TamanhoApoio = "TamanhoApoio",
        TamanhoTitulo = "TamanhoTitulo", TamanhoSecao = "TamanhoSecao", TamanhoDestaque = "TamanhoDestaque", LarguraLateral = "LarguraLateral";

    private static readonly Dictionary<string, (string Claro, string Escuro)> Cores = new()
    {
        [Destaque] = ("#30616E", "#94C7CC"),
        [Sucesso] = ("#3B7059", "#96C7A6"),
        [Aviso] = ("#A65A12", "#E8A35E"),
        [Fundo] = ("#F6F8F9", "#1C2024"),
        [Lateral] = ("#EEF1F4", "#25292D"),
        [Superficie] = ("#FDFEFE", "#2D3135"),
        [Borda] = ("#D8E1E5", "#494F55"),
        [DestaqueSuave] = ("#E8F1F2", "#2F484D"),
        [SucessoSuave] = ("#E6F0EA", "#2C4237"),
        [AvisoSuave] = ("#FBEFE3", "#4A3726"),
        [TextoPrincipal] = ("#1F2A2E", "#E6ECEE"),
        [TextoSecundario] = ("#5C686D", "#A3AEB3"),
        [TextoNoDestaque] = ("#FDFEFE", "#16292D"),
    };

    public static bool Escuro { get; private set; }

    /// <summary>Aplica cores, tamanhos e o tema Fluent (claro/escuro) ao aplicativo inteiro.</summary>
    public static void Aplicar(Application app, TamanhoTexto tamanho, Aparencia aparencia)
    {
        Escuro = aparencia switch
        {
            Aparencia.Clara => false,
            Aparencia.Escura => true,
            _ => SistemaEscuro()
        };
#pragma warning disable WPF0001
        app.ThemeMode = Escuro ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
        foreach (var (chave, (claro, escuro)) in Cores)
        {
            var cor = (Color)ColorConverter.ConvertFromString(Escuro ? escuro : claro);
            var pincel = new SolidColorBrush(cor);
            pincel.Freeze();
            app.Resources[chave] = pincel;
            app.Resources[chave + "Cor"] = cor;
        }
        // O Fluent usa a cor de destaque do Windows (azul); o Orthck usa o azul-petróleo do Mac. Os estilos do Fluent
        // pegam essas cores dentro dos próprios dicionários, então elas são trocadas lá.
        TrocarCoresDeDestaque(app.Resources, (Color)app.Resources[Destaque + "Cor"], (Color)app.Resources[TextoNoDestaque + "Cor"], 0);

        // Tamanho do texto comum (o Mac usa 15, 17 ou 19 pontos; aqui o mesmo em pixels independentes).
        double baseTexto = tamanho switch { TamanhoTexto.Grande => 16, TamanhoTexto.MuitoGrande => 18, _ => 14 };
        app.Resources[TamanhoBase] = baseTexto;
        app.Resources[TamanhoLegenda] = baseTexto - 2;
        app.Resources[TamanhoApoio] = baseTexto - 1;
        app.Resources[TamanhoTitulo] = baseTexto + 6;
        app.Resources[TamanhoSecao] = baseTexto + 1;
        app.Resources[TamanhoDestaque] = baseTexto + 8;
        app.Resources[LarguraLateral] = new GridLength(tamanho switch { TamanhoTexto.Grande => 280, TamanhoTexto.MuitoGrande => 310, _ => 250 });
    }

    private static void TrocarCoresDeDestaque(ResourceDictionary dicionario, Color destaque, Color textoNoDestaque, int nivel)
    {
        if (nivel > 6) return;
        foreach (var chave in dicionario.Keys.OfType<string>().Where(k => k.Contains("Accent", StringComparison.Ordinal)).ToList())
        {
            var noDestaque = chave.Contains("TextOnAccent", StringComparison.Ordinal);
            var cor = noDestaque ? textoNoDestaque : destaque;
            // Uma cor que não puder ser trocada fica como a do Windows; nunca impede o app de abrir.
            try
            {
            switch (dicionario[chave])
            {
                case Color original:
                    dicionario[chave] = Color.FromArgb(original.A, cor.R, cor.G, cor.B);
                    break;
                case SolidColorBrush pincel:
                    var novo = new SolidColorBrush(Color.FromArgb(pincel.Color.A, cor.R, cor.G, cor.B)) { Opacity = pincel.Opacity };
                    novo.Freeze();
                    dicionario[chave] = novo;
                    break;
            }
            }
            catch (InvalidOperationException) { }
            catch (NotSupportedException) { }
        }
        foreach (var filho in dicionario.MergedDictionaries) TrocarCoresDeDestaque(filho, destaque, textoNoDestaque, nivel + 1);
    }

    /// <summary>Tema do Windows (Configurações › Personalização › Cores › "Modo do aplicativo").</summary>
    public static bool SistemaEscuro()
    {
        try
        {
            using var chave = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return chave?.GetValue("AppsUseLightTheme") is int claro && claro == 0;
        }
        catch { return false; }
    }

    public static SolidColorBrush Pincel(string chave) => (SolidColorBrush)Application.Current.Resources[chave];
}

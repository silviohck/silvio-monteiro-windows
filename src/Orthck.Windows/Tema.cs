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
        TamanhoTitulo = "TamanhoTitulo", TamanhoSecao = "TamanhoSecao", TamanhoDestaque = "TamanhoDestaque";

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
        // O Fluent usa a cor de destaque do sistema; o Orthck usa o azul-petróleo do Mac.
        var destaque = (Color)app.Resources[Destaque + "Cor"];
        foreach (var chave in new[] { "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorDark1", "SystemAccentColorLight2", "SystemAccentColorDark2", "SystemAccentColorLight3", "SystemAccentColorDark3" })
            app.Resources[chave] = destaque;
        app.Resources["AccentTextFillColorPrimaryBrush"] = app.Resources[Destaque];
        app.Resources["AccentFillColorDefaultBrush"] = app.Resources[Destaque];
        app.Resources["AccentFillColorSecondaryBrush"] = new SolidColorBrush(Color.FromArgb(0xE6, destaque.R, destaque.G, destaque.B));
        app.Resources["AccentFillColorTertiaryBrush"] = new SolidColorBrush(Color.FromArgb(0xCC, destaque.R, destaque.G, destaque.B));
        app.Resources["TextOnAccentFillColorPrimaryBrush"] = app.Resources[TextoNoDestaque];

        // Tamanho do texto comum (o Mac usa 15, 17 ou 19 pontos; aqui o mesmo em pixels independentes).
        double baseTexto = tamanho switch { TamanhoTexto.Grande => 16, TamanhoTexto.MuitoGrande => 18, _ => 14 };
        app.Resources[TamanhoBase] = baseTexto;
        app.Resources[TamanhoLegenda] = baseTexto - 2;
        app.Resources[TamanhoApoio] = baseTexto - 1;
        app.Resources[TamanhoTitulo] = baseTexto + 6;
        app.Resources[TamanhoSecao] = baseTexto + 1;
        app.Resources[TamanhoDestaque] = baseTexto + 8;
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

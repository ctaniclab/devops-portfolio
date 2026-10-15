namespace MailArchive;

public sealed record ThemePalette(bool Dark, string[] Colors, string? GradientEnd = null, bool Stars = false, int CardRadius = 14, int ButtonRadius = 10)
{
    public static IReadOnlyList<string> Names { get; } = new[] { "Системная", "Светлая", "Тёмная", "Классическая", "Стекло", "Космос", "Океан" };
    public static ThemePalette Get(string name, bool systemDark = false) => name switch
    {
        "Тёмная" => DarkPalette,
        "Системная" when systemDark => DarkPalette,
        "Классическая" => new(false, ["#F4F4FC", "#FFFFFF", "#18324F", "#506581", "#5657CC", "#EEEEFF", "#DDE3F5", "#F7F7FD", "#DFE3F3", "#6A78AF", "#47569E", "#E7E9FF"], CardRadius: 16, ButtonRadius: 12),
        // Glass is translucent over the application's gradient, not the desktop.
        "Стекло" => new(false, ["#CBDDEC", "#DCFFFFFF", "#172F48", "#405B73", "#315D9E", "#DCEBFA", "#9FB7CF", "#ECF6FC", "#CFDDED", "#597DA1", "#335C85", "#CCDEF4"], "#E1D7F1", CardRadius: 18, ButtonRadius: 12),
        "Космос" => new(true, ["#0D1026", "#EE171D35", "#F0F2FF", "#C0C6E6", "#6C48D7", "#343157", "#535473", "#202741", "#353C5C", "#A6A9D6", "#DBDEFF", "#403966"], "#30204D", Stars: true, CardRadius: 18, ButtonRadius: 12),
        "Океан" => new(false, ["#D9ECEE", "#F8FEFF", "#15383E", "#3C6068", "#176770", "#D9F0EF", "#ACCBD0", "#EDF7F8", "#C7DFE2", "#4C7F88", "#235A64", "#C7E9E7"], "#E1F0E9", CardRadius: 16),
        _ => LightPalette
    };
    private static ThemePalette LightPalette => new(false, ["#EEF1F7", "#FFFFFF", "#18243A", "#4B5E7B", "#4659BD", "#E6ECFF", "#CFD8E8", "#F4F6FB", "#D6DEED", "#637BA6", "#3C5788", "#DBE6FC"]);
    private static ThemePalette DarkPalette => new(true, ["#171D2A", "#232D40", "#F0F4FC", "#BAC8DF", "#5368C9", "#344666", "#465774", "#1C2536", "#34425C", "#A2B5D8", "#D0DEFA", "#314A72"]);
}

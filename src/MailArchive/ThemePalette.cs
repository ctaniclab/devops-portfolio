namespace MailArchive;

public enum ThemeScene { Plain, Stars, Orbital, Ocean, Aurora, Neon, Liquid, Terminal }

public sealed record ThemePalette(bool Dark, string[] Colors, string? GradientEnd = null, ThemeScene Scene = ThemeScene.Plain,
    int CardRadius = 14, int ButtonRadius = 10, bool NativeGlass = false, string Font = "Segoe UI", string TitleFont = "Segoe UI",
    double BorderSize = 1, int RowHeight = 38, string Description = "")
{
    public static IReadOnlyList<string> Names { get; } = new[] { "Системная", "Светлая", "Тёмная", "Классическая", "Liquid Glass", "Прозрачная", "Космос", "Океан", "Аврора", "Неон", "Терминал", "Бумага", "Графит" };
    public static string Migrate(string name) => name == "Стекло" ? "Liquid Glass" : Names.Contains(name) ? name : "Системная";
    public static ThemePalette Get(string name, bool systemDark = false) => name switch
    {
        "Тёмная" => DarkPalette,
        "Системная" when systemDark => DarkPalette,
        "Классическая" => new(false, ["#F4F4FC", "#FFFFFF", "#18324F", "#506581", "#5657CC", "#EEEEFF", "#DDE3F5", "#F7F7FD", "#DFE3F3", "#6A78AF", "#47569E", "#E7E9FF"], CardRadius: 16, ButtonRadius: 12, Description: "Прежний стиль · сиреневые карточки и мягкие скругления"),
        "Liquid Glass" or "Стекло" => new(false, ["#DCE8F6", "#D9FFFFFF", "#152D46", "#344B66", "#285B96", "#D7E9FA", "#C2E1FF", "#DBF5FAFF", "#C6DAEC", "#476F9C", "#204D80", "#C5DCF4"], "#D6D3ED", ThemeScene.Liquid,
            CardRadius: 30, ButtonRadius: 22, NativeGlass: true, Description: "Liquid Glass · светлое стекло, блики и объёмные капсулы"),
        "Прозрачная" => new(true, ["#11233B", "#D21A2B48", "#F3F7FF", "#DFE9FA", "#2D5790", "#293F61", "#7898C4", "#DB12233D", "#253F5C", "#9ABCE2", "#D3E7FF", "#354E76"], "#254668",
            CardRadius: 12, ButtonRadius: 8, NativeGlass: true, Description: "Тёмное прозрачное окно · фон рабочего стола с размытием"),
        "Космос" => new(true, ["#080D15", "#EE111C29", "#F6F1E8", "#C8C6C0", "#8A4B17", "#313028", "#71604A", "#182431", "#293441", "#C19F6A", "#FFE3AE", "#39372F"], "#182A3B", ThemeScene.Orbital,
            CardRadius: 8, ButtonRadius: 6, TitleFont: "Bahnschrift", Description: "Орбитальная станция · графит, янтарь, планета и линии орбит"),
        "Бумага" => PaperPalette,
        "Графит" => new(true, ["#141414", "#222222", "#FAFAFA", "#C6C6C6", "#555555", "#363636", "#686868", "#292929", "#3B3B3B", "#AAAAAA", "#EEEEEE", "#404040"],
            CardRadius: 6, ButtonRadius: 4, Description: "Графит · нейтральный серый, без синего оттенка и фоновых эффектов"),
        "Океан" => new(true, ["#072E3C", "#E00B3444", "#E9FBFF", "#B9DFE7", "#156172", "#164F61", "#44879A", "#0B3A4C", "#144A5D", "#88C5D5", "#CEF7FF", "#205769"], "#0A5964", ThemeScene.Ocean,
            CardRadius: 20, ButtonRadius: 10, Description: "Глубина океана · волны, бирюза и морская пена"),
        "Аврора" => new(true, ["#171729", "#E0212339", "#FBF1FE", "#E3CDE8", "#7653A8", "#473C62", "#8A759F", "#2B2D46", "#493F5E", "#BAA1D1", "#EBD4FF", "#57466D"], "#493553", ThemeScene.Aurora,
            CardRadius: 24, ButtonRadius: 16, Description: "Луна над лесом · сиреневое небо и северное сияние"),
        "Неон" => new(true, ["#100C25", "#E816122E", "#F8F4FF", "#D0CEF8", "#792889", "#352348", "#24A7BD", "#201A36", "#2D2547", "#65D9E6", "#C1FCFF", "#48315C"], "#22183A", ThemeScene.Neon,
            CardRadius: 3, ButtonRadius: 3, TitleFont: "Bahnschrift", BorderSize: 1.5, Description: "Киберпанк · лазерная сетка, циановые линии и фуксия"),
        "Терминал" => new(true, ["#09120F", "#0D1D15", "#B9F6CB", "#9AC9A9", "#20543D", "#173B2A", "#548969", "#10271C", "#203D2C", "#83BF96", "#C5F5D1", "#214B34"], Scene: ThemeScene.Terminal,
            CardRadius: 0, ButtonRadius: 0, Font: "Consolas", TitleFont: "Consolas", RowHeight: 34, Description: "Терминал · моноширинный шрифт, зелёный фосфор и прямые линии"),
        _ => LightPalette
    };
    private static ThemePalette PaperPalette => new(false, ["#F6F1E6", "#FFFCF5", "#302B22", "#62553F", "#845525", "#F1E4CC", "#CDBF9F", "#F7EEDD", "#E7DCC6", "#8F7954", "#68502D", "#EAD8B9"],
        CardRadius: 4, ButtonRadius: 4, TitleFont: "Georgia", Description: "Тёплая бумага · книжный заголовок, охра и чёткие границы");
    private static ThemePalette LightPalette => new(false, ["#F4F6F8", "#FFFFFF", "#18212B", "#4B5968", "#175FA8", "#E4EFFB", "#B5C4D4", "#FFFFFF", "#DFE7EF", "#627D97", "#294D70", "#DAE9F8"],
        CardRadius: 12, ButtonRadius: 8, Description: "Светлая · белые поверхности, нейтральный фон и синие акценты без прозрачности");
    private static ThemePalette DarkPalette => new(true, ["#171D2A", "#232D40", "#F0F4FC", "#BAC8DF", "#5368C9", "#344666", "#465774", "#1C2536", "#34425C", "#A2B5D8", "#D0DEFA", "#314A72"], Description: "Спокойная тёмная тема · матовые панели без эффектов");
}

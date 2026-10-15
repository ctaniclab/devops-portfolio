using MailArchive;

internal static class ThemeChecks
{
    public static void Run(Action<bool, string> check)
    {
        double[] Color(string hex)
        {
            hex = hex.TrimStart('#');
            return new[] { Convert.ToInt32(hex[^6..^4], 16) / 255d, Convert.ToInt32(hex[^4..^2], 16) / 255d,
                Convert.ToInt32(hex[^2..], 16) / 255d, hex.Length == 8 ? Convert.ToInt32(hex[..2], 16) / 255d : 1 };
        }
        double[] Over(double[] color, double[] background) => new[] {
            color[0] * color[3] + background[0] * (1-color[3]), color[1] * color[3] + background[1] * (1-color[3]), color[2] * color[3] + background[2] * (1-color[3]), 1 };
        double L(double[] c)
        {
            double F(double x) => x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4);
            return .2126 * F(c[0]) + .7152 * F(c[1]) + .0722 * F(c[2]);
        }
        double Contrast(double[] a, double[] b) => (Math.Max(L(a), L(b)) + .05) / (Math.Min(L(a), L(b)) + .05);
        foreach (string name in ThemePalette.Names)
        {
            var palette = ThemePalette.Get(name);
            bool readable = true;
            foreach (string backdrop in new[] { palette.Colors[0], palette.GradientEnd ?? palette.Colors[0] }.Concat(palette.NativeGlass ? new[] { "#000000", "#FFFFFF" } : Array.Empty<string>()))
                foreach (int background in palette.NativeGlass ? new[] { 1, 5, 7, 11 } : new[] { 0, 1, 5, 7, 11 })
                    foreach (int foreground in new[] { 2, 3 })
                        readable &= Contrast(Color(palette.Colors[foreground]), Over(Color(palette.Colors[background]), Color(backdrop))) >= 4.5;
            check(readable && Contrast(Color("#FFFFFF"), Color(palette.Colors[4])) >= 4.5,
                name + ": labels, selected rows, glass layers and primary buttons have readable contrast");
        }
        check(ThemePalette.Get("Космос").Dark && ThemePalette.Get("Системная", true).Dark && !ThemePalette.Get("Классическая").Dark,
            "Space uses a dark reader; classic and system themes select the intended palette");
        check(ThemePalette.Migrate("Стекло") == "Liquid Glass" && ThemePalette.Get("Liquid Glass").NativeGlass && ThemePalette.Get("Прозрачная").NativeGlass,
            "Old glass selection migrates to Liquid Glass and both glass themes request native transparency");
        check(ThemePalette.Get("Светлая").TitleFont == "Georgia" && ThemePalette.Get("Светлая").CardRadius == 4
            && ThemePalette.Get("Классическая").CardRadius == 16 && ThemePalette.Get("Классическая").ButtonRadius == 12,
            "Paper and classic differ in typography, geometry and palette while classic preserves the old style");
        check(ThemePalette.Get("Терминал").Font == "Consolas" && ThemePalette.Get("Терминал").CardRadius == 0
            && ThemePalette.Get("Аврора").Scene == ThemeScene.Aurora && ThemePalette.Get("Неон").Scene == ThemeScene.Neon
            && ThemePalette.Get("Океан").Scene == ThemeScene.Ocean,
            "Terminal, aurora, neon and ocean use distinct typography, geometry and original vector scenery");
    }
}

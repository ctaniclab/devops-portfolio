using System.Windows;
using System.Windows.Media;

namespace MailArchive;

// Original vector backgrounds; no downloaded wallpapers or third-party theme code.
internal static class ThemeArtwork
{
    public static Color Color(string value) => (Color)ColorConverter.ConvertFromString(value);
    public static SolidColorBrush Brush(string value) => new(Color(value));

    public static Brush Background(ThemePalette palette, bool translucent)
    {
        var art = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, 1400, 900)) };
        var background = new LinearGradientBrush(Color(palette.Colors[0]), Color(palette.GradientEnd ?? palette.Colors[0]), 35);
        if (translucent) background.Opacity = palette.Dark ? .12 : .16;
        art.Children.Add(new GeometryDrawing(background, null, new RectangleGeometry(new Rect(0, 0, 1400, 900))));
        void Path(string data, string fill) => art.Children.Add(new GeometryDrawing(Brush(fill), null, Geometry.Parse(data)));
        void Stroke(string data, string color, double width) => art.Children.Add(new GeometryDrawing(null, new Pen(Brush(color), width), Geometry.Parse(data)));
        void Orb(double x, double y, double radius, string center, string edge) => art.Children.Add(new GeometryDrawing(
            new RadialGradientBrush(Color(center), Color(edge)), null, new EllipseGeometry(new Point(x, y), radius, radius)));
        switch (palette.Scene)
        {
            case ThemeScene.Stars:
                for (int i = 0; i < 90; i++)
                    art.Children.Add(new GeometryDrawing(new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(75 + i % 145), 217, 222, 255)), null,
                        new EllipseGeometry(new Point((i * 137 + 31) % 1400, (i * 251 + 19) % 900), i % 4 == 0 ? 1.5 : .7, i % 4 == 0 ? 1.5 : .7)));
                break;
            case ThemeScene.Liquid:
                Orb(230, 190, 420, "#6D88D5FF", "#0088D5FF"); Orb(1100, 650, 500, "#5FBA9EF2", "#00BA9EF2");
                Stroke("M -160 720 C 200 850 80 80 590 100 C 980 120 550 750 1100 650 C 1450 600 1320 260 1550 220", "#3566B5F0", 135);
                Stroke("M -160 720 C 200 850 80 80 590 100 C 980 120 550 750 1100 650 C 1450 600 1320 260 1550 220", "#85F3FAFF", 3);
                Stroke("M -80 730 C 450 1010 330 120 690 180 C 1030 250 710 750 1200 780 C 1420 790 1380 450 1540 420", "#437D8DE2", 90);
                Stroke("M -80 730 C 450 1010 330 120 690 180 C 1030 250 710 750 1200 780 C 1420 790 1380 450 1540 420", "#85FFFFFF", 2);
                break;
            case ThemeScene.Ocean:
                Orb(1100, 50, 650, "#AA12A9BD", "#0012A9BD");
                Path("M 0 450 C 260 260 450 600 740 440 C 1030 280 1190 490 1400 360 L 1400 900 L 0 900 Z", "#552AC3D0");
                Path("M 0 560 C 310 410 370 750 730 550 C 1050 360 1220 630 1400 480 L 1400 900 L 0 900 Z", "#A008475E");
                Path("M 0 730 C 360 460 550 810 920 670 C 1190 570 1310 780 1400 650 L 1400 900 L 0 900 Z", "#B009293F");
                Stroke("M 0 450 C 260 260 450 600 740 440 C 1030 280 1190 490 1400 360", "#688DEAF1", 2);
                Stroke("M 0 560 C 310 410 370 750 730 550 C 1050 360 1220 630 1400 480", "#3D9BF6EA", 2);
                break;
            case ThemeScene.Aurora:
                Orb(900, 440, 620, "#9C96628C", "#0096628C");
                Stroke("M -80 230 C 390 500 570 -40 1090 190 C 1260 270 1330 350 1470 370", "#263FE4C1", 100);
                Stroke("M -80 230 C 390 500 570 -40 1090 190 C 1260 270 1330 350 1470 370", "#259CDACF", 36);
                art.Children.Add(new GeometryDrawing(Brush("#F8EBFF"), null, new EllipseGeometry(new Point(1170, 135), 36, 36)));
                art.Children.Add(new GeometryDrawing(Brush("#332A46"), null, new EllipseGeometry(new Point(1183, 122), 33, 33)));
                Path("M 0 660 L 150 510 L 310 600 L 510 390 L 750 620 L 990 440 L 1200 590 L 1400 430 L 1400 900 L 0 900 Z", "#78584D76");
                Path("M 0 760 L 220 640 L 450 730 L 760 570 L 1050 720 L 1400 580 L 1400 900 L 0 900 Z", "#BE302C4D");
                for (int i = 0; i < 55; i++)
                {
                    double x = i * 29 - 30, y = 790 + Math.Sin(i * .41) * 45, height = 35 + i * 17 % 95;
                    var tree = new StreamGeometry();
                    using (var g = tree.Open())
                    { g.BeginFigure(new Point(x, y - height), true, true); g.LineTo(new Point(x - height * .25, y), true, false); g.LineTo(new Point(x + height * .25, y), true, false); }
                    art.Children.Add(new GeometryDrawing(Brush("#D5182032"), null, tree));
                }
                break;
            case ThemeScene.Neon:
                Orb(700, 430, 520, "#804C2874", "#004C2874");
                for (int i = -6; i <= 18; i++)
                    art.Children.Add(new GeometryDrawing(null, new Pen(Brush("#463AD9ED"), 1), new LineGeometry(new Point(700, 400), new Point(i * 100, 900))));
                for (int i = 1; i < 14; i++)
                {
                    double y = 400 + i * i * 3;
                    art.Children.Add(new GeometryDrawing(null, new Pen(Brush("#5547DCEB"), 1), new LineGeometry(new Point(0, y), new Point(1400, y))));
                }
                Stroke("M 0 400 L 1400 400", "#4085ECFA", 9); Stroke("M 0 400 L 1400 400", "#CF3ADCED", 1);
                break;
            case ThemeScene.Terminal:
                for (int y = 0; y < 900; y += 5)
                    art.Children.Add(new GeometryDrawing(null, new Pen(Brush("#0D58E89D"), 1), new LineGeometry(new Point(0, y), new Point(1400, y))));
                break;
        }
        var brush = new DrawingBrush(art) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 1400, 900) }; brush.Freeze(); return brush;
    }

    public static Brush Panel(ThemePalette palette)
    {
        if (palette.Scene != ThemeScene.Liquid) return Brush(palette.Colors[1]);
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(.65, 1) };
        fill.GradientStops.Add(new(Color("#F5FFFFFF"), 0));
        fill.GradientStops.Add(new(Color("#CFFFFFFF"), .25));
        fill.GradientStops.Add(new(Color("#D5F5FAFF"), .75));
        fill.GradientStops.Add(new(Color("#EEFFFFFF"), 1));
        fill.Freeze(); return fill;
    }
}

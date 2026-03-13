using System.Windows.Media;

namespace VE.Windows.Extensions;

public static class ColorExtensions
{
    public static Color FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        return hex.Length switch
        {
            6 => Color.FromRgb(
                System.Convert.ToByte(hex[..2], 16),
                System.Convert.ToByte(hex[2..4], 16),
                System.Convert.ToByte(hex[4..6], 16)),
            8 => Color.FromArgb(
                System.Convert.ToByte(hex[..2], 16),
                System.Convert.ToByte(hex[2..4], 16),
                System.Convert.ToByte(hex[4..6], 16),
                System.Convert.ToByte(hex[6..8], 16)),
            _ => Colors.Black
        };
    }

    public static Color WithOpacity(this Color color, double opacity)
    {
        return Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B);
    }

    public static SolidColorBrush ToBrush(this Color color)
    {
        return new SolidColorBrush(color);
    }

    public static string ToHex(this Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}

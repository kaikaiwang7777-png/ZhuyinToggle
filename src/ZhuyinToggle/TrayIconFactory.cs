using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ZhuyinToggle;

// 模式只能由已驗證的狀態來源提供；目前產品只使用 Unknown。
internal enum OutputState { Unknown, Traditional, Simplified }

internal static class TrayIconFactory
{
    public static Icon Create(OutputState state, int size)
    {
        if (size is < 16 or > 256) throw new ArgumentOutOfRangeException(nameof(size));
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var background = state switch
            {
                OutputState.Traditional => Color.FromArgb(0, 90, 180),
                OutputState.Simplified => Color.FromArgb(0, 128, 74),
                _ => Color.FromArgb(76, 81, 89)
            };
            graphics.Clear(background);
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            using var font = new Font("Microsoft JhengHei UI", size * 0.70f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.White);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            string text = state switch { OutputState.Traditional => "繁", OutputState.Simplified => "简", _ => "?" };
            graphics.DrawString(text, font, brush, new RectangleF(0, 0, size, size), format);
        }
        IntPtr handle = bitmap.GetHicon();
        try { using var temporary = Icon.FromHandle(handle); return (Icon)temporary.Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}

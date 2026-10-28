using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using KeyStats.Helpers;

namespace KeyStats.Views.Controls;

/// <summary>
/// 鼠标寿命分区图：自绘鼠标外形，左键 / 右键 / 中键(滚轮) / 侧键
/// 各自按「已点击 / 额定寿命」着色（绿=健康 → 橙 → 红=磨损），区内显示剩余百分比。
/// </summary>
public class MouseLifespanControl : FrameworkElement
{
    public sealed class ButtonWear
    {
        public ButtonWear(long used, long rated)
        {
            Used = Math.Max(0, used);
            Rated = Math.Max(1, rated);
        }

        public long Used { get; }
        public long Rated { get; }
        public double RemainingPercent => Math.Max(0.0, 100.0 - Used * 100.0 / Rated);
    }

    private ButtonWear _left = new(0, 1);
    private ButtonWear _right = new(0, 1);
    private ButtonWear _middle = new(0, 1);
    private ButtonWear _side = new(0, 1);
    private bool _hasSideButtons = true;

    // 最近一次渲染的命中区域（供点击检测）
    private Rect _hitLeft;
    private Rect _hitRight;
    private Rect _hitMiddle;
    private Rect _hitSide1;
    private Rect _hitSide2;

    /// <summary>点击某个分区时触发，参数：Left / Right / Middle / Side。</summary>
    public event Action<string>? ButtonClicked;

    public MouseLifespanControl()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        var point = e.GetPosition(this);
        string? hit = null;
        if (_hitLeft.Contains(point))
        {
            hit = "Left";
        }
        else if (_hitRight.Contains(point))
        {
            hit = "Right";
        }
        else if (_hitMiddle.Contains(point))
        {
            hit = "Middle";
        }
        else if (_hasSideButtons && (_hitSide1.Contains(point) || _hitSide2.Contains(point)))
        {
            hit = "Side";
        }

        if (hit != null)
        {
            ButtonClicked?.Invoke(hit);
            e.Handled = true;
        }
    }

    /// <summary>
    /// 传入四个分区的磨损数据；侧键传入 null 表示该鼠标没有侧键（绘制时隐藏）。
    /// </summary>
    public void Apply(ButtonWear left, ButtonWear right, ButtonWear middle, ButtonWear? side)
    {
        _left = left;
        _right = right;
        _middle = middle;
        _side = side ?? new ButtonWear(0, 1);
        _hasSideButtons = side != null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var isDark = ThemeManager.Instance.IsDarkTheme;
        var textPrimary = ResolveColor("TextPrimaryBrush", isDark ? Colors.White : Colors.Black);
        var borderColor = isDark ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0);
        var surface = ResolveColor("SurfaceBrush", isDark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(250, 250, 250));

        // 布局：左侧鼠标主体，右侧图例说明
        var bodyWidth = Math.Min(ActualHeight * 0.62, ActualWidth * 0.34);
        var bodyHeight = bodyWidth * 1.62;
        var origin = new Point(
            Math.Max(8, ActualWidth * 0.10),
            (ActualHeight - bodyHeight) / 2);
        var body = new Rect(origin, new Size(bodyWidth, bodyHeight));

        var borderPen = new Pen(Frozen(borderColor), 1.0);
        var radius = bodyWidth * 0.30;

        // 主体底
        dc.DrawRoundedRectangle(Frozen(surface), borderPen, body, radius, radius);

        // 按键区裁剪进主体圆角轮廓，避免色块溢出
        dc.PushClip(new RectangleGeometry(body, radius, radius));

        // 按键区色块内缩，保证角部圆弧始终落在主体圆角轮廓内
        var buttonArea = new Rect(body.Left + 6, body.Top + 6, body.Width - 12, body.Height * 0.44);
        var wheelWidth = Math.Max(10, bodyWidth * 0.16);
        var wheelRect = new Rect(
            body.Left + (body.Width - wheelWidth) / 2,
            buttonArea.Top + 4,
            wheelWidth,
            buttonArea.Height * 0.42);
        var halfGap = wheelWidth / 2 + 3;
        var leftRect = new Rect(buttonArea.Left, buttonArea.Top, buttonArea.Width / 2 - halfGap + 1, buttonArea.Height);
        var rightRect = new Rect(buttonArea.Left + buttonArea.Width / 2 + halfGap - 1, buttonArea.Top, leftRect.Width, buttonArea.Height);
        var buttonRadius = radius * 0.55;

        // 记录命中区域（侧键在下方块里补充）
        _hitLeft = leftRect;
        _hitRight = rightRect;
        _hitMiddle = wheelRect;

        // 左右键
        dc.DrawRoundedRectangle(Frozen(WearColor(_left, isDark)), null, leftRect, buttonRadius, buttonRadius);
        dc.DrawRoundedRectangle(Frozen(WearColor(_right, isDark)), null, rightRect, buttonRadius, buttonRadius);

        // 滚轮 / 中键
        dc.DrawRoundedRectangle(Frozen(WearColor(_middle, isDark)), borderPen, wheelRect, wheelWidth / 2, wheelWidth / 2);

        // 分隔线：按键区与握持区
        dc.DrawLine(borderPen, new Point(body.Left + 4, buttonArea.Bottom), new Point(body.Right - 4, buttonArea.Bottom));

        dc.Pop();

        // 重描主体轮廓（键块贴边会盖住边框）
        dc.DrawRoundedRectangle(null, borderPen, body, radius, radius);

        // 侧键（左边缘中部两个小块）
        if (_hasSideButtons)
        {
            var sideWidth = Math.Max(8, bodyWidth * 0.12);
            var sideHeight = bodyHeight * 0.10;
            var sideX = body.Left - sideWidth * 0.55;
            var sideY1 = body.Top + bodyHeight * 0.40;
            var sideY2 = sideY1 + sideHeight + 6;
            var sideBrush = Frozen(WearColor(_side, isDark));
            _hitSide1 = new Rect(sideX, sideY1, sideWidth, sideHeight);
            _hitSide2 = new Rect(sideX, sideY2, sideWidth, sideHeight);
            dc.DrawRoundedRectangle(sideBrush, borderPen, _hitSide1, 4, 4);
            dc.DrawRoundedRectangle(sideBrush, borderPen, _hitSide2, 4, 4);
        }

        // 键区内文字：剩余百分比
        DrawCenteredText(dc, PercentText(_left), leftRect, Math.Max(10, bodyWidth * 0.16), textPrimary, FontWeights.SemiBold);
        DrawCenteredText(dc, PercentText(_right), rightRect, Math.Max(10, bodyWidth * 0.16), textPrimary, FontWeights.SemiBold);
        // 右侧图例
        var legendX = body.Right + Math.Max(16, ActualWidth * 0.06);
        var legendWidth = ActualWidth - legendX - 8;
        if (legendWidth > 40)
        {
            var entries = new List<(string Label, ButtonWear Wear)>
            {
                ("左键", _left),
                ("右键", _right),
                ("中键 / 滚轮", _middle),
            };
            if (_hasSideButtons)
            {
                entries.Add(("侧键", _side));
            }

            var lineHeight = Math.Min(30, ActualHeight / (entries.Count + 0.5));
            var y = (ActualHeight - lineHeight * entries.Count) / 2;
            foreach (var (label, wear) in entries)
            {
                var swatch = new Rect(legendX, y + lineHeight / 2 - 5, 10, 10);
                dc.DrawRoundedRectangle(Frozen(WearColor(wear, isDark)), null, swatch, 2.5, 2.5);

                var labelText = CreateText(label, 12, FontWeights.Medium, Frozen(textPrimary));
                dc.DrawText(labelText, new Point(legendX + 16, y + lineHeight / 2 - labelText.Height / 2));

                var detail = CreateText(
                    $"{wear.RemainingPercent:F1}% 剩 · {FormatCount(wear.Used)} 击",
                    11, FontWeights.Regular, Frozen(Color.FromArgb(200, textPrimary.R, textPrimary.G, textPrimary.B)));
                dc.DrawText(detail, new Point(legendX + 16 + labelText.Width + 8, y + lineHeight / 2 - detail.Height / 2));

                y += lineHeight;
            }
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeManager.Instance.ThemeChanged += OnThemeChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        Dispatcher.BeginInvoke(new Action(InvalidateVisual));
    }

    private static string PercentText(ButtonWear wear)
    {
        return wear.RemainingPercent >= 99.95 ? "100%" : $"{wear.RemainingPercent:F1}%";
    }

    private static Color WearColor(ButtonWear wear, bool isDark)
    {
        var wearRatio = Math.Max(0.0, Math.Min(1.0, wear.Used / (double)wear.Rated));
        var healthy = isDark ? Color.FromRgb(74, 160, 100) : Color.FromRgb(67, 160, 71);
        var mid = Color.FromRgb(230, 145, 56);
        var worn = Color.FromRgb(209, 72, 65);
        return wearRatio < 0.5
            ? Blend(healthy, mid, wearRatio * 2)
            : Blend(mid, worn, (wearRatio - 0.5) * 2);
    }

    private static string FormatCount(long value)
    {
        if (value >= 100_000_000L)
        {
            return $"{value / 100_000_000.0:F2} 亿";
        }
        if (value >= 10_000L)
        {
            return $"{value / 10_000.0:F1} 万";
        }
        return value.ToString("N0", CultureInfo.CurrentCulture);
    }

    private void DrawCenteredText(DrawingContext dc, string text, Rect area, double fontSize, Color color, FontWeight weight)
    {
        var formatted = CreateText(text, fontSize, weight, Frozen(color));
        if (formatted.Width > area.Width - 4)
        {
            formatted = CreateText(text, Math.Max(7, fontSize * area.Width / (formatted.Width + 4)), weight, Frozen(color));
        }
        dc.DrawText(formatted, new Point(
            area.Left + (area.Width - formatted.Width) / 2,
            area.Top + (area.Height - formatted.Height) / 2));
    }

    private FormattedText CreateText(string value, double fontSize, FontWeight weight, Brush brush)
    {
        var typeface = new Typeface(new FontFamily("Segoe UI, Microsoft YaHei"), FontStyles.Normal, weight, FontStretches.Normal);
        return new FormattedText(
            value,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    private static Color ResolveColor(string key, Color fallback)
    {
        var resource = Application.Current?.Resources[key];
        if (resource is SolidColorBrush brush)
        {
            return brush.Color;
        }
        if (resource is Color color)
        {
            return color;
        }
        return fallback;
    }

    private static Color Blend(Color from, Color to, double t)
    {
        var progress = Math.Max(0, Math.Min(1, t));
        return Color.FromArgb(
            (byte)Math.Round(from.A + (to.A - from.A) * progress),
            (byte)Math.Round(from.R + (to.R - from.R) * progress),
            (byte)Math.Round(from.G + (to.G - from.G) * progress),
            (byte)Math.Round(from.B + (to.B - from.B) * progress));
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

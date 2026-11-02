using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Agamemnon.Drawing;

namespace Agamemnon.Render;

/// <summary>2D 画布渲染选项：网格/坐标轴/名称标签/标题。全部只影响显示。</summary>
public sealed record CanvasRenderOptions(bool Grid = true, bool Axes = true, bool Labels = true, string Title = null);

/// <summary>
/// 2D 画布帧渲染接口：只读文档快照 + 视图状态 → PNG 帧。颜色约定：普通=钢蓝、选中=橙红加粗、
/// 锁定=灰、禁用(Enabled=false)=浅蓝虚线；Visible=false 不绘制。不修改文档、不含设备语义。
/// </summary>
public interface ICanvasRenderer
{
    CanvasFrame Render(DesignDocumentSnapshot snapshot, ICanvasView view, CanvasRenderOptions options = null);
}

public sealed class CanvasRenderer : ICanvasRenderer
{
    public CanvasFrame Render(DesignDocumentSnapshot snapshot, ICanvasView view, CanvasRenderOptions options = null)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (view == null) throw new ArgumentNullException(nameof(view));
        options ??= new CanvasRenderOptions();
        var vs = view.Snapshot;
        const int width = 900, height = 700;
        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.White);
            using (var titleFont = new Font("Microsoft Sans Serif", 13, FontStyle.Bold))
            using (var labelFont = new Font("Microsoft Sans Serif", 9))
            {
                if (!string.IsNullOrEmpty(options.Title))
                    g.DrawString(options.Title, titleFont, Brushes.Black, 14, 10);
                // 网格：步长自适应，保证格距不小于 36 px
                if (options.Grid)
                {
                    double[] steps = { 0.1, 0.5, 1, 5, 10, 50 };
                    double stepMm = steps.FirstOrDefault(s => s * vs.PixelsPerMm >= 36, 50);
                    using var grid = new Pen(Color.Gainsboro, 1);
                    for (double x = Math.Floor(ScreenToDesignX(view, 0) / stepMm) * stepMm; ; x += stepMm)
                    {
                        var sx = (float)view.DesignToScreen(x, 0).X;
                        if (sx > width - 8) break;
                        if (sx >= 8) g.DrawLine(grid, sx, 8, sx, height - 8);
                    }
                    for (double y = Math.Floor(ScreenToDesignY(view, height) / stepMm) * stepMm; ; y += stepMm)
                    {
                        var sy = (float)view.DesignToScreen(0, y).Y;
                        if (sy < 8) break;
                        if (sy <= height - 8) g.DrawLine(grid, 8, sy, width - 8, sy);
                    }
                }
                // 坐标轴（设计原点）
                if (options.Axes)
                {
                    using var axis = new Pen(Color.Gray, 1.5f);
                    var o = view.DesignToScreen(0, 0);
                    g.DrawLine(axis, 8, (float)o.Y, width - 8, (float)o.Y);
                    g.DrawLine(axis, (float)o.X, 8, (float)o.X, height - 8);
                    g.DrawString("+X", labelFont, Brushes.Gray, width - 30, (float)Math.Clamp(o.Y + 4, 4, height - 20));
                    g.DrawString("+Y", labelFont, Brushes.Gray, (float)Math.Clamp(o.X + 5, 4, width - 30), 8);
                }
                // 对象：按文档树序绘制，后画者在上
                foreach (var o in snapshot.Objects)
                {
                    if (!o.Visible) continue;
                    var paths = DesignGeometry.Generate(o);
                    var pen = o.Locked ? new Pen(Color.Gray, 1.6f)
                        : !o.Enabled ? new Pen(Color.LightSteelBlue, 1.2f) { DashStyle = DashStyle.Dash }
                        : snapshot.SelectedId == o.Id ? new Pen(Color.OrangeRed, 2.6f)
                        : new Pen(Color.SteelBlue, 1.9f);
                    using (pen)
                    {
                        foreach (var path in paths)
                        {
                            var points = path.Select(p => Point(view, p)).ToArray();
                            if (points.Length > 1) g.DrawLines(pen, points);
                        }
                    }
                    if (options.Labels)
                    {
                        var first = Point(view, paths[0][0]);
                        using var labelBrush = new SolidBrush(pen.Color);
                        g.DrawString(o.Name, labelFont, labelBrush, first.X + 5, first.Y - 16);
                    }
                }
                // 1 mm 比例尺
                float barEnd = 20f + (float)vs.PixelsPerMm;
                g.DrawLine(Pens.Black, 20f, height - 22, barEnd, height - 22);
                g.DrawLine(Pens.Black, 20f, height - 27, 20f, height - 17);
                g.DrawLine(Pens.Black, barEnd, height - 27, barEnd, height - 17);
                g.DrawString("1 mm", labelFont, Brushes.Black, barEnd + 2, height - 34);
                g.DrawString("设计坐标系：mm，+X 右 / +Y 上", labelFont, Brushes.DimGray, width - 260, height - 22);
            }
        }
        return new CanvasFrame(width, height, ToPng(bitmap));
    }

    private static PointF Point(ICanvasView view, DesignPoint3 p) { var s = view.DesignToScreen(p.X, p.Y); return new((float)s.X, (float)s.Y); }
    private static double ScreenToDesignX(ICanvasView view, double x) => view.ScreenToDesign(x, 0).X;
    private static double ScreenToDesignY(ICanvasView view, double y) => view.ScreenToDesign(0, y).Y;
    internal static byte[] ToPng(Image image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}

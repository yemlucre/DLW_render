using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using Agamemnon.Drawing;

namespace Agamemnon.Render;

/// <summary>3D 线框渲染选项：深度着色、地面网格、包围盒、名称标签、标题。</summary>
public sealed record Render3DOptions(bool DepthShade = true, bool GroundGrid = true, bool BoundsBox = true,
    bool Labels = true, string Title = null);

/// <summary>
/// 3D 场景帧渲染接口：只读场景 + 相机 → PNG 线框帧（软件投影，不依赖 WPF/Helix）。
/// 颜色约定与 2D 一致：普通=钢蓝、选中=橙红加粗、锁定=灰、禁用=浅蓝虚线；Visible=false 不绘制。
/// </summary>
public interface ISceneRenderer3D
{
    CanvasFrame Render(Scene3D scene, ICamera3D camera, Render3DOptions options = null);
}

public sealed class SceneRenderer3D : ISceneRenderer3D
{
    private const int Width = 900, Height = 700;

    public CanvasFrame Render(Scene3D scene, ICamera3D camera, Render3DOptions options = null)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (camera == null) throw new ArgumentNullException(nameof(camera));
        options ??= new Render3DOptions();
        if (!camera.IsFitted && scene.Bounds.HasValue) camera.Fit(scene.Bounds.Value, Width, Height);
        using var bitmap = new Bitmap(Width, Height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.White);
            using var titleFont = new Font("Microsoft Sans Serif", 13, FontStyle.Bold);
            using var labelFont = new Font("Microsoft Sans Serif", 9);
            if (!string.IsNullOrEmpty(options.Title))
                g.DrawString(options.Title, titleFont, Brushes.Black, 14, 10);
            if (options.GroundGrid && scene.Bounds.HasValue)
                DrawGroundGrid(g, camera, scene.Bounds.Value);
            // 收集线段（屏幕坐标 + 平均深度），按深度从远到近绘制
            var segments = new List<(PointF A, PointF B, double Depth, Color Color, float WidthPx, DashStyle Dash)>();
            foreach (var o in scene.Objects)
            {
                if (!o.Visible) continue;
                Color baseColor = o.Locked ? Color.Gray : !o.Enabled ? Color.LightSteelBlue : o.Selected ? Color.OrangeRed : Color.SteelBlue;
                float width = o.Selected ? 2.6f : 1.8f;
                DashStyle dash = !o.Enabled ? DashStyle.Dash : DashStyle.Solid;
                foreach (var path in o.Paths)
                {
                    CameraProjection previous = default; bool has = false;
                    foreach (var p in path)
                    {
                        var projection = camera.Project(p, Width, Height);
                        if (has && previous.Depth > 1e-9 && projection.Depth > 1e-9)
                            segments.Add((new((float)previous.X, (float)previous.Y), new((float)projection.X, (float)projection.Y),
                                (previous.Depth + projection.Depth) / 2, baseColor, width, dash));
                        previous = projection; has = true;
                    }
                }
                if (options.Labels && o.Paths.Count > 0 && o.Paths[0].Count > 0)
                {
                    var anchor = camera.Project(o.Paths[0][0], Width, Height);
                    if (anchor.Depth > 1e-9)
                        g.DrawString(o.Name, labelFont, new SolidBrush(baseColor), (float)anchor.X + 5, (float)anchor.Y - 16);
                }
            }
            if (options.BoundsBox && scene.Bounds.HasValue) DrawBoundsBox(g, camera, scene.Bounds.Value, segments);
            double minDepth = segments.Count == 0 ? 0 : segments.Min(s => s.Depth);
            double maxDepth = segments.Count == 0 ? 1 : segments.Max(s => s.Depth);
            foreach (var s in segments.OrderByDescending(s => s.Depth))
            {
                Color color = s.Color;
                if (options.DepthShade && maxDepth > minDepth + 1e-9)
                {
                    double t = (s.Depth - minDepth) / (maxDepth - minDepth); // 0=近 1=远
                    color = Lerp(s.Color, Color.FromArgb(225, 228, 235), 0.15 + 0.6 * t);
                }
                using var pen = new Pen(color, s.WidthPx) { DashStyle = s.Dash };
                g.DrawLine(pen, s.A, s.B);
            }
            using var legendFont = new Font("Microsoft Sans Serif", 9);
            g.DrawString("设计坐标系：mm，+Z 向上 | 相机：方位 " + camera.YawDegrees.ToString("F0") + "° 俯仰 " + camera.PitchDegrees.ToString("F0")
                + "° " + (camera.Perspective ? "透视" : "正交"), legendFont, Brushes.DimGray, 14, Height - 24);
        }
        return new CanvasFrame(Width, Height, CanvasRenderer.ToPng(bitmap));
    }

    private static void DrawGroundGrid(Graphics g, ICamera3D camera, DesignBounds bounds)
    {
        double step = Math.Max(bounds.Width, bounds.Height) / 5;
        if (!(step > 0) || !double.IsFinite(step)) return;
        double z = bounds.Minimum.Z;
        using var grid = new Pen(Color.Gainsboro, 1);
        for (double x = bounds.Minimum.X; x <= bounds.Maximum.X + 1e-9; x += step)
        {
            var a = camera.Project(new(x, bounds.Minimum.Y, z), Width, Height);
            var b = camera.Project(new(x, bounds.Maximum.Y, z), Width, Height);
            if (a.Depth > 1e-9 && b.Depth > 1e-9) g.DrawLine(grid, (float)a.X, (float)a.Y, (float)b.X, (float)b.Y);
        }
        for (double y = bounds.Minimum.Y; y <= bounds.Maximum.Y + 1e-9; y += step)
        {
            var a = camera.Project(new(bounds.Minimum.X, y, z), Width, Height);
            var b = camera.Project(new(bounds.Maximum.X, y, z), Width, Height);
            if (a.Depth > 1e-9 && b.Depth > 1e-9) g.DrawLine(grid, (float)a.X, (float)a.Y, (float)b.X, (float)b.Y);
        }
    }

    private static void DrawBoundsBox(Graphics g, ICamera3D camera, DesignBounds bounds,
        List<(PointF A, PointF B, double Depth, Color Color, float WidthPx, DashStyle Dash)> segments)
    {
        var min = bounds.Minimum; var max = bounds.Maximum;
        DesignPoint3[] corners =
        {
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z)
        };
        int[,] edges = { {0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},{0,4},{1,5},{2,6},{3,7} };
        for (int i = 0; i < edges.GetLength(0); i++)
        {
            var a = camera.Project(corners[edges[i, 0]], Width, Height);
            var b = camera.Project(corners[edges[i, 1]], Width, Height);
            if (a.Depth > 1e-9 && b.Depth > 1e-9)
                segments.Add((new((float)a.X, (float)a.Y), new((float)b.X, (float)b.Y), (a.Depth + b.Depth) / 2, Color.Silver, 1, DashStyle.Solid));
        }
    }

    private static Color Lerp(Color from, Color to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(from.R + (int)((to.R - from.R) * t), from.G + (int)((to.G - from.G) * t), from.B + (int)((to.B - from.B) * t));
    }
}

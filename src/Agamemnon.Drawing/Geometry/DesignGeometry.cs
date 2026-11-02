using System;
using System.Collections.Generic;
using System.Linq;

namespace Agamemnon.Drawing;

/// <summary>
/// 参数化图形 → 世界坐标折线的纯几何生成与校验（单位 mm）。无状态纯函数库，因此不设接口实现之分；
/// 生成结果不包含任何设备指令。所有坐标范围 ±100000 mm，单图形上限 50000 点。
/// </summary>
public static class DesignGeometry
{
    public const int MaxPoints = 50000;

    /// <summary>把图形参数展开为世界坐标路径（mm）：先按锚点定位、再绕锚点旋转、最后平移 Position。</summary>
    public static IReadOnlyList<IReadOnlyList<DesignPoint3>> Generate(DesignObject item)
    {
        Validate(item);
        var local = LocalPaths(item);
        var bounds = Bounds(local);
        double ax = item.Anchor is DesignAnchor.TL or DesignAnchor.ML or DesignAnchor.BL ? bounds.Minimum.X
            : item.Anchor is DesignAnchor.TR or DesignAnchor.MR or DesignAnchor.BR ? bounds.Maximum.X : (bounds.Minimum.X + bounds.Maximum.X) / 2;
        double ay = item.Anchor is DesignAnchor.BL or DesignAnchor.BC or DesignAnchor.BR ? bounds.Minimum.Y
            : item.Anchor is DesignAnchor.TL or DesignAnchor.TC or DesignAnchor.TR ? bounds.Maximum.Y : (bounds.Minimum.Y + bounds.Maximum.Y) / 2;
        // 线段与空间路径保留显式坐标点，不按包围盒锚点重定位。
        if (item.Kind is DesignObjectKind.Line or DesignObjectKind.Path3D) { ax = 0; ay = 0; }
        double a = -item.AngleDegrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        var result = local.Select(path => (IReadOnlyList<DesignPoint3>)Array.AsReadOnly(path.Select(p =>
        {
            double x = p.X - ax, y = p.Y - ay;
            return new DesignPoint3(x * c - y * s + item.Position.X, x * s + y * c + item.Position.Y, p.Z + item.Position.Z);
        }).ToArray())).ToArray();
        foreach (var p in result.SelectMany(p => p)) CheckPoint(p);
        return Array.AsReadOnly(result);
    }

    /// <summary>计算路径集合的轴对齐包围盒（mm）；空集合显式拒绝。</summary>
    public static DesignBounds Bounds(IEnumerable<IReadOnlyList<DesignPoint3>> paths)
    {
        var points = paths.SelectMany(p => p).ToArray();
        if (points.Length == 0) throw new ArgumentException("图形没有有效路径。");
        return new(new(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z)),
            new(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z)));
    }

    /// <summary>按图形类型校验全部参数；任何非法值抛 <see cref="ArgumentException"/>，不做截断或默认替换。</summary>
    public static void Validate(DesignObject o)
    {
        if (o == null || !Enum.IsDefined(o.Kind) || !Enum.IsDefined(o.Anchor)) throw new ArgumentException("无效图形类型或定位点。");
        if (o.Id == Guid.Empty || string.IsNullOrWhiteSpace(o.Name) || o.Name.Length > 120) throw new ArgumentException("图形名称需为 1–120 字符。");
        if (o.Points == null || o.Paths == null) throw new ArgumentException("路径集合不能为空引用。");
        CheckPoint(o.Position); CheckPoint(o.Direction);
        Range(o.AngleDegrees, -360000, 360000, "角度");
        if (o.Kind is DesignObjectKind.Ellipse or DesignObjectKind.Rectangle)
        { Range(o.WidthMm, 0.000001, 10000, "宽度"); Range(o.HeightMm, 0.000001, 10000, "高度"); }
        if (o.Kind == DesignObjectKind.Rectangle)
        { Range(o.CornerPercent, 0, 50, "圆角百分比"); Range(o.SubdivisionMm, 0.000001, 10000, "细分 mm"); }
        if (o.Kind is DesignObjectKind.Ellipse or DesignObjectKind.Spiral2D or DesignObjectKind.Helix3D)
        { if (o.Resolution < 6 || o.Resolution > 10000) throw new ArgumentException("分辨率范围为 6–10000 段。"); }
        if (o.Kind is DesignObjectKind.Spiral2D or DesignObjectKind.Helix3D)
        {
            Range(o.InitialRadiusMm, 0.000001, 10000, "初始半径"); Range(o.RadiusIncrementMm, -10000, 10000, "半圈半径增量");
            if (o.Turns < 1 || o.Turns > 1000 || (long)o.Turns * o.Resolution + 1 > MaxPoints) throw new ArgumentException("圈数为 1–1000 且生成点数不能超过 50000。");
            int halfTurns = o.Kind == DesignObjectKind.Helix3D ? o.Turns * 2 : o.Turns;
            Range(o.InitialRadiusMm + o.RadiusIncrementMm * (halfTurns - 1), 0.000001, 10000, "末半圈半径");
        }
        if (o.Kind == DesignObjectKind.Helix3D)
        {
            if (Length(o.Direction) < 1e-12) throw new ArgumentException("螺旋方向不能为零向量。");
            Range(o.PitchMm, -10000, 10000, "半圈轴向长度"); Range(o.PitchIncrementMm, -10000, 10000, "半圈轴向增量");
        }
        if (o.Kind is DesignObjectKind.Line or DesignObjectKind.Path3D)
        {
            if (o.Points == null || o.Points.Count < 2 || o.Points.Count > MaxPoints || (o.Kind == DesignObjectKind.Line && o.Points.Count != 2)) throw new ArgumentException("路径需 2–50000 个坐标点，线段恰为两点。");
            foreach (var p in o.Points) CheckPoint(p);
            if (o.Kind == DesignObjectKind.Line && o.Points.Any(p => p.Z != 0)) throw new ArgumentException("二维线段原始 Z 必须为 0；空间线段请使用 3D 路径。");
            if (o.Kind == DesignObjectKind.Line && o.Points[0] == o.Points[1]) throw new ArgumentException("线段起点和终点不能重合。");
        }
        if (o.Kind == DesignObjectKind.Svg)
        {
            if (o.Paths == null || o.Paths.Count == 0 || o.Paths.Any(p => p == null || p.Count < 2) || o.Paths.Sum(p => (long)p.Count) > MaxPoints) throw new ArgumentException("SVG 需要有效矢量路径，最多 50000 点。");
            foreach (var p in o.Paths.SelectMany(p => p)) CheckPoint(p);
        }
    }

    /// <summary>单点坐标校验：±100000 mm 内的有限值。</summary>
    public static void CheckPoint(DesignPoint3 p)
    { Range(p.X, -100000, 100000, "X mm"); Range(p.Y, -100000, 100000, "Y mm"); Range(p.Z, -100000, 100000, "Z mm"); }

    private static void Range(double value, double min, double max, string label)
    { if (!double.IsFinite(value) || value < min || value > max) throw new ArgumentException($"{label} 必须为有限值，范围 {min}–{max}。"); }
    private static double Length(DesignPoint3 p) => Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
    private static DesignPoint3 Cross(DesignPoint3 a, DesignPoint3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    private static IReadOnlyList<IReadOnlyList<DesignPoint3>> LocalPaths(DesignObject o)
    {
        if (o.Kind is DesignObjectKind.Line or DesignObjectKind.Path3D) return new[] { o.Points };
        if (o.Kind == DesignObjectKind.Svg) return o.Paths;
        var points = new List<DesignPoint3>();
        if (o.Kind == DesignObjectKind.Ellipse)
        {
            for (int i = 0; i <= o.Resolution; i++)
            { double a = i * Math.Tau / o.Resolution * (o.Clockwise ? -1 : 1); points.Add(new(Math.Cos(a) * o.WidthMm / 2, Math.Sin(a) * o.HeightMm / 2, 0)); }
        }
        else if (o.Kind == DesignObjectKind.Rectangle)
        {
            double w = o.WidthMm / 2, h = o.HeightMm / 2, r = Math.Min(o.WidthMm, o.HeightMm) * o.CornerPercent / 100;
            if (r == 0) points.AddRange(new[] { new DesignPoint3(-w, -h, 0), new(w, -h, 0), new(w, h, 0), new(-w, h, 0), new(-w, -h, 0) });
            else
            {
                double required = Math.Ceiling(Math.PI * r / 2 / o.SubdivisionMm);
                if (required * 4 + 5 > MaxPoints) throw new ArgumentException("圆角细分生成点数超过 50000；请增大细分长度。");
                int n = (int)required;
                var centers = new[] { new DesignPoint2(w - r, h - r), new(-w + r, h - r), new(-w + r, -h + r), new(w - r, -h + r) };
                for (int corner = 0; corner < 4; corner++) for (int i = 0; i <= n; i++)
                { double a = (corner + i / (double)n) * Math.PI / 2; points.Add(new(centers[corner].X + r * Math.Cos(a), centers[corner].Y + r * Math.Sin(a), 0)); }
                points.Add(points[0]);
            }
        }
        else if (o.Kind == DesignObjectKind.Spiral2D)
        {
            double endX = o.InitialRadiusMm;
            for (int half = 0; half < o.Turns; half++)
            {
                double r = o.InitialRadiusMm + o.RadiusIncrementMm * half;
                double center = endX - (half % 2 == 0 ? r : -r);
                for (int i = half == 0 ? 0 : 1; i <= o.Resolution; i++)
                { double a = (half + i / (double)o.Resolution) * Math.PI; points.Add(new(center + r * Math.Cos(a), r * Math.Sin(a) * (o.Clockwise ? -1 : 1), 0)); }
                endX = center + (half % 2 == 0 ? -r : r);
            }
        }
        else if (o.Kind == DesignObjectKind.Helix3D)
        {
            var d = o.Direction * (1 / Length(o.Direction));
            var u = Cross(d, Math.Abs(d.Z) < 0.9 ? new(0, 0, 1) : new(0, 1, 0)); u *= 1 / Length(u);
            var v = Cross(d, u);
            double axial = 0, endX = o.InitialRadiusMm;
            for (int half = 0; half < o.Turns * 2; half++)
            {
                double r0 = o.InitialRadiusMm + o.RadiusIncrementMm * half;
                double center = endX - (half % 2 == 0 ? r0 : -r0);
                int segments = half % 2 == 0 ? o.Resolution / 2 : o.Resolution - o.Resolution / 2;
                double pitch = o.PitchMm + half * o.PitchIncrementMm;
                for (int i = half == 0 ? 0 : 1; i <= segments; i++)
                {
                    double t = i / (double)segments, a = (half + t) * Math.PI;
                    points.Add(u * (center + r0 * Math.Cos(a)) + v * (r0 * Math.Sin(a) * (o.Clockwise ? -1 : 1)) + d * (axial + pitch * t));
                }
                axial += pitch;
                endX = center + (half % 2 == 0 ? -r0 : r0);
            }
        }
        return new[] { (IReadOnlyList<DesignPoint3>)Array.AsReadOnly(points.ToArray()) };
    }
}

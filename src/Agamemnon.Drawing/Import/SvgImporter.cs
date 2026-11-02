using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Agamemnon.Drawing;

public sealed record SvgImportResult(DesignObject Object, string Summary);

/// <summary>
/// SVG 导入接口：只导入矢量轮廓/中心线（mm），不导入填充、颜色、描边宽度或栅格内容，也不产生设备刀路。
/// </summary>
public interface ISvgImporter
{
    /// <summary>读取并解析 SVG 文件；文件上限 5 MB。</summary>
    SvgImportResult ReadFile(string path);
    /// <summary>解析 SVG 文本；name 为导入对象名。</summary>
    SvgImportResult Parse(string xml, string name);
}

/// <summary>
/// <see cref="ISvgImporter"/> 的默认实现。单位换算：mm/cm/in/pt/pc/px（无单位按 96 px/in）；
/// 只支持 preserveAspectRatio=none 或九宫格 meet；样式表、脚本、动画、裁剪、use、文字、位图需先在外部展开为普通路径。
/// SVG 的 y 向下坐标被翻转为设计系的 y 向上。
/// </summary>
public sealed class SvgImporter : ISvgImporter
{
    private readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
    {
        public static Matrix Identity => new(1, 0, 0, 1, 0, 0);
        public DesignPoint2 Map(DesignPoint2 p) => new(A * p.X + C * p.Y + E, B * p.X + D * p.Y + F);
        public static Matrix operator *(Matrix p, Matrix q) => new(p.A * q.A + p.C * q.B, p.B * q.A + p.D * q.B,
            p.A * q.C + p.C * q.D, p.B * q.C + p.D * q.D, p.A * q.E + p.C * q.F + p.E, p.B * q.E + p.D * q.F + p.F);
    }
    private static readonly Regex Numbers = new(@"[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?", RegexOptions.CultureInvariant);
    private static double ParseNumber(string text)
    { if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || !double.IsFinite(v) || Math.Abs(v) > 1e9) throw new ArgumentException("SVG 数值无效或超限。"); return v; }
    private static double[] ParseNumbers(string text)
    {
        if (!string.IsNullOrWhiteSpace(Numbers.Replace(text, "").Replace(",", ""))) throw new ArgumentException("SVG 数值列表格式无效。");
        return Numbers.Matches(text).Select(m => ParseNumber(m.Value)).ToArray();
    }
    private static double LengthMm(string text)
    {
        var m = Regex.Match(text.Trim(), @"^([+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)\s*(mm|cm|in|pt|pc|px)?$", RegexOptions.CultureInvariant);
        if (!m.Success) throw new ArgumentException("SVG 尺寸需为 mm/cm/in/pt/pc/px 或无单位；不接受百分比。");
        double factor = m.Groups[2].Value switch { "mm" => 1, "cm" => 10, "in" => 25.4, "pt" => 25.4 / 72, "pc" => 25.4 / 6, _ => 25.4 / 96 };
        double value = ParseNumber(m.Groups[1].Value) * factor;
        if (value <= 0 || value > 10000) throw new ArgumentException("SVG 画幅需大于 0 且不超过 10000 mm。"); return value;
    }

    public SvgImportResult ReadFile(string path)
    {
        if (new FileInfo(path).Length > DesignImportLimits.MaxFileBytes) throw new ArgumentException("SVG 文件不能超过 5 MB。");
        return Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
    }

    public SvgImportResult Parse(string xml, string name)
    {
        if (xml == null || xml.Length > DesignImportLimits.MaxFileBytes) throw new ArgumentException("SVG 文件过大或为空。");
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = DesignImportLimits.MaxFileBytes });
        var root = XDocument.Load(reader).Root;
        if (root == null || root.Name.LocalName != "svg") throw new ArgumentException("文件根节点必须为 svg。");
        if (root.Descendants().Any(e => e.Name.LocalName is "style" or "script" or "animate" or "animateMotion" or "animateTransform" or "set"))
            throw new ArgumentException("SVG 样式表、脚本或动画需先展开为静态普通路径。");
        string vb = (string)root.Attribute("viewBox"); double[] box = vb == null ? null : ParseNumbers(vb);
        if (box != null && (box.Length != 4 || box[2] <= 0 || box[3] <= 0)) throw new ArgumentException("SVG viewBox 无效。");
        string width = (string)root.Attribute("width"), height = (string)root.Attribute("height");
        if ((width == null || height == null) && box == null) throw new ArgumentException("SVG 需要宽高或 viewBox 来确定物理尺寸。");
        double w = width == null ? LengthMm(box[2].ToString("R", CultureInfo.InvariantCulture)) : LengthMm(width);
        double h = height == null ? LengthMm(box[3].ToString("R", CultureInfo.InvariantCulture)) : LengthMm(height);
        Matrix viewport;
        if (box == null) viewport = new(25.4 / 96, 0, 0, 25.4 / 96, 0, 0);
        else
        {
            double sx = w / box[2], sy = h / box[3], tx = 0, ty = 0;
            string aspect = ((string)root.Attribute("preserveAspectRatio") ?? "xMidYMid meet").Trim();
            if (aspect != "none")
            {
                var parts = aspect.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 2 || !Regex.IsMatch(parts[0], "^x(Min|Mid|Max)Y(Min|Mid|Max)$") || (parts.Length == 2 && parts[1] != "meet")) throw new ArgumentException("SVG 仅支持 preserveAspectRatio=none 或九宫格 meet；slice 请先展开裁剪。");
                sx = sy = Math.Min(sx, sy); tx = (w - box[2] * sx) * (parts[0].StartsWith("xMin") ? 0 : parts[0].StartsWith("xMax") ? 1 : 0.5);
                ty = (h - box[3] * sy) * (parts[0].EndsWith("YMin") ? 0 : parts[0].EndsWith("YMax") ? 1 : 0.5);
            }
            viewport = new(sx, 0, 0, sy, tx - box[0] * sx, ty - box[1] * sy);
        }
        var paths = new List<IReadOnlyList<DesignPoint3>>(); int count = 0, nodes = 0;
        void Walk(XElement node, Matrix parent, bool rootNode = false, int depth = 0)
        {
            if (depth > 128 || ++nodes > 10000) throw new ArgumentException("SVG 嵌套最多 128 层，可见树最多 10000 个元素。");
            string tag = node.Name.LocalName;
            if (tag is "defs" or "title" or "desc" or "metadata" or "namedview") return;
            foreach (string attr in new[] { "clip-path", "mask", "filter", "class", "marker-start", "marker-mid", "marker-end" })
                if (node.Attribute(attr) != null) throw new ArgumentException($"SVG {attr} 暂不支持；请先转为普通路径。");
            string style = (string)node.Attribute("style") ?? "";
            if (Regex.IsMatch(style, @"(?:^|;)\s*(transform|clip-path|mask|filter|d|marker(?:-start|-mid|-end)?)\s*:")) throw new ArgumentException("SVG CSS 几何、裁剪或效果需先展开为路径。");
            if ((string)node.Attribute("display") == "none" || Regex.IsMatch(style, @"(?:^|;)\s*display\s*:\s*none(?:;|$)")) return;
            if ((string)node.Attribute("visibility") is "hidden" or "collapse" || Regex.IsMatch(style, @"(?:^|;)\s*visibility\s*:\s*(hidden|collapse)(?:;|$)")) throw new ArgumentException("SVG visibility 继承需先展开；避免丢失隐藏组中的可见路径。");
            Matrix matrix = parent * Transform((string)node.Attribute("transform"));
            DesignPoint2 Map(DesignPoint2 p) { var q = matrix.Map(p); var output = new DesignPoint2(q.X, -q.Y); DesignGeometry.CheckPoint(new(output.X, output.Y, 0)); return output; }
            double N(string key, double fallback = 0) => node.Attribute(key) == null ? fallback : ParseNumber((string)node.Attribute(key));
            string d = null;
            string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
            switch (tag)
            {
                case "svg": if (!rootNode) throw new ArgumentException("嵌套 SVG 视口需先展开为路径。"); break;
                case "g": break;
                case "path": d = (string)node.Attribute("d") ?? ""; break;
                case "line": d = $"M{F(N("x1"))} {F(N("y1"))} L{F(N("x2"))} {F(N("y2"))}"; break;
                case "polyline": case "polygon":
                { var a = ParseNumbers((string)node.Attribute("points") ?? ""); if (a.Length < 4 || a.Length % 2 != 0 || a.Length / 2 > DesignGeometry.MaxPoints) throw new ArgumentException("SVG points 需 2–50000 对坐标。"); var buffer = new StringBuilder("M" + F(a[0]) + " " + F(a[1])); for (int i = 2; i < a.Length; i += 2) buffer.Append(" L").Append(F(a[i])).Append(' ').Append(F(a[i + 1])); if (tag == "polygon") buffer.Append(" Z"); d = buffer.ToString(); break; }
                case "rect":
                {
                    double x = N("x"), y = N("y"), rw = N("width"), rh = N("height"); if (rw < 0 || rh < 0) throw new ArgumentException("SVG 矩形宽高不能为负。"); if (rw == 0 || rh == 0) return;
                    double rx = N("rx", N("ry")), ry = N("ry", rx); if (rx < 0 || ry < 0) throw new ArgumentException("SVG 圆角不能为负。"); rx = Math.Min(rx, rw / 2); ry = Math.Min(ry, rh / 2);
                    if (rx == 0 || ry == 0) d = $"M{F(x)} {F(y)} h{F(rw)} v{F(rh)} h{F(-rw)} Z";
                    else d = $"M{F(x + rx)} {F(y)} H{F(x + rw - rx)} A{F(rx)} {F(ry)} 0 0 1 {F(x + rw)} {F(y + ry)} V{F(y + rh - ry)} A{F(rx)} {F(ry)} 0 0 1 {F(x + rw - rx)} {F(y + rh)} H{F(x + rx)} A{F(rx)} {F(ry)} 0 0 1 {F(x)} {F(y + rh - ry)} V{F(y + ry)} A{F(rx)} {F(ry)} 0 0 1 {F(x + rx)} {F(y)} Z";
                    break;
                }
                case "circle": case "ellipse":
                {
                    double cx = N("cx"), cy = N("cy"), rx = tag == "circle" ? N("r") : N("rx"), ry = tag == "circle" ? rx : N("ry");
                    if (rx < 0 || ry < 0) throw new ArgumentException("SVG 半径不能为负。"); if (rx == 0 || ry == 0) return;
                    d = $"M{F(cx + rx)} {F(cy)} A{F(rx)} {F(ry)} 0 1 1 {F(cx - rx)} {F(cy)} A{F(rx)} {F(ry)} 0 1 1 {F(cx + rx)} {F(cy)} Z"; break;
                }
                default: throw new ArgumentException($"SVG 元素 {tag} 暂不支持；文字需转路径，位图需先矢量化，use/CSS/裁剪需展开。");
            }
            if (d != null)
            {
                foreach (var path in new SvgPathParser(d, Map).Parse())
                {
                    count += path.Count; if (count > DesignGeometry.MaxPoints) throw new ArgumentException("SVG 总点数超过 50000。");
                    paths.Add(Array.AsReadOnly(path.Select(p => { var q = Map(p); return new DesignPoint3(q.X, q.Y, 0); }).ToArray()));
                }
            }
            else foreach (var child in node.Elements()) Walk(child, matrix, false, depth + 1);
        }
        Walk(root, viewport, true);
        var result = new DesignObject { Kind = DesignObjectKind.Svg, Name = name, Paths = Array.AsReadOnly(paths.ToArray()) }; DesignGeometry.Generate(result);
        var bounds = DesignGeometry.Bounds(DesignGeometry.Generate(result));
        return new(result, $"SVG 已导入：{paths.Count} 条轮廓、{count} 点；几何 {bounds.Width:G6} × {bounds.Height:G6} mm，居中放置在设计原点。只导入矢量轮廓/中心线，不含填充、颜色或描边宽度加工；无单位按 96 px/in 换算。曲线细分误差目标 0.001 mm。");
    }

    private static Matrix Transform(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Matrix.Identity;
        var regex = new Regex(@"([A-Za-z]+)\s*\(([^)]*)\)"); var matches = regex.Matches(text);
        if (!string.IsNullOrWhiteSpace(regex.Replace(text, "").Replace(",", ""))) throw new ArgumentException("SVG transform 语法无效。");
        Matrix result = Matrix.Identity;
        foreach (Match m in matches)
        {
            var a = ParseNumbers(m.Groups[2].Value); Matrix next;
            switch (m.Groups[1].Value)
            {
                case "matrix" when a.Length == 6: next = new(a[0], a[1], a[2], a[3], a[4], a[5]); break;
                case "translate" when a.Length is 1 or 2: next = new(1, 0, 0, 1, a[0], a.Length == 2 ? a[1] : 0); break;
                case "scale" when a.Length is 1 or 2: next = new(a[0], 0, 0, a.Length == 2 ? a[1] : a[0], 0, 0); break;
                case "rotate" when a.Length is 1 or 3:
                { double angle = a[0] * Math.PI / 180, c = Math.Cos(angle), s = Math.Sin(angle); next = new(c, s, -s, c, 0, 0); if (a.Length == 3) next = new Matrix(1, 0, 0, 1, a[1], a[2]) * next * new Matrix(1, 0, 0, 1, -a[1], -a[2]); break; }
                case "skewX" when a.Length == 1: next = new(1, 0, Math.Tan(a[0] * Math.PI / 180), 1, 0, 0); break;
                case "skewY" when a.Length == 1: next = new(1, Math.Tan(a[0] * Math.PI / 180), 0, 1, 0, 0); break;
                default: throw new ArgumentException("SVG transform 类型或参数数量不受支持。");
            }
            result *= next;
        }
        return result;
    }
}

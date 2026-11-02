using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Agamemnon.Drawing;

/// <summary>
/// SVG path "d" 属性解析器：M/L/H/V/C/S/Q/T/A/Z（大小写=绝对/相对），曲线按 0.001 mm 容差自适应细分。
/// 输入为 SVG 用户坐标，输出经外部 map 函数换算为 mm 设计坐标。内部实现，不属于包的公开接口。
/// </summary>
internal sealed class SvgPathParser
{
    private readonly string _data;
    private int _index, _count;
    private static readonly Regex NumberPattern = new(@"\G[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?", RegexOptions.CultureInvariant);
    private readonly List<List<DesignPoint2>> _paths = new();
    private List<DesignPoint2> _path;
    private DesignPoint2 _current, _start, _control;
    private char _previous;
    private readonly Func<DesignPoint2, DesignPoint2> _map;
    private const double ToleranceMm = 0.001;
    public SvgPathParser(string data, Func<DesignPoint2, DesignPoint2> map) { _data = data; _map = map; }
    private void Skip() { while (_index < _data.Length && (char.IsWhiteSpace(_data[_index]) || _data[_index] == ',')) _index++; }
    private double Number()
    {
        Skip(); var m = NumberPattern.Match(_data, _index);
        if (!m.Success) throw new ArgumentException($"SVG 路径第 {_index + 1} 字符需要数值。");
        _index += m.Length; double v = double.Parse(m.Value, CultureInfo.InvariantCulture);
        if (!double.IsFinite(v) || Math.Abs(v) > 1e9) throw new ArgumentException("SVG 坐标超出支持范围。"); return v;
    }
    private bool Flag()
    { Skip(); if (_index >= _data.Length || (_data[_index] != '0' && _data[_index] != '1')) throw new ArgumentException("SVG 弧标志必须为 0 或 1。"); return _data[_index++] == '1'; }
    private DesignPoint2 Point(bool relative)
    { var p = new DesignPoint2(Number(), Number()); return relative ? new(p.X + _current.X, p.Y + _current.Y) : p; }
    private void Add(DesignPoint2 p)
    {
        if (_path == null) throw new ArgumentException("SVG 路径必须先使用 M 指令。");
        if (++_count > DesignGeometry.MaxPoints) throw new ArgumentException("SVG 曲线细分超过 50000 点。"); _path.Add(p);
    }
    public IReadOnlyList<IReadOnlyList<DesignPoint2>> Parse()
    {
        char command = '\0';
        while (true)
        {
            Skip(); if (_index >= _data.Length) break;
            if (char.IsLetter(_data[_index])) command = _data[_index++];
            else if (command == '\0') throw new ArgumentException("SVG 路径缺少指令。");
            bool rel = char.IsLower(command); char cmd = char.ToUpperInvariant(command); var end = _current;
            switch (cmd)
            {
                case 'M': end = Point(rel); _path = new List<DesignPoint2>(); _paths.Add(_path); Add(end); _start = end; command = rel ? 'l' : 'L'; break;
                case 'L': end = Point(rel); Add(end); break;
                case 'H': end = new(Number() + (rel ? _current.X : 0), _current.Y); Add(end); break;
                case 'V': end = new(_current.X, Number() + (rel ? _current.Y : 0)); Add(end); break;
                case 'C':
                { var c1 = Point(rel); var c2 = Point(rel); end = Point(rel); Cubic(_current, c1, c2, end, 0); _control = c2; break; }
                case 'S':
                { var c1 = _previous is 'C' or 'S' ? new DesignPoint2(2 * _current.X - _control.X, 2 * _current.Y - _control.Y) : _current; var c2 = Point(rel); end = Point(rel); Cubic(_current, c1, c2, end, 0); _control = c2; break; }
                case 'Q':
                { var c = Point(rel); end = Point(rel); Quadratic(_current, c, end); _control = c; break; }
                case 'T':
                { var c = _previous is 'Q' or 'T' ? new DesignPoint2(2 * _current.X - _control.X, 2 * _current.Y - _control.Y) : _current; end = Point(rel); Quadratic(_current, c, end); _control = c; break; }
                case 'A':
                { double rx = Number(), ry = Number(), angle = Number(); bool large = Flag(), sweep = Flag(); end = Point(rel); Arc(_current, end, rx, ry, angle, large, sweep); break; }
                case 'Z': Add(_start); end = _start; command = '\0'; break;
                default: throw new ArgumentException($"SVG 路径指令 {command} 不受支持。");
            }
            _current = end; _previous = cmd;
        }
        return _paths.FindAll(p => p.Count > 1).ConvertAll(p => (IReadOnlyList<DesignPoint2>)p.AsReadOnly()).AsReadOnly();
    }
    private static DesignPoint2 Mid(DesignPoint2 a, DesignPoint2 b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
    private static double Distance(DesignPoint2 p, DesignPoint2 a, DesignPoint2 b)
    {
        double x = b.X - a.X, y = b.Y - a.Y, n = x * x + y * y, t = n == 0 ? 0 : Math.Clamp(((p.X - a.X) * x + (p.Y - a.Y) * y) / n, 0, 1);
        return Math.Sqrt(Math.Pow(p.X - a.X - t * x, 2) + Math.Pow(p.Y - a.Y - t * y, 2));
    }
    private void Cubic(DesignPoint2 p, DesignPoint2 c1, DesignPoint2 c2, DesignPoint2 end, int depth)
    {
        var mp = _map(p); var me = _map(end);
        if (Math.Max(Distance(_map(c1), mp, me), Distance(_map(c2), mp, me)) <= ToleranceMm) { Add(end); return; }
        if (depth >= 24) throw new ArgumentException("SVG 曲线无法在支持点数和精度内细分。");
        var a = Mid(p, c1); var b = Mid(c1, c2); var c = Mid(c2, end); var d = Mid(a, b); var e = Mid(b, c); var middle = Mid(d, e);
        Cubic(p, a, d, middle, depth + 1); Cubic(middle, e, c, end, depth + 1);
    }
    private void Quadratic(DesignPoint2 p, DesignPoint2 c, DesignPoint2 end) => Cubic(p,
        new(p.X + (c.X - p.X) * 2 / 3, p.Y + (c.Y - p.Y) * 2 / 3), new(end.X + (c.X - end.X) * 2 / 3, end.Y + (c.Y - end.Y) * 2 / 3), end, 0);
    private void Arc(DesignPoint2 start, DesignPoint2 end, double rx, double ry, double angle, bool large, bool sweep)
    {
        rx = Math.Abs(rx); ry = Math.Abs(ry); if (start == end) return; if (rx == 0 || ry == 0) { Add(end); return; }
        double phi = angle * Math.PI / 180, c = Math.Cos(phi), s = Math.Sin(phi), dx = (start.X - end.X) / 2, dy = (start.Y - end.Y) / 2;
        double xp = c * dx + s * dy, yp = -s * dx + c * dy, lambda = xp * xp / (rx * rx) + yp * yp / (ry * ry);
        if (lambda > 1) { double f = Math.Sqrt(lambda); rx *= f; ry *= f; }
        double numerator = rx * rx * ry * ry - rx * rx * yp * yp - ry * ry * xp * xp;
        double factor = (large == sweep ? -1 : 1) * Math.Sqrt(Math.Max(0, numerator / (rx * rx * yp * yp + ry * ry * xp * xp)));
        double cxp = factor * rx * yp / ry, cyp = -factor * ry * xp / rx;
        double cx = c * cxp - s * cyp + (start.X + end.X) / 2, cy = s * cxp + c * cyp + (start.Y + end.Y) / 2;
        double a = Math.Atan2((yp - cyp) / ry, (xp - cxp) / rx), b = Math.Atan2((-yp - cyp) / ry, (-xp - cxp) / rx), delta = b - a;
        if (sweep && delta < 0) delta += Math.Tau; if (!sweep && delta > 0) delta -= Math.Tau;
        var center = _map(new(cx, cy)); var px = _map(new(cx + c * rx, cy + s * rx)); var py = _map(new(cx - s * ry, cy + c * ry));
        // Frobenius 范数约束斜切/非均匀缩放下变换后椭圆的最大半径。
        double radius = Math.Sqrt(Math.Pow(px.X - center.X, 2) + Math.Pow(px.Y - center.Y, 2) + Math.Pow(py.X - center.X, 2) + Math.Pow(py.Y - center.Y, 2));
        double maxAngle = radius <= ToleranceMm ? Math.PI / 4 : 2 * Math.Acos(Math.Clamp(1 - ToleranceMm / radius, -1, 1));
        int n = (int)Math.Ceiling(Math.Abs(delta) / Math.Max(1e-7, Math.Min(Math.PI / 4, maxAngle)));
        if (n > DesignGeometry.MaxPoints) throw new ArgumentException("SVG 圆弧超过细分点数限制。");
        for (int i = 1; i <= n; i++) { double t = a + delta * i / n; Add(i == n ? end : new(cx + c * rx * Math.Cos(t) - s * ry * Math.Sin(t), cy + s * rx * Math.Cos(t) + c * ry * Math.Sin(t))); }
    }
}

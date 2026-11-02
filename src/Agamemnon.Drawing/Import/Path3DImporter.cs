using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Agamemnon.Drawing;

/// <summary>3D 坐标路径导入接口：文本每行一个 X,Y,Z（mm）坐标点。</summary>
public interface IPath3DImporter
{
    /// <summary>读取坐标文件；文件上限 5 MB。</summary>
    DesignObject ReadFile(string path);
    /// <summary>解析坐标文本；name 为导入对象名。</summary>
    DesignObject Parse(string text, string name);
}

/// <summary>
/// <see cref="IPath3DImporter"/> 的默认实现。分隔符支持逗号/分号/空白；# 开头与空行忽略；
/// 可选一行 "X Y Z" 表头（大小写不敏感）；坐标为不变文化小数（点号），单条路径 2–50000 点。
/// </summary>
public sealed class Path3DImporter : IPath3DImporter
{
    public DesignObject ReadFile(string path)
    {
        if (new FileInfo(path).Length > DesignImportLimits.MaxFileBytes) throw new ArgumentException("路径文件不能超过 5 MB。");
        return Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
    }

    public DesignObject Parse(string text, string name)
    {
        if (text == null || text.Length > DesignImportLimits.MaxFileBytes) throw new ArgumentException("路径文本过大或为空。");
        var points = new List<DesignPoint3>(); int line = 0;
        using var reader = new StringReader(text);
        string row;
        while ((row = reader.ReadLine()) != null)
        {
            line++; row = row.Trim(); if (row.Length == 0 || row.StartsWith('#')) continue;
            var columns = (row.Contains(',') ? row.Split(',') : row.Contains(';') ? row.Split(';')
                : row.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Select(value => value.Trim()).ToArray();
            if (points.Count == 0 && columns.Length == 3 && columns[0].Equals("X", StringComparison.OrdinalIgnoreCase)
                && columns[1].Equals("Y", StringComparison.OrdinalIgnoreCase) && columns[2].Equals("Z", StringComparison.OrdinalIgnoreCase)) continue;
            if (columns.Length != 3) throw new ArgumentException($"第 {line} 行必须恰有 X,Y,Z 三列（mm）。");
            var values = new double[3];
            for (int i = 0; i < 3; i++) if (!double.TryParse(columns[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i])) throw new ArgumentException($"第 {line} 行含无效坐标；小数使用点号。");
            var p = new DesignPoint3(values[0], values[1], values[2]); DesignGeometry.CheckPoint(p); points.Add(p);
            if (points.Count > DesignGeometry.MaxPoints) throw new ArgumentException("路径最多 50000 点。");
        }
        var o = new DesignObject { Kind = DesignObjectKind.Path3D, Name = name, Points = Array.AsReadOnly(points.ToArray()) };
        DesignGeometry.Generate(o); return o;
    }
}

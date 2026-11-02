using System;

namespace Agamemnon.Drawing;

/// <summary>设计坐标 → 位移台命令坐标的纯几何映射。两套坐标单位均为 mm；方向只能是 +1/-1。</summary>
public sealed record DesignCoordinateMapping
{
    public DesignPoint3 DesignOriginMm { get; init; }
    public DesignPoint3 StageOriginMm { get; init; }
    public double RotationDegrees { get; init; }
    public int XDirection { get; init; } = 1;
    public int YDirection { get; init; } = 1;
    public int ZDirection { get; init; } = 1;
}

/// <summary>设计项目级设置：坐标映射与默认加工预设引用。</summary>
public sealed record DesignProjectSettings
{
    public DesignCoordinateMapping Mapping { get; init; } = new();
    public DesignProcessingAssignment DefaultProcessing { get; init; }
}

/// <summary>坐标映射的结构校验与纯几何换算。不含任何设备读写；执行时由工艺/设备层调用。</summary>
public static class DesignMapping
{
    /// <summary>校验映射结构：原点为有限 mm 坐标、旋转 ±360000°、方向仅 ±1。</summary>
    public static void Validate(DesignCoordinateMapping map)
    {
        if (map == null) throw new ArgumentException("缺少坐标转换。");
        DesignGeometry.CheckPoint(map.DesignOriginMm); DesignGeometry.CheckPoint(map.StageOriginMm);
        if (!double.IsFinite(map.RotationDegrees) || Math.Abs(map.RotationDegrees) > 360000 ||
            (map.XDirection != 1 && map.XDirection != -1) || (map.YDirection != 1 && map.YDirection != -1) || (map.ZDirection != 1 && map.ZDirection != -1))
            throw new ArgumentException("转换角度需为有限值（±360000°），轴方向只能为 +1/-1。");
    }

    /// <summary>把设计坐标（mm）换算为位移台命令坐标（mm）；先平移到设计原点、旋转、按轴方向翻转，再平移到位移台原点。</summary>
    public static DesignPoint3 Transform(DesignPoint3 point, DesignCoordinateMapping map)
    {
        Validate(map); DesignGeometry.CheckPoint(point);
        var p = point - map.DesignOriginMm; double a = -map.RotationDegrees * Math.PI / 180;
        var result = new DesignPoint3(map.StageOriginMm.X + map.XDirection * (p.X * Math.Cos(a) - p.Y * Math.Sin(a)),
            map.StageOriginMm.Y + map.YDirection * (p.X * Math.Sin(a) + p.Y * Math.Cos(a)), map.StageOriginMm.Z + map.ZDirection * p.Z);
        if (!double.IsFinite(result.X) || !double.IsFinite(result.Y) || !double.IsFinite(result.Z)) throw new ArgumentException("转换结果非有限值。");
        return result;
    }
}

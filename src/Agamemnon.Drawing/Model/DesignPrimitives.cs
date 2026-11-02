using System;

namespace Agamemnon.Drawing;

/// <summary>定位点：图形包围盒的九宫格参考位置。</summary>
public enum DesignAnchor { TL, TC, TR, ML, C, MR, BL, BC, BR }

/// <summary>二维设计坐标（mm）。用于 SVG 路径解析等平面几何；完整模型坐标见 <see cref="DesignPoint3"/>。</summary>
public readonly record struct DesignPoint2(double X, double Y)
{
    public static DesignPoint2 operator +(DesignPoint2 a, DesignPoint2 b) => new(a.X + b.X, a.Y + b.Y);
    public static DesignPoint2 operator -(DesignPoint2 a, DesignPoint2 b) => new(a.X - b.X, a.Y - b.Y);
    public static DesignPoint2 operator *(DesignPoint2 a, double factor) => new(a.X * factor, a.Y * factor);
}

/// <summary>三维设计坐标（mm）。设计系：原点在左下，+X 向右，+Y 向上，+Z 由图形定义（如螺旋轴向）。</summary>
public readonly record struct DesignPoint3(double X, double Y, double Z)
{
    public static DesignPoint3 operator +(DesignPoint3 a, DesignPoint3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static DesignPoint3 operator -(DesignPoint3 a, DesignPoint3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static DesignPoint3 operator *(DesignPoint3 a, double factor) => new(a.X * factor, a.Y * factor, a.Z * factor);
}

/// <summary>轴对齐包围盒（mm）。Minimum/Maximum 为inclusive端点。</summary>
public readonly record struct DesignBounds(DesignPoint3 Minimum, DesignPoint3 Maximum)
{
    public double Width => Maximum.X - Minimum.X;
    public double Height => Maximum.Y - Minimum.Y;
    public double Depth => Maximum.Z - Minimum.Z;
}

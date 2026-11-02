using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Agamemnon.Drawing;

/// <summary>图形类型。Line/Ellipse/Rectangle/Spiral2D 为参数化二维图形；Path3D/Helix3D 含 Z；Svg 为导入的矢量轮廓。</summary>
public enum DesignObjectKind { Line, Ellipse, Rectangle, Spiral2D, Path3D, Helix3D, Svg }

/// <summary>
/// 关联的加工预设引用。PresetId 为预设 GUID 字符串；Values 对画图包不透明，
/// 预设值目录/参数合法性由工艺流程层在使用时校验，本包仅做结构校验并保存防御性副本。
/// </summary>
public sealed record DesignProcessingAssignment
{
    public string PresetId { get; init; }
    public string PresetName { get; init; }
    public IReadOnlyDictionary<string, JsonElement> Values { get; init; } = new Dictionary<string, JsonElement>();
}

/// <summary>
/// 不可变图形参数。长度单位 mm；AngleDegrees 为 XY 平面内顺时针旋转角（度）；
/// Position 按 <see cref="DesignAnchor"/> 定义的锚点放置。不包含任何设备指令。
/// </summary>
public sealed record DesignObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "图形";
    public bool Enabled { get; init; } = true;
    public bool Visible { get; init; } = true;
    public bool Locked { get; init; }
    public string GroupPath { get; init; } = "";
    public DesignObjectKind Kind { get; init; }
    public DesignPoint3 Position { get; init; }
    public DesignAnchor Anchor { get; init; } = DesignAnchor.C;
    public double AngleDegrees { get; init; }
    public bool LockAspect { get; init; } = true;
    public double WidthMm { get; init; } = 5;
    public double HeightMm { get; init; } = 5;
    public double CornerPercent { get; init; }
    public double SubdivisionMm { get; init; } = 0.01;
    public double InitialRadiusMm { get; init; } = 0.1;
    public double RadiusIncrementMm { get; init; } = 0.5;
    public int Turns { get; init; } = 10;
    public int Resolution { get; init; } = 100;
    public bool Clockwise { get; init; }
    public DesignPoint3 Direction { get; init; } = new(0, 1, 0);
    public double PitchMm { get; init; } = 1;
    public double PitchIncrementMm { get; init; }
    public IReadOnlyList<DesignPoint3> Points { get; init; } = Array.AsReadOnly(new[] { new DesignPoint3(-5, 0, 0), new DesignPoint3(5, 0, 0) });
    public IReadOnlyList<IReadOnlyList<DesignPoint3>> Paths { get; init; } = Array.Empty<IReadOnlyList<DesignPoint3>>();
    public DesignProcessingAssignment Processing { get; init; }
}

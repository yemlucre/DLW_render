using System;
using Agamemnon.Drawing;

namespace Agamemnon.Render;

public enum CanvasUnit { Millimetres, Micrometres }

/// <summary>2D 画布视图状态快照：画布尺寸 mm、比例 px/mm、原点像素偏移与显示单位。设计系：原点左下、+X 右、+Y 上。</summary>
public readonly record struct CanvasViewSnapshot(double WidthMm, double HeightMm, double PixelsPerMm,
    double OriginX, double OriginY, CanvasUnit DisplayUnit);

/// <summary>
/// 2D 视图数学接口：设计坐标(mm) ↔ 屏幕像素的换算与缩放/平移/适应。纯数学，不持有控件、不产生设备指令。
/// </summary>
public interface ICanvasView
{
    CanvasViewSnapshot Snapshot { get; }
    void SetSize(double widthMm, double heightMm);
    void SetDisplayUnit(CanvasUnit unit);
    DesignPoint2 ScreenToDesign(double x, double y);
    DesignPoint2 DesignToScreen(double xMm, double yMm);
    void Fit(double widthPixels, double heightPixels);
    void FitBounds(double minX, double minY, double maxX, double maxY, double widthPixels, double heightPixels);
    void ZoomAt(double screenX, double screenY, double factor);
    void Pan(double dxPixels, double dyPixels);
    bool Contains(DesignPoint2 point);
}

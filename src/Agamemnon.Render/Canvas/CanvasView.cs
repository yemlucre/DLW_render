using System;
using Agamemnon.Drawing;

namespace Agamemnon.Render;

/// <summary><see cref="ICanvasView"/> 的默认实现，语义与现行 CanvasViewService 一致。</summary>
public sealed class CanvasView : ICanvasView
{
    public const double MinSizeMm = 0.001, MaxSizeMm = 10000;
    public const double MinPixelsPerMm = 0.01, MaxPixelsPerMm = 1000000;
    private CanvasViewSnapshot _view = new(10, 10, 20, 40, 240, CanvasUnit.Millimetres);
    public CanvasViewSnapshot Snapshot => _view;

    public void SetSize(double widthMm, double heightMm)
    {
        ValidateSize(widthMm); ValidateSize(heightMm);
        _view = _view with { WidthMm = widthMm, HeightMm = heightMm };
    }

    public void SetDisplayUnit(CanvasUnit unit)
    {
        if (!Enum.IsDefined(unit)) throw new ArgumentOutOfRangeException(nameof(unit));
        _view = _view with { DisplayUnit = unit };
    }

    public DesignPoint2 ScreenToDesign(double x, double y)
    {
        Finite(x); Finite(y);
        return new((x - _view.OriginX) / _view.PixelsPerMm, (_view.OriginY - y) / _view.PixelsPerMm);
    }

    public DesignPoint2 DesignToScreen(double xMm, double yMm)
    {
        Finite(xMm); Finite(yMm);
        return new(_view.OriginX + xMm * _view.PixelsPerMm, _view.OriginY - yMm * _view.PixelsPerMm);
    }

    public void Fit(double widthPixels, double heightPixels)
        => FitBounds(0, 0, _view.WidthMm, _view.HeightMm, widthPixels, heightPixels);

    public void FitBounds(double minX, double minY, double maxX, double maxY, double widthPixels, double heightPixels)
    {
        Finite(minX); Finite(minY); Finite(maxX); Finite(maxY);
        if (maxX < minX || maxY < minY) throw new ArgumentOutOfRangeException(nameof(maxX));
        Finite(widthPixels); Finite(heightPixels);
        if (widthPixels <= 0 || heightPixels <= 0 || widthPixels > 1000000 || heightPixels > 1000000)
            throw new ArgumentOutOfRangeException(nameof(widthPixels));
        double scale = Math.Clamp(Math.Min(Math.Max(1, widthPixels - 64) / Math.Max(0.001, maxX - minX),
            Math.Max(1, heightPixels - 64) / Math.Max(0.001, maxY - minY)), MinPixelsPerMm, MaxPixelsPerMm);
        double x = widthPixels / 2 - (minX + maxX) / 2 * scale, y = heightPixels / 2 + (minY + maxY) / 2 * scale;
        ValidateOrigin(x, y);
        _view = _view with { PixelsPerMm = scale, OriginX = x, OriginY = y };
    }

    public void ZoomAt(double screenX, double screenY, double factor)
    {
        Finite(factor); if (factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        var anchor = ScreenToDesign(screenX, screenY);
        double scale = Math.Clamp(_view.PixelsPerMm * factor, MinPixelsPerMm, MaxPixelsPerMm);
        double x = screenX - anchor.X * scale, y = screenY + anchor.Y * scale;
        ValidateOrigin(x, y);
        _view = _view with { PixelsPerMm = scale, OriginX = x, OriginY = y };
    }

    public void Pan(double dxPixels, double dyPixels)
    {
        Finite(dxPixels); Finite(dyPixels);
        double x = _view.OriginX + dxPixels, y = _view.OriginY + dyPixels;
        ValidateOrigin(x, y); _view = _view with { OriginX = x, OriginY = y };
    }

    public bool Contains(DesignPoint2 point) => double.IsFinite(point.X) && double.IsFinite(point.Y)
        && point.X >= 0 && point.Y >= 0 && point.X <= _view.WidthMm && point.Y <= _view.HeightMm;

    private static void ValidateSize(double value)
    {
        Finite(value);
        if (value < MinSizeMm || value > MaxSizeMm) throw new ArgumentOutOfRangeException(nameof(value), "画布尺寸范围为 0.001–10000 mm。");
    }
    private static void ValidateOrigin(double x, double y)
    {
        Finite(x); Finite(y);
        if (Math.Abs(x) > 1e9 || Math.Abs(y) > 1e9) throw new ArgumentOutOfRangeException(nameof(x), "视图偏移超出支持范围。");
    }
    private static void Finite(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
    }
}

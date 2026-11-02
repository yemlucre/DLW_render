using System;
using Agamemnon.Drawing;

namespace Agamemnon.Render;

/// <summary>单个点的相机投影结果：屏幕像素坐标 + 深度（mm，越大越远；&lt;=0 表示在视点后方，不可投影）。</summary>
public readonly record struct CameraProjection(double X, double Y, double Depth);

/// <summary>
/// 3D 相机接口：围绕目标的环绕视角（方位/俯仰角）、透视/正交投影与适配。设计系 +Z 向上；
/// 纯数学实现，不依赖 WPF/Helix，不持有窗口。
/// </summary>
public interface ICamera3D
{
    /// <summary>目标点（mm），Fit 后为场景包围盒中心。</summary>
    DesignPoint3 Target { get; }
    /// <summary>视距（mm）。</summary>
    double DistanceMm { get; }
    /// <summary>方位角（度，绕 +Z，0=从 +X 方向看）。</summary>
    double YawDegrees { get; }
    /// <summary>俯仰角（度，钳制 ±89°，正值从上方俯视）。</summary>
    double PitchDegrees { get; }
    /// <summary>true=透视，false=正交。</summary>
    bool Perspective { get; }
    /// <summary>透视视场角（度，钳制 5–170）。</summary>
    double FieldOfViewDegrees { get; }
    /// <summary>正交比例（px/mm），Fit 时设置。</summary>
    double OrthoPixelsPerMm { get; }
    bool IsFitted { get; }
    void SetAngles(double yawDegrees, double pitchDegrees);
    void Orbit(double yawDeltaDegrees, double pitchDeltaDegrees);
    void Zoom(double factor);
    void SetPerspective(bool perspective);
    /// <summary>适配包围盒：目标=中心、视距与正交比例按包围盒尺寸设置。</summary>
    void Fit(DesignBounds bounds, double widthPixels, double heightPixels);
    /// <summary>投影一个世界坐标点；depth&lt;=0 的点返回 Depth&lt;=0 且坐标无效，由调用方跳过。</summary>
    CameraProjection Project(DesignPoint3 point, double widthPixels, double heightPixels);
}

public sealed class Camera3D : ICamera3D
{
    private double _yaw = 32, _pitch = 24, _distance = 50, _fov = 45, _orthoScale = 20;
    private DesignPoint3 _target;
    private bool _perspective = true, _fitted;

    public DesignPoint3 Target => _target;
    public double DistanceMm => _distance;
    public double YawDegrees => _yaw;
    public double PitchDegrees => _pitch;
    public bool Perspective => _perspective;
    public double FieldOfViewDegrees => _fov;
    public double OrthoPixelsPerMm => _orthoScale;
    public bool IsFitted => _fitted;

    public void SetAngles(double yawDegrees, double pitchDegrees)
    {
        if (!double.IsFinite(yawDegrees) || !double.IsFinite(pitchDegrees)) throw new ArgumentOutOfRangeException(null, "角度必须为有限值。");
        _yaw = yawDegrees; _pitch = Math.Clamp(pitchDegrees, -89, 89);
    }

    public void Orbit(double yawDeltaDegrees, double pitchDeltaDegrees) => SetAngles(_yaw + yawDeltaDegrees, _pitch + pitchDeltaDegrees);

    public void Zoom(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        _distance = Math.Clamp(_distance / factor, 0.1, 1e7);
        _orthoScale = Math.Clamp(_orthoScale * factor, 1e-4, 1e6);
    }

    public void SetPerspective(bool perspective) => _perspective = perspective;

    public void Fit(DesignBounds bounds, double widthPixels, double heightPixels)
    {
        if (widthPixels <= 0 || heightPixels <= 0) throw new ArgumentOutOfRangeException(nameof(widthPixels));
        double extent = Math.Max(1e-3, Math.Max(Math.Max(bounds.Width, bounds.Height), bounds.Depth));
        _target = new((bounds.Minimum.X + bounds.Maximum.X) / 2, (bounds.Minimum.Y + bounds.Maximum.Y) / 2, (bounds.Minimum.Z + bounds.Maximum.Z) / 2);
        _distance = extent * 2.6;
        _orthoScale = Math.Min(widthPixels, heightPixels) * 0.72 / extent;
        _fitted = true;
    }

    public CameraProjection Project(DesignPoint3 point, double widthPixels, double heightPixels)
    {
        if (widthPixels <= 0 || heightPixels <= 0) throw new ArgumentOutOfRangeException(nameof(widthPixels));
        double yaw = _yaw * Math.PI / 180, pitch = _pitch * Math.PI / 180;
        double horizontal = Math.Cos(pitch);
        var eye = _target + new DesignPoint3(_distance * horizontal * Math.Cos(yaw), _distance * horizontal * Math.Sin(yaw), _distance * Math.Sin(pitch));
        var forward = Normalize(_target - eye);
        var right = Normalize(Cross(forward, new(0, 0, 1)));
        var up = Cross(right, forward);
        var v = point - eye;
        double camX = Dot(v, right), camY = Dot(v, up), depth = Dot(v, forward);
        if (depth <= 1e-9) return new(double.NaN, double.NaN, depth);
        if (_perspective)
        {
            double f = Math.Min(widthPixels, heightPixels) / 2 / Math.Tan(Math.Clamp(_fov, 5, 170) * Math.PI / 360);
            return new(widthPixels / 2 + camX * f / depth, heightPixels / 2 - camY * f / depth, depth);
        }
        return new(widthPixels / 2 + camX * _orthoScale, heightPixels / 2 - camY * _orthoScale, depth);
    }

    private static double Dot(DesignPoint3 a, DesignPoint3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static DesignPoint3 Cross(DesignPoint3 a, DesignPoint3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static DesignPoint3 Normalize(DesignPoint3 p)
    {
        double length = Math.Sqrt(Dot(p, p));
        if (length < 1e-12) throw new ArgumentException("方向向量不能为零。");
        return p * (1 / length);
    }
}

using System;

namespace Agamemnon.Drawing;

/// <summary>一次拖拽会话：绑定对象、开始时的内容版本、拖拽平面与抓取点（mm）。</summary>
public sealed record DesignDragSession(Guid ObjectId, long ContentRevision, DesignPoint3 PlaneOrigin,
    DesignPoint3 PlaneNormal, DesignPoint3 GrabPoint);

/// <summary>
/// 定平面拖拽（mm）：Begin 建会话（视线与平面求交得抓取点），Preview 纯计算平移量不改文档，
/// Commit 校验版本后作为一次可撤销的 <see cref="IDesignDocument.Move"/> 提交。视线不可用时返回 null 而非大坐标。
/// </summary>
public static class DesignDragService
{
    public static DesignDragSession Begin(IDesignDocument document, Guid id, DesignPoint3 hit,
        DesignPoint3 normal, DesignPoint3 rayOrigin, DesignPoint3 rayDirection)
    {
        ArgumentNullException.ThrowIfNull(document);
        var item = document.Find(id);
        if (!item.Visible || item.Locked) throw new InvalidOperationException("隐藏或锁定的对象不能拖动。");
        Validate(hit); Validate(normal);
        double length = Math.Sqrt(Dot(normal, normal));
        if (length < 1e-12) throw new ArgumentException("拖动平面法向量不能为零。");
        normal *= 1 / length;
        var grab = Intersect(rayOrigin, rayDirection, hit, normal)
            ?? throw new InvalidOperationException("拖动平面与视线平行，请切换视角或拖动平面。");
        return new(id, document.ContentRevision, hit, normal, grab);
    }

    public static DesignPoint3? Preview(DesignDragSession session, DesignPoint3 rayOrigin, DesignPoint3 rayDirection)
    {
        ArgumentNullException.ThrowIfNull(session);
        var point = Intersect(rayOrigin, rayDirection, session.PlaneOrigin, session.PlaneNormal);
        return point.HasValue ? point.Value - session.GrabPoint : null;
    }

    public static DesignEditResult Commit(IDesignDocument document, DesignDragSession session, DesignPoint3 delta)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(session); Validate(delta);
        if (document.ContentRevision != session.ContentRevision) throw new InvalidOperationException("文档已更改，本次拖动已取消。");
        var item = document.Find(session.ObjectId);
        if (!item.Visible || item.Locked) throw new InvalidOperationException("对象已隐藏或锁定，本次拖动已取消。");
        return document.Move(session.ObjectId, delta.X, delta.Y, delta.Z);
    }

    private static DesignPoint3? Intersect(DesignPoint3 origin, DesignPoint3 direction, DesignPoint3 plane, DesignPoint3 normal)
    {
        Validate(origin); Validate(direction);
        double length = Math.Sqrt(Dot(direction, direction));
        if (length < 1e-12) throw new ArgumentException("视线方向不能为零。");
        direction *= 1 / length;
        double denominator = Dot(direction, normal);
        if (Math.Abs(denominator) < 1e-6) return null;
        double distance = Dot(plane - origin, normal) / denominator;
        if (!double.IsFinite(distance) || distance < 0) return null;
        var result = origin + direction * distance;
        return double.IsFinite(result.X) && double.IsFinite(result.Y) && double.IsFinite(result.Z) ? result : null;
    }
    private static double Dot(DesignPoint3 a, DesignPoint3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static void Validate(DesignPoint3 p)
    { if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z)) throw new ArgumentException("坐标及方向必须为有限数值。"); }
}

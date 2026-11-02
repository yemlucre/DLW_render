using System;
using System.Collections.Generic;
using System.Linq;
using Agamemnon.Drawing;

namespace Agamemnon.Render;

/// <summary>场景中的一个对象：只读几何（世界坐标折线 mm）+ 显示状态标记。</summary>
public sealed record SceneObject3D(Guid Id, string Name, bool Visible, bool Enabled, bool Locked, bool Selected,
    IReadOnlyList<IReadOnlyList<DesignPoint3>> Paths);

/// <summary>只读 3D 场景：对象集合、整体包围盒与来源文档版本号。</summary>
public sealed record Scene3D(IReadOnlyList<SceneObject3D> Objects, DesignBounds? Bounds, long Revision);

/// <summary>场景构建接口：文档快照 → 只读场景。只读 Drawing 数据，不做任何编辑或设备操作。</summary>
public interface IScene3DBuilder
{
    Scene3D Build(DesignDocumentSnapshot snapshot);
}

public sealed class Scene3DBuilder : IScene3DBuilder
{
    public Scene3D Build(DesignDocumentSnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        var objects = new List<SceneObject3D>();
        foreach (var o in snapshot.Objects)
        {
            var paths = o.Visible ? DesignGeometry.Generate(o) : Array.Empty<IReadOnlyList<DesignPoint3>>();
            objects.Add(new(o.Id, o.Name, o.Visible, o.Enabled, o.Locked, snapshot.SelectedId == o.Id, paths));
        }
        var allPaths = objects.SelectMany(o => o.Paths).ToArray();
        return new(objects, allPaths.Length == 0 ? null : DesignGeometry.Bounds(allPaths), snapshot.Revision);
    }
}

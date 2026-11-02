using System;
using System.Collections.Generic;

namespace Agamemnon.Drawing;

/// <summary>文档快照：对象树（组序）、当前选择、撤销/重做可用性与版本号。</summary>
public sealed record DesignDocumentSnapshot(IReadOnlyList<DesignObject> Objects, Guid? SelectedId, bool CanUndo, bool CanRedo, long Revision);

/// <summary>编辑操作结果：Changed 表示是否产生新状态；Snapshot 始终返回提交后的最新快照。</summary>
public sealed record DesignEditResult(bool Changed, DesignDocumentSnapshot Snapshot);

/// <summary>
/// 设计文档（画图域）的稳定编辑接口。实现约定：
/// 所有变更先校验再原子提交（无部分成功）；非法输入抛 <see cref="ArgumentException"/>，
/// 状态冲突抛 <see cref="InvalidOperationException"/>；每次提交触发 <see cref="Changed"/> 并推进 Revision/ContentRevision。
/// 单位与坐标系：长度 mm，设计系原点在左下、+X 右、+Y 上。不含设备指令。
/// </summary>
public interface IDesignDocument
{
    /// <summary>内容版本号：仅对象/设置变更时推进；选择变化不计入。</summary>
    long ContentRevision { get; }
    /// <summary>项目级设置（坐标映射 + 默认加工预设引用）。</summary>
    DesignProjectSettings Settings { get; }
    /// <summary>任何提交（含选择变化、撤销/重做、整体替换）后触发。</summary>
    event EventHandler Changed;
    /// <summary>当前完整快照。</summary>
    DesignDocumentSnapshot Snapshot { get; }
    /// <summary>当前选中的对象；无选择时为 null。</summary>
    DesignObject Selected { get; }
    /// <summary>按 Id 查找对象；不存在时抛 <see cref="ArgumentException"/>。</summary>
    DesignObject Find(Guid id);

    /// <summary>按类型新建默认图形并提交（Svg/Path3D 需导入，显式拒绝）。</summary>
    DesignEditResult Create(DesignObjectKind kind);
    /// <summary>生成类型默认草稿，不提交；供调用方修改后 Add。</summary>
    DesignObject CreateDraft(DesignObjectKind kind);
    /// <summary>加入新对象（ID 不得重复），提交后选中之。</summary>
    DesignEditResult Add(DesignObject item);
    /// <summary>选择对象；null 取消选择。选择不计入撤销历史。</summary>
    DesignEditResult Select(Guid? id);
    /// <summary>整体替换对象（Kind 不可变）；其余字段以新值覆盖。</summary>
    DesignEditResult Update(DesignObject item);
    /// <summary>按增量平移（mm）。</summary>
    DesignEditResult Move(Guid id, double dxMm, double dyMm, double dzMm = 0);
    /// <summary>调整到目标尺寸（mm）；LockAspect 时按比例联动；螺旋尺寸由参数决定，显式拒绝。</summary>
    DesignEditResult Resize(Guid id, double? widthMm = null, double? heightMm = null, double? depthMm = null);
    /// <summary>移动对象到目标组并在组内定位（beforeId/afterId 二选一；都不给则追加到组尾）。</summary>
    DesignEditResult Place(Guid id, string groupPath, Guid? beforeId = null, Guid? afterId = null);
    /// <summary>整组迁移到目标父组（不允许移入自身或子组，不允许与既有组重名）。</summary>
    DesignEditResult PlaceGroup(string source, string parent, Guid? beforeId = null, Guid? afterId = null);
    /// <summary>在同组内上移/下移一位（direction = -1/+1）。</summary>
    DesignEditResult Shift(Guid id, int direction);
    /// <summary>删除当前选中对象。</summary>
    DesignEditResult DeleteSelected();
    /// <summary>撤销上一步内容变更；无可撤销时 Changed=false。</summary>
    DesignEditResult Undo();
    /// <summary>重做；无可重做时 Changed=false。</summary>
    DesignEditResult Redo();
    /// <summary>更新项目设置（坐标映射结构校验 + 预设引用冻结）。</summary>
    DesignEditResult UpdateSettings(DesignProjectSettings settings);
    /// <summary>为对象（或全部对象/默认值）分配加工预设引用；makeDefault 同时写入项目默认。</summary>
    DesignEditResult AssignProcessing(Guid? objectId, DesignProcessingAssignment assignment, bool makeDefault = false);
    /// <summary>整体替换文档（项目打开/恢复用）：校验、排序并清空撤销历史。</summary>
    DesignEditResult ReplaceAll(IReadOnlyList<DesignObject> objects, DesignProjectSettings settings, Guid? selectedId = null);
    /// <summary>XY 平面命中测试：从显示顺序顶层向下，返回第一个命中对象的 Id；坐标与容差单位 mm。</summary>
    Guid? HitTestXY(double xMm, double yMm, double toleranceMm);
}

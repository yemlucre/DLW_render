using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;

namespace Agamemnon.Drawing;

/// <summary>
/// <see cref="IDesignDocument"/> 的默认实现：快照式状态 + 有限撤销/重做。
/// 文档上限：200 个对象、合计 200000 生成点、100 步历史。所有变更原子提交。
/// </summary>
public sealed class DesignDocument : IDesignDocument
{
    public const int MaxObjects = 200, MaxDocumentPoints = 200000, HistoryLimit = 100;
    private sealed record State(DesignObject[] Items, Guid? Selected, DesignProjectSettings Settings);
    private State _state = new(Array.Empty<DesignObject>(), null, new());
    private readonly List<State> _undo = new(), _redo = new();
    private long _revision;

    public long ContentRevision { get; private set; }
    public DesignProjectSettings Settings => _state.Settings;
    public event EventHandler Changed;
    public DesignDocumentSnapshot Snapshot => new(Array.AsReadOnly(_state.Items), _state.Selected, _undo.Count > 0, _redo.Count > 0, _revision);
    public DesignObject Selected => _state.Items.FirstOrDefault(p => p.Id == _state.Selected);
    public DesignObject Find(Guid id) => _state.Items.FirstOrDefault(p => p.Id == id) ?? throw new ArgumentException("对象已不存在。");

    /// <summary>图形类型的中文显示名。</summary>
    public static string KindName(DesignObjectKind kind) => kind switch
    { DesignObjectKind.Line => "线段", DesignObjectKind.Ellipse => "椭圆", DesignObjectKind.Rectangle => "矩形", DesignObjectKind.Spiral2D => "2D 螺旋", DesignObjectKind.Path3D => "3D 路径", DesignObjectKind.Helix3D => "3D 螺旋", DesignObjectKind.Svg => "SVG", _ => throw new ArgumentException("未知图形") };

    public DesignEditResult Create(DesignObjectKind kind)
        => Add(CreateDraft(kind));

    public DesignObject CreateDraft(DesignObjectKind kind)
    {
        if (kind is DesignObjectKind.Svg or DesignObjectKind.Path3D) throw new ArgumentException("此图形需要导入坐标路径。");
        var o = new DesignObject { Kind = kind, Name = KindName(kind) + " " + (_state.Items.Length + 1) };
        if (kind == DesignObjectKind.Helix3D) o = o with { InitialRadiusMm = 1, RadiusIncrementMm = 0.1, Resolution = 10 };
        return o;
    }

    public DesignEditResult Add(DesignObject item)
    {
        if (item == null) throw new ArgumentException("对象不能为空。");
        if (_state.Items.Any(o => o.Id == item.Id)) throw new ArgumentException("对象 ID 重复。");
        var copy = Freeze(item with { Processing = item.Processing ?? Settings.DefaultProcessing }); var next = TreeOrder(_state.Items.Append(copy).ToArray()); ValidateDocument(next);
        return Commit(new(next, copy.Id, Settings));
    }

    public DesignEditResult Select(Guid? id)
    {
        if (id.HasValue) Find(id.Value);
        if (_state.Selected == id) return Result(false);
        _state = _state with { Selected = id }; _revision++; Changed?.Invoke(this, EventArgs.Empty); return Result(true);
    }

    public DesignEditResult Update(DesignObject item)
    {
        if (item == null) throw new ArgumentException("对象不能为空。");
        var existing = Find(item.Id);
        if (existing.Kind != item.Kind) throw new ArgumentException("编辑不能更改对象类型；请创建新的对象。");
        var copy = Freeze(item); var next = _state.Items.Select(o => o.Id == copy.Id ? copy : o).ToArray();
        ValidateDocument(next); return Commit(new(TreeOrder(next), _state.Selected, Settings));
    }

    public DesignEditResult Move(Guid id, double dxMm, double dyMm, double dzMm = 0)
    {
        var o = Find(id);
        if (dxMm == 0 && dyMm == 0 && dzMm == 0) return Result(false);
        return Update(o with { Position = o.Position + new DesignPoint3(dxMm, dyMm, dzMm) });
    }

    public DesignEditResult Resize(Guid id, double? widthMm = null, double? heightMm = null, double? depthMm = null)
    {
        var o = Find(id);
        foreach (var v in new[] { widthMm, heightMm, depthMm })
            if (v.HasValue && (!double.IsFinite(v.Value) || v.Value <= 0 || v.Value > 10000)) throw new ArgumentException("目标尺寸需大于 0 且不超过 10000 mm。");
        if (o.Kind is DesignObjectKind.Spiral2D or DesignObjectKind.Helix3D) throw new ArgumentException("螺旋尺寸由半径、圈数及轴向长度决定。");
        if (o.Kind is DesignObjectKind.Ellipse or DesignObjectKind.Rectangle)
        {
            if (depthMm.HasValue) throw new ArgumentException("二维图形无深度。");
            double w = widthMm ?? o.WidthMm, h = heightMm ?? o.HeightMm;
            if (o.LockAspect && widthMm.HasValue && !heightMm.HasValue) h = w * o.HeightMm / o.WidthMm;
            if (o.LockAspect && heightMm.HasValue && !widthMm.HasValue) w = h * o.WidthMm / o.HeightMm;
            if (o.LockAspect && widthMm.HasValue && heightMm.HasValue && Math.Abs(w / h - o.WidthMm / o.HeightMm) > 1e-8) throw new ArgumentException("固定比时宽高比例需保持一致。");
            return Update(o with { WidthMm = w, HeightMm = h });
        }
        var bounds = DesignGeometry.Bounds(o.Kind == DesignObjectKind.Svg ? o.Paths : new[] { o.Points });
        double Factor(double? target, double current)
        { if (!target.HasValue) return 1; if (current < 1e-12) throw new ArgumentException("零尺寸轴不能按比例缩放；请编辑端点或坐标。"); return target.Value / current; }
        double fx = Factor(widthMm, bounds.Width), fy = Factor(heightMm, bounds.Height), fz = Factor(depthMm, bounds.Depth);
        if (o.LockAspect && o.Kind != DesignObjectKind.Line)
        {
            double factor = widthMm.HasValue ? fx : heightMm.HasValue ? fy : fz;
            if ((widthMm.HasValue && Math.Abs(fx - factor) > 1e-8) || (heightMm.HasValue && Math.Abs(fy - factor) > 1e-8) || (depthMm.HasValue && Math.Abs(fz - factor) > 1e-8)) throw new ArgumentException("固定比下不能分别改变各轴比例。");
            fx = fy = fz = factor;
        }
        // 围绕既有局部原点缩放；保留 Position 与路径点顺序。
        DesignPoint3 Scale(DesignPoint3 p) => new(p.X * fx, p.Y * fy, p.Z * fz);
        return Update(o.Kind == DesignObjectKind.Svg ? o with { Paths = o.Paths.Select(p => (IReadOnlyList<DesignPoint3>)Array.AsReadOnly(p.Select(Scale).ToArray())).ToArray() }
            : o with { Points = Array.AsReadOnly(o.Points.Select(Scale).ToArray()) });
    }

    /// <summary>组路径结构校验：最多 8 层、160 字符；每层 1–40 字符，以 / 分隔，不能含空层或控制字符。组是纯排序文件夹，不是几何变换。</summary>
    public static void ValidateGroupPath(string path)
    {
        if (path == null || path.Length > 160 || (path.Length > 0 &&
            (path.Split('/').Length > 8 || path.Split('/').Any(p => string.IsNullOrWhiteSpace(p) || p != p.Trim() || p.Length > 40 || p.Any(char.IsControl)))))
            throw new ArgumentException("组路径最多 8 层、160 字符；每层 1–40 字符，以 / 分隔，不能含空层或控制字符。");
    }

    private static DesignObject[] TreeOrder(DesignObject[] items)
    {
        var result = new List<DesignObject>();
        void Folder(string path)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (item.GroupPath == path) { result.Add(item); continue; }
                string prefix = path.Length == 0 ? "" : path + "/";
                if (!item.GroupPath.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string child = prefix + item.GroupPath.Substring(prefix.Length).Split('/')[0];
                if (seen.Add(child)) Folder(child);
            }
        }
        Folder(""); return result.ToArray();
    }

    public DesignEditResult Place(Guid id, string groupPath, Guid? beforeId = null, Guid? afterId = null)
    {
        ValidateGroupPath(groupPath); var item = Find(id);
        if (beforeId.HasValue && afterId.HasValue) throw new ArgumentException("不能同时指定前后插入点。");
        if (afterId.HasValue && Find(afterId.Value).GroupPath != groupPath) throw new ArgumentException("插入目标必须在目标组内。");
        if (beforeId.HasValue && Find(beforeId.Value).GroupPath != groupPath)
            throw new ArgumentException("插入目标必须在目标组内。");
        if (beforeId == id || afterId == id) return Result(false);
        var list = _state.Items.Where(o => o.Id != id).ToList();
        int index = afterId.HasValue ? list.FindIndex(o => o.Id == afterId) + 1 : beforeId.HasValue ? list.FindIndex(o => o.Id == beforeId) : list.FindLastIndex(o => o.GroupPath == groupPath || (groupPath.Length > 0 && o.GroupPath.StartsWith(groupPath + "/", StringComparison.Ordinal))) + 1;
        if (!beforeId.HasValue && !afterId.HasValue && groupPath.Length == 0) index = list.Count;
        if (index == 0 && !list.Any(o => o.GroupPath == groupPath)) index = list.Count;
        list.Insert(index, item with { GroupPath = groupPath }); var next = TreeOrder(list.ToArray());
        if (next.SequenceEqual(_state.Items)) return Result(false);
        return Commit(new(next, id, Settings));
    }

    public DesignEditResult PlaceGroup(string source, string parent, Guid? beforeId = null, Guid? afterId = null)
    {
        ValidateGroupPath(source); ValidateGroupPath(parent);
        if (source.Length == 0 || !_state.Items.Any(o => o.GroupPath == source || o.GroupPath.StartsWith(source + "/", StringComparison.Ordinal)))
            throw new ArgumentException("源组不存在。");
        if (parent == source || parent.StartsWith(source + "/", StringComparison.Ordinal)) throw new ArgumentException("不能将组移入自身或子组。");
        string name = source.Split('/')[^1], destination = parent.Length == 0 ? name : parent + "/" + name;
        bool InGroup(DesignObject o) => o.GroupPath == source || o.GroupPath.StartsWith(source + "/", StringComparison.Ordinal);
        if (destination != source && _state.Items.Any(o => !InGroup(o) && (o.GroupPath == destination || o.GroupPath.StartsWith(destination + "/", StringComparison.Ordinal))))
            throw new ArgumentException("目标已有同名组，请先调整组路径名称。");
        if (beforeId.HasValue && afterId.HasValue) throw new ArgumentException("不能同时指定前后插入点。");
        Guid? anchor = beforeId ?? afterId;
        if (anchor.HasValue && (InGroup(Find(anchor.Value)) || Find(anchor.Value).GroupPath != parent)) throw new ArgumentException("组插入目标必须在目标父组内。");
        var moved = _state.Items.Where(InGroup).Select(o => Freeze(o with { GroupPath = destination + o.GroupPath.Substring(source.Length) })).ToArray();
        var list = _state.Items.Where(o => !InGroup(o)).ToList();
        int index = beforeId.HasValue ? list.FindIndex(o => o.Id == beforeId) : afterId.HasValue ? list.FindIndex(o => o.Id == afterId) + 1 : parent.Length == 0 ? list.Count : list.FindLastIndex(o => o.GroupPath == parent || o.GroupPath.StartsWith(parent + "/", StringComparison.Ordinal)) + 1;
        if (!anchor.HasValue && index == 0) index = list.Count;
        list.InsertRange(index, moved); var next = TreeOrder(list.ToArray());
        if (next.SequenceEqual(_state.Items)) return Result(false);
        return Commit(new(next, _state.Selected, Settings));
    }

    public DesignEditResult Shift(Guid id, int direction)
    {
        if (direction != -1 && direction != 1) throw new ArgumentException("顺序方向只能为 -1 或 +1。");
        var item = Find(id); var siblings = _state.Items.Where(o => o.GroupPath == item.GroupPath).ToArray();
        int index = Array.FindIndex(siblings, o => o.Id == id), target = index + direction;
        if (target < 0 || target >= siblings.Length) return Result(false);
        return Place(id, item.GroupPath, direction < 0 ? siblings[target].Id : target + 1 < siblings.Length ? siblings[target + 1].Id : null);
    }

    public DesignEditResult DeleteSelected()
    {
        if (!_state.Selected.HasValue) return Result(false);
        return Commit(new(_state.Items.Where(o => o.Id != _state.Selected).ToArray(), null, Settings));
    }

    public DesignEditResult Undo() => Restore(_undo, _redo);
    public DesignEditResult Redo() => Restore(_redo, _undo);

    public DesignEditResult UpdateSettings(DesignProjectSettings settings)
        => Commit(new(_state.Items, _state.Selected, FreezeSettings(settings)));

    public DesignEditResult AssignProcessing(Guid? objectId, DesignProcessingAssignment assignment, bool makeDefault = false)
    {
        var frozen = FreezeProcessing(assignment);
        if (objectId.HasValue) Find(objectId.Value);
        if (_state.Items.Length == 0 && !makeDefault) return Result(false);
        var next = _state.Items.Select(o => !objectId.HasValue || o.Id == objectId ? o with { Processing = frozen } : o).ToArray();
        return Commit(new(next, _state.Selected, makeDefault ? Settings with { DefaultProcessing = frozen } : Settings));
    }

    public DesignEditResult ReplaceAll(IReadOnlyList<DesignObject> objects, DesignProjectSettings settings, Guid? selectedId = null)
    {
        if (objects == null || objects.Count > MaxObjects) throw new ArgumentException("对象集合无效。");
        var frozen = TreeOrder(objects.Select(Freeze).ToArray()); var config = FreezeSettings(settings);
        if (frozen.Select(o => o.Id).Distinct().Count() != frozen.Length || (selectedId.HasValue && !frozen.Any(o => o.Id == selectedId)))
            throw new ArgumentException("项目对象 ID 重复或选择不存在。");
        ValidateDocument(frozen);
        _state = new(frozen, selectedId, config); _undo.Clear(); _redo.Clear(); _revision++; ContentRevision++;
        Changed?.Invoke(this, EventArgs.Empty); return Result(true);
    }

    /// <summary>冻结加工预设引用：校验 PresetId 为 GUID、名称 1–80 字符，并深拷贝值字典。预设值目录/参数合法性由工艺流程层校验，此处不校验。</summary>
    public static DesignProcessingAssignment FreezeProcessing(DesignProcessingAssignment assignment)
    {
        if (assignment == null) return null;
        if (!Guid.TryParse(assignment.PresetId, out _) || string.IsNullOrWhiteSpace(assignment.PresetName) || assignment.PresetName.Length > 80)
            throw new ArgumentException("关联预设 ID 或名称无效。");
        return assignment with { Values = new ReadOnlyDictionary<string, JsonElement>(assignment.Values.ToDictionary(p => p.Key, p => p.Value.Clone())) };
    }

    /// <summary>冻结项目设置：坐标映射结构校验 + 默认预设引用冻结。</summary>
    public static DesignProjectSettings FreezeSettings(DesignProjectSettings settings)
    {
        if (settings == null || settings.Mapping == null) throw new ArgumentException("项目坐标转换配置缺失。");
        DesignMapping.Validate(settings.Mapping);
        return settings with { DefaultProcessing = FreezeProcessing(settings.DefaultProcessing) };
    }

    public Guid? HitTestXY(double xMm, double yMm, double toleranceMm)
    {
        if (!double.IsFinite(xMm) || !double.IsFinite(yMm) || !double.IsFinite(toleranceMm) || toleranceMm <= 0) throw new ArgumentException("命中测试坐标和容差无效。");
        foreach (var o in _state.Items.Reverse().Where(o => o.Visible))
        {
            var paths = DesignGeometry.Generate(o); var b = DesignGeometry.Bounds(paths);
            if (xMm < b.Minimum.X - toleranceMm || xMm > b.Maximum.X + toleranceMm || yMm < b.Minimum.Y - toleranceMm || yMm > b.Maximum.Y + toleranceMm) continue;
            foreach (var path in paths) for (int i = 1; i < path.Count; i++)
            {
                var a = path[i - 1]; var z = path[i]; double dx = z.X - a.X, dy = z.Y - a.Y, n = dx * dx + dy * dy;
                double t = n == 0 ? 0 : Math.Clamp(((xMm - a.X) * dx + (yMm - a.Y) * dy) / n, 0, 1);
                if (Math.Sqrt(Math.Pow(xMm - a.X - t * dx, 2) + Math.Pow(yMm - a.Y - t * dy, 2)) <= toleranceMm) return o.Id;
            }
            if (o.Kind is DesignObjectKind.Ellipse or DesignObjectKind.Rectangle)
            {
                var polygon = paths[0]; bool inside = false;
                for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
                    if ((polygon[i].Y > yMm) != (polygon[j].Y > yMm) && xMm < (polygon[j].X - polygon[i].X) * (yMm - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X) inside = !inside;
                if (inside) return o.Id;
            }
        }
        return null;
    }

    private static DesignObject Freeze(DesignObject o)
    {
        if (o == null) throw new ArgumentException("对象不能为空。");
        ValidateGroupPath(o.GroupPath);
        DesignGeometry.Validate(o);
        return o with { Points = Array.AsReadOnly(o.Points.ToArray()), Paths = Array.AsReadOnly(o.Paths.Select(p => (IReadOnlyList<DesignPoint3>)Array.AsReadOnly(p.ToArray())).ToArray()), Processing = FreezeProcessing(o.Processing) };
    }

    private static void ValidateDocument(DesignObject[] items)
    {
        if (items.Length > MaxObjects) throw new ArgumentException("最多支持 200 个对象。");
        long count = 0;
        foreach (var o in items) { count += DesignGeometry.Generate(o).Sum(p => (long)p.Count); if (count > MaxDocumentPoints) throw new ArgumentException("项目预览最多支持 200000 点。"); }
    }

    private DesignEditResult Commit(State next)
    {
        _undo.Add(_state); if (_undo.Count > HistoryLimit) _undo.RemoveAt(0); _redo.Clear(); _state = next; _revision++; ContentRevision++;
        Changed?.Invoke(this, EventArgs.Empty); return Result(true);
    }

    private DesignEditResult Restore(List<State> from, List<State> to)
    {
        if (from.Count == 0) return Result(false);
        to.Add(_state); _state = from[^1]; from.RemoveAt(from.Count - 1); _revision++; ContentRevision++; Changed?.Invoke(this, EventArgs.Empty); return Result(true);
    }

    private DesignEditResult Result(bool changed) => new(changed, Snapshot);
}

# Agamemnon.Render 渲染包契约

检查日期：2026-11-02。能力 ID：`render.frames`（implemented，agentExposed=false）。本文件是 `Function/Agamemnon.Render`（现独立仓库 DLW_render）的接口契约与边界记录。

## 定位与边界

- **职责**：**只读** Drawing 数据 → **输出画面**（PNG 帧）。含两翼：2D 画布（视图数学 + 帧渲染）与 3D 场景（构建 + 软件相机 + 线框帧渲染）。
- **依赖**：仅 `Agamemnon.Drawing`（回归检查强制：不引用任何其他程序集、无 WPF/Helix）。net8.0-windows + GDI+ 位图输出。
- **不在包内**：交互式视口、文档编辑（属 Drawing）、设备遥测、像素模式/加工准备。
- **与上游的关系**：主项目 Agamemnon 现行交互视图（Desktop 2D 画布与 Helix 3D 预览）保持原 Application/Desktop 链路；本包是同一语义的独立演进线，切换需上游确认。

## 公开接口

| 接口/类型 | 实现 | 说明 |
|---|---|---|
| `ICanvasView` | `CanvasView` | 2D 视图数学：Fit/FitBounds/ZoomAt/Pan、Screen↔Design 换算（语义同上游 CanvasViewService）；画布尺寸 0.001–10000 mm，比例 0.01–1000000 px/mm |
| `ICanvasRenderer` | `CanvasRenderer` | 文档快照 + 视图 → 2D 画布 PNG 帧（900×700）：自适应网格、坐标轴、1mm 比例尺、名称标签、标题 |
| `IScene3DBuilder` | `Scene3DBuilder` | 文档快照 → 只读 3D 场景（对象折线 + 状态标记 + 包围盒 + Revision；隐藏对象保留条目但无几何） |
| `ICamera3D` | `Camera3D` | 软件环绕相机：方位角/俯仰角（±89° 钳制）、透视（视场 5–170°）/正交、Fit 适配、Zoom；`Project` 深度 ≤0 表示视点后方（坐标 NaN，调用方跳过） |
| `ISceneRenderer3D` | `SceneRenderer3D` | 场景 + 相机 → 3D 线框 PNG 帧：深度着色（近深远浅）、地面网格、包围盒、按深度从远到近绘制、未 Fit 相机自动适配 |
| `CanvasFrame` | record | 输出帧：宽高 + PNG 字节；`Save(path)` 落盘、`IsPng()` 自校验（签名 + IHDR 尺寸） |

## 约定与限制

- 颜色约定（2D/3D 一致）：普通=钢蓝、选中=橙红加粗、锁定=灰、`Enabled=false`=浅蓝虚线；`Visible=false` 不绘制。
- 单位与坐标系：全部 mm；设计系原点左下、+X 右、+Y 上，3D 相机以 +Z 为上；屏幕 Y 向下（2D 换算自动翻转）。
- 帧尺寸固定 900×700，用途是**验证与预览输出**，不是交互视口；性能未按实时帧率优化。
- 非法输入（负尺寸、反转包围盒、零因子等）抛 `ArgumentException`/`ArgumentOutOfRangeException`；null 快照/场景抛 `ArgumentNullException`。

## 验证

2026-11-02：上游主仓库根 `test.ps1` Debug 与 Release 全量无硬件回归 **1005 项通过**（本包 36 项，`tests/RenderPackageChecks.cs`：包隔离、视图数学、画布帧、场景构建、相机投影、3D 帧）；本独立仓库运行同一检查集，输出 `render-2d.png`、`render-3d.png` 供人工核对。无设备操作。

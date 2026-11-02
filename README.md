# DLW_render（Agamemnon 渲染包）

Agamemnon「Function 功能包分层」的第二个包：**渲染域**——只读 Drawing 数据、输出画面（PNG 帧）。从主项目 `Function/Agamemnon.Render` 于 2026-11-02 抽出为独立仓库；为保证自包含可构建，同时携带其唯一依赖 `Agamemnon.Drawing`（与 [DLW_Drawing](https://github.com/yemlucre/DLW_Drawing) 同源同版本）。

## 定位

只读 `Agamemnon.Drawing` 的文档快照，输出 2D 画布帧与 3D 线框帧。明确不做：文档编辑（属 Drawing）、交互式视口（属宿主 UI）、设备操作。3D 为纯数学软件投影，**无 WPF/Helix 依赖**；回归检查强制"除 Drawing 外不引用任何其他程序集"。

## 目录

```text
src/Agamemnon.Drawing/   依赖包（画图域，同 DLW_Drawing）
src/Agamemnon.Render/
├── Canvas/    ICanvasView + CanvasView（2D 视图数学：适配/缩放/平移/坐标换算）
│              ICanvasRenderer + CanvasRenderer（文档快照+视图 → 2D 画布 PNG 帧）
├── ThreeD/    IScene3DBuilder + Scene3DBuilder（快照 → 只读 3D 场景）
│              ICamera3D + Camera3D（环绕相机：透视/正交/适配/俯仰钳制）
│              ISceneRenderer3D + SceneRenderer3D（深度着色线框 PNG 帧）
└── Frames.cs  CanvasFrame（PNG 字节 + 尺寸 + Save/IsPng）
tests/         独立无硬件检查（36 项，与主仓库同一检查集，输出两帧示例 PNG）
docs/          契约与边界说明（contract.md）
```

## 公开接口速览

```csharp
// 2D：文档快照 + 视图 → 画布帧
ICanvasView view = new CanvasView();
view.FitBounds(minX, minY, maxX, maxY, 900, 700);
CanvasFrame frame = new CanvasRenderer().Render(doc.Snapshot, view,
    new CanvasRenderOptions(Title: "2D"));

// 3D：快照 → 场景 → 相机 → 线框帧（软件投影）
Scene3D scene = new Scene3DBuilder().Build(doc.Snapshot);
ICamera3D camera = new Camera3D();               // 渲染器会对未适配相机自动 Fit
CanvasFrame frame3D = new SceneRenderer3D().Render(scene, camera,
    new Render3DOptions(Title: "3D"));

frame.Save("out.png");                            // 帧落盘
```

颜色约定（2D/3D 一致）：普通=钢蓝、选中=橙红加粗、锁定=灰、禁用=浅蓝虚线、隐藏不绘制。单位 mm；设计系原点左下、+X 右、+Y 上、3D 以 +Z 为上。

## 构建与检查

```powershell
dotnet run --project tests\Agamemnon.Render.Checks.csproj -c Release -- artifacts   # 36 项检查 + 两帧示例
```

需要 .NET 8 SDK（Windows：渲染用 GDI+；Drawing 部分跨平台）。

## 验证记录

- 2026-11-02：上游主仓库 Debug/Release 全量无硬件回归 1005 项通过（本包 36 项）；本仓库独立运行同一检查集通过，输出 render-2d.png / render-3d.png。
- 已知边界：帧为固定 900×700 的验证/预览输出，不是交互视口；上游 Desktop 交互视图未使用本包。

using System;
using System.IO;
using System.Linq;
using Agamemnon.Drawing;
using Agamemnon.Render;

// Function/Agamemnon.Render 渲染包的无硬件检查：包隔离、2D 视图数学、画布帧渲染、
// 3D 场景构建/相机/线框渲染。输出两帧 PNG 到 output 供人工核对。
internal static class RenderPackageChecks
{
    internal static void Run(Action<bool, string> check, string output)
    {
        PackageIsolation(check);
        CanvasViewChecks(check);
        CanvasRenderChecks(check, output);
        SceneChecks(check);
        CameraChecks(check);
        Render3DChecks(check, output);
    }

    private static void PackageIsolation(Action<bool, string> check)
    {
        var assembly = typeof(CanvasView).Assembly;
        check(assembly.GetName().Name == "Agamemnon.Render", "render package assembly is Agamemnon.Render");
        var refs = assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        check(refs.Contains("Agamemnon.Drawing"), "render package references the drawing package");
        check(!refs.Any(n => n != null && n.StartsWith("Agamemnon.") && n != "Agamemnon.Drawing"),
            "render package references no Agamemnon assembly except Drawing");
        check(!refs.Any(n => n == "PresentationFramework" || n == "WindowsBase" || (n != null && n.StartsWith("HelixToolkit"))),
            "render package uses no WPF or Helix dependency (software projection only)");
        check(typeof(ICanvasView).IsAssignableFrom(typeof(CanvasView)) && typeof(ICanvasRenderer).IsAssignableFrom(typeof(CanvasRenderer))
            && typeof(IScene3DBuilder).IsAssignableFrom(typeof(Scene3DBuilder)) && typeof(ICamera3D).IsAssignableFrom(typeof(Camera3D))
            && typeof(ISceneRenderer3D).IsAssignableFrom(typeof(SceneRenderer3D)), "render package implementations satisfy their interfaces");
    }

    private static void CanvasViewChecks(Action<bool, string> check)
    {
        var view = new CanvasView();
        view.SetSize(10, 8);
        view.FitBounds(-1, -1, 6, 5, 900, 700);
        var screen = view.DesignToScreen(3, 2);
        var back = view.ScreenToDesign(screen.X, screen.Y);
        check(Near(back.X, 3) && Near(back.Y, 2), "screen/design coordinate round trip");
        check(view.DesignToScreen(0, 2).Y < view.DesignToScreen(0, 0).Y, "design +Y maps to smaller screen Y");
        var anchor = view.ScreenToDesign(400, 300);
        view.ZoomAt(400, 300, 1.7);
        var anchored = view.ScreenToDesign(400, 300);
        check(Near(anchored.X, anchor.X) && Near(anchored.Y, anchor.Y), "zoom keeps the clicked design point anchored");
        var beforePan = view.Snapshot;
        view.Pan(50, -30);
        var afterPan = view.Snapshot;
        check(Near(afterPan.OriginX - beforePan.OriginX, 50) && Near(beforePan.OriginY - afterPan.OriginY, 30),
            "pan shifts the origin by exact pixel deltas");
        check(view.Contains(new(0, 0)) && view.Contains(new(10, 8)) && !view.Contains(new(10.1, 4)), "canvas contains honours mm bounds");
        Rejects<ArgumentOutOfRangeException>(() => view.SetSize(-1, 5), check, "invalid canvas size rejected");
        Rejects<ArgumentOutOfRangeException>(() => view.FitBounds(5, 5, 1, 1, 900, 700), check, "reversed fit bounds rejected");
    }

    private static void CanvasRenderChecks(Action<bool, string> check, string output)
    {
        var doc = new DesignDocument();
        doc.Create(DesignObjectKind.Rectangle);
        var circleId = doc.Add(new DesignObject { Kind = DesignObjectKind.Ellipse, Name = "圆 r=1mm", WidthMm = 2, HeightMm = 2, Resolution = 120, Position = new(2, 2, 0) }).Snapshot.SelectedId;
        var view = FitTo(doc, 900, 700);
        var renderer = new CanvasRenderer();
        var frame = renderer.Render(doc.Snapshot, view, new CanvasRenderOptions(Title: "2D 画布帧（Agamemnon.Render）"));
        check(frame.IsPng() && frame.WidthPx == 900 && frame.HeightPx == 700, "canvas frame is a valid 900x700 png");
        check(frame.PngBytes.Length > 5000, "canvas frame carries real image data");
        frame.Save(Path.Combine(output, "render-2d.png"));

        doc.Update(doc.Find(circleId.Value) with { Visible = false });
        var hidden = renderer.Render(doc.Snapshot, view);
        check(!hidden.PngBytes.SequenceEqual(frame.PngBytes), "hiding an object changes the canvas frame");
        doc.Update(doc.Find(circleId.Value) with { Visible = true });
        doc.Select(circleId);
        var selected = renderer.Render(doc.Snapshot, view);
        check(!selected.PngBytes.SequenceEqual(frame.PngBytes), "selection highlight changes the canvas frame");
        var empty = renderer.Render(new DesignDocument().Snapshot, new CanvasView());
        check(empty.IsPng(), "empty document still renders a valid frame");
        Rejects<ArgumentNullException>(() => renderer.Render(null, new CanvasView()), check, "null snapshot rejected");
    }

    private static void SceneChecks(Action<bool, string> check)
    {
        var doc = new DesignDocument();
        doc.Create(DesignObjectKind.Helix3D);
        doc.Add(new DesignObject { Kind = DesignObjectKind.Rectangle, Name = "底座", WidthMm = 4, HeightMm = 4 });
        var scene = new Scene3DBuilder().Build(doc.Snapshot);
        check(scene.Objects.Count == 2 && scene.Revision == doc.Snapshot.Revision, "scene mirrors object count and document revision");
        check(scene.Bounds.HasValue && scene.Bounds.Value.Width > 0 && scene.Bounds.Value.Depth > 0, "scene bounds cover XY and Z extents");
        var hiddenId = doc.Add(new DesignObject { Kind = DesignObjectKind.Line, Name = "辅助线", Points = Array.AsReadOnly(new[] { new DesignPoint3(-9, -9, 0), new DesignPoint3(9, 9, 0) }) }).Snapshot.SelectedId;
        doc.Update(doc.Find(hiddenId.Value) with { Visible = false });
        var rescened = new Scene3DBuilder().Build(doc.Snapshot);
        var hiddenObject = rescened.Objects.Single(o => o.Id == hiddenId.Value);
        check(hiddenObject.Visible == false && hiddenObject.Paths.Count == 0 && rescened.Objects.Count == 3, "hidden objects stay listed but carry no geometry");
    }

    private static void CameraChecks(Action<bool, string> check)
    {
        var camera = new Camera3D();
        check(!camera.IsFitted, "camera starts unfitted");
        var bounds = new DesignBounds(new(-2, -2, -1), new(2, 2, 1));
        camera.Fit(bounds, 900, 700);
        check(camera.IsFitted && Near(camera.Target.X, 0) && Near(camera.Target.Z, 0), "fit centers the camera on the bounds");
        var center = camera.Project(camera.Target, 900, 700);
        check(Near(center.X, 450) && Near(center.Y, 350) && center.Depth > 0, "bounds center projects to the screen centre in front of the camera");
        var corner = camera.Project(bounds.Maximum, 900, 700);
        camera.Orbit(40, 0);
        var orbited = camera.Project(bounds.Maximum, 900, 700);
        check(Math.Abs(corner.X - orbited.X) > 1 || Math.Abs(corner.Y - orbited.Y) > 1, "orbit changes the projection");
        camera.SetPerspective(false);
        var ortho = camera.Project(bounds.Maximum, 900, 700);
        check(Math.Abs(orbited.X - ortho.X) > 1 || Math.Abs(orbited.Y - ortho.Y) > 1, "orthographic and perspective projections differ");
        camera.SetAngles(0, 100);
        check(Near(camera.PitchDegrees, 89), "pitch clamps to 89 degrees");
        var zoom = new Camera3D();
        double before = zoom.DistanceMm;
        zoom.Zoom(2);
        check(Near(zoom.DistanceMm, before / 2) && zoom.OrthoPixelsPerMm > 20, "zoom halves the distance and raises the ortho scale");
        double yaw = 32 * Math.PI / 180, pitch = 24 * Math.PI / 180, horizontal = Math.Cos(pitch);
        var offset = new DesignPoint3(Math.Cos(yaw) * horizontal, Math.Sin(yaw) * horizontal, Math.Sin(pitch));
        var behind = new Camera3D().Project(offset * 120, 900, 700);
        check(behind.Depth <= 0 && double.IsNaN(behind.X), "points behind the camera report invalid projection");
        check(new Camera3D().Project(default, 900, 700).Depth > 0, "the target itself projects in front of the camera");
    }

    private static void Render3DChecks(Action<bool, string> check, string output)
    {
        var doc = new DesignDocument();
        doc.Create(DesignObjectKind.Helix3D);
        doc.Add(new DesignObject { Kind = DesignObjectKind.Rectangle, Name = "底座", WidthMm = 5, HeightMm = 5 });
        var scene = new Scene3DBuilder().Build(doc.Snapshot);
        var renderer = new SceneRenderer3D();
        var camera = new Camera3D();
        var frame = renderer.Render(scene, camera, new Render3DOptions(Title: "3D 线框帧（Agamemnon.Render）"));
        check(frame.IsPng() && frame.WidthPx == 900 && frame.HeightPx == 700, "3D frame is a valid 900x700 png");
        check(camera.IsFitted, "renderer auto-fits an unfitted camera");
        check(frame.PngBytes.Length > 5000, "3D frame carries real image data");
        frame.Save(Path.Combine(output, "render-3d.png"));
        camera.SetPerspective(false);
        var ortho = renderer.Render(scene, camera);
        check(!ortho.PngBytes.SequenceEqual(frame.PngBytes), "orthographic 3D frame differs from perspective");
        var empty = renderer.Render(new Scene3D(Array.Empty<SceneObject3D>(), null, 0), new Camera3D());
        check(empty.IsPng(), "empty scene still renders a valid frame");
        Rejects<ArgumentNullException>(() => renderer.Render(null, new Camera3D()), check, "null scene rejected");
    }

    private static CanvasView FitTo(DesignDocument doc, double width, double height)
    {
        var view = new CanvasView();
        var bounds = DesignGeometry.Bounds(doc.Snapshot.Objects.SelectMany(DesignGeometry.Generate));
        view.FitBounds(bounds.Minimum.X - 1, bounds.Minimum.Y - 1, bounds.Maximum.X + 1, bounds.Maximum.Y + 1, width, height);
        return view;
    }
    private static bool Near(double a, double b, double epsilon = 1e-6) => Math.Abs(a - b) < epsilon;

    private static void Rejects<TException>(Action action, Action<bool, string> check, string name) where TException : Exception
    {
        bool threw = false;
        try { action(); } catch (TException) { threw = true; } catch (Exception) { threw = false; }
        check(threw, name);
    }
}

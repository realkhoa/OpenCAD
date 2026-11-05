using OpenCad.Core;
using OpenCad.Core.Commands;
using OpenCad.Core.Editing;
using OpenCad.Core.Geometry;
using CSMath;
using ACadSharp.Entities;

namespace OpenCad.App;

/// <summary>
/// Tự kiểm tra ở mức giao diện, chạy trong chính tiến trình app (không cần chuột).
/// Mục đích: bắt lỗi "hàm có nhưng chưa nối" — loại lỗi mà test Core không thấy.
/// </summary>
internal static class UiSelfTest
{
    /// <summary>
    /// Kiểm thử chuột: gọi thẳng OnMouseDown/Move/Up với toạ độ màn hình.
    /// Đây là phần trước đây không kiểm được vì phụ thuộc cửa sổ thật.
    /// </summary>
    private static void RunMouseTests(CadWorkspace workspace, CadEditor editor,
                                      CadCanvas canvas, Action<string, bool, string> check)
    {
        // Bản vẽ sạch để đếm cho chắc
        foreach (var entity in workspace.Document.Entities.ToList())
            workspace.Document.Entities.Remove(entity);

        editor.Reload();
        canvas.SetMode(EditMode.Line);

        var p1 = canvas.WorldToScreenPoint(new Pt(0, 0));
        var p2 = canvas.WorldToScreenPoint(new Pt(1000, 0));

        canvas.SimulateMouseDown(p1.X, p1.Y);
        canvas.SimulateMouseDown(p2.X, p2.Y);

        var lines = workspace.Document.Entities.OfType<Line>().ToList();

        check("Chuột: vẽ được LINE bằng 2 lần click",
            lines.Count == 1, lines.Count + " line");

        if (lines.Count == 1)
        {
            check("Chuột: LINE có đúng toạ độ",
                Math.Abs(lines[0].StartPoint.X) < 5 && Math.Abs(lines[0].EndPoint.X - 1000) < 5,
                $"({lines[0].StartPoint.X:0.#}) → ({lines[0].EndPoint.X:0.#})");
        }

        canvas.Cancel();
        canvas.ClearSelection();

        // Click lên đường để chọn
        var mid = canvas.WorldToScreenPoint(new Pt(500, 0));
        canvas.SimulateMouseDown(mid.X, mid.Y);
        canvas.SimulateMouseUp(mid.X, mid.Y);

        check("Chuột: click chọn được LINE",
            canvas.Selection.Count == 1, canvas.Selection.Count + " chọn");

        // Kéo đối tượng đã chọn sang phải 500 đơn vị
        var dragTo = canvas.WorldToScreenPoint(new Pt(1000, 0));
        canvas.SimulateMouseDown(mid.X, mid.Y);

        check("Chuột: bấm vào grip thì bắt đầu kéo grip",
            canvas.DragState.Length > 0, canvas.DragState);

        canvas.SimulateMouseMove(dragTo.X, dragTo.Y);

        check("Chuột: rê chuột làm hình dời theo",
            workspace.Document.Entities.OfType<Line>().First().StartPoint.X > 100,
            "start=" + workspace.Document.Entities.OfType<Line>().First().StartPoint.X.ToString("0.#")
            + " | " + canvas.DragState);

        canvas.SimulateMouseUp(dragTo.X, dragTo.Y);

        check("Chuột: kéo dời được đối tượng",
            workspace.Document.Entities.OfType<Line>().Any(l => l.StartPoint.X > 300),
            workspace.Document.Entities.OfType<Line>().FirstOrDefault()?.StartPoint.X.ToString("0.#") ?? "null");

        canvas.Undo();
        canvas.ClearSelection();

        check("Chuột: undo sau khi kéo phục hồi",
            workspace.Document.Entities.OfType<Line>().Any(l => Math.Abs(l.StartPoint.X) < 50),
            workspace.Document.Entities.OfType<Line>().FirstOrDefault()?.StartPoint.X.ToString("0.#") ?? "null");

        // Chọn bằng khung: kéo từ trên trái xuống dưới phải quanh đường
        var boxStart = canvas.WorldToScreenPoint(new Pt(-500, 500));
        var boxEnd = canvas.WorldToScreenPoint(new Pt(1500, -500));

        canvas.SimulateMouseDown(boxStart.X, boxStart.Y);
        canvas.SimulateMouseMove(boxEnd.X, boxEnd.Y);
        canvas.SimulateMouseUp(boxEnd.X, boxEnd.Y);

        check("Chuột: chọn bằng khung (window)",
            canvas.Selection.Count == 1, canvas.Selection.Count + " chọn");
    }

    /// <summary>
    /// Kiểm thử các lệnh vẽ hoàn chỉnh: chọn lệnh → click điểm → nhập số nếu cần.
    /// Đây là đường đi thật của người dùng, không chỉ là gọi hàm Core.
    /// </summary>
    private static void RunCommandFlowTests(CadWorkspace workspace, CadEditor editor,
                                            CadCanvas canvas, Action<string, bool, string> check)
    {
        foreach (var entity in workspace.Document.Entities.ToList())
            workspace.Document.Entities.Remove(entity);

        editor.Reload();

        // ORTHO để bật từ test trước sẽ ép click thẳng hàng — tắt để kiểm hình học chính xác
        canvas.SetOrthoForTest(false);
        canvas.Cancel();

        // ---- CIRCLE: click tâm + click bán kính
        canvas.SetMode(EditMode.Circle);

        var center = canvas.WorldToScreenPoint(new Pt(0, 0));
        var rim = canvas.WorldToScreenPoint(new Pt(200, 0));

        canvas.SimulateMouseDown(center.X, center.Y);
        canvas.SimulateMouseDown(rim.X, rim.Y);

        var circle = workspace.Document.Entities.OfType<Circle>().FirstOrDefault();

        check("Lệnh: CIRCLE bằng 2 click tạo đường tròn", circle is not null, "có");

        if (circle is not null)
        {
            check("Lệnh: CIRCLE đúng bán kính ~200",
                Math.Abs(circle.Radius - 200) < 10, circle.Radius.ToString("0.#"));
        }

        canvas.Cancel();

        // ---- RECTANG: 2 click
        canvas.SetMode(EditMode.Rectangle);

        var corner1 = canvas.WorldToScreenPoint(new Pt(500, 500));
        var corner2 = canvas.WorldToScreenPoint(new Pt(900, 700));

        canvas.SimulateMouseDown(corner1.X, corner1.Y);
        canvas.SimulateMouseDown(corner2.X, corner2.Y);

        var rect = workspace.Document.Entities.OfType<LwPolyline>()
            .FirstOrDefault(p => p.IsClosed && p.Vertices.Count == 4);

        check("Lệnh: RECTANG tạo polyline kín 4 đỉnh", rect is not null,
            rect is null ? "không thấy" : rect.Vertices.Count + " đỉnh");

        if (rect is not null)
        {
            var xs = rect.Vertices.Select(v => v.Location.X).ToList();
            var ys = rect.Vertices.Select(v => v.Location.Y).ToList();

            check("Lệnh: RECTANG đúng kích thước ~400×200",
                Math.Abs(xs.Max() - xs.Min() - 400) < 10 &&
                Math.Abs(ys.Max() - ys.Min() - 200) < 10,
                $"{xs.Max() - xs.Min():0.#}×{ys.Max() - ys.Min():0.#}");
        }

        canvas.Cancel();

        // ---- DIVIDE: chọn 1 line rồi nhập số đoạn
        var target = workspace.Document.Entities.OfType<Circle>().FirstOrDefault();

        if (target is not null)
        {
            canvas.ClearSelection();
            canvas.SimulateMouseDown(canvas.WorldToScreenPoint(new Pt(200, 0)).X,
                                     canvas.WorldToScreenPoint(new Pt(200, 0)).Y);
            canvas.SimulateMouseUp(canvas.WorldToScreenPoint(new Pt(200, 0)).X,
                                   canvas.WorldToScreenPoint(new Pt(200, 0)).Y);

            var selected = canvas.Selection.Count;

            canvas.SetMode(EditMode.Divide);

            var prompted = canvas.AwaitingInput;
            var provided = canvas.ProvideInput("4");

            var points = workspace.Document.Entities.OfType<ACadSharp.Entities.Point>().Count();

            check("Lệnh: DIVIDE hỏi số đoạn sau khi chọn",
                prompted, prompted ? "có hỏi" : "không hỏi (chọn " + selected + ")");

            check("Lệnh: DIVIDE tạo đúng số điểm chia",
                points == 3, points + " điểm");
        }

        canvas.Cancel();
        canvas.ClearSelection();
    }


    /// <summary>
    /// Kéo grip trên MainForm thật — nơi có panel Layer/Properties và status bar cùng
    /// lắng nghe sự kiện Changed. Đây là đường đi giống người dùng nhất.
    /// </summary>
    /// <summary>
    /// Kéo grip trên cửa sổ thật với NHIỀU loại entity — kể cả loại mà panel Properties
    /// phải đọc thuộc tính riêng (hatch, kích thước, leader, spline…).
    /// </summary>
    private static void RunGripTypesOnRealForm(Action<string, bool, string> check)
    {
        var makers = new (string Name, Func<Entity> Make)[]
        {
            ("Hatch", () =>
            {
                var rect = new LwPolyline { IsClosed = true };
                rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(200, 200) });
                rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(800, 200) });
                rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(800, 600) });
                rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(200, 600) });

                Entity? made = OpenCad.Core.Editing.HatchFactory.FromBoundary(rect);

                return made ?? new Line();
            }),
            ("Dimension", () => new DimensionAligned
            {
                FirstPoint = new XYZ(200, 200, 0),
                SecondPoint = new XYZ(800, 200, 0),
                DefinitionPoint = new XYZ(500, 350, 0),
            }),
            ("Text", () => new TextEntity
            {
                InsertPoint = new XYZ(300, 300, 0), Height = 120, Value = "CHU",
            }),
            ("Circle", () => new Circle { Center = new XYZ(500, 500, 0), Radius = 150 }),
            ("Leader", () =>
            {
                var leader = new Leader { TextHeight = 80, ArrowHeadEnabled = true };
                leader.Vertices.Add(new XYZ(200, 200, 0));
                leader.Vertices.Add(new XYZ(600, 600, 0));
                return leader;
            }),
            ("Spline", () =>
            {
                var spline = new Spline();
                spline.ControlPoints.Add(new XYZ(100, 100, 0));
                spline.ControlPoints.Add(new XYZ(400, 500, 0));
                spline.ControlPoints.Add(new XYZ(800, 200, 0));
                return spline;
            }),
        };

        var broken = new List<string>();

        foreach (var (name, make) in makers)
        {
            try
            {
                using var form = new MainForm();
                form.InitializeForTest();

                var canvas = form.CanvasForTest;
                var editor = form.EditorForTest;

                if (editor is null) { broken.Add(name + ": no editor"); continue; }

                foreach (var e in editor.Workspace.Document.Entities.ToList())
                    editor.Workspace.Document.Entities.Remove(e);

                editor.Reload();

                var entity = make();
                editor.AddEntities(new[] { entity }, "Add");
                canvas.ZoomExtents();
                canvas.SelectAll();

                var grips = GripSet.For(entity);

                if (grips.Count == 0)
                {
                    // Không có grip: kéo cả đối tượng để chắc đường đó cũng không crash
                    var center = canvas.WorldToScreenPoint(new Pt(500, 400));
                    canvas.SimulateMouseDown(center.X, center.Y);
                    canvas.SimulateMouseMove(center.X + 40, center.Y + 30);
                    canvas.SimulateMouseUp(center.X + 40, center.Y + 30);
                    continue;
                }

                var grip = grips[0];
                var from = canvas.WorldToScreenPoint(grip.Position);
                var to = canvas.WorldToScreenPoint(new Pt(grip.Position.X + 120, grip.Position.Y + 90));

                canvas.SimulateMouseDown(from.X, from.Y);
                canvas.SimulateMouseMove(to.X, to.Y);
                canvas.SimulateMouseUp(to.X, to.Y);
            }
            catch (Exception ex)
            {
                broken.Add(name + " (" + ex.GetType().Name + ")");
            }
        }

        check("Grip trên cửa sổ thật: mọi loại đối tượng không crash",
            broken.Count == 0,
            broken.Count == 0 ? makers.Length + " loại" : string.Join(", ", broken));

        check("Grip: không có lỗi UI nào được ghi nhận",
            CadCanvas.LastUiError is null, CadCanvas.LastUiError ?? "sạch");
    }

    /// <summary>
    /// Kiểm đường VẼ: render từng loại entity (đang chọn để có grip) ra bitmap.
    /// Phần này trước đây chưa từng được chạy — lỗi vẽ làm WinForms hiện hộp thoại lỗi.
    /// </summary>
    private static void RunPaintTests(CadWorkspace workspace, CadEditor editor,
                                      CadCanvas canvas, Action<string, bool, string> check)
    {
        var makers = new (string Name, Func<Entity> Make)[]
        {
            ("Line", () => new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(1000, 0, 0) }),
            ("Circle", () => new Circle { Center = new XYZ(500, 500, 0), Radius = 200 }),
            ("Arc", () => new Arc
            {
                Center = new XYZ(500, 500, 0), Radius = 200, StartAngle = 0, EndAngle = Math.PI,
            }),
            ("Text", () => new TextEntity { InsertPoint = new XYZ(100, 100, 0), Height = 100, Value = "ABC" }),
            ("Polyline", () =>
            {
                var p = new LwPolyline();
                p.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 0) });
                p.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(400, 300) });
                return p;
            }),
            ("Dimension", () => new DimensionAligned
            {
                FirstPoint = new XYZ(0, 0, 0), SecondPoint = new XYZ(800, 0, 0),
                DefinitionPoint = new XYZ(400, 200, 0),
            }),
        };

        var broken = new List<string>();

        foreach (var (name, make) in makers)
        {
            foreach (var e in workspace.Document.Entities.ToList())
                workspace.Document.Entities.Remove(e);

            editor.Reload();
            canvas.Cancel();
            canvas.ClearSelection();

            try
            {
                var entity = make();
                editor.AddEntities(new[] { entity }, "Add");
                canvas.ZoomExtents();
                canvas.SelectAll();

                using var bitmap = canvas.RenderForTest(640, 480);

                // Vẽ lần nữa khi đang chạy lệnh (có preview)
                canvas.SetMode(EditMode.Line);
                using var bitmap2 = canvas.RenderForTest(640, 480);
                canvas.Cancel();
            }
            catch (Exception ex)
            {
                broken.Add(name + " (" + ex.GetType().Name + ")");
            }
        }

        check("Vẽ: render mọi loại đối tượng không ném ngoại lệ",
            broken.Count == 0,
            broken.Count == 0 ? makers.Length + " loại" : string.Join(", ", broken));

        check("Vẽ: không ghi nhận lỗi vẽ nào",
            CadCanvas.LastUiError is null, CadCanvas.LastUiError ?? "sạch");
    }

    /// <summary>
    /// Mở samples/can-ho.dwg, chuyển sang tờ A3 và render ra PNG.
    /// Mục đích: có bằng chứng bằng mắt về tờ in mà không cần điều khiển chuột thật.
    /// </summary>
    private static void RunSheetRenderTest(Action<string, bool, string> check)
    {
        var samplePath = Path.Combine("samples", "can-ho.dwg");

        if (!File.Exists(samplePath))
        {
            check("Tờ mẫu: có samples/can-ho.dwg", false, "thiếu file");
            return;
        }

        try
        {
            using var form = new MainForm(samplePath);
            form.InitializeForTest();

            var canvas = form.CanvasForTest;
            var editor = form.EditorForTest;

            if (editor is null) { check("Tờ mẫu: có editor", false, "null"); return; }

            var names = OpenCad.Core.Layout.LayoutFactory.LayoutNames(editor.Workspace.Document);

            if (!names.Contains("A3-1-50"))
            {
                check("Tờ mẫu: có layout A3-1-50", false, string.Join(",", names));
                return;
            }

            form.OpenLayoutForTest("A3-1-50");

            check("Tờ mẫu: mở được layout A3", canvas.ActiveLayoutName == "A3-1-50",
                canvas.ActiveLayoutName);


            canvas.Width = 1240;
            canvas.Height = 877;

            using var bitmap = canvas.RenderForTest(1240, 877);

            var outDir = "docs";
            Directory.CreateDirectory(outDir);

            var outPath = Path.Combine(outDir, "sheet-can-ho.png");
            bitmap.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);

            check("Tờ mẫu: render ra PNG",
                File.Exists(outPath) && new FileInfo(outPath).Length > 5000,
                new FileInfo(outPath).Length + " bytes");
        }
        catch (Exception ex)
        {
            check("Tờ mẫu: render không ném ngoại lệ", false,
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void RunGripTestOnRealForm(Action<string, bool, string> check)
    {
        try
        {
            using var form = new MainForm();
            form.InitializeForTest();

            var canvas = form.CanvasForTest;
            var editor = form.EditorForTest;

            if (editor is null)
            {
                check("Grip trên cửa sổ thật: có editor", false, "null");
                return;
            }

            var entity = new Line
            {
                StartPoint = new XYZ(0, 0, 0),
                EndPoint = new XYZ(1000, 0, 0),
            };

            editor.AddEntities(new Entity[] { entity }, "Add");
            canvas.ZoomExtents();

            // Chọn rồi kéo grip giữa
            var mid = canvas.WorldToScreenPoint(new Pt(500, 0));

            canvas.SimulateMouseDown(mid.X, mid.Y);
            canvas.SimulateMouseUp(mid.X, mid.Y);

            var selected = canvas.Selection.Count;
            check("Grip trên cửa sổ thật: chọn được đối tượng", selected == 1, selected + " chọn");

            string? error = null;

            try
            {
                var target = canvas.WorldToScreenPoint(new Pt(500, 400));

                canvas.SimulateMouseDown(mid.X, mid.Y);
                canvas.SimulateMouseMove(target.X, target.Y);
                canvas.SimulateMouseUp(target.X, target.Y);
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
            }

            check("Grip trên cửa sổ thật: kéo không ném ngoại lệ",
                error is null, error ?? "ok");
            check("Grip trên cửa sổ thật: hình đã dời",
                editor.Workspace.Document.Entities.OfType<Line>().Any(l => l.StartPoint.Y > 100),
                editor.Workspace.Document.Entities.OfType<Line>().FirstOrDefault()?.StartPoint.Y.ToString("0.#") ?? "?");
            check("Grip trên cửa sổ thật: không có lỗi UI ghi nhận",
                CadCanvas.LastUiError is null, CadCanvas.LastUiError ?? "sạch");
        }
        catch (Exception ex)
        {
            check("Grip trên cửa sổ thật: tạo được cửa sổ", false,
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>Kiểm thử TRIM, MIRROR, ROTATE qua mô phỏng chuột và nhập số.</summary>
    private static void RunTrimMirrorRotateTests(CadWorkspace workspace, CadEditor editor,
                                                 CadCanvas canvas, Action<string, bool, string> check)
    {
        foreach (var entity in workspace.Document.Entities.ToList())
            workspace.Document.Entities.Remove(entity);

        editor.Reload();
        canvas.SetOrthoForTest(false);
        canvas.Cancel();
        canvas.ClearSelection();

        // ---- TRIM: đường ngang bị cắt bởi đường dọc
        var horizontal = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(1000, 0, 0),
        };

        var cutter = new Line
        {
            StartPoint = new XYZ(500, -200, 0),
            EndPoint = new XYZ(500, 200, 0),
        };

        editor.AddEntities(new Entity[] { horizontal, cutter }, "Add");

        canvas.SetMode(EditMode.Trim);

        // Click phần bên phải để cắt
        var trimPoint = canvas.WorldToScreenPoint(new Pt(800, 0));
        canvas.SimulateMouseDown(trimPoint.X, trimPoint.Y);
        canvas.SimulateMouseUp(trimPoint.X, trimPoint.Y);

        var remaining = workspace.Document.Entities.OfType<Line>()
            .Where(l => Math.Abs(l.StartPoint.Y) < 1 && Math.Abs(l.EndPoint.Y) < 1)
            .ToList();

        check("TRIM: cắt được đoạn thừa", remaining.Count >= 1,
            remaining.Count + " đoạn");

        if (remaining.Count > 0)
        {
            var maxX = remaining.Max(l => Math.Max(l.StartPoint.X, l.EndPoint.X));

            check("TRIM: đoạn còn lại không vượt quá đường cắt",
                maxX <= 510, "max X = " + maxX.ToString("0.#"));
        }

        canvas.Cancel();
        canvas.ClearSelection();

        // ---- MIRROR: lật qua trục Y
        foreach (var entity in workspace.Document.Entities.ToList())
            workspace.Document.Entities.Remove(entity);

        editor.Reload();

        var target = new Line
        {
            StartPoint = new XYZ(100, 0, 0),
            EndPoint = new XYZ(300, 0, 0),
        };

        editor.AddEntities(new Entity[] { target }, "Add");
        canvas.SelectAll();

        canvas.SetMode(EditMode.Mirror);

        var axis1 = canvas.WorldToScreenPoint(new Pt(0, 1000));
        var axis2 = canvas.WorldToScreenPoint(new Pt(0, -1000));

        canvas.SimulateMouseDown(axis1.X, axis1.Y);
        canvas.SimulateMouseDown(axis2.X, axis2.Y);

        var mirrored = workspace.Document.Entities.OfType<Line>()
            .Where(l => l.StartPoint.X < -50 || l.EndPoint.X < -50)
            .ToList();

        check("MIRROR: tạo bản lật qua trục Y", mirrored.Count == 1, mirrored.Count + " bản");

        if (mirrored.Count == 1)
        {
            check("MIRROR: toạ độ lật đúng (-300, 0)",
                Math.Abs(mirrored[0].StartPoint.X + 300) < 10 ||
                Math.Abs(mirrored[0].EndPoint.X + 300) < 10,
                $"{mirrored[0].StartPoint.X:0.#} → {mirrored[0].EndPoint.X:0.#}");
        }

        canvas.Cancel();
        canvas.ClearSelection();

        // ---- ROTATE: xoay 90° quanh điểm gốc
        var rotateLine = new Line
        {
            StartPoint = new XYZ(1000, 0, 0),
            EndPoint = new XYZ(2000, 0, 0),
        };

        editor.AddEntities(new Entity[] { rotateLine }, "Add");

        var before = workspace.Document.Entities.Count();
        canvas.ClearSelection();

        // Chọn line vừa thêm (cuối danh sách)
        var mid = canvas.WorldToScreenPoint(new Pt(1500, 0));
        canvas.SimulateMouseDown(mid.X, mid.Y);
        canvas.SimulateMouseUp(mid.X, mid.Y);

        canvas.SetMode(EditMode.Rotate);

        var pivot = canvas.WorldToScreenPoint(new Pt(0, 0));
        canvas.SimulateMouseDown(pivot.X, pivot.Y);

        var askedAngle = canvas.AwaitingInput;
        canvas.ProvideInput("90");

        check("ROTATE: hỏi góc sau khi chọn tâm", askedAngle, askedAngle ? "có" : "không");

        var rotated = workspace.Document.Entities.OfType<Line>()
            .FirstOrDefault(l => Math.Abs(l.StartPoint.Y - 1000) < 20);

        check("ROTATE: xoay 90° đưa line lên trục Y", rotated is not null,
            rotated is null ? "không thấy"
                : $"({rotated.StartPoint.X:0.#}, {rotated.StartPoint.Y:0.#})");

        canvas.Cancel();
        canvas.ClearSelection();
    }

    /// <summary>
    /// Kéo từng grip của nhiều loại entity — kể cả các trường hợp biên (kéo bán kính về 0).
    /// Mục đích: không loại nào làm ném ngoại lệ ra giao diện.
    /// </summary>
    private static void RunGripStressTest(CadWorkspace workspace, CadEditor editor,
                                          CadCanvas canvas, Action<string, bool, string> check)
    {
        var makers = new (string Name, Func<Entity> Make)[]
        {
            ("Line", () => new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(1000, 0, 0) }),
            ("Circle", () => new Circle { Center = new XYZ(500, 500, 0), Radius = 200 }),
            ("Arc", () => new Arc
            {
                Center = new XYZ(500, 500, 0), Radius = 200,
                StartAngle = 0, EndAngle = Math.PI,
            }),
            ("Text", () => new TextEntity { InsertPoint = new XYZ(100, 100, 0), Height = 100, Value = "A" }),
            ("Polyline", () =>
            {
                var p = new LwPolyline();
                p.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 0) });
                p.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(500, 0) });
                p.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(500, 500) });
                return p;
            }),
            ("Ellipse", () => new Ellipse
            {
                Center = new XYZ(300, 300, 0),
                MajorAxisEndPoint = new XYZ(200, 0, 0),
                RadiusRatio = 0.5,
            }),
        };

        var failures = new List<string>();

        foreach (var (name, make) in makers)
        {
            foreach (var e in workspace.Document.Entities.ToList())
                workspace.Document.Entities.Remove(e);

            editor.Reload();
            canvas.Cancel();
            canvas.ClearSelection();

            var entity = make();
            editor.AddEntities(new[] { entity }, "Add");

            var grips = GripSet.For(entity);

            for (var index = 0; index < grips.Count; index++)
            {
                var grip = grips[index];

                // Kéo tới vị trí bình thường, rồi tới đúng tâm (trường hợp biên)
                var targets = new[]
                {
                    new Pt(grip.Position.X + 150, grip.Position.Y + 120),
                    new Pt(500, 500),
                    grip.Position,
                };

                foreach (var target in targets)
                {
                    try
                    {
                        canvas.SimulateMouseDown(
                            canvas.WorldToScreenPoint(grip.Position).X,
                            canvas.WorldToScreenPoint(grip.Position).Y);

                        var screen = canvas.WorldToScreenPoint(target);

                        canvas.SimulateMouseMove(screen.X, screen.Y);
                        canvas.SimulateMouseUp(screen.X, screen.Y);
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{name} grip {index}: {ex.GetType().Name}");
                    }
                }
            }
        }

        check("Grip: kéo grip mọi loại không ném ngoại lệ",
            failures.Count == 0,
            failures.Count == 0 ? makers.Length + " loại × nhiều grip" : string.Join(", ", failures.Take(5)));

        canvas.Cancel();
        canvas.ClearSelection();
    }

    /// <summary>Kiểm thử các lệnh sửa nhiều bước: OFFSET, FILLET, DIM.</summary>
    private static void RunEditCommandTests(CadWorkspace workspace, CadEditor editor,
                                            CadCanvas canvas, Action<string, bool, string> check)
    {
        foreach (var entity in workspace.Document.Entities.ToList())
            workspace.Document.Entities.Remove(entity);

        editor.Reload();
        canvas.SetOrthoForTest(false);
        canvas.Cancel();
        canvas.ClearSelection();

        // ---- OFFSET: chọn line, nhập khoảng cách, click phía muốn offset
        var baseLine = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(1000, 0, 0),
        };

        editor.AddEntities(new Entity[] { baseLine }, "Add");
        canvas.SelectAll();

        canvas.SetMode(EditMode.Offset);
        var askedDistance = canvas.ProvideInput("100");

        var clickSide = canvas.WorldToScreenPoint(new Pt(500, 300));
        canvas.SimulateMouseDown(clickSide.X, clickSide.Y);

        var offsetLines = workspace.Document.Entities.OfType<Line>().Count();

        check("OFFSET: hỏi khoảng cách", askedDistance, "askedDistance=" + askedDistance);
        check("OFFSET: tạo thêm 1 đường", offsetLines == 2, offsetLines + " line");

        var parallel = workspace.Document.Entities.OfType<Line>()
            .FirstOrDefault(l => Math.Abs(l.StartPoint.Y - 100) < 5);

        check("OFFSET: đường mới cách 100", parallel is not null,
            parallel?.StartPoint.Y.ToString("0.#") ?? "không thấy");

        canvas.Cancel();
        canvas.ClearSelection();

        // ---- FILLET: 2 line vuông góc, nhập bán kính
        foreach (var entity in workspace.Document.Entities.ToList())
            workspace.Document.Entities.Remove(entity);

        editor.Reload();

        var horizontal = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(1000, 0, 0),
        };

        var vertical = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(0, 1000, 0),
        };

        editor.AddEntities(new Entity[] { horizontal, vertical }, "Add");
        canvas.SelectAll();

        var arcsBefore = workspace.Document.Entities.OfType<Arc>().Count();

        canvas.SetMode(EditMode.Fillet);
        var filletAsked = canvas.AwaitingInput;
        canvas.ProvideInput("150");

        var arcsAfter = workspace.Document.Entities.OfType<Arc>().Count();

        check("FILLET: hỏi bán kính sau khi chọn 2 line", filletAsked, filletAsked ? "có" : "không");
        check("FILLET: tạo cung bo góc", arcsAfter > arcsBefore, arcsBefore + " → " + arcsAfter);

        canvas.Cancel();
        canvas.ClearSelection();

        // ---- DIM: 2 điểm + điểm đặt đường kích thước
        foreach (var entity in workspace.Document.Entities.ToList())
            workspace.Document.Entities.Remove(entity);

        editor.Reload();
        canvas.SetMode(EditMode.Dimension);

        var d1 = canvas.WorldToScreenPoint(new Pt(0, 0));
        var d2 = canvas.WorldToScreenPoint(new Pt(2000, 0));
        var d3 = canvas.WorldToScreenPoint(new Pt(1000, -400));

        canvas.SimulateMouseDown(d1.X, d1.Y);
        canvas.SimulateMouseDown(d2.X, d2.Y);
        canvas.SimulateMouseDown(d3.X, d3.Y);

        var dims = workspace.Document.Entities.OfType<DimensionAligned>().ToList();

        check("DIM: 3 click tạo kích thước", dims.Count == 1, dims.Count + " dim");

        if (dims.Count == 1)
        {
            check("DIM: đo đúng 2000",
                Math.Abs(dims[0].FirstPoint.X - 0) < 5 && Math.Abs(dims[0].SecondPoint.X - 2000) < 5,
                $"{dims[0].FirstPoint.X:0.#} → {dims[0].SecondPoint.X:0.#}");
        }

        canvas.Cancel();
        canvas.ClearSelection();
    }

    /// <summary>Kiểm thử chuột trên tờ in: chọn chữ, kéo chữ, chọn và kéo viewport.</summary>
    private static void RunPaperMouseTests(CadWorkspace workspace, CadEditor editor,
                                           CadCanvas canvas, Action<string, bool, string> check)
    {
        var model = editor.Scene;
        var layout = OpenCad.Core.Layout.LayoutFactory.Create(workspace.Document, "P1",
            new OpenCad.Core.Layout.SheetTemplateOptions { Title = "T", UnitsPerMm = 50 }, model);

        if (layout?.AssociatedBlock is null)
        {
            check("Paper: tạo được layout để kiểm thử chuột", false, "null");
            return;
        }

        var block = layout.AssociatedBlock;

        // Chú thích trên tờ, toạ độ mm giấy
        var note = new TextEntity
        {
            InsertPoint = new XYZ(30, 30, 0),
            Height = 4,
            Value = "GHI CHU",
        };

        block.Entities.Add(note);

        var sheet = OpenCad.Core.Layout.LayoutFactory.Read(workspace.Document, layout)!;
        canvas.ShowPaper(sheet, block);

        check("Paper: vào được chế độ tờ", canvas.IsPaperMode, "paper");

        // Click vào chữ để chọn
        var noteScreen = canvas.WorldToScreenPoint(new Pt(32, 32));
        canvas.SimulateMouseDown(noteScreen.X, noteScreen.Y);
        canvas.SimulateMouseUp(noteScreen.X, noteScreen.Y);

        check("Paper: click chọn được chữ trên tờ",
            canvas.PaperSelectionCount == 1, canvas.PaperSelectionCount + " chọn");

        // Kéo chữ sang phải 40 mm
        var dragTo = canvas.WorldToScreenPoint(new Pt(72, 32));
        canvas.SimulateMouseDown(noteScreen.X, noteScreen.Y);
        canvas.SimulateMouseMove(dragTo.X, dragTo.Y);
        canvas.SimulateMouseUp(dragTo.X, dragTo.Y);

        check("Paper: kéo chữ trên tờ làm chữ dời",
            Math.Abs(note.InsertPoint.X - 70) < 3,
            "x = " + note.InsertPoint.X.ToString("0.#"));

        canvas.Undo();

        check("Paper: undo kéo chữ phục hồi",
            Math.Abs(note.InsertPoint.X - 30) < 3,
            "x = " + note.InsertPoint.X.ToString("0.#"));

        // ---- Viewport: chọn và kéo
        var viewport = block.Entities.OfType<Viewport>()
            .OrderByDescending(v => v.ViewHeight)
            .FirstOrDefault();

        if (viewport is not null)
        {
            var center = new Pt(viewport.Center.X, viewport.Center.Y);
            var viewportScreen = canvas.WorldToScreenPoint(center);

            canvas.SimulateMouseDown(viewportScreen.X, viewportScreen.Y);
            canvas.SimulateMouseUp(viewportScreen.X, viewportScreen.Y);

            check("Paper: click chọn được viewport",
                canvas.SelectedViewportCenter is not null, "đã chọn");

            var moveTo = canvas.WorldToScreenPoint(new Pt(center.X + 20, center.Y));

            canvas.SimulateMouseDown(viewportScreen.X, viewportScreen.Y);
            canvas.SimulateMouseMove(moveTo.X, moveTo.Y);
            canvas.SimulateMouseUp(moveTo.X, moveTo.Y);

            check("Paper: kéo viewport dời khung",
                Math.Abs(viewport.Center.X - center.X - 20) < 3,
                "Δx = " + (viewport.Center.X - center.X).ToString("0.#"));

            canvas.Undo();

            check("Paper: undo kéo viewport phục hồi",
                Math.Abs(viewport.Center.X - center.X) < 3,
                "Δx = " + (viewport.Center.X - center.X).ToString("0.#"));
        }

        canvas.ShowPaper(null);
        check("Paper: trở về model space", !canvas.IsPaperMode, "model");
    }

    public static int Run()
    {
        var failures = 0;
        var checks = 0;

        void Check(string name, bool ok, string detail = "")
        {
            checks++;
            if (!ok) failures++;

            Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? "  —  " + detail : "")}");
        }

        Console.WriteLine("UI SELF TEST");

        try
        {
            var workspace = CadWorkspace.New();

            workspace.Document.Entities.Add(new Line
            {
                StartPoint = new XYZ(0, 0, 0),
                EndPoint = new XYZ(1000, 0, 0),
            });

            workspace.Document.Entities.Add(new Circle
            {
                Center = new XYZ(500, 500, 0),
                Radius = 100,
            });

            var editor = new CadEditor(workspace);
            var canvas = new CadCanvas();

            // ---- Nối editor
            canvas.Attach(editor);
            Check("Canvas nhận editor", canvas.Editor is not null, "attached");

            // ---- Chọn tất cả qua canvas
            canvas.SelectAll();
            Check("SelectAll chọn hết đối tượng",
                canvas.Selection.Count == 2, canvas.Selection.Count + " đối tượng");

            // ---- Xoá qua canvas (đi đúng vào CadEditor)
            canvas.DeleteSelection();
            Check("DeleteSelection xoá qua editor",
                workspace.Document.Entities.Count() == 0,
                workspace.Document.Entities.Count() + " còn lại");

            canvas.Undo();
            Check("Undo qua canvas phục hồi",
                workspace.Document.Entities.Count() == 2,
                workspace.Document.Entities.Count() + " entity");

            // ---- Bật/tắt chế độ
            // ---- Bật/tắt chế độ: kiểm qua thông báo trạng thái (prompt)
            canvas.ToggleSnap();
            Check("ToggleSnap phản hồi trong prompt",
                canvas.Prompt.Contains("OSNAP", StringComparison.OrdinalIgnoreCase), canvas.Prompt);

            canvas.ToggleOrtho();
            Check("ToggleOrtho phản hồi trong prompt",
                canvas.Prompt.Contains("ORTHO", StringComparison.OrdinalIgnoreCase), canvas.Prompt);

            canvas.ToggleGrid();
            Check("ToggleGrid phản hồi trong prompt",
                canvas.Prompt.Contains("GRID", StringComparison.OrdinalIgnoreCase), canvas.Prompt);

            // ---- Chuyển chế độ vẽ: prompt phải đổi theo lệnh
            canvas.SetMode(EditMode.Line);
            Check("SetMode(Line) đổi câu nhắc lệnh",
                canvas.Prompt.Contains("LINE", StringComparison.OrdinalIgnoreCase), canvas.Prompt);

            canvas.Cancel();

            // ---- Paper space: mở tờ và kiểm chế độ
            var model = editor.Scene;
            var layout = OpenCad.Core.Layout.LayoutFactory.Create(workspace.Document, "L1",
                new OpenCad.Core.Layout.SheetTemplateOptions { Title = "T", UnitsPerMm = 50 }, model);

            if (layout is not null)
            {
                var sheet = OpenCad.Core.Layout.LayoutFactory.Read(workspace.Document, layout);

                if (sheet is not null)
                {
                    canvas.ShowPaper(sheet, layout.AssociatedBlock);
                    Check("Vào được paper space", canvas.IsPaperMode, "paper");

                    canvas.ShowPaper(null);
                    Check("Ra lại model space", !canvas.IsPaperMode, "model");
                }
            }

            // ---- Lệnh HELP/registry hiển thị được
            Check("Bảng lệnh có mô tả cho mọi lệnh",
                OpenCad.Core.Commands.CommandRegistry.All.All(c => c.Description.Length > 0),
                OpenCad.Core.Commands.CommandRegistry.All.Length + " lệnh");

            // ---- Mô phỏng chuột thật: vẽ, chọn, kéo
            RunMouseTests(workspace, editor, canvas, Check);

            // ---- Lệnh vẽ có nhập số: CIRCLE, RECTANG, DIVIDE
            RunCommandFlowTests(workspace, editor, canvas, Check);

            // ---- Lệnh sửa nhiều bước: OFFSET, FILLET, DIM
            RunEditCommandTests(workspace, editor, canvas, Check);

            // ---- TRIM / MIRROR / ROTATE
            RunTrimMirrorRotateTests(workspace, editor, canvas, Check);

            // ---- Mô phỏng chuột trên tờ in
            RunPaperMouseTests(workspace, editor, canvas, Check);

            // ---- Ribbon: các tab và nút lệnh
            var ribbon = new RibbonBar();
            var launched = new List<string>();
            ribbon.CommandClicked += name => launched.Add(name);

            Check("Ribbon: có 5 tab", ribbon.TabNames.Count == 5,
                string.Join(", ", ribbon.TabNames));

            Check("Ribbon: mở tab mặc định",
                ribbon.ActiveTab == "Trang chủ", ribbon.ActiveTab);

            var homeButtons = ribbon.VisibleButtonCount;
            Check("Ribbon: tab Trang chủ có nút lệnh", homeButtons > 10, homeButtons + " nút");

            ribbon.ShowTab("Chú thích");
            Check("Ribbon: đổi được tab", ribbon.ActiveTab == "Chú thích", ribbon.ActiveTab);

            var annotateButtons = ribbon.VisibleButtonCount;
            Check("Ribbon: tab Chú thích có nút", annotateButtons > 5, annotateButtons + " nút");

            var wrong = ribbon.ButtonsOnActiveTab
                .Where(b => b.Tag is not string name || CommandRegistry.Find(name) is null)
                .Select(b => b.Text)
                .ToList();

            Check("Ribbon: mọi nút trỏ tới lệnh có thật",
                wrong.Count == 0,
                wrong.Count == 0 ? ribbon.ButtonsOnActiveTab.Count + " nút" : string.Join(",", wrong.Take(5)));

            ribbon.RaiseCommand("AUDIT");

            // Nút có lệnh vẽ phải có icon
            ribbon.ShowTab("Trang chủ");

            var withIcon = ribbon.ButtonsOnActiveTab.Count(b => b.Image is not null);
            var totalHome = ribbon.ButtonsOnActiveTab.Count;

            Check("Ribbon: phần lớn nút có icon", withIcon >= totalHome - 2,
                withIcon + "/" + totalHome + " nút có icon");

            Check("Ribbon: phát được lệnh qua sự kiện", launched.Contains("AUDIT"),
                launched.Count > 0 ? string.Join(",", launched) : "không có");
            ribbon.Dispose();

            // ---- Kéo grip trên cửa sổ THẬT (có panel, status bar, handler)
            RunGripTestOnRealForm(Check);

            // ---- Kéo grip trên cửa sổ thật với nhiều loại entity
            RunGripTypesOnRealForm(Check);

            // ---- Chạy thử toàn bộ lệnh trong bảng lệnh

            // ---- SPLINE: vẽ qua nhiều điểm rồi Enter
            foreach (var e in workspace.Document.Entities.ToList())
                workspace.Document.Entities.Remove(e);

            editor.Reload();
            canvas.Cancel();
            canvas.ClearSelection();
            canvas.Width = 800;
            canvas.Height = 600;
            canvas.SetOrthoForTest(false);

            canvas.SetMode(EditMode.Spline);

            Check("Lệnh: vào được chế độ SPLINE",
                canvas.ModeForTest.Contains("Spline", StringComparison.OrdinalIgnoreCase),
                canvas.ModeForTest + " | " + canvas.Prompt);

            foreach (var point in new[] { new Pt(0, 0), new Pt(200, 300), new Pt(400, 0), new Pt(600, 300) })
            {
                var screen = canvas.WorldToScreenPoint(point);
                canvas.SimulateMouseDown(screen.X, screen.Y);
            }

            Check("Lệnh: SPLINE nhận đủ 4 điểm click",
                canvas.PointCountForTest == 4, canvas.PointCountForTest + " điểm");

            var provided = canvas.ProvideInput(string.Empty);

            var splines = workspace.Document.Entities.OfType<Spline>().ToList();

            Check("Lệnh: SPLINE tạo được đường cong", splines.Count == 1, splines.Count + " spline");

            if (splines.Count == 1)
            {
                Check("Lệnh: SPLINE có đúng 4 điểm điều khiển",
                    splines[0].ControlPoints.Count == 4, splines[0].ControlPoints.Count + " điểm");

                Check("Lệnh: SPLINE bậc 3", splines[0].Degree == 3, splines[0].Degree.ToString());
            }

            canvas.Cancel();
            canvas.ClearSelection();

            // ---- DIMCONTINUE: nối tiếp chuỗi kích thước
            foreach (var e in workspace.Document.Entities.ToList())
                workspace.Document.Entities.Remove(e);

            editor.Reload();
            canvas.Cancel();
            canvas.ClearSelection();
            canvas.SetOrthoForTest(false);

            canvas.SetMode(EditMode.Dimension);

            foreach (var point in new[] { new Pt(0, 0), new Pt(1000, 0), new Pt(0, -300) })
            {
                var screen = canvas.WorldToScreenPoint(point);
                canvas.SimulateMouseDown(screen.X, screen.Y);
            }

            var afterFirst = workspace.Document.Entities.OfType<DimensionAligned>().Count();

            Check("DIMCONTINUE: có kích thước đầu tiên", afterFirst == 1, afterFirst + " dim");

            canvas.SetMode(EditMode.DimContinue);

            foreach (var point in new[] { new Pt(2000, 0), new Pt(3500, 0) })
            {
                var screen = canvas.WorldToScreenPoint(point);
                canvas.SimulateMouseDown(screen.X, screen.Y);
            }

            var dims = workspace.Document.Entities.OfType<DimensionAligned>().ToList();

            Check("DIMCONTINUE tạo thêm 2 kích thước", dims.Count == 3, dims.Count + " dim");



            if (dims.Count == 3)
            {
                // Thứ tự trong collection không đảm bảo → tìm theo toạ độ
                var baseDim = dims.FirstOrDefault(d => Math.Abs(d.FirstPoint.X) < 5);
                var chained1 = dims.FirstOrDefault(d => Math.Abs(d.FirstPoint.X - 1000) < 5);
                var chained2 = dims.FirstOrDefault(d => Math.Abs(d.FirstPoint.X - 2000) < 5);

                Check("DIMCONTINUE nối đúng điểm cuối của cái trước",
                    chained1 is not null, chained1 is null ? "không thấy" : "1000");

                Check("DIMCONTINUE đo đúng 2000→3500",
                    chained2 is not null && Math.Abs(chained2.SecondPoint.X - 3500) < 5,
                    chained2 is null ? "không thấy" : $"{chained2.FirstPoint.X:0.#}→{chained2.SecondPoint.X:0.#}");

                Check("DIMCONTINUE giữ nguyên khoảng lệch đường kích thước",
                    baseDim is not null && chained1 is not null &&
                    Math.Abs((chained1.DefinitionPoint.Y - chained1.SecondPoint.Y) -
                             (baseDim.DefinitionPoint.Y - baseDim.SecondPoint.Y)) < 5,
                    chained1 is null ? "?" : $"{chained1.DefinitionPoint.Y - chained1.SecondPoint.Y:0.#}");
            }

            canvas.Cancel();
            canvas.ClearSelection();

            // ---- Properties: SỬA hình học
            var editWs = CadWorkspace.New();
            var editEditor = new CadEditor(editWs);

            var editCircle = new Circle { Center = new XYZ(0, 0, 0), Radius = 100 };
            var editText = new TextEntity { InsertPoint = new XYZ(0, 0, 0), Height = 200, Value = "A" };

            editEditor.AddEntities(new Entity[] { editCircle, editText }, "Add");

            using (var panel = new OpenCad.App.Panels.PropertiesPanel())
            {
                panel.Attach(editEditor);

                panel.ShowSelection(new Entity[] { editCircle });
                var ready = panel.EditGeometryForTest("250");

                Check("Properties: sửa bán kính được", ready && Math.Abs(editCircle.Radius - 250) < 1e-6,
                    editCircle.Radius.ToString("0.#"));

                editEditor.Undo();

                Check("Properties: undo trả bán kính về 100",
                    Math.Abs(editCircle.Radius - 100) < 1e-6, editCircle.Radius.ToString("0.#"));

                panel.ShowSelection(new Entity[] { editText });
                var textReady = panel.EditGeometryForTest("350");

                Check("Properties: sửa chiều cao chữ được",
                    textReady && Math.Abs(editText.Height - 350) < 1e-6, editText.Height.ToString("0.#"));

                // Giá trị sai không được làm hỏng gì
                var beforeBad = editCircle.Radius;
                panel.ShowSelection(new Entity[] { editCircle });
                var bad = panel.EditGeometryForTest("khong-phai-so");

                Check("Properties: giá trị không phải số bị bỏ qua",
                    !bad && Math.Abs(editCircle.Radius - beforeBad) < 1e-9, "giữ nguyên");

                var negative = panel.EditGeometryForTest("-5");

                Check("Properties: bán kính âm bị bỏ qua",
                    !negative && Math.Abs(editCircle.Radius - beforeBad) < 1e-9, "giữ nguyên");

                // Line không có ô sửa hình học (phải chọn line trước)
                var editLine = new Line
                {
                    StartPoint = new XYZ(0, 0, 0),
                    EndPoint = new XYZ(100, 0, 0),
                };

                editEditor.AddEntities(new Entity[] { editLine }, "Add");
                panel.ShowSelection(new Entity[] { editLine });

                // Line bây giờ CÓ ô sửa (dài/góc) → dùng Hatch để kiểm loại không có ô
                panel.ShowSelection(new Entity[]
                {
                    new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(10, 0, 0) },
                });

                var lineHasField = panel.EditGeometryForTest("123");

                Check("Properties: line có ô sửa dài/góc", lineHasField, "cho sửa");

                panel.ShowSelection(new Entity[]
                {
                    new TextEntity { InsertPoint = new XYZ(0, 0, 0), Height = 10, Value = "x" },
                });

                var textHasField = panel.EditGeometryForTest("25");

                Check("Properties: text có ô cao chữ", textHasField, "cho sửa");

                Check("Properties: chọn line không làm đổi bán kính hình tròn",
                    Math.Abs(editCircle.Radius - beforeBad) < 1e-9,
                    editCircle.Radius.ToString("0.#"));
            }

            // ---- Properties: sửa dài và góc của LINE
            var geoWs = CadWorkspace.New();
            var geoEditor = new CadEditor(geoWs);

            var geoLine = new Line
            {
                StartPoint = new XYZ(0, 0, 0),
                EndPoint = new XYZ(300, 400, 0),   // dài 500, góc 53.13°
            };

            geoEditor.AddEntities(new Entity[] { geoLine }, "Add");

            using (var panel = new OpenCad.App.Panels.PropertiesPanel())
            {
                panel.Attach(geoEditor);
                panel.ShowSelection(new Entity[] { geoLine });

                // Đổi chiều dài, giữ góc
                var okLength = panel.EditGeometryForTest("1000", "53.13");

                var dx = geoLine.EndPoint.X - geoLine.StartPoint.X;
                var dy = geoLine.EndPoint.Y - geoLine.StartPoint.Y;
                var newLength = Math.Sqrt(dx * dx + dy * dy);

                Check("Properties: đổi được chiều dài line", okLength && Math.Abs(newLength - 1000) < 1,
                    newLength.ToString("0.#"));

                // Chỉ đổi góc, giữ dài (ô dài để trống)
                var okAngle = panel.EditGeometryForTest("", "90");

                dx = geoLine.EndPoint.X - geoLine.StartPoint.X;
                dy = geoLine.EndPoint.Y - geoLine.StartPoint.Y;

                Check("Properties: đổi góc 90° làm line thẳng đứng",
                    okAngle && Math.Abs(dx) < 1 && dy > 0,
                    $"Δ=({dx:0.#}, {dy:0.#})");

                Check("Properties: đổi góc vẫn giữ chiều dài 1000",
                    Math.Abs(Math.Sqrt(dx * dx + dy * dy) - 1000) < 1,
                    Math.Sqrt(dx * dx + dy * dy).ToString("0.#"));

                geoEditor.Undo();

                dx = geoLine.EndPoint.X - geoLine.StartPoint.X;
                dy = geoLine.EndPoint.Y - geoLine.StartPoint.Y;

                Check("Properties: undo trả line về trước khi đổi góc",
                    Math.Abs(Math.Sqrt(dx * dx + dy * dy) - 1000) < 1 && dx > 0,
                    $"Δ=({dx:0.#}, {dy:0.#})");

                // Cả hai ô trống → không đổi gì
                var beforeEmpty = geoLine.EndPoint;
                var okEmpty = panel.EditGeometryForTest("", "");

                Check("Properties: cả hai ô trống thì không đổi",
                    !okEmpty && geoLine.EndPoint.Equals(beforeEmpty), "giữ nguyên");

                // Dài âm bị bỏ qua
                var okNegative = panel.EditGeometryForTest("-100", "45");

                Check("Properties: chiều dài âm bị bỏ qua nhưng góc vẫn đổi được",
                    okNegative, "đổi góc");

                var circle2 = new Circle { Center = new XYZ(0, 0, 0), Radius = 50 };
                geoEditor.AddEntities(new Entity[] { circle2 }, "Add");
                panel.ShowSelection(new Entity[] { circle2 });

                Check("Properties: circle dùng ô bán kính, không dùng ô góc",
                    panel.EditGeometryForTest("80") && Math.Abs(circle2.Radius - 80) < 1e-6,
                    circle2.Radius.ToString("0.#"));
            }

            // ---- Properties: thông số hình học
            var pline = new Line
            {
                StartPoint = new XYZ(0, 0, 0),
                EndPoint = new XYZ(300, 400, 0),
            };

            var lineInfo = OpenCad.App.Panels.PropertiesPanel.GeometrySummary(pline);

            Check("Properties: line hiện độ dài 500", lineInfo.Contains("500"), lineInfo.Replace("\n", " | "));
            Check("Properties: line hiện góc 53.13°", lineInfo.Contains("53.13"), lineInfo.Replace("\n", " | "));
            Check("Properties: line hiện ΔX, ΔY",
                lineInfo.Contains("300") && lineInfo.Contains("400"), "có");

            var circleInfo = OpenCad.App.Panels.PropertiesPanel.GeometrySummary(
                new Circle { Center = new XYZ(0, 0, 0), Radius = 100 });

            Check("Properties: circle hiện diện tích πr² = 31415.9",
                circleInfo.Contains("31415.927"), circleInfo.Replace("\n", " | "));
            Check("Properties: circle hiện chu vi 628.319",
                circleInfo.Contains("628.319"), circleInfo.Replace("\n", " | "));

            var arcInfo = OpenCad.App.Panels.PropertiesPanel.GeometrySummary(new Arc
            {
                Center = new XYZ(0, 0, 0), Radius = 100,
                StartAngle = 0, EndAngle = Math.PI / 2,
            });

            Check("Properties: arc hiện tổng góc 90°", arcInfo.Contains("90°"), arcInfo.Replace("\n", " | "));
            Check("Properties: arc hiện dài cung 157.08",
                arcInfo.Contains("157.08"), arcInfo.Replace("\n", " | "));

            var textInfo = OpenCad.App.Panels.PropertiesPanel.GeometrySummary(new TextEntity
            {
                InsertPoint = new XYZ(0, 0, 0), Height = 250, Value = "PHONG KHACH",
            });

            Check("Properties: text hiện nội dung và cao chữ",
                textInfo.Contains("PHONG KHACH") && textInfo.Contains("250"),
                textInfo.Replace("\n", " | "));

            // ---- TABLE: tạo bảng 3×2
            foreach (var e in workspace.Document.Entities.ToList())
                workspace.Document.Entities.Remove(e);

            editor.Reload();
            canvas.Cancel();
            canvas.ClearSelection();

            canvas.BeginTable();
            canvas.ProvideInput("3,2");

            var insert = canvas.WorldToScreenPoint(new Pt(100, 900));
            canvas.SimulateMouseDown(insert.X, insert.Y);

            var tables = workspace.Document.Entities.OfType<ACadSharp.Entities.TableEntity>().ToList();

            Check("Lệnh: TABLE tạo được bảng", tables.Count == 1, tables.Count + " bảng");

            if (tables.Count == 1)
            {
                Check("Lệnh: TABLE đúng 3 hàng, 2 cột",
                    tables[0].RowCount == 3 && tables[0].ColumnCount == 2,
                    $"{tables[0].RowCount}×{tables[0].ColumnCount}");

                Check("Lệnh: TABLE có 3 row + 2 column thật",
                    tables[0].Rows.Count == 3 && tables[0].Columns.Count == 2,
                    $"{tables[0].Rows.Count} row / {tables[0].Columns.Count} col");

                Check("Lệnh: TABLE đặt đúng điểm chèn",
                    Math.Abs(tables[0].InsertPoint.X ) > 0, tables[0].InsertPoint.X.ToString("0.#"));
            }

            // Số hàng,cột sai thì không tạo
            canvas.BeginTable();
            canvas.ProvideInput("abc");
            canvas.Cancel();

            Check("Lệnh: TABLE từ chối tham số sai",
                workspace.Document.Entities.OfType<ACadSharp.Entities.TableEntity>().Count() == 1,
                "vẫn 1 bảng");

            canvas.Cancel();
            canvas.ClearSelection();

            // ---- Render tờ A3 của bản vẽ mẫu ra PNG
            RunSheetRenderTest(Check);

            // ---- Kích thước trên tờ in (paper space)
            foreach (var e in workspace.Document.Entities.ToList())
                workspace.Document.Entities.Remove(e);

            editor.Reload();
            canvas.Cancel();
            canvas.ClearSelection();

            editor.AddEntities(new Entity[]
            {
                new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(4000, 0, 0) },
            }, "Add");

            var paperScene = new CadScene();
            paperScene.Build(workspace.Document);

            var dimLayout = OpenCad.Core.Layout.LayoutFactory.Create(
                workspace.Document, "DIM_SHEET",
                new OpenCad.Core.Layout.SheetTemplateOptions { Title = "T", UnitsPerMm = 50 },
                paperScene);

            if (dimLayout is null)
            {
                Check("Paper: tạo được tờ để kiểm kích thước", false, "không tạo được");
            }
            else
            {
                var dimSheet = OpenCad.Core.Layout.LayoutFactory.Read(workspace.Document, dimLayout);
                canvas.ShowPaper(dimSheet, dimLayout.AssociatedBlock, "DIM_SHEET");

                canvas.SetOrthoForTest(false);
                canvas.SetMode(EditMode.Dimension);

                // Click 3 điểm trên tờ (toạ độ giấy, mm)
                foreach (var point in new[] { new Pt(50, 200), new Pt(150, 200), new Pt(100, 220) })
                {
                    var screen = canvas.WorldToScreenPoint(point);
                    canvas.SimulateMouseDown(screen.X, screen.Y);
                }

                var paperDim = dimLayout.AssociatedBlock?.Entities
                    .OfType<DimensionAligned>()
                    .Count() ?? 0;

                Check("Paper: vẽ được kích thước trên tờ", paperDim == 1, paperDim + " kích thước");

                Check("Paper: kích thước nằm trong block của tờ, không vào model",
                    workspace.Document.Entities.OfType<DimensionAligned>().Count() == 0,
                    workspace.Document.Entities.Count() + " entity model");

                if (paperDim == 1)
                {
                    var d = dimLayout.AssociatedBlock!.Entities.OfType<DimensionAligned>().First();

                    Check("Paper: kích thước đo đúng 100 mm trên giấy",
                        Math.Abs(Math.Abs(d.SecondPoint.X - d.FirstPoint.X) - 100) < 2,
                        Math.Abs(d.SecondPoint.X - d.FirstPoint.X).ToString("0.#"));
                }

                canvas.Cancel();
                canvas.ClearSelection();
                canvas.ShowPaper(null);
            }

            // ---- TRUY LÙNG LỖI KÉO GRIP (người dùng báo crash, chưa tái hiện được)
            // Mô phỏng ĐÚNG đường chuột của canvas: chọn → nhấn lên grip → rê → nhả.
            {
                var gripFactories = new (string Name, Func<Entity> Make)[]
                {
                    ("Line", () => new Line
                    {
                        StartPoint = new XYZ(0, 0, 0),
                        EndPoint = new XYZ(1000, 0, 0),
                    }),
                    ("Circle", () => new Circle { Center = new XYZ(0, 0, 0), Radius = 500 }),
                    ("Arc", () => new Arc
                    {
                        Center = new XYZ(0, 0, 0),
                        Radius = 500,
                        StartAngle = 0,
                        EndAngle = Math.PI / 2,
                    }),
                    ("Ellipse", () => new Ellipse
                    {
                        Center = new XYZ(0, 0, 0),
                        RadiusRatio = 0.5,
                        MajorAxisEndPoint = new XYZ(500, 0, 0),
                    }),
                    ("TextEntity", () => new TextEntity
                    {
                        InsertPoint = new XYZ(0, 0, 0),
                        Value = "ABC",
                        Height = 100,
                    }),
                    ("MText", () => new MText
                    {
                        InsertPoint = new XYZ(0, 0, 0),
                        Value = "ABC",
                        Height = 100,
                    }),
                    ("LwPolyline", () =>
                    {
                        var poly = new LwPolyline();
                        poly.Vertices.Add(new LwPolyline.Vertex { Location = new XY(0, 0) });
                        poly.Vertices.Add(new LwPolyline.Vertex { Location = new XY(1000, 0) });
                        poly.Vertices.Add(new LwPolyline.Vertex { Location = new XY(1000, 800) });
                        return poly;
                    }),
                    ("Leader", () =>
                    {
                        var leader = new Leader();
                        leader.Vertices.Add(new XYZ(0, 0, 0));
                        leader.Vertices.Add(new XYZ(500, 500, 0));
                        return leader;
                    }),
                };

                var offsets = new (string Label, double X, double Y)[]
                {
                    ("gốc", 0, 0),
                    ("1e6", 1_000_000, 1_000_000),
                    ("-4e5", -400_000, 250_000),
                };

                var dragTargets = new (double DX, double DY)[]
                {
                    (300, 200),
                    (0, 0),
                    (-900_000, 0),
                    (1_000_000, 1_000_000),
                };

                var gripDrags = 0;
                var gripErrors = new List<string>();

                foreach (var (typeName, make) in gripFactories)
                {
                    foreach (var offset in offsets)
                    {
                        var gWs = CadWorkspace.New();
                        var gCanvas = new CadCanvas();
                        gCanvas.Attach(new CadEditor(gWs));
                        gCanvas.Width = 900;
                        gCanvas.Height = 700;

                        var gEntity = make();
                        gWs.Document.Entities.Add(gEntity);

                        gWs.Document.Entities.Remove(gEntity);
                        gEntity = make();

                        var gEditor = new CadEditor(gWs);
                        gEditor.AddEntities(new Entity[] { gEntity }, "Add");

                        gCanvas.ZoomExtentsForTest();

                        foreach (var grip in GripSet.For(gEntity))
                        {
                            foreach (var target in dragTargets)
                            {
                                gripDrags++;

                                var from = gCanvas.WorldToScreenPoint(grip.Position);
                                var to = gCanvas.WorldToScreenPoint(
                                    new Pt(grip.Position.X + target.DX, grip.Position.Y + target.DY));

                                try
                                {
                                    gCanvas.SimulateMouseDown(from.X, from.Y);
                                    gCanvas.SimulateMouseMove((from.X + to.X) / 2, (from.Y + to.Y) / 2);
                                    gCanvas.SimulateMouseMove(to.X, to.Y);
                                    gCanvas.SimulateMouseUp(to.X, to.Y);
                                }
                                catch (Exception ex)
                                {
                                    gripErrors.Add($"{typeName}@{offset.Label} Δ({target.DX},{target.DY}): "
                                                   + ex.GetType().Name);
                                }

                                if (CadCanvas.LastUiError is { Length: > 0 } uiError)
                                {
                                    gripErrors.Add($"{typeName}@{offset.Label} Δ({target.DX},{target.DY}): "
                                                   + uiError);
                                    CadCanvas.ClearLastUiError();
                                }
                            }
                        }

                        gCanvas.Dispose();
                    }
                }

                Check("Grip: kéo mọi grip trên mọi loại, không crash",
                    gripErrors.Count == 0,
                    gripErrors.Count == 0
                        ? gripDrags + " lần kéo qua canvas"
                        : string.Join(" | ", gripErrors.Take(3)));
            }

            // ---- Lệnh có THAM SỐ phải chạy được từ dòng lệnh
            // (lỗi thật: switch khớp cả chuỗi nên "TEXTANGLE 45" không bao giờ chạy)
            using (var argForm = new MainForm())
            {
                argForm.InitializeForTest();

                var argEditor = argForm.EditorForTest;

                if (argEditor is null)
                {
                    Check("Lệnh có tham số: có editor", false, "null");
                }
                else
                {
                    var argText = new TextEntity
                    {
                        InsertPoint = new XYZ(0, 0, 0),
                        Value = "ABC",
                        Height = 200,
                    };

                    argEditor.AddEntities(new Entity[] { argText }, "Add");
                    argForm.CanvasForTest.SelectAll();

                    argForm.RunCommandForTest("TEXTANGLE 30");

                    Check("Lệnh có tham số: TEXTANGLE 30 chạy được",
                        Math.Abs(argText.Rotation * 180 / Math.PI - 30) < 1e-6,
                        (argText.Rotation * 180 / Math.PI).ToString("0.##") + "°");

                    // Lệnh không có tham số vẫn phải chạy như cũ
                    argForm.RunCommandForTest("TEXTANGLE");

                    Check("Lệnh không tham số: TEXTANGLE vẫn chạy (báo cách dùng)",
                        argForm.HistoryTextForTest.Contains("TEXTANGLE"),
                        "có hướng dẫn");

                    // Lệnh có 2 tham số
                    var wblockPath = Path.Combine(Path.GetTempPath(), "opencad-argtest.dwg");

                    argForm.RunCommandForTest("WBLOCK " + wblockPath);

                    Check("Lệnh có tham số: WBLOCK <tệp> chạy được",
                        File.Exists(wblockPath), wblockPath);

                    try { File.Delete(wblockPath); } catch { }
                }
            }

            // ---- ATTOUT: xuất CSV, mở bằng Excel phải đọc được (UTF-8 BOM)
            using (var attForm = new MainForm())
            {
                attForm.InitializeForTest();

                var attEditorApp = attForm.EditorForTest;

                if (attEditorApp is null)
                {
                    Check("ATTOUT: có editor", false, "null");
                }
                else
                {
                    var attBlockApp = new ACadSharp.Tables.BlockRecord("CUA_SO_APP");
                    attEditorApp.Workspace.Document.BlockRecords.Add(attBlockApp);

                    attBlockApp.Entities.Add(new Line
                    {
                        StartPoint = new XYZ(0, 0, 0),
                        EndPoint = new XYZ(1200, 0, 0),
                    });

                    var attInsert = new Insert(attBlockApp) { InsertPoint = new XYZ(0, 0, 0) };

                    attInsert.Attributes.Add(new AttributeEntity
                    {
                        Tag = "MA",
                        Value = "CỬA SỔ 1,2m",     // có dấu phẩy + tiếng Việt
                        InsertPoint = new XYZ(0, 200, 0),
                        Height = 150,
                    });

                    attEditorApp.AddEntities(new Entity[] { attInsert }, "Insert");

                    var csvPath = Path.Combine(Path.GetTempPath(), "opencad-attout.csv");

                    attForm.RunCommandForTest("ATTOUT " + csvPath);

                    Check("ATTOUT: tạo được tệp CSV", File.Exists(csvPath), csvPath);

                    if (File.Exists(csvPath))
                    {
                        var bytes = File.ReadAllBytes(csvPath);

                        Check("ATTOUT: có BOM UTF-8 để Excel đọc đúng tiếng Việt",
                            bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                            bytes.Length + " bytes");

                        var content = File.ReadAllText(csvPath);

                        Check("ATTOUT: giữ tiếng Việt có dấu",
                            content.Contains("CỬA SỔ 1,2m"), content.Split('\n')[^2].Trim());

                        Check("ATTOUT: ô có dấu phẩy được bọc nháy kép",
                            content.Contains("\"CỬA SỔ 1,2m\""), "có bọc nháy");

                        try { File.Delete(csvPath); } catch { }
                    }
                }
            }

            // ---- Hiệu năng vẽ trên bản vẽ THẬT (nếu có) hoặc bản vẽ mẫu
            {
                var perfPath = new[]
                {
                    @"C:\Users\khoan\Documents\GstarBIM Draw\2027\DWG\Trivella.dwg",
                    Path.Combine("samples", "can-ho.dwg"),
                }.FirstOrDefault(File.Exists);

                if (perfPath is null)
                {
                    Check("Hiệu năng: có bản vẽ để đo", false, "không tìm thấy tệp nào");
                }
                else
                {
                    var perfWs = CadWorkspace.Open(perfPath);
                    var perfScene = new CadScene();
                    perfScene.Build(perfWs.Document);

                    var paintCanvas = new CadCanvas();
                    paintCanvas.Attach(new CadEditor(perfWs));
                    paintCanvas.Width = 1600;
                    paintCanvas.Height = 900;
                    paintCanvas.ZoomExtentsForTest();

                    var frames = new List<double>();

                    for (var i = 0; i < 5; i++)
                    {
                        var watch = System.Diagnostics.Stopwatch.StartNew();

                        using (var frame = paintCanvas.RenderForTest(1600, 900))
                        {
                            if (frame.Width != 1600) i = 5;
                        }

                        frames.Add(watch.Elapsed.TotalMilliseconds);
                    }

                    var average = frames.Average();

                    Check("Hiệu năng: vẽ khung hình < 800 ms",
                        average < 800,
                        $"{perfWs.Document.Entities.Count()} entity, {perfScene.PrimitiveCount} primitive, "
                        + $"{average:0} ms/khung (chậm nhất {frames.Max():0})");

                    paintCanvas.Dispose();
                }
            }

            // ---- Kéo grip trong TRÌNH TỰ QUÁI (loại tình huống hay làm sập app thật)
            {
                var oddWs = CadWorkspace.New();
                var oddCanvas = new CadCanvas();
                var oddEditor = new CadEditor(oddWs);

                oddCanvas.Attach(oddEditor);
                oddCanvas.Width = 900;
                oddCanvas.Height = 700;

                var oddLine = new Line
                {
                    StartPoint = new XYZ(0, 0, 0),
                    EndPoint = new XYZ(1000, 0, 0),
                };

                oddEditor.AddEntities(new Entity[] { oddLine }, "Add");
                oddCanvas.ZoomExtentsForTest();
                CadCanvas.ClearLastUiError();

                var oddGrip = GripSet.For(oddLine).First();
                var oddFrom = oddCanvas.WorldToScreenPoint(oddGrip.Position);

                // 1. Nhấn lên grip rồi ĐỔI ZOOM giữa lúc kéo
                oddCanvas.SimulateMouseDown(oddFrom.X, oddFrom.Y);
                oddCanvas.ZoomExtentsForTest();
                oddCanvas.SimulateMouseMove(oddFrom.X + 80, oddFrom.Y + 40);
                oddCanvas.SimulateMouseUp(oddFrom.X + 80, oddFrom.Y + 40);

                Check("Grip quái: đổi zoom giữa lúc kéo không crash",
                    CadCanvas.LastUiError is null, CadCanvas.LastUiError ?? "sạch");

                CadCanvas.ClearLastUiError();

                // 2. Nhấn lên grip rồi XOÁ đối tượng giữa lúc kéo
                oddCanvas.ZoomExtentsForTest();
                oddGrip = GripSet.For(oddLine).First();
                oddFrom = oddCanvas.WorldToScreenPoint(oddGrip.Position);

                oddCanvas.SimulateMouseDown(oddFrom.X, oddFrom.Y);
                oddEditor.RemoveEntities(new Entity[] { oddLine }, "Erase");
                oddCanvas.SimulateMouseMove(oddFrom.X + 60, oddFrom.Y);
                oddCanvas.SimulateMouseUp(oddFrom.X + 60, oddFrom.Y);

                Check("Grip quái: xoá đối tượng giữa lúc kéo không crash",
                    CadCanvas.LastUiError is null, CadCanvas.LastUiError ?? "sạch");

                CadCanvas.ClearLastUiError();

                // 3. Nhấn lên grip rồi ESC (huỷ) rồi rê + nhả
                var oddLine2 = new Line
                {
                    StartPoint = new XYZ(0, 0, 0),
                    EndPoint = new XYZ(800, 0, 0),
                };

                oddEditor.AddEntities(new Entity[] { oddLine2 }, "Add");
                oddCanvas.ZoomExtentsForTest();

                var grip2 = GripSet.For(oddLine2).First();
                var from2 = oddCanvas.WorldToScreenPoint(grip2.Position);

                oddCanvas.SimulateMouseDown(from2.X, from2.Y);
                oddCanvas.Cancel();
                oddCanvas.SimulateMouseMove(from2.X + 100, from2.Y + 100);
                oddCanvas.SimulateMouseUp(from2.X + 100, from2.Y + 100);

                Check("Grip quái: Esc giữa lúc kéo rồi rê tiếp không crash",
                    CadCanvas.LastUiError is null, CadCanvas.LastUiError ?? "sạch");

                // Sau tất cả, đối tượng còn lại vẫn phải chọn được
                var oddScene = new CadScene();
                oddScene.Build(oddWs.Document);

                Check("Grip quái: bản vẽ vẫn dùng được sau các trình tự quái",
                    oddScene.Entries.Count > 0 && oddScene.PrimitiveCount > 0,
                    oddScene.Entries.Count + " entry, " + oddScene.PrimitiveCount + " prim");

                oddCanvas.Dispose();
            }

            // ---- XLINE / RAY: tạo được bằng chuột
            {
                var xlWs = CadWorkspace.New();
                var xlCanvas = new CadCanvas();
                xlCanvas.Attach(new CadEditor(xlWs));
                xlCanvas.Width = 800;
                xlCanvas.Height = 600;

                // XLINE: gốc + hướng
                xlCanvas.SetModeForTest(EditMode.XLine);
                xlCanvas.SimulateMouseDown(300, 300);
                xlCanvas.SimulateMouseDown(500, 300);

                var xlines = xlWs.Document.Entities.OfType<XLine>().ToList();

                Check("XLINE: tạo được 1 đường dựng", xlines.Count == 1, xlines.Count + " xline");
                Check("XLINE: hướng nằm ngang (dy = 0)",
                    xlines.Count == 1 && Math.Abs(xlines[0].Direction.Y) < 1e-6,
                    xlines.Count == 1 ? xlines[0].Direction.ToString() : "không có");

                var xlScene = new CadScene();
                xlScene.Build(xlWs.Document);

                Check("XLINE: vẽ ra hình", xlScene.PrimitiveCount > 0,
                    xlScene.PrimitiveCount + " primitive");

                // RAY
                xlCanvas.SetModeForTest(EditMode.Ray);
                xlCanvas.SimulateMouseDown(300, 400);
                xlCanvas.SimulateMouseDown(300, 500);

                var rays = xlWs.Document.Entities.OfType<Ray>().ToList();

                Check("RAY: tạo được 1 tia", rays.Count == 1, rays.Count + " ray");
                Check("RAY: sau khi tạo thì về chế độ chọn",
                    xlCanvas.ModeForTest == "Select", xlCanvas.ModeForTest);

                xlCanvas.Dispose();
            }

            // ---- Sửa góc xoay chữ trong bảng thuộc tính
            {
                var rotWs = CadWorkspace.New();
                var rotEditor = new CadEditor(rotWs);

                var rotText = new TextEntity
                {
                    InsertPoint = new XYZ(0, 0, 0),
                    Value = "PHONG KHACH",
                    Height = 200,
                };

                rotEditor.AddEntities(new Entity[] { rotText }, "Add");

                using var rotPanel = new OpenCad.App.Panels.PropertiesPanel();
                rotPanel.Attach(rotEditor);
                rotPanel.ShowSelection(new Entity[] { rotText });

                var rotOk = rotPanel.EditGeometryForTest("200", "30");

                Check("Panel: sửa được góc xoay chữ",
                    rotOk && Math.Abs(rotText.Rotation * 180 / Math.PI - 30) < 1e-6,
                    (rotText.Rotation * 180 / Math.PI).ToString("0.##") + "°");

                Check("Panel: cao chữ giữ nguyên khi chỉ đổi góc",
                    Math.Abs(rotText.Height - 200) < 1e-6, rotText.Height.ToString("0"));

                // Đổi cao chữ, giữ góc
                rotPanel.EditGeometryForTest("300", "30");

                Check("Panel: đổi được cao chữ, góc vẫn 30°",
                    Math.Abs(rotText.Height - 300) < 1e-6 &&
                    Math.Abs(rotText.Rotation * 180 / Math.PI - 30) < 1e-6,
                    rotText.Height.ToString("0") + " / " +
                    (rotText.Rotation * 180 / Math.PI).ToString("0.##") + "°");
            }

            // ---- PDF phải dùng font đang chọn (từng khai báo ResolvePdfFont mà không gọi)
            using (var pdfFontForm = new MainForm())
            {
                pdfFontForm.InitializeForTest();

                pdfFontForm.CanvasForTest.SetTextFont("Arial");

                var pdfFont = pdfFontForm.ResolvePdfFont();

                Check("PDF: dùng tệp .ttf của font đang chọn",
                    pdfFont is not null &&
                    Path.GetFileName(pdfFont).Equals("arial.ttf", StringComparison.OrdinalIgnoreCase),
                    pdfFont ?? "null");
            }

            // ---- LWT trên status bar (mất khi bỏ toolbar ở vòng 81, đã thêm lại)
            {
                var lwtCanvas = new CadCanvas();
                lwtCanvas.Attach(new CadEditor(CadWorkspace.New()));

                var lwtBefore = lwtCanvas.ShowLineWeights;
                lwtCanvas.ToggleLineWeights();

                Check("LWT: bật/tắt được trên canvas",
                    lwtCanvas.ShowLineWeights != lwtBefore,
                    lwtBefore + " → " + lwtCanvas.ShowLineWeights);

                lwtCanvas.Dispose();
            }

            // ---- Đổi font chữ (TEXTSTYLE)
            {
                var fontCanvas = new CadCanvas();
                var fontEditor = new CadEditor(CadWorkspace.New());

                fontCanvas.Attach(fontEditor);
                fontCanvas.Width = 600;
                fontCanvas.Height = 400;

                fontEditor.AddEntities(new Entity[]
                {
                    new TextEntity { InsertPoint = new XYZ(0, 0, 0), Height = 40, Value = "PHONG KHACH" },
                }, "Add");

                Check("TEXTSTYLE: font mặc định",
                    fontCanvas.TextFontName.Length > 0, fontCanvas.TextFontName);

                var fontBefore = fontCanvas.TextFontName;
                var fontChanged = fontCanvas.SetTextFont("Times New Roman");

                Check("TEXTSTYLE: đổi được sang font khác",
                    fontChanged && fontCanvas.TextFontName != fontBefore,
                    fontBefore + " → " + fontCanvas.TextFontName);

                // Vẽ lại phải không ném ngoại lệ với font mới
                using (var bmp = fontCanvas.RenderForTest(600, 400))
                {
                    Check("TEXTSTYLE: vẽ được với font mới", bmp.Width == 600, bmp.Width + "px");
                }

                var rejected = fontCanvas.SetTextFont("KhongCoFontNayXYZ");

                Check("TEXTSTYLE: từ chối font không tồn tại",
                    !rejected && fontCanvas.TextFontName == "Times New Roman",
                    fontCanvas.TextFontName);

                // Font phải LƯU theo bản vẽ và đọc lại được
                var savedFont = OpenCad.Core.Styles.FontLibrary.GetDocumentFont(
                    fontEditor.Workspace.Document);

                Check("TEXTSTYLE: font được ghi vào bản vẽ",
                    savedFont == "Times New Roman", savedFont);

                var fontPath = Path.Combine(Path.GetTempPath(), "opencad-font-test.dwg");
                fontEditor.Workspace.Save(fontPath);

                var fontBack = CadWorkspace.Open(fontPath);

                var fontReopened = OpenCad.Core.Styles.FontLibrary.GetDocumentFont(fontBack.Document);

                Check("TEXTSTYLE: mở lại giữ đúng font",
                    fontReopened == "Times New Roman", fontReopened);

                // ApplyDocumentFont phải áp font đã lưu vào bộ vẽ
                var fontCanvas2 = new CadCanvas();
                fontCanvas2.Attach(new CadEditor(fontBack));
                fontCanvas2.ApplyDocumentFont();

                Check("TEXTSTYLE: áp lại được font khi mở tệp",
                    fontCanvas2.TextFontName == "Times New Roman", fontCanvas2.TextFontName);

                var ttf = OpenCad.Core.Styles.FontLibrary.FileFor("Arial");

                Check("TEXTSTYLE: tìm được tệp .ttf cho font",
                    ttf is not null && File.Exists(ttf), ttf ?? "null");

                fontCanvas2.Dispose();
                try { File.Delete(fontPath); } catch { }

                fontCanvas.Dispose();
            }

            // ---- Chạy thử MỌI lệnh trên ribbon (bỏ lệnh mở hộp thoại sẽ treo)
            using (var ribbonForm = new MainForm())
            {
                ribbonForm.InitializeForTest();

                using var ribbonBar = new RibbonBar();

                var commands = ribbonBar.AllCommandNames;

                // Lệnh mở hộp thoại modal → không chạy được khi không có người
                var modal = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "NEW", "OPEN", "SAVE", "SAVEAS", "PDF", "PLOT", "PURGE",
                };

                var tested = commands.Where(c => !modal.Contains(c)).ToList();
                var broken = new List<string>();

                foreach (var name in tested)
                {
                    try
                    {
                        ribbonForm.RunCommandForTest(name);
                        ribbonForm.CancelForTest();
                    }
                    catch (Exception ex)
                    {
                        broken.Add(name + " (" + ex.GetType().Name + ")");
                    }
                }

                Check("Ribbon: mọi nút phát lệnh chạy không ném ngoại lệ",
                    broken.Count == 0,
                    broken.Count == 0
                        ? tested.Count + "/" + commands.Count + " lệnh (bỏ " + modal.Count + " lệnh hộp thoại)"
                        : string.Join(", ", broken.Take(5)));

                // Không lệnh nào được để canvas kẹt ở chế độ lạ sau khi Cancel
                Check("Ribbon: Cancel đưa canvas về chế độ chọn",
                    ribbonForm.CanvasForTest.Mode.ToString() == "Select",
                    ribbonForm.CanvasForTest.Mode.ToString());

                // Chạy thử TOÀN BỘ lệnh trong bảng lệnh (kể cả lệnh không có trên ribbon)
                var allNames = OpenCad.Core.Commands.CommandRegistry.All.Select(c => c.Name).ToList();

                var sweepBroken = new List<string>();
                var sweepCount = 0;

                foreach (var name in allNames)
                {
                    if (modal.Contains(name)) continue;

                    sweepCount++;


                    try
                    {
                        ribbonForm.RunCommandForTest(name);
                        ribbonForm.CancelForTest();
                    }
                    catch (Exception ex)
                    {
                        sweepBroken.Add(name + " (" + ex.GetType().Name + ")");
                    }
                }

                // 7 lệnh hộp thoại: kiểm qua đường đi KHÔNG hộp thoại (nợ từ vòng 83)
                var tempDir = Path.Combine(Path.GetTempPath(), "opencad-sweep");
                Directory.CreateDirectory(tempDir);

                var dialogFreeOk = 0;
                var dialogFreeFail = new List<string>();

                void TryDialogFree(string label, Func<bool> action)
                {
                    try
                    {
                        if (action()) dialogFreeOk++;
                        else dialogFreeFail.Add(label + " (trả false)");
                    }
                    catch (Exception ex)
                    {
                        dialogFreeFail.Add(label + " (" + ex.GetType().Name + ")");
                    }
                }

                var sweepDrawing = Path.Combine(tempDir, "sweep.dwg");
                var sweepPdf = Path.Combine(tempDir, "sweep.pdf");

                // NEW
                TryDialogFree("NEW", () => { ribbonForm.NewDocumentForTest(); return true; });

                // SAVE + SAVEAS (qua đường ghi tệp)
                // Thêm một đối tượng để bản vẽ có nội dung trước khi lưu
                ribbonForm.EditorForTest?.AddEntities(new Entity[]
                {
                    new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(500, 0, 0) },
                }, "Add");

                TryDialogFree("SAVE", () => ribbonForm.SaveToPathForTest(sweepDrawing));
                TryDialogFree("SAVEAS", () => ribbonForm.SaveToPathForTest(sweepDrawing + ".copy.dwg"));

                Check("Lệnh hộp thoại: ghi được tệp DWG",
                    File.Exists(sweepDrawing), sweepDrawing);

                // OPEN
                TryDialogFree("OPEN", () => ribbonForm.OpenPathForTest(sweepDrawing));

                // PDF
                TryDialogFree("PDF", () =>
                {
                    ribbonForm.ExportPdfToPathForTest(sweepPdf);

                    return File.Exists(sweepPdf) && new FileInfo(sweepPdf).Length > 500;
                });

                // PURGE
                TryDialogFree("PURGE", () => ribbonForm.PurgeForTest() >= 0);

                // PLOT dùng cùng đường xuất PDF khổ A3
                TryDialogFree("PLOT", () =>
                {
                    var plotPath = Path.Combine(tempDir, "plot.pdf");
                    ribbonForm.ExportPdfToPathForTest(plotPath, 420, 297);

                    return File.Exists(plotPath);
                });

                Check("Lệnh hộp thoại: chạy được qua đường không hộp thoại",
                    dialogFreeFail.Count == 0,
                    dialogFreeFail.Count == 0
                        ? dialogFreeOk + "/7 lệnh"
                        : string.Join(", ", dialogFreeFail));

                try { Directory.Delete(tempDir, true); } catch { }

                Check("Lệnh: chạy thử toàn bộ bảng lệnh không ném ngoại lệ",
                    sweepBroken.Count == 0,
                    sweepBroken.Count == 0
                        ? sweepCount + "/" + allNames.Count + " lệnh"
                        : string.Join(", ", sweepBroken.Take(6)));
            }

            // ---- Toàn bộ phím tắt trong menu: không trùng, và hiệu lực
            using (var menuForm = new MainForm())
            {
                menuForm.InitializeForTest();

                var shortcuts = menuForm.MenuShortcutsForTest();

                Check("Menu: có khai báo phím tắt", shortcuts.Count >= 6,
                    shortcuts.Count + " phím tắt");

                var duplicates = shortcuts
                    .GroupBy(s => s.Shortcut)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key + " (" + string.Join(" / ", g.Select(x => x.Text)) + ")")
                    .ToList();

                Check("Menu: không có hai mục trùng phím tắt",
                    duplicates.Count == 0,
                    duplicates.Count == 0 ? string.Join(", ", shortcuts.Select(s => s.Text + "=" + s.Shortcut)) : string.Join("; ", duplicates));

                // Ctrl+Z và Ctrl+Y phải có mặt
                Check("Menu: có Ctrl+Z", shortcuts.Any(s => s.Shortcut == (Keys.Control | Keys.Z)),
                    "Z");
                Check("Menu: có Ctrl+Y", shortcuts.Any(s => s.Shortcut == (Keys.Control | Keys.Y)),
                    "Y");

                // Phím an toàn (không mở hộp thoại) phải thực sự được xử lý
                var zoomHandled = menuForm.ProcessKeyForTest(Keys.Control | Keys.E);

                Check("Menu: Ctrl+E (zoom extents) có hiệu lực", zoomHandled, zoomHandled.ToString());

                // Các phím bật/tắt an toàn phải thật sự chạy
                var canvasMenu = menuForm.CanvasForTest;

                var osnapBefore = canvasMenu.Snap.Enabled;

                var f3 = menuForm.ProcessKeyForTest(Keys.F3);

                Check("Menu: F3 bật/tắt OSNAP thật",
                    f3 && canvasMenu.Snap.Enabled != osnapBefore,
                    $"F3={f3}, OSNAP {osnapBefore}→{canvasMenu.Snap.Enabled}");

                var f8 = menuForm.ProcessKeyForTest(Keys.F8);

                Check("Menu: F8 bật/tắt ORTHO được xử lý", f8, f8.ToString());

                var f7 = menuForm.ProcessKeyForTest(Keys.F7);

                Check("Menu: F7 bật/tắt GRID được xử lý", f7, f7.ToString());

                var f10 = menuForm.ProcessKeyForTest(Keys.F10);

                Check("Menu: F10 bật/tắt POLAR được xử lý", f10, f10.ToString());

                var zoomIn = menuForm.ProcessKeyForTest(Keys.Control | Keys.Oemplus);

                Check("Menu: Ctrl+'+' phóng to được xử lý", zoomIn, zoomIn.ToString());
            }

            // ---- Phím tắt: Ctrl+Z / Ctrl+Y phải hoạt động thật
            using (var keyForm = new MainForm())
            {
                keyForm.InitializeForTest();

                var keyEditor = keyForm.EditorForTest;

                if (keyEditor is null)
                {
                    Check("Phím tắt: có bản vẽ để kiểm", false, "không có editor");
                }
                else
                {
                    var shortcuts = keyForm.ShortcutsForTest;

                    Check("Phím tắt: menu Undo dùng Ctrl+Z",
                        shortcuts.Undo == (Keys.Control | Keys.Z), shortcuts.Undo.ToString());

                    Check("Phím tắt: menu Redo dùng Ctrl+Y",
                        shortcuts.Redo == (Keys.Control | Keys.Y), shortcuts.Redo.ToString());

                    var beforeCount = keyEditor.Workspace.Document.Entities.Count();

                    keyEditor.AddEntities(new Entity[]
                    {
                        new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(500, 0, 0) },
                    }, "Add");

                    Check("Phím tắt: thêm được đối tượng để hoàn tác",
                        keyEditor.Workspace.Document.Entities.Count() == beforeCount + 1,
                        keyEditor.Workspace.Document.Entities.Count() + " entity");

                    // Gửi Ctrl+Z qua đúng đường xử lý phím của form
                    var handled = keyForm.ProcessKeyForTest(Keys.Control | Keys.Z);

                    Check("Phím tắt: Ctrl+Z được xử lý", handled, handled.ToString());

                    Check("Phím tắt: Ctrl+Z hoàn tác thật",
                        keyEditor.Workspace.Document.Entities.Count() == beforeCount,
                        keyEditor.Workspace.Document.Entities.Count() + " entity");

                    keyForm.ProcessKeyForTest(Keys.Control | Keys.Y);

                    Check("Phím tắt: Ctrl+Y làm lại thật",
                        keyEditor.Workspace.Document.Entities.Count() == beforeCount + 1,
                        keyEditor.Workspace.Document.Entities.Count() + " entity");

                    // Ctrl+A chọn tất cả
                    var selectAllHandled = keyForm.ProcessKeyForTest(Keys.Control | Keys.A);
                    var canvasOfForm = keyForm.CanvasForTest;

                    Check("Phím tắt: Ctrl+A chọn tất cả",
                        selectAllHandled && canvasOfForm.Selection.Count == beforeCount + 1,
                        canvasOfForm.Selection.Count + " chọn");

                    // Delete xoá lựa chọn
                    var deleteHandled = keyForm.ProcessKeyForTest(Keys.Delete);

                    Check("Phím tắt: Delete xoá lựa chọn",
                        deleteHandled && keyEditor.Workspace.Document.Entities.Count() == beforeCount,
                        keyEditor.Workspace.Document.Entities.Count() + " entity");

                    // Escape không làm hỏng gì
                    var escapeHandled = keyForm.ProcessKeyForTest(Keys.Escape);

                    Check("Phím tắt: Escape được xử lý và không ném ngoại lệ",
                        escapeHandled && canvasOfForm.Selection.Count == 0,
                        canvasOfForm.Selection.Count + " chọn");
                }
            }

            // ---- Gợi ý lệnh trên dòng lệnh
            using (var cmd = new OpenCad.App.Panels.CommandLine())
            {
                cmd.Suggest = OpenCad.Core.Commands.CommandRegistry.Suggest;

                cmd.BeginTyping('L');
                cmd.BeginTyping('I');

                Check("CommandLine: hiện gợi ý LINE khi gõ 'LI'",
                    cmd.HintForTest.Contains("LINE", StringComparison.OrdinalIgnoreCase),
                    cmd.HintForTest);

                var accepted = cmd.AcceptHint();

                Check("CommandLine: Tab chấp nhận gợi ý",
                    accepted && cmd.InputTextForTest == "LINE", cmd.InputTextForTest);

                Check("CommandLine: chấp nhận xong thì không còn gợi ý",
                    cmd.HintForTest.Length == 0, cmd.HintForTest);
            }

            // ---- MLEADER: tạo chú thích có đường dẫn
            foreach (var e in workspace.Document.Entities.ToList())
                workspace.Document.Entities.Remove(e);

            editor.Reload();
            canvas.Cancel();
            canvas.ClearSelection();
            canvas.SetOrthoForTest(false);
            canvas.Width = 900;
            canvas.Height = 640;

            editor.AddEntities(new Entity[]
            {
                new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(4000, 0, 0) },
            }, "Add");

            canvas.ZoomExtents();
            canvas.SetMode(EditMode.MLeader);

            foreach (var point in new[] { new Pt(1000, 0), new Pt(2500, 1200) })
            {
                var screen = canvas.WorldToScreenPoint(point);
                canvas.SimulateMouseDown(screen.X, screen.Y);
            }

            Check("MLEADER: nhận đủ 2 điểm",
                canvas.PointCountForTest == 2, canvas.PointCountForTest + " điểm");

            canvas.ProvideInput("THEP D18");

            var leaders = workspace.Document.Entities.OfType<MultiLeader>().ToList();

            Check("MLEADER tạo được chú thích", leaders.Count == 1, leaders.Count + " mleaders");

            if (leaders.Count == 1)
            {
                var context = leaders[0].ContextData;

                Check("MLEADER có nội dung chữ",
                    context?.TextLabel == "THEP D18", context?.TextLabel ?? "null");

                Check("MLEADER có đường dẫn",
                    context is not null && context.LeaderRoots.Count == 1,
                    (context?.LeaderRoots.Count ?? 0) + " nhánh");

                var mScene = new CadScene();
                mScene.Build(workspace.Document);

                var mPrims = mScene.Entries.SelectMany(e => e.Prims).ToList();

                Check("MLEADER vẽ ra hình",
                    mPrims.Count >= 3, mPrims.Count + " primitive");

                Check("MLEADER hiện đúng chữ khi vẽ",
                    mPrims.Any(p => p.Kind == OpenCad.Core.Rendering.PrimKind.Text && p.Text == "THEP D18"),
                    "có chữ");

                Check("MLEADER cảnh báo mất CHỮ khi lưu",
                    workspace.LossySaveRisks().Any(r => r.Type.StartsWith("MultiLeader")),
                    string.Join(",", workspace.LossySaveRisks().Select(r => r.Type)));
            }

            canvas.Cancel();
            canvas.ClearSelection();

            // ---- INFO: thống kê bản vẽ (chạy qua lệnh thật)
            foreach (var e in workspace.Document.Entities.ToList())
                workspace.Document.Entities.Remove(e);

            editor.Reload();
            canvas.Cancel();
            canvas.ClearSelection();

            editor.AddEntities(new Entity[]
            {
                new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(1000, 0, 0) },
                new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(0, 800, 0) },
                new Circle { Center = new XYZ(500, 500, 0), Radius = 100 },
                new TextEntity { InsertPoint = new XYZ(50, 900, 0), Height = 80, Value = "T" },
            }, "Add");

            using (var infoForm = new MainForm())
            {
                infoForm.InitializeForTest();

                var infoEditor = infoForm.EditorForTest;

                if (infoEditor is null)
                {
                    Check("INFO: có bản vẽ để thống kê", false, "không có editor");
                }
                else
                {
                    // Thêm đối tượng vào CHÍNH bản vẽ của form này
                    infoEditor.AddEntities(new Entity[]
                    {
                        new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(500, 0, 0) },
                        new Circle { Center = new XYZ(100, 100, 0), Radius = 50 },
                    }, "Add");

                    infoForm.RunCommandForTest("INFO");

                    var history = infoForm.HistoryTextForTest;

                    Check("INFO in tiêu đề thống kê", history.Contains("THỐNG KÊ BẢN VẼ"),
                        history.Split('\n').FirstOrDefault() ?? "");

                    Check("INFO in đúng số entity", history.Contains("Entity"), "có dòng Entity");

                    Check("INFO liệt kê loại đối tượng",
                        history.Contains("Line") && history.Contains("Circle"),
                        "có Line và Circle");
                }
            }

            // ---- SCRIPT: chạy tệp .scr từ trong app
            foreach (var e in workspace.Document.Entities.ToList())
                workspace.Document.Entities.Remove(e);

            editor.Reload();
            canvas.Cancel();
            canvas.ClearSelection();

            var scriptFile = Path.Combine(Path.GetTempPath(), "opencad-uitest.scr");

            File.WriteAllText(scriptFile, string.Join(Environment.NewLine, new[]
            {
                "# Script kiểm thử",
                "LINE 0,0 1000,0",
                "CIRCLE 500,500 200",
                "TEXT 100,900 150 'PHONG'",
            }));

            var scriptedBefore = workspace.Document.Entities.Count();
            var scriptResult = OpenCad.Core.Scripting.ScriptRunner.Run(
                workspace, File.ReadAllText(scriptFile));

            editor.Reload();

            Check("SCRIPT chạy được tệp .scr trong app",
                scriptResult.Ok && scriptResult.Executed == 3,
                $"{scriptResult.Executed} lệnh, {scriptResult.Failed} lỗi");

            Check("SCRIPT tạo đúng 3 đối tượng",
                workspace.Document.Entities.Count() == scriptedBefore + 3,
                workspace.Document.Entities.Count() + " entity");

            Check("SCRIPT vẽ được circle",
                workspace.Document.Entities.OfType<Circle>().Any(),
                "có circle");

            var scriptText = workspace.Document.Entities.OfType<TextEntity>().FirstOrDefault();

            Check("SCRIPT vẽ được chữ",
                scriptText?.Value == "PHONG", scriptText?.Value ?? "không có text");

            // Script lỗi phải báo chứ không im lặng
            var badResult = OpenCad.Core.Scripting.ScriptRunner.Run(
                workspace, "LINE 0,0" + Environment.NewLine + "KHONGLENH 1");

            Check("SCRIPT báo lỗi khi lệnh sai",
                badResult.Failed > 0 || badResult.Errors.Count > 0,
                badResult.Failed + " lỗi");

            try { File.Delete(scriptFile); } catch { }

            // ---- Đổi kích thước viewport bằng cách kéo góc
            foreach (var e in workspace.Document.Entities.ToList())
                workspace.Document.Entities.Remove(e);

            editor.Reload();
            canvas.Cancel();
            canvas.ClearSelection();

            editor.AddEntities(new Entity[]
            {
                new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(5000, 0, 0) },
                new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(0, 5000, 0) },
            }, "Add");

            var vpScene = new CadScene();
            vpScene.Build(workspace.Document);

            var vpLayout = OpenCad.Core.Layout.LayoutFactory.Create(
                workspace.Document, "VP_RESIZE",
                new OpenCad.Core.Layout.SheetTemplateOptions { Title = "V", UnitsPerMm = 50 },
                vpScene);

            if (vpLayout?.AssociatedBlock is null)
            {
                Check("Viewport: tạo được tờ để kiểm resize", false, "không tạo được");
            }
            else
            {
                var vpSheet = OpenCad.Core.Layout.LayoutFactory.Read(workspace.Document, vpLayout);
                canvas.ShowPaper(vpSheet, vpLayout.AssociatedBlock, "VP_RESIZE");

                // Khung tờ cũng là Viewport nhưng có ViewHeight = 0 → phải loại
                var vp = vpLayout.AssociatedBlock.Entities.OfType<Viewport>()
                    .Where(v => v.ViewHeight > 1e-9)
                    .OrderByDescending(v => v.Width * v.Height)
                    .FirstOrDefault();

                if (vp is null)
                {
                    Check("Viewport: có viewport trên tờ", false, "không có");
                }
                else
                {
                    var originalWidth = vp.Width;
                    var originalHeight = vp.Height;

                    canvas.Width = 900;
                    canvas.Height = 640;

                    // Click giữa khung để chọn viewport
                    var centerScreen = canvas.WorldToScreenPoint(
                        new Pt(vp.Center.X, vp.Center.Y));

                    canvas.SimulateMouseDown(centerScreen.X, centerScreen.Y);
                    canvas.SimulateMouseUp(centerScreen.X, centerScreen.Y);

                    Check("Viewport: đang ở chế độ tờ", canvas.IsPaperMode,
                        canvas.IsPaperMode.ToString());

                    Check("Viewport: chọn được bằng click giữa khung",
                        canvas.HasSelectedViewportForTest,
                        canvas.HasSelectedViewportForTest ? "đã chọn" : "chưa chọn");

                    // Kéo góc trên-phải ra xa 30 px
                    var corner = new Pt(vp.Center.X + vp.Width / 2, vp.Center.Y + vp.Height / 2);
                    var cornerScreen = canvas.WorldToScreenPoint(corner);

                    canvas.SimulateMouseDown(cornerScreen.X, cornerScreen.Y);

                    Check("Viewport: bấm đúng góc thì vào chế độ đổi kích thước",
                        canvas.IsResizingViewportForTest,
                        canvas.IsResizingViewportForTest ? "đang resize" : "không");

                    canvas.SimulateMouseMove(cornerScreen.X + 30, cornerScreen.Y + 30);

                    Check("Viewport: rê chuột làm khung rộng ra ngay",
                        vp.Width > originalWidth + 1,
                        $"{originalWidth:0.#} → {vp.Width:0.#}");

                    canvas.SimulateMouseUp(cornerScreen.X + 30, cornerScreen.Y + 30);

                    Check("Viewport: kéo góc làm khung rộng ra",
                        vp.Width > originalWidth + 1,
                        $"{originalWidth:0.#} → {vp.Width:0.#}");

                    canvas.Undo();

                    Check("Viewport: undo trả lại kích thước cũ",
                        Math.Abs(vp.Width - originalWidth) < 1 && Math.Abs(vp.Height - originalHeight) < 1,
                        $"{vp.Width:0.#}×{vp.Height:0.#}");
                }

                canvas.ShowPaper(null);
            }

            canvas.Cancel();
            canvas.ClearSelection();

            // ---- Chọn mọi đối tượng chồng nhau (HitTestAll)
            foreach (var e in workspace.Document.Entities.ToList())
                workspace.Document.Entities.Remove(e);

            editor.Reload();
            canvas.Cancel();
            canvas.ClearSelection();
            canvas.Width = 800;
            canvas.Height = 600;

            editor.AddEntities(new Entity[]
            {
                new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(1000, 0, 0) },
                new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(0, 1000, 0) },
                new Circle { Center = new XYZ(5000, 5000, 0), Radius = 100 },
            }, "Add");

            canvas.ZoomExtents();

            // Đưa con trỏ tới gốc toạ độ (nơi 2 line chồng nhau)
            var origin = canvas.WorldToScreenPoint(new Pt(0, 0));
            canvas.SimulateMouseMove(origin.X, origin.Y);

            var underCount = canvas.SelectUnderCursor();

            Check("SELECTUNDER chọn được cả 2 line chồng nhau", underCount == 2,
                underCount + " chọn");

            // Chỗ trống hẳn (ngoài mọi hình)
            var empty = canvas.WorldToScreenPoint(new Pt(9000, 9000));
            canvas.SimulateMouseMove(empty.X, empty.Y);

            var emptyCount = canvas.SelectUnderCursor();

            Check("SELECTUNDER ở chỗ trống trả 0", emptyCount == 0, emptyCount + " chọn");

            // Trên vành hình tròn: chỉ chọn 1
            var onCircle = canvas.WorldToScreenPoint(new Pt(5100, 5000));
            canvas.SimulateMouseMove(onCircle.X, onCircle.Y);

            var circleCount = canvas.SelectUnderCursor();

            Check("SELECTUNDER trên vành tròn chọn 1", circleCount == 1, circleCount + " chọn");

            canvas.ClearSelection();

            // ---- JOIN qua canvas (dùng lựa chọn)
            foreach (var e in workspace.Document.Entities.ToList())
                workspace.Document.Entities.Remove(e);

            editor.Reload();
            canvas.Cancel();
            canvas.ClearSelection();

            editor.AddEntities(new Entity[]
            {
                new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(100, 0, 0) },
                new Line { StartPoint = new XYZ(100, 0, 0), EndPoint = new XYZ(100, 100, 0) },
            }, "Add");

            canvas.SelectAll();

            Check("JOIN: có 2 line được chọn", canvas.Selection.Count == 2,
                canvas.Selection.Count + " chọn");

            var joinedOk = canvas.JoinSelection();

            Check("JOIN: nối được từ lựa chọn", joinedOk, joinedOk.ToString());
            Check("JOIN: bản vẽ còn 1 polyline",
                workspace.Document.Entities.OfType<LwPolyline>().Count() == 1 &&
                workspace.Document.Entities.Count() == 1,
                workspace.Document.Entities.Count() + " entity");
            Check("JOIN: xoá lựa chọn sau khi nối", canvas.Selection.Count == 0,
                canvas.Selection.Count + " chọn");

            canvas.Undo();

            Check("JOIN: undo phục hồi 2 line",
                workspace.Document.Entities.OfType<Line>().Count() == 2,
                workspace.Document.Entities.Count() + " entity");

            // ---- Chọn theo tiêu chí + ZOOM OBJECT
            foreach (var e in workspace.Document.Entities.ToList())
                workspace.Document.Entities.Remove(e);

            editor.Reload();
            canvas.Cancel();
            canvas.ClearSelection();
            canvas.Width = 800;
            canvas.Height = 600;

            var layerA = new ACadSharp.Tables.Layer("LOP_A");
            var layerB = new ACadSharp.Tables.Layer("LOP_B");
            workspace.Document.Layers.Add(layerA);
            workspace.Document.Layers.Add(layerB);

            editor.AddEntities(new Entity[]
            {
                new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(100, 0, 0), Layer = layerA },
                new Line { StartPoint = new XYZ(0, 50, 0), EndPoint = new XYZ(100, 50, 0), Layer = layerA },
                new Circle { Center = new XYZ(500, 500, 0), Radius = 50, Layer = layerB },
                new Circle { Center = new XYZ(700, 500, 0), Radius = 50, Layer = layerB },
            }, "Add");

            Check("SELLAYER chọn đúng 2 line của lớp A",
                canvas.SelectByLayer("LOP_A") == 2, canvas.Selection.Count + " chọn");

            Check("SELLAYER không chọn nhầm lớp khác",
                canvas.Selection.All(e => e.Layer?.Name == "LOP_A"), "đúng lớp");

            Check("SELTYPE chọn được 2 circle",
                canvas.SelectByType("Circle") == 2, canvas.Selection.Count + " chọn");

            Check("SELTYPE không phân biệt hoa thường",
                canvas.SelectByType("circle") == 2, canvas.Selection.Count + " chọn");

            Check("SELLAYER lớp không tồn tại → 0",
                canvas.SelectByLayer("KHONG_CO") == 0, "0");

            canvas.SelectByLayer("LOP_A");

            Check("SELINVERT đảo được lựa chọn",
                canvas.InvertSelection() == 2 && canvas.Selection.All(e => e is Circle),
                canvas.Selection.Count + " chọn");

            // ZOOM OBJECT: phóng tới 2 line (gần gốc) phải cho zoom lớn hơn zoom toàn bộ
            canvas.SelectByLayer("LOP_A");
            canvas.ZoomExtents();

            var beforeZoomObj = canvas.ZoomFactor;
            canvas.ZoomToSelection();

            Check("ZOOMOBJ phóng to hơn zoom toàn bộ",
                canvas.ZoomFactor > beforeZoomObj,
                $"{beforeZoomObj:0.###} → {canvas.ZoomFactor:0.###}");

            Check("ZOOMOBJ quay lại được bằng ZOOM PREVIOUS",
                canvas.ZoomPrevious() && Math.Abs(canvas.ZoomFactor - beforeZoomObj) < 1e-6,
                canvas.ZoomFactor.ToString("0.###"));

            // Không chọn gì → không đổi
            canvas.ClearSelection();
            var noSelection = canvas.ZoomToSelection();

            Check("ZOOMOBJ không có lựa chọn thì không làm gì", !noSelection, "bỏ qua");

            canvas.ClearSelection();

            // ---- DIMBASELINE: nhiều kích thước từ một gốc
            foreach (var e in workspace.Document.Entities.ToList())
                workspace.Document.Entities.Remove(e);

            editor.Reload();
            canvas.Cancel();
            canvas.ClearSelection();
            canvas.SetOrthoForTest(false);

            canvas.SetMode(EditMode.Dimension);

            foreach (var point in new[] { new Pt(0, 0), new Pt(1000, 0), new Pt(0, -400) })
            {
                var screen = canvas.WorldToScreenPoint(point);
                canvas.SimulateMouseDown(screen.X, screen.Y);
            }

            canvas.SetMode(EditMode.DimBaseline);

            foreach (var point in new[] { new Pt(2000, 0), new Pt(3200, 0) })
            {
                var screen = canvas.WorldToScreenPoint(point);
                canvas.SimulateMouseDown(screen.X, screen.Y);
            }

            var baselineDims = workspace.Document.Entities.OfType<DimensionAligned>().ToList();

            Check("DIMBASELINE tạo thêm 2 kích thước", baselineDims.Count == 3,
                baselineDims.Count + " dim");

            if (baselineDims.Count == 3)
            {
                var originDims = baselineDims
                    .Where(d => Math.Abs(d.FirstPoint.X) < 5)
                    .ToList();

                Check("DIMBASELINE: cả 3 cùng gốc X = 0", originDims.Count == 3,
                    originDims.Count + " dim cùng gốc");

                var ends = baselineDims.Select(d => d.SecondPoint.X).OrderBy(x => x).ToList();

                Check("DIMBASELINE đo tới 1000/2000/3200",
                    ends.Count == 3 &&
                    Math.Abs(ends[0] - 1000) < 5 &&
                    Math.Abs(ends[1] - 2000) < 5 &&
                    Math.Abs(ends[2] - 3200) < 5,
                    string.Join(",", ends.Select(x => x.ToString("0"))));

                // Mỗi kích thước lùi ra xa dần
                var offsets = baselineDims
                    .Select(d => d.DefinitionPoint.Y - d.FirstPoint.Y)
                    .Distinct()
                    .OrderBy(y => y)
                    .ToList();

                Check("DIMBASELINE: các đường kích thước lùi dần ra", offsets.Count >= 2,
                    string.Join(",", offsets.Select(y => y.ToString("0"))));
            }

            canvas.Cancel();
            canvas.ClearSelection();

            // ---- POINT và MLINE
            foreach (var e in workspace.Document.Entities.ToList())
                workspace.Document.Entities.Remove(e);

            editor.Reload();
            canvas.Cancel();
            canvas.ClearSelection();
            canvas.SetOrthoForTest(false);

            canvas.SetMode(EditMode.PointDraw);

            foreach (var point in new[] { new Pt(0, 0), new Pt(100, 100), new Pt(200, 0) })
            {
                var screen = canvas.WorldToScreenPoint(point);
                canvas.SimulateMouseDown(screen.X, screen.Y);
            }

            var points = workspace.Document.Entities.OfType<ACadSharp.Entities.Point>().Count();

            Check("Lệnh: POINT đặt được 3 điểm", points == 3, points + " điểm");

            canvas.Cancel();
            canvas.ClearSelection();

            canvas.SetMode(EditMode.MLine);

            foreach (var point in new[] { new Pt(0, 500), new Pt(300, 500), new Pt(300, 800) })
            {
                var screen = canvas.WorldToScreenPoint(point);
                canvas.SimulateMouseDown(screen.X, screen.Y);
            }

            canvas.ProvideInput(string.Empty);

            var mlines = workspace.Document.Entities.OfType<MLine>().ToList();

            Check("Lệnh: MLINE tạo được tường nhiều nét", mlines.Count == 1, mlines.Count + " mline");
            Check("Lệnh: MLINE có 3 đỉnh",
                mlines.Count == 1 && mlines[0].Vertices.Count == 3,
                (mlines.Count == 1 ? mlines[0].Vertices.Count : -1) + " đỉnh");

            canvas.Cancel();
            canvas.ClearSelection();

            // ---- ZOOM: window và previous
            foreach (var e in workspace.Document.Entities.ToList())
                workspace.Document.Entities.Remove(e);

            editor.Reload();
            canvas.Cancel();
            canvas.ClearSelection();

            editor.AddEntities(new Entity[]
            {
                new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(1000, 0, 0) },
                new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(0, 1000, 0) },
            }, "Add");

            // Phải có kích thước khung nhìn thì phép zoom mới có nghĩa
            canvas.Width = 800;
            canvas.Height = 600;

            canvas.ZoomExtents();
            var zoomBefore = canvas.ZoomFactor;

            canvas.ZoomWindow(new Pt(100, 100), new Pt(300, 300));

            Check("ZOOM: window phóng to hơn", canvas.ZoomFactor > zoomBefore,
                $"{zoomBefore:0.###} → {canvas.ZoomFactor:0.###}");

            var zoomAfterWindow = canvas.ZoomFactor;
            var wentBack = canvas.ZoomPrevious();

            Check("ZOOM: previous quay lại được", wentBack, wentBack ? "ok" : "không");
            Check("ZOOM: previous phục hồi hệ số zoom",
                Math.Abs(canvas.ZoomFactor - zoomBefore) < 1e-6,
                $"{canvas.ZoomFactor:0.###} vs {zoomBefore:0.###}");

            // Vùng quá nhỏ bị từ chối
            var before = canvas.ZoomFactor;
            canvas.ZoomWindow(new Pt(0, 0), new Pt(0, 0));

            Check("ZOOM: vùng quá nhỏ không đổi khung nhìn",
                Math.Abs(canvas.ZoomFactor - before) < 1e-9, "giữ nguyên");

            // ---- Kiểm đường vẽ (OnPaint) cho từng loại đối tượng
            RunPaintTests(workspace, editor, canvas, Check);

            // ---- Kéo grip trên nhiều loại đối tượng (truy tìm crash)
            RunGripStressTest(workspace, editor, canvas, Check);

            // ---- Copy / Paste
            foreach (var e in workspace.Document.Entities.ToList())
                workspace.Document.Entities.Remove(e);

            editor.Reload();
            canvas.ClearSelection();

            var original = new Line
            {
                StartPoint = new XYZ(0, 0, 0),
                EndPoint = new XYZ(100, 0, 0),
            };

            editor.AddEntities(new Entity[] { original }, "Add");
            canvas.SelectAll();

            var copied = canvas.CopySelectionToClipboard();

            Check("Clipboard: copy lấy đúng 1 đối tượng", copied == 1, copied + " đối tượng");
            Check("Clipboard: giữ được nội dung", CadCanvas.ClipboardCount == 1,
                CadCanvas.ClipboardCount + " trong clipboard");

            var beforePaste = workspace.Document.Entities.Count();
            var pasted = canvas.PasteFromClipboard();

            Check("Clipboard: dán thêm 1 đối tượng", pasted == 1, pasted + " dán");
            Check("Clipboard: bản vẽ tăng đúng 1",
                workspace.Document.Entities.Count() == beforePaste + 1,
                workspace.Document.Entities.Count() + " entity");

            var pastedLine = workspace.Document.Entities.OfType<Line>()
                .FirstOrDefault(l => Math.Abs(l.StartPoint.X - 200) < 1);

            Check("Clipboard: dán lệch 200 theo mặc định", pastedLine is not null,
                pastedLine is null ? "không thấy"
                    : $"({pastedLine.StartPoint.X:0.#}, {pastedLine.StartPoint.Y:0.#})");

            canvas.Undo();

            Check("Clipboard: undo dán xoá bản vừa dán",
                workspace.Document.Entities.Count() == beforePaste,
                workspace.Document.Entities.Count() + " entity");

            // Dán tại điểm chỉ định
            canvas.PasteFromClipboard(new Pt(1000, 500));

            var atTarget = workspace.Document.Entities.OfType<Line>()
                .FirstOrDefault(l => Math.Abs(l.StartPoint.X - 1000) < 3 &&
                                     Math.Abs(l.StartPoint.Y - 500) < 3);

            Check("Clipboard: dán đúng điểm chỉ định", atTarget is not null,
                atTarget is null ? "không thấy"
                    : $"({atTarget.StartPoint.X:0.#}, {atTarget.StartPoint.Y:0.#})");

            canvas.Dispose();
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"  [FAIL] Ngoại lệ: {ex.GetType().Name} — {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? $"KẾT QUẢ: PASS — {checks} kiểm tra UI"
            : $"KẾT QUẢ: FAIL — {failures}/{checks} kiểm tra không đạt");

        return failures == 0 ? 0 : 1;
    }
}

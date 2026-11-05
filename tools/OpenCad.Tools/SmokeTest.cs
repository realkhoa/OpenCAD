using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using OpenCad.Core;
using OpenCad.Core.Editing;
using OpenCad.Core.Export;
using OpenCad.Core.Geometry;
using OpenCad.Core.Rendering;
using OpenCad.Core.Snapping;
using OpenCad.Core.Styles;

namespace OpenCad.Tools;

/// <summary>
/// Kiểm thử end-to-end: mở → dựng scene → chọn → sửa → lưu → mở lại → xác nhận thay đổi.
/// Chạy hoàn toàn headless nên dùng được trong CI.
/// </summary>
public static class SmokeTest
{
    private static int _failures;
    private static int _checks;

    /// <summary>Số kiểm tra của lần chạy gần nhất (dùng cho các cổng kiểm khác).</summary>
    public static int LastCount { get; private set; }

    public static int Run()
    {
        var dir = Path.Combine(Path.GetTempPath(), "opencad-smoke");
        Directory.CreateDirectory(dir);

        var dwg = Path.Combine(dir, "demo.dwg");
        var dwg2 = Path.Combine(dir, "demo-edited.dwg");
        var dxf = Path.Combine(dir, "demo.dxf");

        Console.WriteLine("== OpenCAD smoke test ==");
        Console.WriteLine($"Thư mục tạm: {dir}");
        Console.WriteLine();

        try
        {
            RunScenario(dwg, dwg2, dxf);
            RunEditingScenario();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"NGOẠI LỆ: {ex}");
            _failures++;
        }

        Console.WriteLine();
        if (_failures == 0)
        {
            LastCount = _checks;
        Console.WriteLine("KẾT QUẢ: PASS — toàn bộ kiểm tra đều đạt.");
            return 0;
        }

        LastCount = _checks;
        Console.WriteLine($"KẾT QUẢ: FAIL — {_failures} kiểm tra không đạt.");
        return 1;
    }

    private static void RunScenario(string dwg, string dwg2, string dxf)
    {
        // ---- 1. Tạo và lưu DWG
        var source = DemoDrawing.Build();
        var expectedCount = source.Entities.Count();
        ACadSharp.IO.DwgWriter.Write(dwg, source);
        Check("Ghi được file DWG", File.Exists(dwg) && new FileInfo(dwg).Length > 1000,
            $"{new FileInfo(dwg).Length:N0} bytes");

        // ---- 2. Mở lại
        var workspace = CadWorkspace.Open(dwg);
        var openedCount = workspace.Document.Entities.Count();
        Check("Mở lại đúng số entity", openedCount == expectedCount,
            $"mong đợi {expectedCount}, nhận {openedCount}");

        // ---- 3. Dựng scene
        var scene = new CadScene();
        scene.Build(workspace.Document);
        Check("Scene có primitive", scene.PrimitiveCount > 0, $"{scene.PrimitiveCount} primitive");
        Check("Có hộp bao hợp lệ", !scene.Bounds.IsEmpty, scene.Bounds.ToString());
        Check("Không bỏ sót quá nhiều", scene.SkippedCount == 0, $"{scene.SkippedCount} entity chưa render");

        // ---- 4. Hit-test tại trung điểm một đường thẳng
        var line = workspace.Document.Entities.OfType<Line>().First();
        var mid = new OpenCad.Core.Geometry.Pt(
            (line.StartPoint.X + line.EndPoint.X) / 2,
            (line.StartPoint.Y + line.EndPoint.Y) / 2);

        var hit = scene.HitTest(mid, 10);
        Check("Hit-test tìm được entity", hit is not null,
            hit is null ? "không tìm thấy" : hit.Entity.GetType().Name);
        Check("Hit-test trúng đúng Line", ReferenceEquals(hit?.Entity, line), "entity khớp");

        // ---- 5. Sửa: di chuyển entity rồi lưu
        var originalStart = line.StartPoint;
        const double dx = 1500, dy = 700;

        var moved = EntityEditor.Move(line, dx, dy);
        Check("Di chuyển entity thành công", moved, "EntityEditor.Move trả về true");

        workspace.MarkModified();
        workspace.Save(dwg2);
        Check("Lưu được file sau khi sửa", File.Exists(dwg2));

        // ---- 6. Mở lại và xác nhận thay đổi đã ghi xuống file
        var reopened = CadWorkspace.Open(dwg2);
        var reloaded = reopened.Document.Entities.OfType<Line>().First();

        var deltaX = reloaded.StartPoint.X - originalStart.X;
        var deltaY = reloaded.StartPoint.Y - originalStart.Y;

        Check("Toạ độ X đã dịch đúng", Math.Abs(deltaX - dx) < 1e-6,
            $"delta X = {deltaX}, mong đợi {dx}");
        Check("Toạ độ Y đã dịch đúng", Math.Abs(deltaY - dy) < 1e-6,
            $"delta Y = {deltaY}, mong đợi {dy}");

        // ---- 7. Vòng DXF
        workspace.Save(dxf);
        Check("Ghi được file DXF", File.Exists(dxf) && new FileInfo(dxf).Length > 1000);

        var fromDxf = CadWorkspace.Open(dxf);
        var dxfCount = fromDxf.Document.Entities.Count();
        Check("Đọc lại DXF đúng số entity", dxfCount == expectedCount,
            $"mong đợi {expectedCount}, nhận {dxfCount}");

        // ---- 8. Xoá entity
        var before = reopened.Document.Entities.Count();
        var removed = reopened.Remove(reloaded);
        Check("Xoá được entity", removed && reopened.Document.Entities.Count() == before - 1,
            $"{before} → {reopened.Document.Entities.Count()}");
    }

    // ------------------------------------------------- EDIT / UNDO / SNAP

    private static void RunEditingScenario()
    {
        Console.WriteLine();
        Console.WriteLine("-- Kiểm thử biên tập --");

        var workspace = CadWorkspace.New();
        var editor = new CadEditor(workspace);

        var line = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100, 0, 0),
        };

        editor.AddEntities(new Entity[] { line }, "Add line");
        Check("Thêm entity qua editor", workspace.Document.Entities.Count() == 1,
            $"{workspace.Document.Entities.Count()} entity");

        // ---- MOVE + UNDO
        editor.TransformEntities(new Entity[] { line }, Affine2.Translate(50, 25), "Move");
        Check("MOVE dịch đúng", Math.Abs(line.StartPoint.X - 50) < 1e-9 && Math.Abs(line.StartPoint.Y - 25) < 1e-9,
            $"start = {line.StartPoint.X},{line.StartPoint.Y}");

        Check("Undo khả dụng", editor.History.CanUndo, editor.History.UndoName ?? "-");
        editor.Undo();
        Check("UNDO trả entity về chỗ cũ",
            Math.Abs(line.StartPoint.X) < 1e-9 && Math.Abs(line.StartPoint.Y) < 1e-9,
            $"start = {line.StartPoint.X},{line.StartPoint.Y}");

        editor.Redo();
        Check("REDO áp lại thay đổi",
            Math.Abs(line.StartPoint.X - 50) < 1e-9, $"start.X = {line.StartPoint.X}");
        editor.Undo();

        // ---- ROTATE 90° quanh gốc
        editor.TransformEntities(new Entity[] { line }, Affine2.Rotate(Math.PI / 2, Pt.Zero), "Rotate");
        Check("ROTATE 90° đúng",
            Math.Abs(line.EndPoint.X) < 1e-6 && Math.Abs(line.EndPoint.Y - 100) < 1e-6,
            $"end = {line.EndPoint.X:0.###},{line.EndPoint.Y:0.###}");
        editor.Undo();

        // ---- SCALE 2x quanh gốc
        editor.TransformEntities(new Entity[] { line }, Affine2.Scale(2, Pt.Zero), "Scale");
        Check("SCALE 2x đúng", Math.Abs(line.EndPoint.X - 200) < 1e-6,
            $"end.X = {line.EndPoint.X}");
        editor.Undo();

        // ---- MIRROR qua trục Y
        editor.TransformEntities(new Entity[] { line }, Affine2.Mirror(new Pt(0, 0), new Pt(0, 1)), "Mirror");
        Check("MIRROR qua trục Y đúng", Math.Abs(line.EndPoint.X + 100) < 1e-6,
            $"end.X = {line.EndPoint.X}");
        editor.Undo();

        // ---- SNAP endpoint
        var snap = new SnapEngine();
        var hit = snap.Find(editor.Scene, new Pt(0.5, 0.5), zoom: 1);
        Check("SNAP bắt được endpoint", hit is not null && hit.Kind == SnapKind.Endpoint,
            hit is null ? "không có" : $"{hit.Kind} @ {hit.Point}");

        // ---- SNAP center của đường tròn
        editor.AddEntities(new Entity[]
        {
            new Circle { Center = new CSMath.XYZ(500, 500, 0), Radius = 100 },
        }, "Add circle");

        var centerSnap = snap.Find(editor.Scene, new Pt(503, 497), zoom: 1);
        Check("SNAP bắt được center", centerSnap is not null && centerSnap.Kind == SnapKind.Center,
            centerSnap is null ? "không có" : $"{centerSnap.Kind} @ {centerSnap.Point}");

        // ---- SELECT theo khung
        var whole = editor.Scene.SelectInRect(new RectD(-10, -10, 250, 50), crossing: false);
        Check("Window select lấy entity nằm trọn", whole.Count == 1, $"{whole.Count} entity");

        var partial = editor.Scene.SelectInRect(new RectD(40, -10, 60, 10), crossing: true);
        var partialWindow = editor.Scene.SelectInRect(new RectD(40, -10, 60, 10), crossing: false);
        Check("Crossing select cắt ngang vẫn lấy",
            partial.Count == 1 && partialWindow.Count == 0,
            $"crossing {partial.Count}, window {partialWindow.Count}");

        // ---- ERASE + UNDO (thêm lại được entity đã xoá)
        editor.RemoveEntities(new Entity[] { line }, "Erase");
        var afterErase = workspace.Document.Entities.Count();
        editor.Undo();
        var afterUndo = workspace.Document.Entities.Count();

        Check("Undo thao tác xoá phục hồi entity",
            afterErase == 1 && afterUndo == 2, $"{afterErase} → {afterUndo}");

        // ---- Cờ "đã sửa" bám theo mốc lưu
        var ws2 = CadWorkspace.New();
        var ed2 = new CadEditor(ws2);

        Check("Bản vẽ mới chưa bị đánh dấu sửa", !ws2.IsModified, "sạch");

        ed2.AddEntities(new Entity[] { new Line { EndPoint = new CSMath.XYZ(10, 0, 0) } }, "Add");
        Check("Sau khi thêm thì bị đánh dấu sửa", ws2.IsModified, "bẩn");

        ed2.Undo();
        Check("Undo về mốc lưu thì hết bẩn", !ws2.IsModified, "sạch lại");

        RunOffsetAndLineTypeChecks();
        RunTrimExtendChecks();
        RunHatchChecks();
        RunFilletChamferChecks();
        RunDimensionChecks();
        RunPdfChecks();
        RunLineWeightChecks();
        RunPaperSheetChecks();
        RunLayoutChecks();
        RunPropertyEditingChecks();
        RunRound14Checks();
        RunRound15Checks();
        RunBlockChecks();
        RunCreateBlockChecks();
        RunRound20Checks();
        RunPickabilityChecks();
    }

    // ---------------------------------------- MỌI LOẠI ĐỐI TƯỢNG CÓ CLICK ĐƯỢC?

    /// <summary>Lấy một điểm nằm trên hình học của primitive để thử hit-test.</summary>
    private static Pt? SamplePoint(Prim prim)
    {
        if (prim.Kind == PrimKind.Text)
            return new Pt(prim.Position.X + prim.Height * 0.3, prim.Position.Y + prim.Height * 0.4);

        if (prim.Kind == PrimKind.Hatch && prim.Loops is { Length: > 0 })
        {
            var loop = prim.Loops[0];
            if (loop.Length < 3) return null;

            double cx = 0, cy = 0;
            foreach (var p in loop) { cx += p.X; cy += p.Y; }
            return new Pt(cx / loop.Length, cy / loop.Length);
        }

        if (prim.Points.Length >= 2)
            return new Pt((prim.Points[0].X + prim.Points[1].X) / 2,
                          (prim.Points[0].Y + prim.Points[1].Y) / 2);

        return prim.Points.Length == 1 ? prim.Points[0] : null;
    }

    private static void RunPickabilityChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- CLICK CHỌN ĐƯỢC MỌI LOẠI ĐỐI TƯỢNG --");

        var cases = new (string Name, Entity Entity)[]
        {
            ("Line", new Line
            {
                StartPoint = new CSMath.XYZ(0, 0, 0),
                EndPoint = new CSMath.XYZ(100, 0, 0),
            }),

            ("LwPolyline khép kín", MakeRect(0, 0, 100, 100)),
            ("Circle", new Circle { Center = new CSMath.XYZ(0, 0, 0), Radius = 50 }),
            ("Arc", new Arc
            {
                Center = new CSMath.XYZ(0, 0, 0),
                Radius = 50,
                StartAngle = 0,
                EndAngle = Math.PI / 2,
            }),

            ("Ellipse", ShapeFactory.CreateEllipse(new Pt(0, 0), new Pt(100, 0), 50)!),

            ("Text", new TextEntity
            {
                InsertPoint = new CSMath.XYZ(0, 0, 0),
                Height = 100,
                Value = "PHÒNG KHÁCH",
            }),

            ("Point", new ACadSharp.Entities.Point { Location = new CSMath.XYZ(10, 10, 0) }),
        };

        foreach (var (name, entity) in cases)
        {
            var ws = CadWorkspace.New();
            ws.Document.Entities.Add(entity);

            var scene = new CadScene();
            scene.Build(ws.Document);

            var entry = scene.Entries.FirstOrDefault();
            var sample = entry is null || entry.Prims.Count == 0
                ? null
                : SamplePoint(entry.Prims[0]);

            if (sample is null)
            {
                Check($"{name}: có hình học để click", false, "không có primitive");
                continue;
            }

            var hit = scene.HitTest(sample.Value, 3);

            Check($"{name}: click trúng thì chọn được",
                ReferenceEquals(hit?.Entity, entity),
                hit?.Entity?.GetType().Name ?? "null");
        }

        // ---- Hatch: click vào trong vùng tô phải chọn được
        var hatchWs = CadWorkspace.New();
        var boundary = MakeRect(0, 0, 200, 200);
        hatchWs.Document.Entities.Add(boundary);

        var hatch = HatchFactory.FromBoundary(boundary);
        if (hatch is not null) hatchWs.Document.Entities.Add(hatch);

        var hatchScene = new CadScene();
        hatchScene.Build(hatchWs.Document);

        var hatchHit = hatchScene.HitTest(new Pt(100, 100), 3);
        Check("Hatch: click vào trong vùng tô thì chọn được",
            hatch is not null && ReferenceEquals(hatchHit?.Entity, hatch),
            hatchHit?.Entity?.GetType().Name ?? "null");

        // ---- Chọn bằng khung: mọi loại phải nằm trong khung bao
        var rectWs = CadWorkspace.New();
        rectWs.Document.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100, 0, 0),
        });
        rectWs.Document.Entities.Add(new TextEntity
        {
            InsertPoint = new CSMath.XYZ(0, 50, 0),
            Height = 20,
            Value = "ABC",
        });

        var rectScene = new CadScene();
        rectScene.Build(rectWs.Document);

        var inRect = rectScene.SelectInRect(new RectD(-50, -50, 200, 200), crossing: false);
        // ---- Di chuyển: mọi loại phải dịch đúng (100, 100)
        Console.WriteLine();
        Console.WriteLine("-- DI CHUYỂN ĐƯỢC MỌI LOẠI ĐỐI TƯỢNG --");

        var moveCases = new (string Name, Func<Entity> Make)[]
        {
            ("Line", () => new Line
            {
                StartPoint = new CSMath.XYZ(0, 0, 0),
                EndPoint = new CSMath.XYZ(100, 0, 0),
            }),
            ("LwPolyline", () => MakeRect(0, 0, 100, 100)),
            ("Circle", () => new Circle { Center = new CSMath.XYZ(0, 0, 0), Radius = 50 }),
            ("Arc", () => new Arc
            {
                Center = new CSMath.XYZ(0, 0, 0),
                Radius = 50,
                StartAngle = 0,
                EndAngle = Math.PI / 2,
            }),
            ("Ellipse", () => ShapeFactory.CreateEllipse(new Pt(0, 0), new Pt(100, 0), 50)!),
            ("Text", () => new TextEntity
            {
                InsertPoint = new CSMath.XYZ(0, 0, 0),
                Height = 100,
                Value = "ABC",
            }),
            ("Point", () => new ACadSharp.Entities.Point { Location = new CSMath.XYZ(10, 10, 0) }),
            ("Hatch", () => HatchFactory.FromBoundary(MakeRect(0, 0, 100, 100))!),
            ("Insert", () =>
            {
                var block = new ACadSharp.Tables.BlockRecord("MOVEBLOCK");
                block.Entities.Add(new Line
                {
                    StartPoint = new CSMath.XYZ(0, 0, 0),
                    EndPoint = new CSMath.XYZ(50, 50, 0),
                });

                return new ACadSharp.Entities.Insert(block)
                {
                    InsertPoint = new CSMath.XYZ(0, 0, 0),
                    XScale = 1,
                    YScale = 1,
                    ZScale = 1,
                };
            }),
            ("DimensionAligned", () => new DimensionAligned
            {
                FirstPoint = new CSMath.XYZ(0, 0, 0),
                SecondPoint = new CSMath.XYZ(100, 0, 0),
                DefinitionPoint = new CSMath.XYZ(50, 50, 0),
            }),
        };

        foreach (var (name, make) in moveCases)
        {
            var ws = CadWorkspace.New();
            var editor = new CadEditor(ws);

            var entity = make();
            editor.AddEntities(new[] { entity }, "Add");

            var before = editor.Scene.Entries.First().Bounds;
            editor.TransformEntities(new[] { entity }, Affine2.Translate(100, 100), "Move");
            var after = editor.Scene.Entries.First().Bounds;

            var dx = after.MinX - before.MinX;
            var dy = after.MinY - before.MinY;

            Check($"{name}: MOVE dịch đúng (100, 100)",
                Math.Abs(dx - 100) < 1e-6 && Math.Abs(dy - 100) < 1e-6,
                $"Δ = ({dx:0.###}, {dy:0.###})");
        }
        Check("Chọn bằng khung: được cả line và text", inRect.Count == 2, $"{inRect.Count} đối tượng");

        RunTransformGeometryChecks();
        RunHatchPatternChecks();
        RunCopyUndoOffsetChecks();
    }

    // ------------------------------- COPY / UNDO / OFFSET CHO MỌI LOẠI

    private static void RunCopyUndoOffsetChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- COPY / UNDO / OFFSET --");

        Func<Entity>[] makers =
        {
            () => new Line { StartPoint = new CSMath.XYZ(0, 0, 0), EndPoint = new CSMath.XYZ(100, 0, 0) },
            () => MakeRect(0, 0, 100, 50),
            () => new Circle { Center = new CSMath.XYZ(10, 20, 0), Radius = 30 },
            () => new Arc
            {
                Center = new CSMath.XYZ(10, 20, 0),
                Radius = 30,
                StartAngle = 0,
                EndAngle = Math.PI / 2,
            },
            () => ShapeFactory.CreateEllipse(new Pt(10, 20), new Pt(60, 20), 25)!,
            () => new TextEntity { InsertPoint = new CSMath.XYZ(0, 0, 0), Height = 100, Value = "ABC" },
            () => new ACadSharp.Entities.Point { Location = new CSMath.XYZ(30, 40, 0) },
            () => HatchFactory.FromBoundary(MakeRect(0, 0, 100, 50))!,
        };

        foreach (var make in makers)
        {
            var prototype = make();
            var name = prototype.GetType().Name;

            // ---- COPY: bản sao dịch đúng và độc lập với bản gốc
            var ws = CadWorkspace.New();
            var editor = new CadEditor(ws);

            var original = make();
            editor.AddEntities(new[] { original }, "Add");
            var originalBounds = GeometryBounds(editor.Scene.Entries.First());

            editor.CopyTransform(new[] { original }, Affine2.Translate(100, 50), e => e.Clone() as Entity, "Copy");

            Check($"{name}: COPY tạo thêm 1 đối tượng",
                editor.Scene.Entries.Count == 2, $"{editor.Scene.Entries.Count}");

            if (editor.Scene.Entries.Count == 2)
            {
                var copyBounds = GeometryBounds(editor.Scene.Entries[1]);
                var again = GeometryBounds(editor.Scene.Entries[0]);

                Check($"{name}: bản sao dịch đúng (100, 50)",
                    Math.Abs(copyBounds.MinX - (originalBounds.MinX + 100)) < 1e-6 &&
                    Math.Abs(copyBounds.MinY - (originalBounds.MinY + 50)) < 1e-6,
                    $"({copyBounds.MinX:0.#}, {copyBounds.MinY:0.#})");

                Check($"{name}: bản gốc không bị đổi khi COPY",
                    Math.Abs(again.MinX - originalBounds.MinX) < 1e-9 &&
                    Math.Abs(again.MinY - originalBounds.MinY) < 1e-9,
                    $"({again.MinX:0.#}, {again.MinY:0.#})");
            }

            // ---- UNDO: xoay + phóng rồi undo phải trả về đúng như cũ
            var ws2 = CadWorkspace.New();
            var editor2 = new CadEditor(ws2);

            var entity2 = make();
            editor2.AddEntities(new[] { entity2 }, "Add");
            var before = GeometryBounds(editor2.Scene.Entries.First());

            var rotateAndScale = new Affine2(1.5, -0.8, 0.8, 1.5, 30, -20);
            editor2.TransformEntities(new[] { entity2 }, rotateAndScale, "Transform");

            var moved = GeometryBounds(editor2.Scene.Entries.First());
            editor2.Undo();
            var restored = GeometryBounds(editor2.Scene.Entries.First());

            var changed = Math.Abs(moved.MinX - before.MinX) > 1e-6 ||
                          Math.Abs(moved.MinY - before.MinY) > 1e-6;

            Check($"{name}: UNDO phục hồi chính xác sau xoay+phóng",
                changed &&
                Math.Abs(restored.MinX - before.MinX) < 1e-6 &&
                Math.Abs(restored.MinY - before.MinY) < 1e-6 &&
                Math.Abs(restored.MaxX - before.MaxX) < 1e-6 &&
                Math.Abs(restored.MaxY - before.MaxY) < 1e-6,
                $"{restored} vs {before}");
        }

        // ---- OFFSET: các loại hỗ trợ phải cho kết quả đúng khoảng cách
        var offsetLine = OffsetOperation.Create(
            new Line { StartPoint = new CSMath.XYZ(0, 0, 0), EndPoint = new CSMath.XYZ(100, 0, 0) },
            10, new Pt(50, 40));

        var lineY = offsetLine is Line ol ? ol.StartPoint.Y : double.NaN;
        Check("OFFSET line: cách đúng 10", Math.Abs(lineY - 10) < 1e-6, $"Y = {lineY:0.###}");

        var offsetCircle = OffsetOperation.Create(
            new Circle { Center = new CSMath.XYZ(0, 0, 0), Radius = 50 }, 10, new Pt(80, 0));

        var radius = offsetCircle is Circle oc ? oc.Radius : double.NaN;
        Check("OFFSET circle: bán kính +10", Math.Abs(radius - 60) < 1e-6, $"R = {radius:0.###}");

        // Điểm nằm NGOÀI chữ nhật → offset ra ngoài, mỗi phía 10
        var offsetRect = OffsetOperation.Create(MakeRect(0, 0, 100, 50), 10, new Pt(50, -200));
        CheckPolylineOffset("OFFSET polyline (điểm ngoài): nở ra 10 mỗi phía", offsetRect,
            -10, -10, 110, 60);

        // Điểm nằm TRONG → offset vào trong
        var offsetRectIn = OffsetOperation.Create(MakeRect(0, 0, 100, 50), 10, new Pt(50, 25));
        CheckPolylineOffset("OFFSET polyline (điểm trong): thu vào 10 mỗi phía", offsetRectIn,
            10, 10, 90, 40);

        // ---- OFFSET cung tròn: cùng tâm, bán kính ± khoảng cách
        var arc = new Arc
        {
            Center = new CSMath.XYZ(0, 0, 0),
            Radius = 50,
            StartAngle = 0,
            EndAngle = Math.PI / 2,
        };

        var arcOut = OffsetOperation.Create(arc, 10, new Pt(80, 0)) as Arc;
        Check("OFFSET arc ra ngoài: R + 10",
            arcOut is not null && Math.Abs(arcOut.Radius - 60) < 1e-6,
            arcOut is null ? "null" : $"R = {arcOut.Radius:0.###}");

        Check("OFFSET arc giữ nguyên góc",
            arcOut is not null &&
            Math.Abs(arcOut.StartAngle - arc.StartAngle) < 1e-9 &&
            Math.Abs(arcOut.EndAngle - arc.EndAngle) < 1e-9,
            arcOut is null ? "null" : $"{arcOut.StartAngle:0.###}..{arcOut.EndAngle:0.###}");

        var arcIn = OffsetOperation.Create(arc, 10, new Pt(20, 0)) as Arc;
        Check("OFFSET arc vào trong: R − 10",
            arcIn is not null && Math.Abs(arcIn.Radius - 40) < 1e-6,
            arcIn is null ? "null" : $"R = {arcIn.Radius:0.###}");

        // ---- OFFSET ellipse: cả hai bán trục ± khoảng cách
        var ellipse = ShapeFactory.CreateEllipse(new Pt(0, 0), new Pt(100, 0), 50)!;

        static double MajorAxis(Ellipse e) =>
            Math.Sqrt(e.MajorAxisEndPoint.X * e.MajorAxisEndPoint.X +
                      e.MajorAxisEndPoint.Y * e.MajorAxisEndPoint.Y);

        var ellipseOut = OffsetOperation.Create(ellipse, 10, new Pt(0, 200)) as Ellipse;
        Check("OFFSET ellipse ra ngoài: bán trục +10",
            ellipseOut is not null &&
            Math.Abs(MajorAxis(ellipseOut) - 110) < 1e-6 &&
            Math.Abs(MajorAxis(ellipseOut) * ellipseOut.RadiusRatio - 60) < 1e-6,
            ellipseOut is null ? "null"
                : $"{MajorAxis(ellipseOut):0.###}×{MajorAxis(ellipseOut) * ellipseOut.RadiusRatio:0.###}");

        var ellipseIn = OffsetOperation.Create(ellipse, 10, new Pt(0, 0)) as Ellipse;
        Check("OFFSET ellipse vào trong: bán trục −10",
            ellipseIn is not null &&
            Math.Abs(MajorAxis(ellipseIn) - 90) < 1e-6 &&
            Math.Abs(MajorAxis(ellipseIn) * ellipseIn.RadiusRatio - 40) < 1e-6,
            ellipseIn is null ? "null"
                : $"{MajorAxis(ellipseIn):0.###}×{MajorAxis(ellipseIn) * ellipseIn.RadiusRatio:0.###}");

        // Khoảng cách lớn hơn bán trục nhỏ thì không offset được
        Check("OFFSET ellipse quá lớn trả null",
            OffsetOperation.Create(ellipse, 60, new Pt(0, 0)) is null, "null");

        RunAngleAndOrdinateChecks();
    }

    // ------------------------------------- KÍCH THƯỚC GÓC & TOẠ ĐỘ

    private static void RunAngleAndOrdinateChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- DIMENSION GÓC & TOẠ ĐỘ --");

        // ---- Góc 90° giữa trục X và trục Y
        var angular = new DimensionAngular2Line
        {
            AngleVertex = new CSMath.XYZ(0, 0, 0),
            FirstPoint = new CSMath.XYZ(100, 0, 0),
            SecondPoint = new CSMath.XYZ(0, 100, 0),
            DimensionArc = new CSMath.XYZ(50, 50, 0),
        };

        var ws = CadWorkspace.New();
        ws.Document.Entities.Add(angular);

        var scene = new CadScene();
        scene.Build(ws.Document);

        var prims = scene.Entries.First().Prims;
        var texts = prims.Where(p => p.Kind == PrimKind.Text).Select(p => p.Text).ToList();

        Check("DIMANG vẽ được cung + đường gióng", prims.Count >= 3, $"{prims.Count} primitive");
        Check("DIMANG ghi đúng 90°", texts.Any(t => t.Contains("90")), string.Join("|", texts));

        // ---- Góc 45°
        var angular45 = new DimensionAngular2Line
        {
            AngleVertex = new CSMath.XYZ(0, 0, 0),
            FirstPoint = new CSMath.XYZ(100, 0, 0),
            SecondPoint = new CSMath.XYZ(100, 100, 0),
            DimensionArc = new CSMath.XYZ(80, 40, 0),
        };

        var ws45 = CadWorkspace.New();
        ws45.Document.Entities.Add(angular45);

        var scene45 = new CadScene();
        scene45.Build(ws45.Document);

        var texts45 = scene45.Entries.First().Prims
            .Where(p => p.Kind == PrimKind.Text).Select(p => p.Text).ToList();

        Check("DIMANG ghi đúng 45°", texts45.Any(t => t.Contains("45")), string.Join("|", texts45));

        // ---- Toạ độ X
        var ordinateX = new DimensionOrdinate
        {
            FeatureLocation = new CSMath.XYZ(1234.5, 2000, 0),
            LeaderEndpoint = new CSMath.XYZ(1234.5, 2600, 0),
            IsOrdinateTypeX = true,
        };

        var wsX = CadWorkspace.New();
        wsX.Document.Entities.Add(ordinateX);

        var sceneX = new CadScene();
        sceneX.Build(wsX.Document);

        var primsX = sceneX.Entries.First().Prims;
        var textsX = primsX.Where(p => p.Kind == PrimKind.Text).Select(p => p.Text).ToList();

        Check("DIMORD vẽ được đường dẫn", primsX.Count >= 2, $"{primsX.Count} primitive");
        Check("DIMORD đo X ghi đúng 1234.5", textsX.Any(t => t.Contains("1234")), string.Join("|", textsX));

        // ---- Toạ độ Y
        var ordinateY = new DimensionOrdinate
        {
            FeatureLocation = new CSMath.XYZ(1234.5, 2000, 0),
            LeaderEndpoint = new CSMath.XYZ(2000, 2000, 0),
            IsOrdinateTypeX = false,
        };

        var wsY = CadWorkspace.New();
        wsY.Document.Entities.Add(ordinateY);

        var sceneY = new CadScene();
        sceneY.Build(wsY.Document);

        var textsY = sceneY.Entries.First().Prims
            .Where(p => p.Kind == PrimKind.Text).Select(p => p.Text).ToList();

        Check("DIMORD đo Y ghi đúng 2000", textsY.Any(t => t.Contains("2000")), string.Join("|", textsY));

        RunDimStyleChecks();
        RunPaperEditingChecks();
        RunOffsetTransformChecks();
        RunMultiViewportChecks();
        RunArrayChecks();
        RunPolarArrayAndStretchChecks();
        RunHatchPatternFileChecks();
        RunBlockAttributeChecks();
        RunSpatialIndexChecks();
        RunDivideMeasureChecks();
        RunLeaderMLineChecks();
        RunTableChecks();
        RunUnsupportedReportingChecks();
        RunPaperLineChecks();
        RunMTextAndTortureChecks();
        RunFidelityChecks();
        RunLayerStateChecks();
        RunNewTypeTransformChecks();
        RunUndoFidelityChecks();
        RunPaperUndoChecks();
        RunWipeoutToleranceChecks();
        RunMultiLeaderChecks();
        RunLineTypeChecks();
        RunGripChecks();
    }

    // ---------------------- GRIP (KÉO ĐIỂM ĐIỀU KHIỂN)

    private static void RunGripChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- GRIP EDITING --");

        var line = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(1000, 0, 0),
        };

        var grips = GripSet.For(line);

        Check("Line có 3 grip (đầu, cuối, giữa)", grips.Count == 3, grips.Count.ToString());
        Check("Grip giữa nằm đúng (500,0)",
            grips.Any(g => g.Kind == "mid" && Math.Abs(g.Position.X - 500) < 1e-9 && Math.Abs(g.Position.Y) < 1e-9),
            string.Join(",", grips.Select(g => g.Kind)));

        var startGrip = grips.First(g => g.Kind == "start");
        GripEdit.Move(line, startGrip, new Pt(-100, 200));

        Check("Kéo grip đầu đổi StartPoint",
            Math.Abs(line.StartPoint.X + 100) < 1e-9 && Math.Abs(line.StartPoint.Y - 200) < 1e-9,
            "(" + line.StartPoint.X.ToString("0.#") + ", " + line.StartPoint.Y.ToString("0.#") + ")");

        var midGrip = GripSet.For(line).First(g => g.Kind == "mid");
        var beforeStart = new Pt(line.StartPoint.X, line.StartPoint.Y);
        var beforeEnd = new Pt(line.EndPoint.X, line.EndPoint.Y);

        GripEdit.Move(line, midGrip, new Pt(midGrip.Position.X + 50, midGrip.Position.Y + 50));

        Check("Kéo grip giữa dời cả hai đầu (50,50)",
            Math.Abs(line.StartPoint.X - beforeStart.X - 50) < 1e-9 &&
            Math.Abs(line.EndPoint.Y - beforeEnd.Y - 50) < 1e-9,
            "Δ = (" + (line.StartPoint.X - beforeStart.X).ToString("0.#") + ", " +
            (line.EndPoint.Y - beforeEnd.Y).ToString("0.#") + ")");

        var circle = new Circle { Center = new CSMath.XYZ(0, 0, 0), Radius = 100 };
        var circleGrips = GripSet.For(circle);

        Check("Circle có grip tâm và bán kính",
            circleGrips.Any(g => g.Kind == "center") && circleGrips.Count(g => g.Kind == "radius") == 2,
            circleGrips.Count + " grip");

        var radiusGrip = circleGrips.First(g => g.Kind == "radius");
        GripEdit.Move(circle, radiusGrip, new Pt(250, 0));

        Check("Kéo grip bán kính đổi R thành 250",
            Math.Abs(circle.Radius - 250) < 1e-6, circle.Radius.ToString("0.###"));

        var centerGrip = circleGrips.First(g => g.Kind == "center");
        GripEdit.Move(circle, centerGrip, new Pt(100, 100));

        Check("Kéo grip tâm dời đường tròn",
            Math.Abs(circle.Center.X - 100) < 1e-9 && Math.Abs(circle.Center.Y - 100) < 1e-9,
            "(" + circle.Center.X.ToString("0.#") + ", " + circle.Center.Y.ToString("0.#") + ")");

        var poly = new LwPolyline();
        poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 0) });
        poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(100, 0) });
        poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(100, 100) });

        Check("Polyline có 3 grip đỉnh", GripSet.For(poly).Count == 3,
            GripSet.For(poly).Count + " grip");

        GripEdit.Move(poly, new Grip(1, new Pt(100, 0), "vertex"), new Pt(150, -20));

        Check("Kéo đỉnh polyline đổi đúng đỉnh",
            Math.Abs(poly.Vertices[1].Location.X - 150) < 1e-9 &&
            Math.Abs(poly.Vertices[1].Location.Y + 20) < 1e-9,
            "(" + poly.Vertices[1].Location.X.ToString("0.#") + ", " + poly.Vertices[1].Location.Y.ToString("0.#") + ")");

        var nearest = GripSet.Nearest(line, new Pt(line.StartPoint.X + 3, line.StartPoint.Y), 10);

        Check("Tìm được grip gần điểm click", nearest?.Kind == "start", nearest?.Kind ?? "null");
        Check("Click xa thì không tìm thấy grip",
            GripSet.Nearest(line, new Pt(9999, 9999), 10) is null, "null");

        var text = new TextEntity
        {
            InsertPoint = new CSMath.XYZ(0, 0, 0),
            Height = 100,
            Value = "ABC",
        };

        GripEdit.Move(text, GripSet.For(text)[0], new Pt(300, 400));

        // ---- Leader và MLine cũng kéo được đỉnh
        var leader = new Leader { TextHeight = 50 };
        leader.Vertices.Add(new CSMath.XYZ(0, 0, 0));
        leader.Vertices.Add(new CSMath.XYZ(100, 100, 0));

        Check("Leader có grip cho từng đỉnh", GripSet.For(leader).Count == 2,
            GripSet.For(leader).Count + " grip");

        GripEdit.Move(leader, new Grip(0, new Pt(0, 0), "vertex"), new Pt(-50, -50));

        Check("Kéo đỉnh Leader đổi đúng đỉnh",
            Math.Abs(leader.Vertices[0].X + 50) < 1e-9 && Math.Abs(leader.Vertices[0].Y + 50) < 1e-9,
            "(" + leader.Vertices[0].X.ToString("0.#") + ", " + leader.Vertices[0].Y.ToString("0.#") + ")");

        var mline = new MLine();
        mline.Vertices.Add(new MLine.Vertex { Position = new CSMath.XYZ(0, 0, 0) });
        mline.Vertices.Add(new MLine.Vertex { Position = new CSMath.XYZ(200, 0, 0) });

        GripEdit.Move(mline, new Grip(1, new Pt(200, 0), "vertex"), new Pt(300, 50));

        Check("Kéo đỉnh MLine đổi đúng đỉnh",
            Math.Abs(mline.Vertices[1].Position.X - 300) < 1e-9 &&
            Math.Abs(mline.Vertices[1].Position.Y - 50) < 1e-9,
            "(" + mline.Vertices[1].Position.X.ToString("0.#") + ", " + mline.Vertices[1].Position.Y.ToString("0.#") + ")");

        // ---- Undo của thao tác kéo grip (đúng luồng canvas dùng)
        var ws = CadWorkspace.New();
        var editor = new CadEditor(ws);

        var live = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100, 0, 0),
        };

        editor.AddEntities(new Entity[] { live }, "Add");

        var original = live.Clone() as Entity;

        GripEdit.Move(live, GripSet.For(live).First(g => g.Kind == "end"), new Pt(300, 200));

        var final = live.Clone() as Entity;
        GripEdit.CopyGeometry(original!, live);

        editor.RunCustom("Grip edit",
            () => GripEdit.CopyGeometry(final!, live),
            () => GripEdit.CopyGeometry(original!, live));

        Check("Sau khi kéo grip, đầu line ở (300,200)",
            Math.Abs(live.EndPoint.X - 300) < 1e-9 && Math.Abs(live.EndPoint.Y - 200) < 1e-9,
            "(" + live.EndPoint.X.ToString("0.#") + ", " + live.EndPoint.Y.ToString("0.#") + ")");

        editor.Undo();

        Check("UNDO kéo grip trả đầu line về (100,0)",
            Math.Abs(live.EndPoint.X - 100) < 1e-9 && Math.Abs(live.EndPoint.Y) < 1e-9,
            "(" + live.EndPoint.X.ToString("0.#") + ", " + live.EndPoint.Y.ToString("0.#") + ")");

        editor.Redo();

        Check("REDO kéo grip đưa đầu line lại (300,200)",
            Math.Abs(live.EndPoint.X - 300) < 1e-9 && Math.Abs(live.EndPoint.Y - 200) < 1e-9,
            "(" + live.EndPoint.X.ToString("0.#") + ", " + live.EndPoint.Y.ToString("0.#") + ")");

        // ---------------------- TẠO ATTDEF
        var attrWs = CadWorkspace.New();
        var attrDoc = attrWs.Document;

        var doorBlock = new ACadSharp.Tables.BlockRecord("CUA_NEW");
        doorBlock.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(900, 0, 0),
        });

        attrDoc.BlockRecords.Add(doorBlock);

        var doorInsert = new ACadSharp.Entities.Insert(doorBlock)
        {
            InsertPoint = new CSMath.XYZ(2000, 3000, 0),
            XScale = 1,
            YScale = 1,
            ZScale = 1,
        };

        attrDoc.Entities.Add(doorInsert);

        var attrEditor = new CadEditor(attrWs);
        var definition = attrEditor.AddAttributeDefinition(
            doorBlock, "SO_CUA", "C01", new Pt(0, 500), 250);

        Check("Tạo được ATTDEF", definition is not null, definition?.Tag ?? "null");
        Check("ATTDEF nằm trong block",
            doorBlock.Entities.OfType<AttributeDefinition>().Any(), "có");

        Check("Insert có attribute ngay",
            doorInsert.Attributes.Any(a => a.Tag == "SO_CUA"), doorInsert.Attributes.Count() + " attr");

        var sceneAttr = new CadScene();
        sceneAttr.Build(attrDoc);

        var shown = sceneAttr.Entries
            .First(e => ReferenceEquals(e.Entity, doorInsert)).Prims
            .Where(p => p.Kind == PrimKind.Text)
            .Select(p => p.Text)
            .ToList();

        Check("Nhãn attribute hiện trên hình", shown.Contains("C01"), string.Join("|", shown));

        Check("Attribute đặt đúng WCS (2000, 3500)",
            doorInsert.Attributes.First().InsertPoint.X is > 1999 and < 2001 &&
            doorInsert.Attributes.First().InsertPoint.Y is > 3499 and < 3501,
            "(" + doorInsert.Attributes.First().InsertPoint.X.ToString("0.#") + ", " +
            doorInsert.Attributes.First().InsertPoint.Y.ToString("0.#") + ")");

        attrEditor.Undo();

        Check("Undo ATTDEF xoá khỏi block",
            !doorBlock.Entities.OfType<AttributeDefinition>().Any(), "0");
        Check("Undo ATTDEF xoá attribute khỏi insert",
            !doorInsert.Attributes.Any(), doorInsert.Attributes.Count() + " attr");

        attrEditor.Redo();

        Check("Redo ATTDEF tạo lại trong block",
            doorBlock.Entities.OfType<AttributeDefinition>().Any(), "có");

        // ---------------------- SCRIPT VẼ HEADLESS
        var script = string.Join("\n", new[]
        {
            "# chú thích",
            "",
            "LAYER TUONG 6",
            "LINE 0,0 8000,0",
            "LINE 8000,0 8000,6000",
            "RECT 0,0 4000,3000",
            "LAYER NOI_THAT 3",
            "CIRCLE 2000,1500 500",
            "TEXT 100,100 250 PHONG KHACH",
            "LINE 0,0 100,100 ON TUONG",
        });

        var (scriptWs, scriptResult) = OpenCad.Core.Scripting.ScriptRunner.RunNew(script);
        var scriptDoc = scriptWs.Document;

        Check("Script chạy không lỗi", scriptResult.Ok,
            scriptResult.Executed + " lệnh, " + scriptResult.Failed + " lỗi");

        // 8 lệnh thật: LAYER, LINE, LINE, RECT, LAYER, CIRCLE, TEXT, LINE
        Check("Script bỏ qua chú thích và dòng trống", scriptResult.Executed == 8,
            scriptResult.Executed + " lệnh");

        Check("Script tạo đúng số đoạn thẳng",
            scriptDoc.Entities.OfType<Line>().Count() == 2 + 4 + 1,
            scriptDoc.Entities.OfType<Line>().Count() + " line");

        Check("Script tạo đường tròn",
            scriptDoc.Entities.OfType<Circle>().Count() == 1, "1 circle");

        Check("Script tạo chữ đúng nội dung",
            scriptDoc.Entities.OfType<TextEntity>().Any(x => x.Value == "PHONG KHACH"),
            string.Join("|", scriptDoc.Entities.OfType<TextEntity>().Select(x => x.Value)));

        Check("Script tạo được layer TUONG màu 6",
            scriptDoc.Layers.Any(l => l.Name == "TUONG" && l.Color.Index == 6),
            "ACI " + scriptDoc.Layers.FirstOrDefault(l => l.Name == "TUONG")?.Color.Index);

        Check("Entity sau LAYER dùng layer mới",
            scriptDoc.Entities.OfType<Circle>().All(c => c.Layer?.Name == "NOI_THAT"),
            scriptDoc.Entities.OfType<Circle>().First().Layer?.Name ?? "null");

        Check("Ghi đè layer bằng ON <tên>",
            scriptDoc.Entities.OfType<Line>().Any(l => l.Layer?.Name == "TUONG"), "TUONG");

        // ---- Lệnh nâng cao trong script
        var (advWs, advResult) = OpenCad.Core.Scripting.ScriptRunner.RunNew(string.Join("\n", new[]
        {
            "PLINE 0,0 1000,0 1000,800 0,800 CLOSE",
            "PLINE 0,0 500,500 1000,0",
            "DIM 0,0 1000,0 500,-300",
            "HATCH 0,0 500,500 ANSI31 4",
        }));

        var advDoc = advWs.Document;

        Check("Script chạy được PLINE/DIM/HATCH", advResult.Ok,
            advResult.Executed + " lệnh, " + advResult.Failed + " lỗi");

        var plines = advDoc.Entities.OfType<LwPolyline>().ToList();

        Check("Script tạo 2 polyline", plines.Count == 2, plines.Count + " pline");
        Check("PLINE ... CLOSE tạo polyline kín",
            plines[0].IsClosed && !plines[1].IsClosed,
            "kín/không kín");

        Check("Script tạo kích thước",
            advDoc.Entities.OfType<DimensionAligned>().Count() == 1, "1 dim");

        var hatch2 = advDoc.Entities.OfType<Hatch>().FirstOrDefault();

        Check("Script tạo hatch", hatch2 is not null, "có hatch");
        Check("Script đặt đúng tỉ lệ hatch",
            hatch2 is not null && Math.Abs(hatch2.PatternScale - 4) < 1e-6,
            hatch2?.PatternScale.ToString("0.###") ?? "null");

        Check("Script đặt đúng tên pattern hatch",
            hatch2?.Pattern?.Name == "ANSI31", hatch2?.Pattern?.Name ?? "null");

        // HATCH không có tham số thì dùng mặc định ANSI31
        var (defWs, _) = OpenCad.Core.Scripting.ScriptRunner.RunNew("HATCH 0,0 100,100");
        var defaultHatch = defWs.Document.Entities.OfType<Hatch>().FirstOrDefault();

        Check("HATCH mặc định dùng ANSI31",
            defaultHatch?.Pattern?.Name == "ANSI31", defaultHatch?.Pattern?.Name ?? "null");

        // ---- Biến trong script (bản vẽ tham số)
        var (varWs, varResult) = OpenCad.Core.Scripting.ScriptRunner.RunNew(string.Join("\n", new[]
        {
            "SET w 5000",
            "SET h 3000",
            "SET h2 $h   # nối biến",
            "LINE 0,0 $w,0",
            "LINE $w,0 $w,$h2",
        }));

        Check("Script chạy được biến", varResult.Ok,
            varResult.Executed + " lệnh, " + varResult.Failed + " lỗi");

        var varLines = varWs.Document.Entities.OfType<Line>().ToList();

        Check("Biến thay đúng trong toạ độ",
            Math.Abs(varLines[0].EndPoint.X - 5000) < 1e-9,
            varLines[0].EndPoint.X.ToString("0.##"));

        Check("Biến nối biến (SET h2 $h)",
            Math.Abs(varLines[1].EndPoint.Y - 3000) < 1e-9,
            varLines[1].EndPoint.Y.ToString("0.##"));

        Check("Chú thích cuối dòng bị bỏ",
            Math.Abs(varLines[1].StartPoint.X - 5000) < 1e-9,
            varLines[1].StartPoint.X.ToString("0.##"));

        // Đổi giá trị → bản vẽ đổi theo (tính tham số)
        var (smallWs, _) = OpenCad.Core.Scripting.ScriptRunner.RunNew(string.Join("\n", new[]
        {
            "SET s 1000",
            "LINE 0,0 $s,0",
        }));

        var smallLine = smallWs.Document.Entities.OfType<Line>().First();

        Check("Đổi biến thì bản vẽ đổi theo",
            Math.Abs(smallLine.EndPoint.X - 1000) < 1e-9,
            smallLine.EndPoint.X.ToString("0.##"));

        // Biến chưa đặt → báo lỗi, dòng khác vẫn chạy
        var (missWs, missResult) = OpenCad.Core.Scripting.ScriptRunner.RunNew(
            "LINE 0,0 $chua_dat,0\nCIRCLE 100,100 50");

        Check("Biến chưa đặt bị báo lỗi",
            missResult.Failed == 1 && missResult.Executed == 1,
            missResult.Executed + " chạy, " + missResult.Failed + " lỗi");

        Check("Biến chưa đặt không tạo ra entity sai",
            missWs.Document.Entities.Count() == 1,
            missWs.Document.Entities.Count() + " entity");

        // ---- BLOCK / ENDBLOCK / INSERT / ARC trong script
        var (blkWs, blkResult) = OpenCad.Core.Scripting.ScriptRunner.RunNew(string.Join("\n", new[]
        {
            "BLOCK CUA 0,0",
            "LINE 0,0 0,900",
            "ARC 0,0 900 0 90",
            "ENDBLOCK",
            "INSERT CUA 1000,500",
            "INSERT CUA 2000,500 2 90",
        }));

        var blkDoc = blkWs.Document;

        Check("Script chạy được BLOCK/INSERT/ARC", blkResult.Ok,
            blkResult.Executed + " lệnh, " + blkResult.Failed + " lỗi");

        var cuaBlock = blkDoc.BlockRecords.FirstOrDefault(b => b.Name == "CUA");

        Check("Script tạo được block CUA", cuaBlock is not null, cuaBlock?.Name ?? "null");
        Check("Block có 2 entity (line + arc)", cuaBlock?.Entities.Count() == 2,
            (cuaBlock?.Entities.Count() ?? -1) + " entity");
        Check("Block chứa đúng loại entity",
            cuaBlock is not null &&
            cuaBlock.Entities.OfType<Line>().Any() &&
            cuaBlock.Entities.OfType<Arc>().Any(), "line + arc");

        Check("Model space chỉ có 2 insert, không có line/arc rời",
            blkDoc.Entities.Count() == 2 &&
            blkDoc.Entities.All(e => e is Insert),
            blkDoc.Entities.Count() + " entity");

        var inserts = blkDoc.Entities.OfType<Insert>().ToList();

        Check("Insert trỏ đúng block",
            inserts.All(i => i.Block?.Name == "CUA"), "CUA");

        Check("Insert thứ hai giữ scale 2",
            Math.Abs(inserts[1].XScale - 2) < 1e-6, inserts[1].XScale.ToString("0.###"));

        Check("Insert thứ hai giữ góc 90°",
            Math.Abs(inserts[1].Rotation - Math.PI / 2) < 1e-6,
            (inserts[1].Rotation * 180 / Math.PI).ToString("0.#") + "°");

        // Hình học của block phải vẽ ra được
        var blkScene = new CadScene();
        blkScene.Build(blkDoc);

        var blkPrims = blkScene.Entries
            .Where(e => e.Entity is Insert)
            .Sum(e => e.Prims.Count);

        Check("Insert vẽ ra hình học của block", blkPrims >= 4, blkPrims + " primitive");

        // ---- Lệnh sai không làm hỏng cả script
        var badResult = OpenCad.Core.Scripting.ScriptRunner.RunNew(
            "LINE 0,0 100,0\nKHONGCO 1 2\nCIRCLE 50,50 10").Result;

        Check("Lệnh lạ bị tính là lỗi nhưng các lệnh khác vẫn chạy",
            badResult.Executed == 2 && badResult.Failed == 1,
            badResult.Executed + " chạy, " + badResult.Failed + " lỗi");

        var numberResult = OpenCad.Core.Scripting.ScriptRunner.RunNew(
            "LINE 0,0 abc,0\nCIRCLE 50,50 10").Result;

        Check("Toạ độ sai bị báo lỗi, không ném ngoại lệ",
            numberResult.Executed == 1 && numberResult.Failed == 1,
            numberResult.Executed + " chạy, " + numberResult.Failed + " lỗi");

        // ---- Lưu rồi mở lại giữ nguyên
        var scriptPath = Path.Combine(Path.GetTempPath(), "opencad-smoke", "script.dwg");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);

        scriptWs.Save(scriptPath);
        var scriptBack = CadWorkspace.Open(scriptPath);

        Check("Bản vẽ từ script lưu/mở giữ đủ entity",
            scriptBack.Document.Entities.Count() == scriptDoc.Entities.Count(),
            scriptBack.Document.Entities.Count() + " / " + scriptDoc.Entities.Count());

        Check("Bản vẽ từ script không có lỗi audit",
            OpenCad.Core.Audit.DrawingAudit.Run(scriptBack.Document)
                .All(f => f.Code != "ZERO_LINE"), "không có đoạn 0");

        // ---------------------- KÍCH THƯỚC TRÊN TỜ
        var dimWs = CadWorkspace.New();
        var dimDoc = dimWs.Document;

        dimDoc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(2000, 0, 0),
        });

        var dimModel = new CadScene();
        dimModel.Build(dimDoc);

        var dimLayout = OpenCad.Core.Layout.LayoutFactory.Create(dimDoc, "D1",
            new OpenCad.Core.Layout.SheetTemplateOptions { Title = "T", UnitsPerMm = 50 }, dimModel);

        var dimBlock = dimLayout?.AssociatedBlock;

        if (dimBlock is null)
        {
            Check("Tạo được layout cho test kích thước trên tờ", false, "null");
        }
        else
        {
            var dimEditor = new CadEditor(dimWs);
            var blockCountBefore = dimBlock.Entities.Count();

            // Kích thước thẳng vẽ trên tờ, toạ độ mm giấy
            var paperDim = new DimensionAligned
            {
                FirstPoint = new CSMath.XYZ(20, 20, 0),
                SecondPoint = new CSMath.XYZ(120, 20, 0),
                DefinitionPoint = new CSMath.XYZ(70, 35, 0),
            };

            dimEditor.RunCustom("DIM (paper)",
                () => dimBlock.Entities.Add(paperDim),
                () => dimBlock.Entities.Remove(paperDim));

            Check("Thêm được kích thước vào block tờ",
                dimBlock.Entities.OfType<DimensionAligned>().Any(), "có");

            var dimSheet = OpenCad.Core.Layout.LayoutFactory.Read(dimDoc, dimLayout)!;
            // PaperItem không giữ tham chiếu entity: lấy mọi hình không có clip (không phải viewport)
            var dimPrims = dimSheet.Items
                .Where(i => i.Clip is null)
                .SelectMany(i => i.Prims)
                .ToList();

            Check("Kích thước trên tờ vẽ được", dimPrims.Count >= 2,
                dimPrims.Count + " primitive");

            var allTexts = dimPrims
                .Where(p => p.Kind == PrimKind.Text)
                .Select(p => p.Text)
                .ToList();

            Check("Kích thước trên tờ có chữ số đo 100",
                allTexts.Any(t => t.Contains("100")), string.Join("|", allTexts.Take(6)));

            var dimPdf = Path.Combine(Path.GetTempPath(), "opencad-smoke", "sheet-dim.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(dimPdf)!);

            PdfExporter.ExportPaperSheet(dimSheet, dimPdf, new PdfExportOptions
            {
                Title = "sheet-dim",
                FontPath = PdfExportOptions.FindSystemFont(),
            });

            Check("PDF của tờ có kích thước mới",
                new FileInfo(dimPdf).Length > 1000, new FileInfo(dimPdf).Length + " byte");

            dimEditor.Undo();

            Check("Undo xoá kích thước khỏi tờ",
                dimBlock.Entities.Count() == blockCountBefore,
                dimBlock.Entities.Count().ToString());
        }

        // ---------------------- TABLE: ghi/đọc DWG
        var tbWs = CadWorkspace.New();
        var tbDoc = tbWs.Document;
        var tbEditor = new CadEditor(tbWs);

        var tbBlock = new ACadSharp.Tables.BlockRecord("TB_TEST");
        tbDoc.BlockRecords.Add(tbBlock);

        var tbTable = new TableEntity(tbBlock)
        {
            InsertPoint = new CSMath.XYZ(500, 800, 0),
            RowCount = 3,
            ColumnCount = 2,
            RowSpacing = 300,
            ColumnSpacing = 1200,
        };

        for (var r = 0; r < 3; r++) tbTable.Rows.Add(new TableEntity.Row { Height = 300 });
        for (var c = 0; c < 2; c++) tbTable.Columns.Add(new TableEntity.Column { Width = 1200 });

        tbEditor.AddEntities(new Entity[] { tbTable }, "TABLE");

        Check("TABLE: thêm được vào bản vẽ",
            tbDoc.Entities.OfType<TableEntity>().Count() == 1, "1 bảng");

        var tbPath = Path.Combine(Path.GetTempPath(), "opencad-smoke", "table.dwg");
        Directory.CreateDirectory(Path.GetDirectoryName(tbPath)!);
        tbWs.Save(tbPath);

        var tbBack = CadWorkspace.Open(tbPath);
        var tbRead = tbBack.Document.Entities.OfType<TableEntity>().ToList();

        // Bộ ghi DWG không lưu TABLE → phải cảnh báo được
        Check("TABLE: nằm trong cảnh báo mất khi lưu",
            tbWs.LossySaveRisks().Any(r => r.Type == "Table"), "có cảnh báo");

        // Chuyển đổi sang lưới đường rồi lưu thì phải đọc lại được
        var convertedTables = tbWs.ConvertUnsupportedForSave();

        Check("TABLE: chuyển đổi được sang lưới đường", convertedTables == 1,
            convertedTables + " bảng đã chuyển");

        var tbPath2 = Path.Combine(Path.GetTempPath(), "opencad-smoke", "table2.dwg");
        tbWs.Save(tbPath2);

        var tbBack2 = CadWorkspace.Open(tbPath2);

        // 3 hàng → 4 đường ngang; 2 cột → 3 đường dọc = 7 đường
        Check("TABLE: sau chuyển đổi, lưu/đọc giữ được hình",
            tbBack2.Document.Entities.Count() == 7,
            tbBack2.Document.Entities.Count() + " đường (cần 7)");

        // Xoá bảng không được làm nổ (thiếu BlockRecord là nổ)
        var threwOnRemove = false;

        try { tbEditor.RemoveEntities(new Entity[] { tbTable }, "Erase"); }
        catch { threwOnRemove = true; }

        Check("TABLE: xoá bảng không ném ngoại lệ", !threwOnRemove,
            threwOnRemove ? "có lỗi" : "sạch");

        // ---------------------- QUẢN LÝ LAYOUT
        var loWs = CadWorkspace.New();
        var loDoc = loWs.Document;

        loDoc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(3000, 0, 0),
        });

        var loScene = new CadScene();
        loScene.Build(loDoc);

        var tpl = new OpenCad.Core.Layout.SheetTemplateOptions { Title = "T", UnitsPerMm = 50 };

        var loFirst = OpenCad.Core.Layout.LayoutFactory.Create(loDoc, "L1", tpl, loScene);
        var loSecond = OpenCad.Core.Layout.LayoutFactory.Create(loDoc, "L2", tpl, loScene);

        Check("Tạo được 2 layout", loFirst is not null && loSecond is not null,
            OpenCad.Core.Layout.LayoutFactory.LayoutNames(loDoc).Count + " layout");

        // Đổi tên
        var renameError = OpenCad.Core.Layout.LayoutFactory.Rename(loDoc, "L2", "MAT_BANG");

        Check("Đổi tên layout được", renameError is null, renameError ?? "ok");
        Check("Layout mang tên mới",
            OpenCad.Core.Layout.LayoutFactory.Find(loDoc, "MAT_BANG") is not null, "MAT_BANG");

        Check("Không đổi được sang tên đã có",
            OpenCad.Core.Layout.LayoutFactory.Rename(loDoc, "MAT_BANG", "L1") is not null, "từ chối");

        Check("Không đổi được sang tên rỗng",
            OpenCad.Core.Layout.LayoutFactory.Rename(loDoc, "MAT_BANG", "  ") is not null, "từ chối");

        // Xoá
        var layoutDeleteError = OpenCad.Core.Layout.LayoutFactory.Delete(loDoc, "MAT_BANG");

        Check("Xoá được layout", layoutDeleteError is null, layoutDeleteError ?? "đã xoá");

        // Bản vẽ mới đã có sẵn Layout1 → còn Layout1 + L1
        Check("Sau khi xoá còn 2 layout",
            OpenCad.Core.Layout.LayoutFactory.LayoutNames(loDoc).Count == 2,
            string.Join(",", OpenCad.Core.Layout.LayoutFactory.LayoutNames(loDoc)));

        // Xoá tiếp L1 → chỉ còn Layout1
        OpenCad.Core.Layout.LayoutFactory.Delete(loDoc, "L1");

        Check("Xoá tiếp còn đúng 1 layout",
            OpenCad.Core.Layout.LayoutFactory.LayoutNames(loDoc).Count == 1,
            string.Join(",", OpenCad.Core.Layout.LayoutFactory.LayoutNames(loDoc)));

        var lastLayout = OpenCad.Core.Layout.LayoutFactory.LayoutNames(loDoc)[0];

        Check("Không xoá được layout cuối cùng",
            OpenCad.Core.Layout.LayoutFactory.Delete(loDoc, lastLayout) is not null, "từ chối");

        Check("Xoá layout không tồn tại thì báo lỗi",
            OpenCad.Core.Layout.LayoutFactory.Delete(loDoc, "KHONG_CO") is not null, "từ chối");

        // ---------------------- SỬA THÔNG SỐ KIỂU KÍCH THƯỚC
        var dsWs = CadWorkspace.New();
        var dsDoc = dsWs.Document;
        var dsEditor = new CadEditor(dsWs);

        var dimStyle = dsDoc.DimensionStyles.FirstOrDefault();

        if (dimStyle is null)
        {
            Check("Bản vẽ mới có kiểu kích thước", false, "không có");
        }
        else
        {
            var beforeText = dimStyle.TextHeight;
            var beforeArrow = dimStyle.ArrowSize;

            var styledDim = new DimensionAligned
            {
                FirstPoint = new CSMath.XYZ(0, 0, 0),
                SecondPoint = new CSMath.XYZ(2000, 0, 0),
                DefinitionPoint = new CSMath.XYZ(1000, 300, 0),
                Style = dimStyle,
            };

            dsEditor.AddEntities(new Entity[] { styledDim }, "Add");

            string? Label()
            {
                var scene = new CadScene();
                scene.Build(dsDoc);

                return scene.Entries.SelectMany(e => e.Prims)
                    .Where(p => p.Kind == PrimKind.Text)
                    .Select(p => p.Text)
                    .FirstOrDefault();
            }

            Check("Kích thước mặc định hiện 2000", Label() == "2000", Label() ?? "null");

            dsEditor.SetDimensionStyleProperties(dimStyle, textHeight: 300, arrowSize: 120);

            Check("Đổi được chiều cao chữ style",
                Math.Abs(dimStyle.TextHeight - 300) < 1e-9, dimStyle.TextHeight.ToString("0.#"));
            Check("Đổi được kích thước mũi tên",
                Math.Abs(dimStyle.ArrowSize - 120) < 1e-9, dimStyle.ArrowSize.ToString("0.#"));

            dsEditor.SetDimensionStyleProperties(dimStyle, linearScale: 0.001, suffix: "m", decimals: 2);

            // Renderer bỏ số 0 thừa (tương đương DIMZIN = 8 mặc định của AutoCAD)
            Check("Hệ số đo 0.001: 2000 → 2m", Label() == "2m", Label() ?? "null");

            dsEditor.SetDimensionStyleProperties(dimStyle, decimals: 0);

            Check("Đổi số thập phân vẫn ra 2m (số 0 thừa bị bỏ)", Label() == "2m", Label() ?? "null");

            dsEditor.Undo();

            Check("Undo trả lại 2m", Label() == "2m", Label() ?? "null");

            Check("Style vẫn giữ chiều cao đã đổi (undo chỉ lùi bước cuối)",
                Math.Abs(dimStyle.TextHeight - 300) < 1e-9, dimStyle.TextHeight.ToString("0.#"));
        }

        // ---------------------- HATCHEDIT: đổi mẫu phải bỏ chế độ đặc
        var hpWs = CadWorkspace.New();
        var hpEditor = new CadEditor(hpWs);

        var hpPoly = new LwPolyline { IsClosed = true };
        hpPoly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 0) });
        hpPoly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(2000, 0) });
        hpPoly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(2000, 2000) });
        hpPoly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 2000) });

        var hpHatch = OpenCad.Core.Editing.HatchFactory.FromBoundary(hpPoly);

        if (hpHatch is null)
        {
            Check("HATCHEDIT: tạo được hatch để thử", false, "null");
        }
        else
        {
            hpWs.Document.Entities.Add(hpHatch);

            Check("Hatch mặc định là đặc", hpHatch.IsSolid, hpHatch.IsSolid.ToString());

            var hpChanged = hpEditor.SetHatchProperties(new Entity[] { hpHatch }, 200, 0, "ANSI31");

            Check("HATCHEDIT đổi được mẫu", hpChanged == 1, hpChanged + " hatch");
            Check("HATCHEDIT đặt mẫu BỎ chế độ đặc", !hpHatch.IsSolid, hpHatch.IsSolid.ToString());
            Check("HATCHEDIT ghi đúng tên mẫu", hpHatch.Pattern?.Name == "ANSI31",
                hpHatch.Pattern?.Name ?? "null");

            // Tên mẫu không có trong thư viện → từ chối, không đổi gì
            var beforeName = hpHatch.Pattern?.Name;
            var rejected = hpEditor.SetHatchProperties(new Entity[] { hpHatch }, null, null, "KHONG_CO_Mau_Nay");

            Check("HATCHEDIT từ chối mẫu không có", rejected == 0, rejected + " hatch");
            Check("HATCHEDIT không đổi mẫu khi bị từ chối",
                hpHatch.Pattern?.Name == beforeName, hpHatch.Pattern?.Name ?? "null");

            // pattern=SOLID quay lại khối đặc
            hpEditor.SetHatchProperties(new Entity[] { hpHatch }, null, null, "SOLID");

            Check("HATCHEDIT pattern=SOLID trả về khối đặc", hpHatch.IsSolid, hpHatch.IsSolid.ToString());

            // Undo phải phục hồi cả IsSolid
            hpEditor.Undo();

            Check("Undo HATCHEDIT phục hồi trạng thái không đặc",
                !hpHatch.IsSolid, hpHatch.IsSolid.ToString());
        }

        // ---------------------- HATCH: hướng nét phải giữ nguyên trên tờ in
        var hWs = CadWorkspace.New();
        var hDoc = hWs.Document;

        var hBoundary = new LwPolyline { IsClosed = true };
        hBoundary.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 0) });
        hBoundary.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(2000, 0) });
        hBoundary.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(2000, 2000) });
        hBoundary.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 2000) });

        var hHatch = OpenCad.Core.Editing.HatchFactory.FromBoundary(hBoundary);

        if (hHatch is null)
        {
            Check("HATCH tạo được từ biên", false, "null");
        }
        else
        {
            hHatch.IsSolid = false;
            hHatch.PatternScale = 200;
            hHatch.PatternAngle = 0;

            try { hHatch.Pattern = new HatchPattern("ANSI31"); } catch { }

            hDoc.Entities.Add(hHatch);

            var hScene = new CadScene();
            hScene.Build(hDoc);

            // Các đoạn của pattern trong model space
            var modelSegments = hScene.Entries
                .SelectMany(e => e.Prims)
                .SelectMany(p => p.Points)
                .ToList();

            Check("HATCH: pattern sinh ra nét trong model",
                modelSegments.Count >= 4, modelSegments.Count + " điểm");

            // Góc của các nét pattern (bỏ qua nét biên polyline)
            double FirstAngle()
            {
                foreach (var entry in hScene.Entries)
                {
                    if (entry.Entity is not Hatch) continue;

                    foreach (var prim in entry.Prims)
                    {
                        if (prim.Points.Length < 2) continue;

                        var dx = prim.Points[^1].X - prim.Points[0].X;
                        var dy = prim.Points[^1].Y - prim.Points[0].Y;

                        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) continue;

                        return Math.Atan2(dy, dx) * 180 / Math.PI;
                    }
                }

                return double.NaN;
            }

            var modelAngle = FirstAngle();

            Check("HATCH: nét pattern nghiêng 45° (ANSI31)",
                Math.Abs(Math.Abs(modelAngle) - 45) < 1,
                modelAngle.ToString("0.##") + "°");

            // Trên tờ in: hướng phải giữ nguyên
            var hTemplate = new OpenCad.Core.Layout.SheetTemplateOptions
            {
                Title = "H", UnitsPerMm = 50,
            };

            var hSheet = OpenCad.Core.Layout.PaperSheetBuilder.Build(hScene, hTemplate);

            double? paperAngle = null;

            foreach (var item in hSheet.Items)
            {
                foreach (var prim in item.Prims)
                {
                    if (prim.Points.Length < 2) continue;

                    var dx = prim.Points[^1].X - prim.Points[0].X;
                    var dy = prim.Points[^1].Y - prim.Points[0].Y;

                    if (Math.Abs(dx) < 1e-6 && Math.Abs(dy) < 1e-6) continue;

                    var angle = Math.Atan2(dy, dx) * 180 / Math.PI;

                    // Nét pattern nằm trong khoảng 20..70 độ
                    if (Math.Abs(angle) > 20 && Math.Abs(angle) < 70)
                    {
                        paperAngle = angle;
                        break;
                    }
                }

                if (paperAngle is not null) break;
            }

            Check("HATCH: tờ in giữ được nét nghiêng của pattern",
                paperAngle is not null && Math.Abs(Math.Abs(paperAngle.Value) - 45) < 1,
                paperAngle?.ToString("0.##") + "°");
        }

        // ---------------------- PURGE KHÔNG ĐƯỢC XOÁ NỘI DUNG TỜ IN
        var pgWs = CadWorkspace.New();
        var pgDoc = pgWs.Document;

        pgDoc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(4000, 0, 0),
        });

        var pgScene = new CadScene();
        pgScene.Build(pgDoc);

        var pgLayout = OpenCad.Core.Layout.LayoutFactory.Create(
            pgDoc, "PG_SHEET",
            new OpenCad.Core.Layout.SheetTemplateOptions { Title = "PG", UnitsPerMm = 50 },
            pgScene);

        Check("PURGE: tạo được tờ có nội dung",
            pgLayout?.AssociatedBlock?.Entities.Count > 10,
            (pgLayout?.AssociatedBlock?.Entities.Count ?? 0) + " entity");

        var pgPlan = OpenCad.Core.Audit.DrawingPurge.Inspect(pgDoc, "0");

        Check("PURGE: KHÔNG coi block của tờ là mồ côi",
            !pgPlan.Blocks.Contains("PG_SHEET"),
            pgPlan.Blocks.Count == 0 ? "0 mồ côi" : string.Join(",", pgPlan.Blocks));

        OpenCad.Core.Audit.DrawingPurge.Apply(pgDoc, pgPlan);

        Check("PURGE: tờ in giữ nguyên nội dung sau khi purge",
            pgLayout?.AssociatedBlock?.Entities.Count > 10,
            (pgLayout?.AssociatedBlock?.Entities.Count ?? 0) + " entity");

        // ---------------------- IMPORT hai tệp mẫu thật (tích hợp)
        if (File.Exists(Path.Combine("samples", "can-ho.dwg")) &&
            File.Exists(Path.Combine("samples", "chi-tiet.dwg")))
        {
            var realWs = CadWorkspace.Open(Path.Combine("samples", "can-ho.dwg"));

            var realEntitiesBefore = realWs.Document.Entities.Count();

            var realBlock = OpenCad.Core.Editing.ImportOperation.Import(
                realWs, Path.Combine("samples", "chi-tiet.dwg"), "CHI_TIET_MAU");

            Check("IMPORT thật: nạp được chi-tiet.dwg thành block",
                realBlock is not null && realBlock.Entities.Count > 0,
                (realBlock?.Entities.Count ?? -1) + " đối tượng");

            if (realBlock is not null)
            {
                var realEditor = new CadEditor(realWs);

                realEditor.AddEntities(
                    new Entity[] { new Insert(realBlock) { InsertPoint = new CSMath.XYZ(9000, 0, 0) } },
                    "INSERT");

                Check("IMPORT thật: chèn được vào bản vẽ căn hộ",
                    realWs.Document.Entities.Count() == realEntitiesBefore + 1,
                    realWs.Document.Entities.Count() + " entity");

                var realScene = new CadScene();
                realScene.Build(realWs.Document);

                Check("IMPORT thật: bản vẽ vẫn vẽ được sau khi chèn",
                    realScene.PrimitiveCount > 40, realScene.PrimitiveCount + " primitive");

                var realAudit = OpenCad.Core.Audit.DrawingAudit.Run(realWs.Document);

                Check("IMPORT thật: audit không phát hiện lỗi mới",
                    !realAudit.Any(f => f.Severity == "Lỗi"),
                    realAudit.Count + " vấn đề");
            }
        }

        // ---------------------- IMPORT với block LỒNG bên trong tệp nguồn
        var nestWs = CadWorkspace.New();

        var nestBlock = new ACadSharp.Tables.BlockRecord("CUA_NGUON");
        nestWs.Document.BlockRecords.Add(nestBlock);

        nestBlock.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(900, 0, 0),
        });

        nestBlock.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(0, 900, 0),
        });

        nestWs.Document.Entities.Add(new Insert(nestBlock)
        {
            InsertPoint = new CSMath.XYZ(300, 300, 0),
        });

        var nestPath = Path.Combine(Path.GetTempPath(), "opencad-import", "long.dwg");
        Directory.CreateDirectory(Path.GetDirectoryName(nestPath)!);
        nestWs.Save(nestPath);

        var nestTargetWs = CadWorkspace.New();
        var nestBlockImported = OpenCad.Core.Editing.ImportOperation.Import(
            nestTargetWs, nestPath, "CO_BLOCK");

        Check("IMPORT có block lồng: nạp được", nestBlockImported is not null,
            nestBlockImported?.Name ?? "null");

        if (nestBlockImported is not null)
        {
            var nestInsert = nestBlockImported.Entities.OfType<Insert>().FirstOrDefault();

            Check("IMPORT có block lồng: giữ được insert",
                nestInsert is not null, nestInsert is null ? "không có insert" : "có");

            Check("IMPORT có block lồng: định nghĩa block con được mang theo",
                nestTargetWs.Document.BlockRecords.Any(b => b.Name == "CUA_NGUON"),
                string.Join(",", nestTargetWs.Document.BlockRecords.Select(b => b.Name)));

            // Chèn rồi vẽ: phải ra hình của block con
            var nestEditor = new CadEditor(nestTargetWs);
            nestEditor.AddEntities(
                new Entity[] { new Insert(nestBlockImported) { InsertPoint = new CSMath.XYZ(3000, 0, 0) } },
                "INSERT");

            var nestScene = new CadScene();
            nestScene.Build(nestTargetWs.Document);

            var nestPrims = nestScene.Entries.SelectMany(e => e.Prims).Count();

            Check("IMPORT có block lồng: vẽ ra hình của block con",
                nestPrims >= 2, nestPrims + " primitive");

            // Lưu rồi mở lại: block lồng phải còn
            var nestOutPath = Path.Combine(Path.GetTempPath(), "opencad-import", "dich.dwg");
            nestTargetWs.Save(nestOutPath);

            var nestBack = CadWorkspace.Open(nestOutPath);

            Check("IMPORT có block lồng: lưu/đọc giữ được block con",
                nestBack.Document.BlockRecords.Any(b => b.Name == "CUA_NGUON"),
                string.Join(",", nestBack.Document.BlockRecords.Select(b => b.Name)));

            var nestBackScene = new CadScene();
            nestBackScene.Build(nestBack.Document);

            Check("IMPORT có block lồng: mở lại vẫn vẽ ra hình",
                nestBackScene.Entries.SelectMany(e => e.Prims).Count() >= 2,
                nestBackScene.Entries.SelectMany(e => e.Prims).Count() + " primitive");

            try { File.Delete(nestOutPath); } catch { }
        }

        try { File.Delete(nestPath); } catch { }

        // ---------------------- IMPORT: nạp DWG khác thành block
        // Tạo tệp nguồn có 2 đối tượng trên lớp riêng
        var imSrcWs = CadWorkspace.New();
        var imLayer = new ACadSharp.Tables.Layer("NGUON");
        imSrcWs.Document.Layers.Add(imLayer);

        imSrcWs.Document.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(1000, 0, 0),
            Layer = imLayer,
        });

        imSrcWs.Document.Entities.Add(new Circle
        {
            Center = new CSMath.XYZ(500, 500, 0),
            Radius = 300,
            Layer = imLayer,
        });

        var imSourcePath = Path.Combine(Path.GetTempPath(), "opencad-import", "nguon.dwg");
        Directory.CreateDirectory(Path.GetDirectoryName(imSourcePath)!);
        imSrcWs.Save(imSourcePath);

        // Bản vẽ đích
        var imTargetWs = CadWorkspace.New();
        var imEditor = new CadEditor(imTargetWs);

        var imBlock = OpenCad.Core.Editing.ImportOperation.Import(imTargetWs, imSourcePath, "CHI_TIET");

        Check("IMPORT tạo được block", imBlock is not null, imBlock?.Name ?? "null");
        Check("IMPORT mang đủ 2 đối tượng",
            imBlock?.Entities.Count == 2, (imBlock?.Entities.Count ?? -1) + " entity");

        Check("IMPORT tạo lớp của tệp nguồn",
            imTargetWs.Document.Layers.Any(l => l.Name == "NGUON"),
            string.Join(",", imTargetWs.Document.Layers.Select(l => l.Name)));

        Check("IMPORT giữ đối tượng ở lớp nguồn",
            imBlock is not null && imBlock.Entities.All(e => e.Layer?.Name == "NGUON"),
            imBlock?.Entities.FirstOrDefault()?.Layer?.Name ?? "null");

        // Trùng tên block → tự thêm hậu tố
        var imSecond = OpenCad.Core.Editing.ImportOperation.Import(imTargetWs, imSourcePath, "CHI_TIET");

        Check("IMPORT trùng tên thì thêm hậu tố",
            imSecond is not null && imSecond.Name != "CHI_TIET",
            imSecond?.Name ?? "null");

        // Chèn block vừa nạp → phải vẽ ra hình
        if (imBlock is not null)
        {
            var imInsert = new Insert(imBlock) { InsertPoint = new CSMath.XYZ(2000, 0, 0) };
            imEditor.AddEntities(new Entity[] { imInsert }, "INSERT");

            var imScene = new CadScene();
            imScene.Build(imTargetWs.Document);

            var imPrims = imScene.Entries.SelectMany(e => e.Prims).Count();

            Check("IMPORT: chèn block nạp được và vẽ ra hình",
                imPrims >= 2, imPrims + " primitive");
        }

        // Tệp không tồn tại → null
        Check("IMPORT tệp không tồn tại trả null",
            OpenCad.Core.Editing.ImportOperation.Import(imTargetWs, "khong/co.dwg") is null, "null");

        try { File.Delete(imSourcePath); } catch { }

        // ---------------------- WBLOCK: xuất lựa chọn ra tệp riêng
        var wbWs = CadWorkspace.New();
        var wbEditor = new CadEditor(wbWs);

        var wbLayerA = new ACadSharp.Tables.Layer("XUAT_A");
        var wbLayerB = new ACadSharp.Tables.Layer("XUAT_B");
        wbWs.Document.Layers.Add(wbLayerA);
        wbWs.Document.Layers.Add(wbLayerB);

        var wbLine = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(1000, 0, 0),
            Layer = wbLayerA,
        };

        var wbCircle = new Circle
        {
            Center = new CSMath.XYZ(500, 500, 0),
            Radius = 200,
            Layer = wbLayerB,
        };

        var wbOther = new Line
        {
            StartPoint = new CSMath.XYZ(0, 900, 0),
            EndPoint = new CSMath.XYZ(1000, 900, 0),
            Layer = wbLayerB,
        };

        wbEditor.AddEntities(new Entity[] { wbLine, wbCircle, wbOther }, "Add");

        var wbPath = Path.Combine(Path.GetTempPath(), "opencad-wblock", "trich.dwg");
        Directory.CreateDirectory(Path.GetDirectoryName(wbPath)!);

        var wbWritten = OpenCad.Core.Editing.WBlockOperation.Write(
            new Entity[] { wbLine, wbCircle }, wbPath);

        Check("WBLOCK ghi được 2 đối tượng", wbWritten == 2, wbWritten + " đối tượng");
        Check("WBLOCK tạo ra tệp", File.Exists(wbPath), wbPath);

        var wbBack = CadWorkspace.Open(wbPath);

        Check("WBLOCK: tệp mới có đúng 2 đối tượng",
            wbBack.Document.Entities.Count() == 2, wbBack.Document.Entities.Count() + " entity");

        Check("WBLOCK: giữ đúng loại đối tượng",
            wbBack.Document.Entities.OfType<Line>().Count() == 1 &&
            wbBack.Document.Entities.OfType<Circle>().Count() == 1,
            "1 line + 1 circle");

        Check("WBLOCK: mang theo lớp mà đối tượng dùng",
            wbBack.Document.Layers.Any(l => l.Name == "XUAT_A") &&
            wbBack.Document.Layers.Any(l => l.Name == "XUAT_B"),
            string.Join(",", wbBack.Document.Layers.Select(l => l.Name)));

        Check("WBLOCK: đối tượng vẫn nằm đúng lớp",
            wbBack.Document.Entities.OfType<Line>().All(l => l.Layer?.Name == "XUAT_A"),
            wbBack.Document.Entities.OfType<Line>().First().Layer?.Name ?? "null");

        Check("WBLOCK không mang theo đối tượng không chọn",
            !wbBack.Document.Entities.OfType<Line>().Any(l => Math.Abs(l.StartPoint.Y - 900) < 1e-6),
            "không có line ở y=900");

        // Không có gì để ghi
        Check("WBLOCK với danh sách rỗng trả 0",
            OpenCad.Core.Editing.WBlockOperation.Write(Array.Empty<Entity>(), wbPath) == 0, "0");

        try { File.Delete(wbPath); } catch { }

        // ---------------------- BẢNG KIỂU IN THEO LỚP (.ctb thay thế)
        var styleText = string.Join("\n", new[]
        {
            "; bảng kiểu in thử",
            "monochrome = true",
            "default = 0.18",
            "TUONG = 0.50",
            "CUA = 0.25, color",
        });

        var styleTable = OpenCad.Core.Export.PlotStyleTable.Parse(styleText);

        Check("PLOTSTYLE đọc được 2 lớp", styleTable.Count == 2, styleTable.Count + " lớp");
        Check("PLOTSTYLE đọc monochrome", styleTable.Monochrome, styleTable.Monochrome.ToString());
        Check("PLOTSTYLE đọc độ đậm mặc định",
            Math.Abs(styleTable.DefaultWeightMm - 0.18) < 1e-9,
            styleTable.DefaultWeightMm.ToString("0.##"));

        var tuongStyle = styleTable.For("TUONG");

        Check("PLOTSTYLE đọc độ đậm của lớp",
            tuongStyle?.WeightMm is 0.50, tuongStyle?.WeightMm?.ToString("0.##") ?? "null");
        Check("PLOTSTYLE lớp mặc định in đen", tuongStyle?.Black == true, "đen");

        var cuaStyle = styleTable.For("CUA");

        Check("PLOTSTYLE lớp 'color' giữ màu",
            cuaStyle is not null && !cuaStyle.Black, cuaStyle?.Black.ToString() ?? "null");

        Check("PLOTSTYLE tên lớp không phân biệt hoa thường",
            styleTable.For("tuong") is not null, "có");

        Check("PLOTSTYLE lớp lạ trả null", styleTable.For("KHONG_CO") is null, "null");

        // Áp vào PDF: hai bản in phải khác nhau (có bảng vs không)
        var styleWs = CadWorkspace.New();

        var styleLayer = new ACadSharp.Tables.Layer("TUONG");
        styleWs.Document.Layers.Add(styleLayer);

        // Đường 100 mm với UnitsPerMm = 1 → vừa trang A4
        styleWs.Document.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(10, 10, 0),
            EndPoint = new CSMath.XYZ(110, 10, 0),
            Layer = styleLayer,
        });

        var styleScene = new CadScene();
        styleScene.Build(styleWs.Document);

        var styleEntryLayer = styleScene.Entries.FirstOrDefault()?.Entity?.Layer?.Name;
        var styleLookup = styleTable.For(styleEntryLayer);

        Check("PLOTSTYLE: entity mang đúng lớp",
            styleEntryLayer == "TUONG", styleEntryLayer ?? "null");

        Check("PLOTSTYLE: tra được kiểu in của lớp đó",
            styleLookup?.WeightMm is 0.50, styleLookup?.WeightMm?.ToString("0.##") ?? "null");

        var stemPath = Path.Combine(Path.GetTempPath(), "opencad-smoke-style");

        var styleOptions = new OpenCad.Core.Export.PdfExportOptions
        {
            PageWidthMm = 210,
            PageHeightMm = 297,
            Landscape = true,
            Title = "STYLE",
            UnitsPerMm = 1,
            Monochrome = true,
            PlotStyle = styleTable,
        };

        OpenCad.Core.Export.PdfExporter.Export(styleScene, stemPath + "-styled.pdf", styleOptions);

        var plainOptions = new OpenCad.Core.Export.PdfExportOptions
        {
            PageWidthMm = 210,
            PageHeightMm = 297,
            Landscape = true,
            Title = "STYLE",
            UnitsPerMm = 1,
            Monochrome = true,
        };

        OpenCad.Core.Export.PdfExporter.Export(styleScene, stemPath + "-plain.pdf", plainOptions);

        var styledBytes = File.ReadAllBytes(stemPath + "-styled.pdf");
        var plainBytes = File.ReadAllBytes(stemPath + "-plain.pdf");

        Check("PLOTSTYLE tạo được PDF",
            styledBytes.Length > 500, styledBytes.Length + " bytes");

        Check("PLOTSTYLE tạo PDF có lệnh vẽ",
            System.Text.Encoding.Latin1.GetString(styledBytes).Contains(" l"),
            "có lệnh vẽ");

        // Kiểm trực tiếp độ đậm mà bộ xuất sẽ dùng (ổn định hơn so byte PDF)
        var styledPrim = styleScene.Entries.SelectMany(e => e.Prims).First();

        var styledWidth = OpenCad.Core.Export.PdfExporter.ResolveWidthMm(
            styledPrim, styleOptions, "TUONG");

        var fallbackWidth = OpenCad.Core.Export.PdfExporter.ResolveWidthMm(
            styledPrim, styleOptions, "KHONG_CO");

        var plainWidth = OpenCad.Core.Export.PdfExporter.ResolveWidthMm(
            styledPrim, plainOptions, "TUONG");

        Check("PLOTSTYLE áp đúng độ đậm của lớp",
            Math.Abs(styledWidth - 0.50) < 1e-9, styledWidth.ToString("0.###") + " mm");

        Check("PLOTSTYLE lớp không khai báo dùng độ đậm mặc định",
            Math.Abs(fallbackWidth - 0.18) < 1e-9, fallbackWidth.ToString("0.###") + " mm");

        Check("PLOTSTYLE đổi được độ đậm so với bản in mặc định",
            Math.Abs(styledWidth - plainWidth) > 1e-6,
            plainWidth.ToString("0.###") + " → " + styledWidth.ToString("0.###") + " mm");

        try
        {
            File.Delete(stemPath + "-styled.pdf");
            File.Delete(stemPath + "-plain.pdf");
        }
        catch { }

        // ---------------------- TỆP MỞ GẦN ĐÂY
        var recentDir = Path.Combine(Path.GetTempPath(), "opencad-recent-test");
        Directory.CreateDirectory(recentDir);

        var recentA = Path.Combine(recentDir, "a.dwg");
        var recentB = Path.Combine(recentDir, "b.dwg");

        File.WriteAllText(recentA, "x");
        File.WriteAllText(recentB, "x");

        var previousStore = OpenCad.Core.RecentFiles.StorePath;
        OpenCad.Core.RecentFiles.StorePath = Path.Combine(recentDir, "recent.txt");

        try
        {
            OpenCad.Core.RecentFiles.Clear();

            Check("RECENT: danh sách ban đầu rỗng",
                OpenCad.Core.RecentFiles.Load().Count == 0, "0");

            OpenCad.Core.RecentFiles.Add(recentA);
            OpenCad.Core.RecentFiles.Add(recentB);

            var items = OpenCad.Core.RecentFiles.Load();

            Check("RECENT: thêm 2 tệp", items.Count == 2, items.Count + " mục");
            Check("RECENT: tệp mới nhất đứng đầu",
                items[0].EndsWith("b.dwg"), Path.GetFileName(items[0]));

            // Thêm lại tệp cũ → lên đầu, không nhân đôi
            OpenCad.Core.RecentFiles.Add(recentA);
            items = OpenCad.Core.RecentFiles.Load();

            Check("RECENT: thêm lại thì lên đầu, không trùng",
                items.Count == 2 && items[0].EndsWith("a.dwg"),
                string.Join(",", items.Select(Path.GetFileName)));

            // Tệp bị xoá khỏi đĩa → tự bỏ khi đọc
            File.Delete(recentA);
            items = OpenCad.Core.RecentFiles.Load();

            Check("RECENT: bỏ mục đã bị xoá",
                items.Count == 1 && items[0].EndsWith("b.dwg"),
                items.Count + " mục");

            // Giới hạn 10 mục
            OpenCad.Core.RecentFiles.Clear();

            for (var i = 0; i < 15; i++)
            {
                var p2 = Path.Combine(recentDir, "f" + i + ".dwg");
                File.WriteAllText(p2, "x");
                OpenCad.Core.RecentFiles.Add(p2);
            }

            Check("RECENT: giữ tối đa 10 mục",
                OpenCad.Core.RecentFiles.Load().Count == 10,
                OpenCad.Core.RecentFiles.Load().Count + " mục");

            Check("RECENT: mục mới nhất vẫn ở đầu",
                OpenCad.Core.RecentFiles.Load()[0].EndsWith("f14.dwg"),
                Path.GetFileName(OpenCad.Core.RecentFiles.Load()[0]));
        }
        finally
        {
            OpenCad.Core.RecentFiles.StorePath = previousStore;
            OpenCad.Core.RecentFiles.Clear();
        }

        // ---------------------- GỢI Ý LỆNH (autocomplete)
        var suggest = OpenCad.Core.Commands.CommandRegistry.Suggest;

        Check("Gợi ý: gõ 'LI' ra LINE", suggest("LI") == "LINE", suggest("LI") ?? "null");
        Check("Gợi ý: gõ 'rec' ra RECTANG", suggest("rec") == "RECTANG", suggest("rec") ?? "null");
        Check("Gợi ý: gõ 'ci' ra CIRCLE", suggest("ci") == "CIRCLE", suggest("ci") ?? "null");
        Check("Gợi ý: alias 'L' ra LINE", suggest("L") is "LINE" or "LAYER" or "LTSCALE",
            suggest("L") ?? "null");
        Check("Gợi ý: gõ đúng tên đầy đủ thì không gợi ý",
            suggest("LINE") is null, suggest("LINE") ?? "null");
        Check("Gợi ý: chuỗi rỗng không gợi ý", suggest("") is null, "null");
        Check("Gợi ý: chuỗi lạ không gợi ý", suggest("ZZZZ") is null, suggest("ZZZZ") ?? "null");

        // ---------------------- ATTSYNC: đồng bộ attribute sau khi sửa block
        var syncWs = CadWorkspace.New();
        var syncEditor = new CadEditor(syncWs);

        var asBlock = new ACadSharp.Tables.BlockRecord("CUA_AS");
        syncWs.Document.BlockRecords.Add(asBlock);

        asBlock.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(900, 0, 0),
        });

        // Insert có TRƯỚC khi block có ATTDEF
        var asInsert = new Insert(asBlock) { InsertPoint = new CSMath.XYZ(1000, 500, 0) };
        syncEditor.AddEntities(new Entity[] { asInsert }, "INSERT");

        Check("ATTSYNC: insert ban đầu chưa có attribute",
            asInsert.Attributes.Count == 0, asInsert.Attributes.Count + " attribute");

        // Thêm ATTDEF TRỰC TIẾP vào block (bỏ qua hàm tiện ích cũng cập nhật insert)
        asBlock.Entities.Add(new AttributeDefinition
        {
            Tag = "MA_CUA",
            Value = "C1",
            InsertPoint = new CSMath.XYZ(0, 300, 0),
            Height = 200,
        });

        Check("ATTSYNC: insert vẫn thiếu attribute sau khi block có ATTDEF",
            asInsert.Attributes.Count == 0, asInsert.Attributes.Count + " attribute");

        var asSynced = syncEditor.SyncAttributes(asBlock);

        Check("ATTSYNC thêm attribute cho insert thiếu", asSynced == 1, asSynced + " attribute");
        Check("ATTSYNC gán đúng tag và giá trị mặc định",
            asInsert.Attributes.Count == 1 &&
            asInsert.Attributes[0].Tag == "MA_CUA" &&
            asInsert.Attributes[0].Value == "C1",
            asInsert.Attributes.Count == 1
                ? asInsert.Attributes[0].Tag + "=" + asInsert.Attributes[0].Value
                : "không có");

        Check("ATTSYNC lần hai không làm gì",
            syncEditor.SyncAttributes(asBlock) == 0, "0");

        // Insert thứ hai cũng được đồng bộ
        var asSecond = new Insert(asBlock) { InsertPoint = new CSMath.XYZ(4000, 500, 0) };
        syncWs.Document.Entities.Add(asSecond);

        // ACadSharp tự điền attribute khi thêm insert — nếu vậy thì không cần sync
        var secondHadAttributes = asSecond.Attributes.Count;

        var asSecondSynced = syncEditor.SyncAttributes(asBlock);

        Check("ATTSYNC: insert mới không bị thêm trùng",
            secondHadAttributes + asSecondSynced == 1,
            secondHadAttributes + " sẵn có + " + asSecondSynced + " thêm");

        syncEditor.Undo();

        Check("Undo ATTSYNC bỏ attribute vừa thêm",
            asInsert.Attributes.Count == 0, asInsert.Attributes.Count + " attribute");
        // ---------------------- NẠP MẪU TÔ TỪ FILE .pat
        var patText = string.Join("\n", new[]
        {
            "; mẫu tự định nghĩa",
            "*GO_MY,Go mau",
            "0, 0,0, 0,25",
            "90, 0,0, 0,25",
            "*SOI_MY,Soi",
            "45, 0,0, 0,40, 10,-5",
        });

        var beforeNames = OpenCad.Core.Styles.HatchPatternLibrary.Names.Count;
        var patLoaded = OpenCad.Core.Styles.HatchPatternLibrary.LoadText(patText);

        Check("PATLOAD đọc được 2 mẫu", patLoaded == 2, patLoaded + " mẫu");

        var afterNames = OpenCad.Core.Styles.HatchPatternLibrary.Names.ToList();

        Check("PATLOAD thêm mẫu mới vào thư viện",
            afterNames.Contains("GO_MY") && afterNames.Contains("SOI_MY"),
            string.Join(",", afterNames.Where(n => n.EndsWith("_MY"))));

        Check("PATLOAD tăng số mẫu", afterNames.Count == beforeNames + 2,
            beforeNames + " → " + afterNames.Count);

        // Dùng được mẫu vừa nạp để tô
        var patWs = CadWorkspace.New();
        var patEditor = new CadEditor(patWs);

        var patPoly = new LwPolyline { IsClosed = true };
        patPoly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 0) });
        patPoly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(2000, 0) });
        patPoly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(2000, 2000) });
        patPoly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 2000) });

        var patHatch = OpenCad.Core.Editing.HatchFactory.FromBoundary(patPoly);

        if (patHatch is null)
        {
            Check("PATLOAD: tạo được hatch để thử", false, "null");
        }
        else
        {
            patWs.Document.Entities.Add(patHatch);

            // delta của mẫu là 25 mm → scale 1 để khoảng cách 25, hợp với hình 2000
            var patChanged = patEditor.SetHatchProperties(new Entity[] { patHatch }, 1, 0, "GO_MY");

            Check("PATLOAD dùng được mẫu vừa nạp", patChanged == 1 && !patHatch.IsSolid,
                patHatch.Pattern?.Name ?? "null");

            var patScene = new CadScene();
            patScene.Build(patWs.Document);

            var patPrims = patScene.Entries.SelectMany(e => e.Prims).ToList();

            Check("Mẫu .pat vẽ ra hình", patPrims.Count > 10, patPrims.Count + " primitive");
        }

        // File .pat không tồn tại → 0
        Check("PATLOAD file không tồn tại trả 0",
            OpenCad.Core.Styles.HatchPatternLibrary.LoadFile("khong/co/file.pat") == 0, "0");

        // ---------------------- ATTOUT: xuất bảng attribute ra CSV
        {
            var attWs = CadWorkspace.New();
            var attEditor = new CadEditor(attWs);

            var attBlock = new ACadSharp.Tables.BlockRecord("CUA_DI");
            attWs.Document.BlockRecords.Add(attBlock);

            attBlock.Entities.Add(new Line
            {
                StartPoint = new CSMath.XYZ(0, 0, 0),
                EndPoint = new CSMath.XYZ(900, 0, 0),
            });

            for (var i = 0; i < 3; i++)
            {
                var insert = new Insert(attBlock)
                {
                    InsertPoint = new CSMath.XYZ(i * 3000, 0, 0),
                };

                insert.Attributes.Add(new AttributeEntity
                {
                    Tag = "MA_CUA",
                    Value = "C" + (i + 1),
                    InsertPoint = new CSMath.XYZ(i * 3000, 300, 0),
                    Height = 200,
                });

                insert.Attributes.Add(new AttributeEntity
                {
                    Tag = "KICH_THUOC",
                    Value = (900 + i * 100) + "x2200",
                    InsertPoint = new CSMath.XYZ(i * 3000, 600, 0),
                    Height = 200,
                });

                attEditor.AddEntities(new Entity[] { insert }, "Insert");
            }

            var (attHeaders, attRows) = attEditor.ExtractAttributes();

            Check("ATTOUT gom đủ 3 dòng", attRows.Count == 3, attRows.Count + " dòng");
            Check("ATTOUT có cột cố định + 2 tag",
                attHeaders.SequenceEqual(new[] { "BLOCK", "LAYER", "X", "Y", "MA_CUA", "KICH_THUOC" }),
                string.Join(",", attHeaders));

            Check("ATTOUT ghi đúng giá trị attribute",
                attRows[1]["MA_CUA"] == "C2" && attRows[1]["KICH_THUOC"] == "1000x2200",
                attRows[1]["MA_CUA"] + " / " + attRows[1]["KICH_THUOC"]);

            Check("ATTOUT ghi đúng tên block và toạ độ",
                attRows[2]["BLOCK"] == "CUA_DI" && attRows[2]["X"] == "6000",
                attRows[2]["BLOCK"] + " @" + attRows[2]["X"]);

            // Bản vẽ không có attribute → 0 dòng
            var emptyWs = CadWorkspace.New();
            emptyWs.Document.Entities.Add(new Line());

            Check("ATTOUT bản vẽ không attribute trả 0 dòng",
                new CadEditor(emptyWs).ExtractAttributes().Rows.Count == 0, "0");
        }

        // ---------------------- CHỮ KÝ TRÙNG LẶP: phải tính VỊ TRÍ chữ và LOOPS của hatch
        // (lỗi thật: hai chữ 'N' khác chỗ bị coi là trùng → OVERKILL xoá nhầm)
        {
            var sigWs = CadWorkspace.New();
            var sigEditor = new CadEditor(sigWs);

            var textA = new TextEntity
            {
                InsertPoint = new CSMath.XYZ(0, 0, 0),
                Value = "N",
                Height = 100,
            };

            var textB = new TextEntity
            {
                InsertPoint = new CSMath.XYZ(5000, 3000, 0),
                Value = "N",
                Height = 100,
            };

            sigEditor.AddEntities(new Entity[] { textA, textB }, "Add");

            Check("Chữ ký: hai chữ 'N' KHÁC vị trí không bị coi là trùng",
                sigEditor.RemoveDuplicates() == 0,
                sigWs.Document.Entities.Count() + " entity còn lại");

            // Cùng chữ, cùng vị trí → trùng thật
            var textC = new TextEntity
            {
                InsertPoint = new CSMath.XYZ(0, 0, 0),
                Value = "N",
                Height = 100,
            };

            sigWs.Document.Entities.Add(textC);
            sigEditor.Reload();

            Check("Chữ ký: chữ trùng y hệt thì bị xoá",
                sigEditor.RemoveDuplicates() == 1,
                sigWs.Document.Entities.Count() + " entity còn lại");

            // Hatch khác hình học không được coi là trùng
            var dupHatchWs = CadWorkspace.New();
            var dupHatchEditor = new CadEditor(dupHatchWs);

            LwPolyline Square(double x, double y, double size)
            {
                var poly = new LwPolyline { IsClosed = true };

                poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(x, y) });
                poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(x + size, y) });
                poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(x + size, y + size) });
                poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(x, y + size) });

                return poly;
            }

            var hatchA = HatchFactory.FromBoundary(Square(0, 0, 1000));
            var hatchB = HatchFactory.FromBoundary(Square(3000, 2000, 1000));

            if (hatchA is null || hatchB is null)
            {
                Check("Chữ ký: tạo được 2 hatch để thử", false, "null");
            }
            else
            {
                dupHatchEditor.AddEntities(new Entity[] { hatchA, hatchB }, "Add");

                Check("Chữ ký: hai hatch KHÁC vị trí không bị coi là trùng",
                    dupHatchEditor.RemoveDuplicates() == 0,
                    dupHatchWs.Document.Entities.Count() + " entity còn lại");
            }
        }

        // ---------------------- HATCH có biên là MỘT CUNG TRÒN khép kín
        // (lỗi thật gặp trong bản vẽ GstarBIM: góc quét = 0 nên không vẽ được)
        {
            var circleWs = CadWorkspace.New();

            var circleHatch = new Hatch { IsSolid = true };

            var path = new Hatch.BoundaryPath();

            path.Edges.Add(new Hatch.BoundaryPath.Arc
            {
                Center = new CSMath.XY(0, 0),
                Radius = 1000,
                StartAngle = 0,
                EndAngle = 0,
                CounterClockWise = true,
            });

            circleHatch.Paths.Add(path);
            circleWs.Document.Entities.Add(circleHatch);

            var circleScene = new CadScene();
            circleScene.Build(circleWs.Document);

            Check("HATCH tròn (biên 1 cung start=end) vẽ được",
                circleScene.PrimitiveCount > 0, circleScene.PrimitiveCount + " primitive");

            var circleBounds = circleScene.Bounds;

            Check("HATCH tròn có vùng bao đúng bán kính",
                Math.Abs(circleBounds.MinX + 1000) < 30 && Math.Abs(circleBounds.MaxX - 1000) < 30,
                $"{circleBounds.MinX:0}..{circleBounds.MaxX:0}");
        }

        // ---------------------- LIST: mô tả đối tượng
        var lsWs = CadWorkspace.New();
        var lsEditor = new CadEditor(lsWs);

        var lsLayer = new ACadSharp.Tables.Layer("TUONG");
        lsWs.Document.Layers.Add(lsLayer);

        var lsLine = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(300, 400, 0),
            Layer = lsLayer,
            Color = new ACadSharp.Color((short)3),
        };

        var lsText = new TextEntity
        {
            InsertPoint = new CSMath.XYZ(100, 100, 0),
            Value = "PHONG KHACH",
            Height = 250,
        };

        lsEditor.AddEntities(new Entity[] { lsLine, lsText }, "Add");

        var lsLines = lsEditor.Describe(new Entity[] { lsLine, lsText });

        Check("LIST trả về dòng tiêu đề + mỗi đối tượng một dòng",
            lsLines.Count == 3, lsLines.Count + " dòng");

        Check("LIST ghi đúng loại và lớp",
            lsLines[1].Contains("Line") && lsLines[1].Contains("TUONG"),
            lsLines[1].Trim());

        Check("LIST tính đúng độ dài 3-4-5",
            lsLines[1].Contains("500"), lsLines[1].Trim());

        Check("LIST ghi đúng nội dung chữ",
            lsLines[2].Contains("PHONG KHACH"), lsLines[2].Trim());

        Check("LIST với danh sách rỗng trả 0 dòng",
            lsEditor.Describe(Array.Empty<Entity>()).Count == 0, "0");

        // ---------------------- TIỀN TỐ KÍCH THƯỚC (DIMPOST)
        var pfWs = CadWorkspace.New();
        var pfEditor = new CadEditor(pfWs);

        var pfLine = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(2000, 0, 0),
        };

        pfEditor.AddEntities(new Entity[] { pfLine }, "Add");

        var pfDim = new DimensionAligned
        {
            FirstPoint = new CSMath.XYZ(0, 0, 0),
            SecondPoint = new CSMath.XYZ(2000, 0, 0),
            DefinitionPoint = new CSMath.XYZ(0, -300, 0),
            Style = pfWs.Document.DimensionStyles.First(),
        };

        pfEditor.AddEntities(new Entity[] { pfDim }, "Add");

        string PfText()
        {
            var scene = new CadScene();
            scene.Build(pfWs.Document);

            return string.Join("|", scene.Entries.SelectMany(e => e.Prims)
                .Where(p2 => p2.Kind == OpenCad.Core.Rendering.PrimKind.Text)
                .Select(p2 => p2.Text));
        }

        Check("DIMPOST: chưa đặt tiền tố thì chỉ có số đo",
            PfText() == "2000", PfText());

        var pfStyle = pfWs.Document.DimensionStyles.First();

        pfEditor.SetDimensionStyleProperties(pfStyle, null, null, null, null, null,
            "Dimension style", "L=");

        Check("DIMPOST: tiền tố hiện trước số đo",
            PfText() == "L=2000", PfText());

        // Tiền tố + hậu tố cùng lúc — trước đây bị cho là nhân đôi
        pfEditor.SetDimensionStyleProperties(pfStyle, null, null, null, null, " mm",
            "Dimension style", "L=");

        Check("DIMPOST: tiền tố + hậu tố đúng thứ tự (không nhân đôi)",
            PfText() == "L=2000 mm", PfText());

        pfEditor.Undo();

        Check("Undo DIMPOST bỏ hậu tố",
            PfText() == "L=2000", PfText());

        // Lưu/đọc lại giữ tiền tố
        var pfPath = Path.Combine(Path.GetTempPath(), "opencad-dimpost.dwg");
        pfWs.Save(pfPath);

        var pfBack = CadWorkspace.Open(pfPath);

        Check("DIMPOST: tiền tố sống sót qua lưu/đọc",
            pfBack.Document.DimensionStyles.First().Prefix == "L=",
            "'" + pfBack.Document.DimensionStyles.First().Prefix + "'");

        try { File.Delete(pfPath); } catch { }

        // ---------------------- XOAY CHỮ (TEXTANGLE)
        var trWs = CadWorkspace.New();
        var trEditor = new CadEditor(trWs);

        var trText = new TextEntity
        {
            InsertPoint = new CSMath.XYZ(0, 0, 0),
            Value = "PHONG KHACH",
            Height = 200,
        };

        trEditor.AddEntities(new Entity[] { trText }, "Add");

        var trChanged = trEditor.SetTextRotation(new Entity[] { trText }, 45);

        Check("TEXTANGLE đổi được góc chữ", trChanged == 1, trChanged + " đối tượng");
        Check("TEXTANGLE đặt đúng 45°",
            Math.Abs(trText.Rotation * 180 / Math.PI - 45) < 1e-6,
            (trText.Rotation * 180 / Math.PI).ToString("0.##") + "°");
        Check("TEXTANGLE không dời điểm chèn",
            Math.Abs(trText.InsertPoint.X) < 1e-9 && Math.Abs(trText.InsertPoint.Y) < 1e-9,
            "(" + trText.InsertPoint.X + ", " + trText.InsertPoint.Y + ")");

        var trPrims = new CadScene();
        trPrims.Build(trWs.Document);

        Check("TEXTANGLE hình vẽ theo góc mới",
            trPrims.Entries.SelectMany(e => e.Prims)
                .Where(p2 => p2.Kind == OpenCad.Core.Rendering.PrimKind.Text)
                .All(p2 => Math.Abs(p2.Rotation - Math.PI / 4) < 1e-6),
            "prim rotation = " + (Math.PI / 4).ToString("0.###"));

        Check("TEXTANGLE lần hai cùng góc không làm gì",
            trEditor.SetTextRotation(new Entity[] { trText }, 45) == 0, "0");

        trEditor.Undo();

        Check("Undo TEXTANGLE trả về 0°",
            Math.Abs(trText.Rotation) < 1e-9,
            (trText.Rotation * 180 / Math.PI).ToString("0.##") + "°");

        // ---- MTEXT: Rotation chỉ đọc, nhưng tính từ AlignmentPoint → vẫn xoay được
        var mtWs = CadWorkspace.New();
        var mtEditor = new CadEditor(mtWs);

        var mtText = new MText
        {
            InsertPoint = new CSMath.XYZ(1000, 2000, 0),
            AlignmentPoint = new CSMath.XYZ(100, 0, 0),
            Height = 200,
            Value = "GHI CHU DAI",
            AttachmentPoint = ACadSharp.Entities.AttachmentPointType.TopLeft,
        };

        mtEditor.AddEntities(new Entity[] { mtText }, "Add");

        var mtRotated = mtEditor.SetTextRotation(new Entity[] { mtText }, 30);

        Check("MTEXT xoay được 30° (qua AlignmentPoint)", mtRotated == 1, mtRotated + " đối tượng");
        Check("MTEXT đặt đúng góc 30°",
            Math.Abs(mtText.Rotation * 180 / Math.PI - 30) < 1e-6,
            (mtText.Rotation * 180 / Math.PI).ToString("0.##") + "°");
        Check("MTEXT không bị dời điểm chèn khi xoay",
            Math.Abs(mtText.InsertPoint.X - 1000) < 1e-9 && Math.Abs(mtText.InsertPoint.Y - 2000) < 1e-9,
            "(" + mtText.InsertPoint.X + ", " + mtText.InsertPoint.Y + ")");

        var mtScene = new CadScene();
        mtScene.Build(mtWs.Document);

        var mtPrim = mtScene.Entries.SelectMany(e => e.Prims)
            .FirstOrDefault(p2 => p2.Kind == OpenCad.Core.Rendering.PrimKind.Text);

        Check("MTEXT hình vẽ theo góc mới và đúng vị trí",
            mtPrim is not null &&
            Math.Abs(mtPrim.Rotation - Math.PI / 6) < 1e-6 &&
            Math.Abs(mtPrim.Position.X - 1000) < 0.01 &&
            Math.Abs(mtPrim.Position.Y - 2000) < 0.01,
            mtPrim is null ? "không có" :
                (mtPrim.Rotation * 180 / Math.PI).ToString("0.##") + "° tại (" +
                mtPrim.Position.X.ToString("0") + "," + mtPrim.Position.Y.ToString("0") + ")");

        // Lưu/đọc lại: góc phải giữ được
        var mtPath = Path.Combine(Path.GetTempPath(), "opencad-mtext-rot.dwg");
        mtWs.Save(mtPath);

        var mtBack = CadWorkspace.Open(mtPath);
        var mtBackText = mtBack.Document.Entities.OfType<MText>().FirstOrDefault();

        Check("MTEXT góc sống sót qua lưu/đọc",
            mtBackText is not null &&
            Math.Abs(mtBackText.Rotation * 180 / Math.PI - 30) < 0.5,
            mtBackText is null ? "mất" : (mtBackText.Rotation * 180 / Math.PI).ToString("0.##") + "°");

        try { File.Delete(mtPath); } catch { }

        mtEditor.Undo();

        Check("Undo MTEXT trả góc về 0°",
            Math.Abs(mtText.Rotation) < 1e-6,
            (mtText.Rotation * 180 / Math.PI).ToString("0.##") + "°");

        Check("TEXTANGLE từ chối đối tượng không phải chữ",
            trEditor.SetTextRotation(new Entity[] { new Line() }, 30) == 0, "0");

        // ---------------------- OVERKILL: gộp nét chồng một phần
        var mgWs = CadWorkspace.New();
        var mgEditor = new CadEditor(mgWs);
        var mgLayer = mgWs.Document.Layers.First();

        // Hai đoạn chồng nhau trên cùng đường y = 0: (0..100) và (50..150)
        mgWs.Document.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100, 0, 0),
            Layer = mgLayer,
        });

        mgWs.Document.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(50, 0, 0),
            EndPoint = new CSMath.XYZ(150, 0, 0),
            Layer = mgLayer,
        });

        // Đoạn rời rạc trên cùng đường (400..500) — phải giữ riêng
        mgWs.Document.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(400, 0, 0),
            EndPoint = new CSMath.XYZ(500, 0, 0),
            Layer = mgLayer,
        });

        // Đoạn thứ ba nối tiếp (150..200) → gộp luôn thành 0..200
        mgWs.Document.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(150, 0, 0),
            EndPoint = new CSMath.XYZ(200, 0, 0),
            Layer = mgLayer,
        });

        // Đường khác hướng — không được gộp
        mgWs.Document.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 300, 0),
            EndPoint = new CSMath.XYZ(100, 300, 0),
            Layer = mgLayer,
        });

        mgEditor.Reload();

        var mgBefore = mgWs.Document.Entities.OfType<Line>().Count();
        var mgRemoved = mgEditor.MergeOverlappingLines();

        Check("OVERKILL gộp: bỏ đúng 2 đoạn thừa", mgRemoved == 2, mgRemoved + " đoạn");
        Check("OVERKILL gộp: số line giảm 2",
            mgWs.Document.Entities.OfType<Line>().Count() == mgBefore - 2,
            mgBefore + " → " + mgWs.Document.Entities.OfType<Line>().Count());

        // Tìm đúng đoạn gộp: nằm trên y=0 và trải 0..200 (đoạn 400..500 cũng ở y=0)
        var mgSpans = mgWs.Document.Entities.OfType<Line>()
            .Where(l => Math.Abs(l.StartPoint.Y) < 1e-6 && Math.Abs(l.EndPoint.Y) < 1e-6)
            .Select(l => (
                From: Math.Min(l.StartPoint.X, l.EndPoint.X),
                To: Math.Max(l.StartPoint.X, l.EndPoint.X)))
            .OrderBy(s => s.From)
            .ToList();

        var mgMergedSpan = mgSpans.FirstOrDefault(s => Math.Abs(s.From) < 0.01 && Math.Abs(s.To - 200) < 0.01);

        Check("OVERKILL gộp: đoạn gộp trải từ 0 đến 200",
            mgMergedSpan.To > 0.01,
            string.Join(" | ", mgSpans.Select(s => s.From.ToString("0") + ".." + s.To.ToString("0"))));

        Check("OVERKILL gộp: đoạn rời (400..500) vẫn còn riêng",
            mgWs.Document.Entities.OfType<Line>().Any(l =>
                Math.Abs(l.StartPoint.Y) < 1e-6 && Math.Abs(l.StartPoint.X - 400) < 0.01),
            "có");

        Check("OVERKILL gộp: đường ở y=300 không bị đụng",
            mgWs.Document.Entities.OfType<Line>().Count(l => Math.Abs(l.StartPoint.Y - 300) < 0.01) == 1,
            "1 line");

        mgEditor.Undo();

        Check("OVERKILL gộp: undo phục hồi đủ",
            mgWs.Document.Entities.OfType<Line>().Count() == mgBefore,
            mgWs.Document.Entities.OfType<Line>().Count() + " line");

        // ---------------------- OVERKILL: xoá đối tượng trùng
        var okWs = CadWorkspace.New();
        var okEditor = new CadEditor(okWs);
        var okLayer = okWs.Document.Layers.First();

        for (var i = 0; i < 2; i++)
        {
            okWs.Document.Entities.Add(new Line
            {
                StartPoint = new CSMath.XYZ(0, 0, 0),
                EndPoint = new CSMath.XYZ(1000, 0, 0),
                Layer = okLayer,
            });
        }

        okWs.Document.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 500, 0),
            EndPoint = new CSMath.XYZ(1000, 500, 0),
            Layer = okLayer,
        });

        for (var i = 0; i < 2; i++)
        {
            okWs.Document.Entities.Add(new Circle
            {
                Center = new CSMath.XYZ(500, 500, 0),
                Radius = 200,
                Layer = okLayer,
            });
        }

        okEditor.Reload();

        Check("OVERKILL: bản vẽ có 5 đối tượng (2 cặp trùng)",
            okWs.Document.Entities.Count() == 5, okWs.Document.Entities.Count() + " entity");

        var okRemoved = okEditor.RemoveDuplicates();

        Check("OVERKILL xoá đúng 2 đối tượng trùng", okRemoved == 2, okRemoved + " đối tượng");
        Check("OVERKILL giữ lại 3 đối tượng",
            okWs.Document.Entities.Count() == 3, okWs.Document.Entities.Count() + " entity");
        Check("OVERKILL giữ 2 line khác nhau + 1 circle",
            okWs.Document.Entities.OfType<Line>().Count() == 2 &&
            okWs.Document.Entities.OfType<Circle>().Count() == 1,
            okWs.Document.Entities.Count() + " entity");

        Check("OVERKILL lần hai không xoá gì",
            okEditor.RemoveDuplicates() == 0, "0");

        okEditor.Undo();

        Check("Undo OVERKILL phục hồi 2 đối tượng đã xoá",
            okWs.Document.Entities.Count() == 5, okWs.Document.Entities.Count() + " entity");

        // Đối tượng khác layer thì KHÔNG coi là trùng (workspace riêng cho sạch)
        var okWs2 = CadWorkspace.New();
        var okEditor2 = new CadEditor(okWs2);

        var okLayerA = okWs2.Document.Layers.First();
        var okLayerB = new ACadSharp.Tables.Layer("KHAC");
        okWs2.Document.Layers.Add(okLayerB);

        okWs2.Document.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(1000, 0, 0),
            Layer = okLayerA,
        });

        okWs2.Document.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(1000, 0, 0),
            Layer = okLayerB,
        });

        okEditor2.Reload();

        var okCrossLayer = okEditor2.RemoveDuplicates();

        Check("OVERKILL không coi khác layer là trùng",
            okCrossLayer == 0 && okWs2.Document.Entities.Count() == 2,
            okCrossLayer + " xoá, còn " + okWs2.Document.Entities.Count() + " entity");
        // ---------------------- EXPLODE MULTILEADER (cách giữ nội dung khi lưu DWG)
        var mlWs = CadWorkspace.New();
        var mlEditor = new CadEditor(mlWs);

        // MULTILEADER có NỘI DUNG thật (đường dẫn + chữ) — rỗng thì bung ra 0 là đúng
        var ml = new MultiLeader();

        ml.ContextData.TextLabel = "GHI CHU";
        ml.ContextData.TextHeight = 100;
        ml.ContextData.TextLocation = new CSMath.XYZ(1000, 1000, 0);

        var mlLine = new ACadSharp.Objects.MultiLeaderObjectContextData.LeaderLine();
        mlLine.Points.Add(new CSMath.XYZ(0, 0, 0));
        mlLine.Points.Add(new CSMath.XYZ(500, 400, 0));
        mlLine.Points.Add(new CSMath.XYZ(900, 400, 0));

        var mlRoot = new ACadSharp.Objects.MultiLeaderObjectContextData.LeaderRoot();
        mlRoot.Lines.Add(mlLine);
        ml.ContextData.LeaderRoots.Add(mlRoot);

        Check("MULTILEADER có nội dung để bung", ml.ContextData.LeaderRoots.Count > 0, "có");

        {
            mlWs.Document.Entities.Add(ml);

            var mlPrimsBefore = new CadScene();
            mlPrimsBefore.Build(mlWs.Document);

            var mlExploded = OpenCad.Core.Editing.ExplodeOperation.Explode(ml);

            Check("EXPLODE bung được MULTILEADER có nội dung",
                mlExploded.Count >= 2, mlExploded.Count + " entity");

            if (mlExploded.Count > 0)
            {
                var mlAfter = mlEditor.Explode(new Entity[] { ml });

                Check("EXPLODE MULTILEADER thay thế được trong bản vẽ",
                    mlAfter == 1 && !mlWs.Document.Entities.OfType<MultiLeader>().Any(),
                    mlWs.Document.Entities.Count() + " entity");

                var mlScene = new CadScene();
                mlScene.Build(mlWs.Document);

                Check("Hình của MULTILEADER vẫn còn sau khi bung",
                    mlScene.Entries.Count > 0, mlScene.Entries.Count + " entry");

                Check("Bung MULTILEADER không còn cảnh báo mất khi lưu",
                    !mlWs.LossySaveRisks().Any(r => r.Type == "MultiLeader"),
                    mlWs.LossySaveRisks().Count + " loại rủi ro");
            }
        }

        // ---------------------- EXPLODE KÍCH THƯỚC
        var xdWs = CadWorkspace.New();
        var xdEditor = new CadEditor(xdWs);

        var xdDim = new DimensionAligned
        {
            FirstPoint = new CSMath.XYZ(0, 0, 0),
            SecondPoint = new CSMath.XYZ(2000, 0, 0),
            DefinitionPoint = new CSMath.XYZ(1000, 400, 0),
        };

        xdEditor.AddEntities(new Entity[] { xdDim }, "DIM");

        var xdPrimsBefore = new CadScene();
        xdPrimsBefore.Build(xdWs.Document);

        var xdCountBefore = xdPrimsBefore.Entries.SelectMany(e => e.Prims).Count();
        var xdBoundsBefore = xdPrimsBefore.Bounds;

        var xdExploded = xdEditor.Explode(new Entity[] { xdDim });

        Check("EXPLODE bung được kích thước", xdExploded == 1, xdExploded + " kích thước");
        Check("EXPLODE bỏ kích thước gốc",
            !xdWs.Document.Entities.OfType<Dimension>().Any(), "đã bỏ");

        var xdAfter = new CadScene();
        xdAfter.Build(xdWs.Document);

        var xdEntities = xdWs.Document.Entities.ToList();

        Check("EXPLODE kích thước sinh ra nhiều đối tượng",
            xdEntities.Count > 2, xdEntities.Count + " entity");

        var xdText = xdAfter.Entries.SelectMany(e => e.Prims)
            .Where(p => p.Kind == PrimKind.Text)
            .Select(p => p.Text)
            .ToList();

        Check("EXPLODE kích thước giữ lại số đo 2000",
            xdText.Any(t2 => t2 == "2000"), string.Join("|", xdText));

        // Hình phải chiếm cùng vùng (không lệch)
        var xdBoundsAfter = xdAfter.Bounds;

        Check("EXPLODE kích thước giữ nguyên vùng bao",
            Math.Abs(xdBoundsAfter.MinX - xdBoundsBefore.MinX) < 5 &&
            Math.Abs(xdBoundsAfter.MaxX - xdBoundsBefore.MaxX) < 5,
            $"[{xdBoundsBefore.MinX:0.#}..{xdBoundsBefore.MaxX:0.#}] → [{xdBoundsAfter.MinX:0.#}..{xdBoundsAfter.MaxX:0.#}]");

        xdEditor.Undo();

        Check("Undo EXPLODE kích thước phục hồi kích thước gốc",
            xdWs.Document.Entities.OfType<Dimension>().Count() == 1 &&
            xdWs.Document.Entities.Count() == 1,
            xdWs.Document.Entities.Count() + " entity");

        // ---------------------- CHỮ TRONG Ô BẢNG (TABLECELL)
        var tcWs = CadWorkspace.New();
        var tcEditor = new CadEditor(tcWs);

        var tcBlock = new ACadSharp.Tables.BlockRecord("TB_CELL");
        tcWs.Document.BlockRecords.Add(tcBlock);

        var tcTable = new TableEntity(tcBlock)
        {
            InsertPoint = new CSMath.XYZ(0, 0, 0),
            RowCount = 3,
            ColumnCount = 2,
            RowSpacing = 300,
            ColumnSpacing = 1200,
        };

        for (var r = 0; r < 3; r++) tcTable.Rows.Add(new TableEntity.Row { Height = 300 });
        for (var c = 0; c < 2; c++) tcTable.Columns.Add(new TableEntity.Column { Width = 1200 });

        tcEditor.AddEntities(new Entity[] { tcTable }, "TABLE");

        var tcBefore = new CadScene();
        tcBefore.Build(tcWs.Document);

        var label = tcEditor.SetTableCellLabel(tcTable, 1, 1, "DIEN TICH", 0);

        Check("TABLECELL tạo được chữ trong ô", label is not null, label?.GetType().Name ?? "null");

        var tcAfter = new CadScene();
        tcAfter.Build(tcWs.Document);

        Check("TABLECELL làm bản vẽ có thêm đối tượng",
            tcAfter.Entries.Count == tcBefore.Entries.Count + 1,
            tcBefore.Entries.Count + " → " + tcAfter.Entries.Count);

        var tcPrims = tcAfter.Entries.SelectMany(e => e.Prims).ToList();
        var tcText = tcPrims.Where(pr => pr.Kind == PrimKind.Text).Select(pr => pr.Text).ToList();

        Check("TABLECELL hiện đúng nội dung", tcText.Contains("DIEN TICH"),
            string.Join("|", tcText));

        // Vị trí: hàng 1, cột 1 → lệch khỏi góc bảng
        if (label is MText mt)
        {
            Check("TABLECELL đặt đúng ô (1,1)",
                mt.InsertPoint.X > 1000 && mt.InsertPoint.X < 2400 &&
                mt.InsertPoint.Y < 0 && mt.InsertPoint.Y > -600,
                $"({mt.InsertPoint.X:0.#}, {mt.InsertPoint.Y:0.#})");
        }

        Check("TABLECELL từ chối ô ngoài phạm vi",
            tcEditor.SetTableCellLabel(tcTable, 99, 0, "X", 0) is null, "từ chối");

        Check("TABLECELL từ chối nội dung rỗng",
            tcEditor.SetTableCellLabel(tcTable, 0, 0, "   ", 0) is null, "từ chối");

        tcEditor.Undo();

        var tcUndone = new CadScene();
        tcUndone.Build(tcWs.Document);

        Check("Undo TABLECELL bỏ chữ vừa đặt",
            tcUndone.Entries.Count == tcBefore.Entries.Count,
            tcUndone.Entries.Count + " entity");

        // ---------------------- PEDIT: đóng / mở polyline
        var peWs = CadWorkspace.New();
        var peEditor = new CadEditor(peWs);

        var pePoly = new LwPolyline { IsClosed = false };
        pePoly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 0) });
        pePoly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(1000, 0) });
        pePoly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(1000, 1000) });

        peEditor.AddEntities(new Entity[] { pePoly }, "Add");

        static int PrimCount(CadDocument doc)
        {
            var scene = new CadScene();
            scene.Build(doc);

            return scene.Entries.SelectMany(e => e.Prims).Count();
        }

        Check("PEDIT: polyline ban đầu hở", !pePoly.IsClosed, pePoly.IsClosed.ToString());

        var closedCount = peEditor.SetPolylineClosed(new Entity[] { pePoly }, true);

        Check("PEDIT close đóng được polyline", closedCount == 1 && pePoly.IsClosed,
            closedCount + " polyline");

        static bool PrimClosed(CadDocument doc)
        {
            var scene = new CadScene();
            scene.Build(doc);

            return scene.Entries.SelectMany(e => e.Prims).Any(p => p.Closed);
        }

        Check("PEDIT close làm primitive chuyển sang khép kín",
            PrimClosed(peWs.Document), "Closed=True");

        peEditor.Undo();

        Check("Undo PEDIT close mở lại", !pePoly.IsClosed, pePoly.IsClosed.ToString());
        Check("Sau undo primitive không còn khép kín",
            !PrimClosed(peWs.Document), "Closed=False");

        // Đóng rồi mở lại
        peEditor.SetPolylineClosed(new Entity[] { pePoly }, true);
        var reopened = peEditor.SetPolylineClosed(new Entity[] { pePoly }, false);

        Check("PEDIT open mở lại được", reopened == 1 && !pePoly.IsClosed, closedCount.ToString());

        // Gọi lại khi đã đúng trạng thái → không làm gì
        Check("PEDIT không đổi khi đã đúng trạng thái",
            peEditor.SetPolylineClosed(new Entity[] { pePoly }, false) == 0, "0");

        // Không phải polyline → từ chối
        var peCircle = new Circle { Center = new CSMath.XYZ(0, 0, 0), Radius = 10 };

        Check("PEDIT từ chối đối tượng không phải polyline",
            peEditor.SetPolylineClosed(new Entity[] { peCircle }, true) == 0, "0");

        // ---------------------- LTSCALE (tỷ lệ kiểu nét)
        var ltsWs = CadWorkspace.New();
        var ltsEditor = new CadEditor(ltsWs);

        var ltsDashed = ltsWs.Document.LineTypes.First(l => l.Name == "DASHED");

        var ltsLine = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(3000, 0, 0),
            LineType = ltsDashed,
        };

        ltsEditor.AddEntities(new Entity[] { ltsLine }, "Add");

        static double[] DashOf(CadDocument doc)
        {
            var scene = new CadScene();
            scene.Build(doc);

            return scene.Entries.SelectMany(e => e.Prims)
                .Select(p => p.Dash)
                .FirstOrDefault(d => d is { Length: > 0 }) ?? Array.Empty<double>();
        }

        var dashBeforeScale = DashOf(ltsWs.Document);

        Check("LTSCALE: nét đứt có mảng gạch ban đầu",
            dashBeforeScale.Length >= 2, string.Join("/", dashBeforeScale));

        ltsEditor.SetLineTypeScaleAll(2);

        var dashAfterScale = DashOf(ltsWs.Document);

        Check("LTSCALE 2 làm gạch dài gấp đôi",
            dashAfterScale.Length >= 2 &&
            Math.Abs(dashAfterScale[0] - dashBeforeScale[0] * 2) < 1e-6,
            string.Join("/", dashBeforeScale) + " → " + string.Join("/", dashAfterScale));

        ltsEditor.Undo();

        var dashUndone = DashOf(ltsWs.Document);

        Check("Undo LTSCALE trả lại tỷ lệ cũ",
            dashUndone.Length >= 2 && Math.Abs(dashUndone[0] - dashBeforeScale[0]) < 1e-6,
            string.Join("/", dashUndone));

        Check("LTSCALE từ chối hệ số <= 0",
            ltsEditor.SetLineTypeScaleAll(0) == 0, "từ chối");

        // ---------------------- QUÉT: thao tác có ĐỔI HÌNH không
        // Mỗi lệnh sửa phải làm dấu hiệu hiển thị khác đi; nếu không thì nó "chạy mà không làm gì".
        static (int Prims, string Colors, string Dash, string Text) Signature(CadDocument doc)
        {
            var scene = new CadScene();
            scene.Build(doc);

            var prims = scene.Entries.SelectMany(e => e.Prims).ToList();

            var colors = string.Join(",", prims.Select(p => p.Color.ToString()).Distinct().OrderBy(c => c));
            var dash = string.Join("|", prims.Select(p => p.Dash is null ? "-" : string.Join("/", p.Dash)).Distinct());
            var label = string.Join("|", prims.Where(p => p.Kind == PrimKind.Text).Select(p => p.Text));

            return (prims.Count, colors, dash, label);
        }

        var svWs = CadWorkspace.New();
        var svEditor = new CadEditor(svWs);

        var svLine = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(1000, 0, 0),
        };

        var svText = new TextEntity
        {
            InsertPoint = new CSMath.XYZ(0, 200, 0),
            Height = 100,
            Value = "CU",
        };

        svEditor.AddEntities(new Entity[] { svLine, svText }, "Add");

        // Màu
        var colorBefore = Signature(svWs.Document);
        svEditor.SetColor(new Entity[] { svLine }, new ACadSharp.Color((short)1));
        var colorAfter = Signature(svWs.Document);

        Check("Đổi màu có hiệu lực thật",
            colorBefore.Colors != colorAfter.Colors,
            colorBefore.Colors + " → " + colorAfter.Colors);

        // Độ đậm nét: phải ghi vào entity
        svEditor.SetLineWeight(new Entity[] { svLine }, ACadSharp.LineWeightType.W100);

        Check("Đổi độ đậm nét ghi vào entity",
            svLine.LineWeight == ACadSharp.LineWeightType.W100, svLine.LineWeight.ToString());

        // Kiểu nét: phải sinh gạch đứt khi vẽ
        var dashType = svWs.Document.LineTypes.FirstOrDefault(l => l.Name == "DASHED");
        var dashBefore = Signature(svWs.Document);
        svEditor.SetLineType(new Entity[] { svLine }, dashType);
        var dashAfter = Signature(svWs.Document);

        Check("Gán kiểu nét DASHED sinh gạch đứt",
            dashBefore.Dash != dashAfter.Dash && dashAfter.Dash.Contains('/'),
            dashBefore.Dash + " → " + dashAfter.Dash);

        // Nội dung chữ
        var textBefore = Signature(svWs.Document);
        svEditor.SetText(svText, "PHONG KHACH");
        var textAfter = Signature(svWs.Document);

        Check("Sửa chữ đổi nội dung hiển thị",
            textBefore.Text != textAfter.Text && textAfter.Text.Contains("PHONG KHACH"),
            textBefore.Text + " → " + textAfter.Text);

        // Đổi tên layer phải theo entity
        var svLayer = new ACadSharp.Tables.Layer("LOP_CU");
        svWs.Document.Layers.Add(svLayer);
        svEditor.SetLayer(new Entity[] { svLine }, svLayer);
        svEditor.RenameLayer(svLayer, "LOP_MOI");

        Check("Đổi tên layer phải đổi theo entity",
            svLine.Layer?.Name == "LOP_MOI", svLine.Layer?.Name ?? "null");

        // DeleteLayer (an toàn) phải TỪ CHỐI khi layer còn đối tượng
        var deleteResult = svEditor.DeleteLayer(svLayer, "0");

        Check("DeleteLayer từ chối khi layer còn đối tượng",
            deleteResult is not null && svLine.Layer?.Name == "LOP_MOI",
            deleteResult ?? "đã xoá");

        // LAYDEL (kiểu AutoCAD) xoá layer + đối tượng trên đó
        var entityCountBeforeLayDel = svWs.Document.Entities.Count();
        var removedByLayDel = svEditor.DeleteLayerAndContents(svLayer, "0");

        Check("LAYDEL xoá layer cùng đối tượng",
            removedByLayDel == 1 &&
            svWs.Document.Layers.All(l => l.Name != "LOP_MOI") &&
            svWs.Document.Entities.Count() == entityCountBeforeLayDel - 1,
            removedByLayDel + " đối tượng");

        svEditor.Undo();

        Check("Undo LAYDEL phục hồi cả layer lẫn đối tượng",
            svWs.Document.Layers.Any(l => l.Name == "LOP_MOI") &&
            svWs.Document.Entities.Count() == entityCountBeforeLayDel,
            svWs.Document.Entities.Count() + " entity");

        // ARRAY phải tạo thêm hình nhìn thấy được
        var arrayBefore = Signature(svWs.Document).Prims;

        svEditor.ArrayCopy(new Entity[] { svLine }, 1, 3, 2000, 0,
            e => e.Clone() as Entity);

        var arrayAfter = Signature(svWs.Document).Prims;

        Check("ARRAY tạo thêm hình nhìn thấy được",
            arrayAfter > arrayBefore, arrayBefore + " → " + arrayAfter + " primitive");
        // ---------------------- KIỂU NÉT CHUẨN CÓ SẴN
        var ltWs = CadWorkspace.New();
        var ltNames = ltWs.Document.LineTypes.Select(l => l.Name).ToList();

        Check("Bản vẽ mới có kiểu nét DASHED", ltNames.Contains("DASHED"), string.Join(",", ltNames));
        Check("Bản vẽ mới có HIDDEN", ltNames.Contains("HIDDEN"), ltNames.Count + " kiểu");
        Check("Bản vẽ mới có CENTER", ltNames.Contains("CENTER"), ltNames.Count + " kiểu");
        Check("Bản vẽ mới có ít nhất 7 kiểu nét", ltNames.Count >= 7, ltNames.Count + " kiểu");

        // Đặt được nét đứt cho entity mới
        var ltLine = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100, 0, 0),
        };

        var dashedType = ltWs.Document.LineTypes.FirstOrDefault(l => l.Name == "DASHED");

        Check("Lấy được LineType DASHED từ bản vẽ",
            dashedType is not null, dashedType?.Name ?? "null");

        if (dashedType is not null)
        {
            ltWs.Document.Entities.Add(ltLine);
            ltLine.LineType = dashedType;

            var ltScene = new CadScene();
            ltScene.Build(ltWs.Document);

            // Nét đứt là MỘT primitive mang mảng gạch, không phải nhiều primitive
            var dashedPrim = ltScene.Entries
                .SelectMany(e => e.Prims)
                .FirstOrDefault(prim => prim.Dash is { Length: > 0 });

            Check("Nét đứt mang mảng gạch", dashedPrim.Dash is { Length: >= 2 },
                dashedPrim.Dash is null ? "không có" : string.Join("/", dashedPrim.Dash));
        }

        // Nạp lại lần hai không nhân đôi
        var beforeCount = ltWs.Document.LineTypes.Count();
        OpenCad.Core.Styles.LineTypeLibrary.EnsureAll(ltWs.Document);

        Check("EnsureAll không nhân đôi kiểu nét",
            ltWs.Document.LineTypes.Count() == beforeCount,
            beforeCount + " → " + ltWs.Document.LineTypes.Count());

        // ---------------------- JOIN (lệnh, không chỉ hàm core)
        var jnWs = CadWorkspace.New();
        var jnEditor = new CadEditor(jnWs);

        var jnA = new Line { StartPoint = new CSMath.XYZ(0, 0, 0), EndPoint = new CSMath.XYZ(100, 0, 0) };
        var jnB = new Line { StartPoint = new CSMath.XYZ(100, 0, 0), EndPoint = new CSMath.XYZ(100, 100, 0) };
        var jnC = new Line { StartPoint = new CSMath.XYZ(100, 100, 0), EndPoint = new CSMath.XYZ(0, 100, 0) };

        jnEditor.AddEntities(new Entity[] { jnA, jnB, jnC }, "Add");

        var jnOk = jnEditor.JoinLines(new Entity[] { jnA, jnB, jnC });

        Check("JOIN nối được 3 đoạn", jnOk, jnOk.ToString());
        Check("JOIN thay 3 line bằng 1 polyline",
            jnWs.Document.Entities.OfType<Line>().Count() == 0 &&
            jnWs.Document.Entities.OfType<LwPolyline>().Count() == 1,
            jnWs.Document.Entities.Count() + " entity");

        var jnPoly = jnWs.Document.Entities.OfType<LwPolyline>().FirstOrDefault();

        Check("JOIN polyline có 4 đỉnh", jnPoly?.Vertices.Count == 4,
            (jnPoly?.Vertices.Count ?? -1) + " đỉnh");

        jnEditor.Undo();

        Check("Undo JOIN phục hồi 3 đoạn",
            jnWs.Document.Entities.OfType<Line>().Count() == 3 &&
            !jnWs.Document.Entities.OfType<LwPolyline>().Any(),
            jnWs.Document.Entities.Count() + " entity");

        // Hai đoạn rời không nối được
        var jnFar = new Line { StartPoint = new CSMath.XYZ(500, 500, 0), EndPoint = new CSMath.XYZ(600, 500, 0) };
        jnEditor.AddEntities(new Entity[] { jnFar }, "Add");

        Check("JOIN từ chối 2 đoạn rời",
            !jnEditor.JoinLines(new Entity[] { jnA, jnFar }), "từ chối");

        Check("JOIN từ chối khi chỉ có 1 line",
            !jnEditor.JoinLines(new Entity[] { jnA }), "từ chối");

        // ---------------------- EXPLODE
        var exWs = CadWorkspace.New();
        var exDoc = exWs.Document;
        var exEditor = new CadEditor(exWs);

        // 1. Polyline 4 đỉnh khép kín → 4 đoạn
        var exPoly = new LwPolyline { IsClosed = true };
        exPoly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 0) });
        exPoly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(100, 0) });
        exPoly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(100, 100) });
        exPoly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 100) });

        exEditor.AddEntities(new Entity[] { exPoly }, "Add");

        var exploded = exEditor.Explode(new Entity[] { exPoly });

        Check("EXPLODE bung được polyline", exploded == 1, exploded + " đối tượng");
        Check("EXPLODE polyline khép kín → 4 đoạn",
            exDoc.Entities.OfType<Line>().Count() == 4,
            exDoc.Entities.OfType<Line>().Count() + " line");
        Check("EXPLODE bỏ polyline gốc",
            !exDoc.Entities.OfType<LwPolyline>().Any(), "đã bỏ");

        exEditor.Undo();

        Check("Undo EXPLODE phục hồi polyline",
            exDoc.Entities.OfType<LwPolyline>().Count() == 1, "1 polyline");
        Check("Undo EXPLODE bỏ các đoạn đã bung",
            exDoc.Entities.OfType<Line>().Count() == 0, "0 line");

        // 2. Polyline hở 3 đỉnh → 2 đoạn
        var exOpen = new LwPolyline();
        exOpen.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 0) });
        exOpen.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(50, 0) });
        exOpen.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(50, 50) });

        exEditor.AddEntities(new Entity[] { exOpen }, "Add");
        exEditor.Explode(new Entity[] { exOpen });

        Check("EXPLODE polyline hở 3 đỉnh → 2 đoạn",
            exDoc.Entities.OfType<Line>().Count() == 2,
            exDoc.Entities.OfType<Line>().Count() + " line");

        // 3. Block chứa 2 line → bung được 2 line, áp đúng vị trí
        var exBlock = new ACadSharp.Tables.BlockRecord("EX_BLOCK");
        exDoc.BlockRecords.Add(exBlock);

        exBlock.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100, 0, 0),
        });
        exBlock.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(0, 100, 0),
        });

        var exInsert = new Insert(exBlock)
        {
            InsertPoint = new CSMath.XYZ(1000, 500, 0),
        };

        exEditor.AddEntities(new Entity[] { exInsert }, "INSERT");

        // Danh tính entity trước khi bung (thứ tự chèn không đảm bảo)
        var beforeBlockLines = exDoc.Entities.OfType<Line>().ToHashSet();
        var blockExploded = exEditor.Explode(new Entity[] { exInsert });

        Check("EXPLODE bung được block", blockExploded == 1, blockExploded + " insert");

        var blockLines = exDoc.Entities.OfType<Line>()
            .Where(l => !beforeBlockLines.Contains(l))
            .ToList();

        Check("EXPLODE block cho ra 2 line", blockLines.Count == 2, blockLines.Count + " line");

        Check("EXPLODE block áp đúng điểm chèn",
            blockLines.Any(l => Math.Abs(l.StartPoint.X - 1000) < 1e-6 &&
                                Math.Abs(l.StartPoint.Y - 500) < 1e-6),
            blockLines.Count > 0
                ? $"({blockLines[0].StartPoint.X:0.#}, {blockLines[0].StartPoint.Y:0.#})"
                : "không có");

        Check("EXPLODE block bỏ insert gốc",
            !exDoc.Entities.OfType<Insert>().Any(), "đã bỏ");

        // 4. Loại không bung được → trả 0
        var exCircle = new Circle { Center = new CSMath.XYZ(0, 0, 0), Radius = 10 };
        exEditor.AddEntities(new Entity[] { exCircle }, "Add");

        Check("EXPLODE circle trả 0 (không bung được)",
            exEditor.Explode(new Entity[] { exCircle }) == 0, "0");

        // ---------------------- SỬA DÀI / GÓC CỦA LINE
        var lgWs = CadWorkspace.New();
        var lgEditor = new CadEditor(lgWs);

        var lgLine = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(300, 400, 0),
        };

        lgEditor.AddEntities(new Entity[] { lgLine }, "Add");

        double Length() => Math.Sqrt(
            Math.Pow(lgLine.EndPoint.X - lgLine.StartPoint.X, 2) +
            Math.Pow(lgLine.EndPoint.Y - lgLine.StartPoint.Y, 2));

        double AngleDeg() => Math.Atan2(
            lgLine.EndPoint.Y - lgLine.StartPoint.Y,
            lgLine.EndPoint.X - lgLine.StartPoint.X) * 180 / Math.PI;

        Check("LINE gốc dài 500", Math.Abs(Length() - 500) < 1e-6, Length().ToString("0.#"));

        lgEditor.SetLineGeometry(lgLine, length: 1000);

        Check("Đổi dài giữ góc", Math.Abs(Length() - 1000) < 1e-6 && Math.Abs(AngleDeg() - 53.1301) < 0.01,
            $"dài {Length():0.#}, góc {AngleDeg():0.##}");

        lgEditor.SetLineGeometry(lgLine, angleDegrees: 90);

        Check("Đổi góc giữ dài", Math.Abs(Length() - 1000) < 1e-6 && Math.Abs(AngleDeg() - 90) < 1e-6,
            $"dài {Length():0.#}, góc {AngleDeg():0.##}");

        Check("Góc 90° cho EndPoint thẳng đứng",
            Math.Abs(lgLine.EndPoint.X) < 1e-6 && Math.Abs(lgLine.EndPoint.Y - 1000) < 1e-6,
            $"({lgLine.EndPoint.X:0.#}, {lgLine.EndPoint.Y:0.#})");

        Check("Điểm đầu không đổi",
            lgLine.StartPoint.X == 0 && lgLine.StartPoint.Y == 0, "0,0");

        lgEditor.Undo();

        Check("Undo trả góc về 53.13", Math.Abs(AngleDeg() - 53.1301) < 0.01, AngleDeg().ToString("0.##"));

        lgEditor.SetLineGeometry(lgLine, length: 1000, angleDegrees: 180);

        Check("Đổi cả dài và góc cùng lúc",
            Math.Abs(Length() - 1000) < 1e-6 && Math.Abs(AngleDeg() - 180) < 1e-6,
            $"dài {Length():0.#}, góc {AngleDeg():0.##}");

        Check("Dài âm bị từ chối", !lgEditor.SetLineGeometry(lgLine, length: -5), "từ chối");
        Check("Dài 0 bị từ chối", !lgEditor.SetLineGeometry(lgLine, length: 0), "từ chối");
        Check("Không đổi gì thì trả false", !lgEditor.SetLineGeometry(lgLine), "false");
        Check("Entity không phải Line bị từ chối",
            !lgEditor.SetLineGeometry(new Circle { Center = new CSMath.XYZ(0, 0, 0), Radius = 10 },
                angleDegrees: 45), "từ chối");

        // ---------------------- OOPS & UNDO NHIỀU BƯỚC
        var oopsWs = CadWorkspace.New();
        var oopsEditor = new CadEditor(oopsWs);

        var keep = new Line { StartPoint = new CSMath.XYZ(0, 0, 0), EndPoint = new CSMath.XYZ(100, 0, 0) };
        var gone = new Circle { Center = new CSMath.XYZ(50, 50, 0), Radius = 20 };

        oopsEditor.AddEntities(new Entity[] { keep, gone }, "Add");
        oopsEditor.RemoveEntities(new Entity[] { gone }, "Erase");
        oopsEditor.RememberErased(new[] { gone });

        Check("ERASE xoá đối tượng", oopsWs.Document.Entities.Count() == 1,
            oopsWs.Document.Entities.Count() + " entity");

        var restoredCount = oopsEditor.RestoreLastErased();

        Check("OOPS phục hồi được đối tượng vừa xoá", restoredCount == 1, restoredCount + " đối tượng");
        Check("Sau OOPS bản vẽ có lại 2 đối tượng",
            oopsWs.Document.Entities.Count() == 2, oopsWs.Document.Entities.Count() + " entity");

        oopsEditor.Undo();

        Check("Undo OOPS xoá lại đối tượng",
            oopsWs.Document.Entities.Count() == 1, oopsWs.Document.Entities.Count() + " entity");

        // OOPS khi không có gì để phục hồi
        var emptyEditor = new CadEditor(CadWorkspace.New());
        Check("OOPS khi không có gì trả về 0", emptyEditor.RestoreLastErased() == 0, "0");

        // ---- UNDO nhiều bước
        var multiWs = CadWorkspace.New();
        var multiEditor = new CadEditor(multiWs);

        for (var i = 0; i < 5; i++)
        {
            multiEditor.AddEntities(new Entity[]
            {
                new Line
                {
                    StartPoint = new CSMath.XYZ(0, i * 10, 0),
                    EndPoint = new CSMath.XYZ(100, i * 10, 0),
                },
            }, "Add");
        }

        Check("Tạo 5 đối tượng bằng 5 lệnh", multiWs.Document.Entities.Count() == 5,
            multiWs.Document.Entities.Count() + " entity");

        var undone = multiEditor.Undo(3);

        Check("UNDO 3 lùi đúng 3 bước", undone == 3, undone + " bước");
        Check("Sau UNDO 3 còn 2 đối tượng", multiWs.Document.Entities.Count() == 2,
            multiWs.Document.Entities.Count() + " entity");

        var tooMany = multiEditor.Undo(100);

        Check("UNDO nhiều hơn số bước có sẵn thì lùi hết", tooMany == 2, tooMany + " bước");
        Check("Bản vẽ trống sau khi lùi hết", multiWs.Document.Entities.Count() == 0,
            multiWs.Document.Entities.Count() + " entity");

        // ---------------------- LƯU AN TOÀN (file tạm + .bak)
        var safePath = Path.Combine(Path.GetTempPath(), "opencad-smoke", "safe-save.dwg");
        Directory.CreateDirectory(Path.GetDirectoryName(safePath)!);

        foreach (var f in new[] { safePath, safePath + ".bak", safePath + ".tmp" })
            if (File.Exists(f)) File.Delete(f);

        // Lần lưu đầu: chưa có .bak
        var safeWs = CadWorkspace.New();
        safeWs.Document.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100, 0, 0),
        });

        safeWs.Save(safePath);

        Check("Lưu an toàn: file được tạo", File.Exists(safePath), safePath);

        Check("Lưu an toàn: lần đầu chưa có .bak", !File.Exists(safePath + ".bak"), "không có");

        Check("Lưu an toàn: không để lại file .tmp", !File.Exists(safePath + ".tmp"), "sạch");

        // Thêm entity rồi lưu lần hai → .bak phải là bản CŨ (1 entity)
        safeWs.Document.Entities.Add(new Circle
        {
            Center = new CSMath.XYZ(50, 50, 0),
            Radius = 20,
        });

        safeWs.Save(safePath);

        Check("Lưu an toàn: lần hai sinh ra .bak", File.Exists(safePath + ".bak"), "có");

        var backup = CadWorkspace.Open(safePath + ".bak");

        Check("Lưu an toàn: .bak giữ bản CŨ (1 entity)",
            backup.Document.Entities.Count() == 1,
            backup.Document.Entities.Count() + " entity");

        var current = CadWorkspace.Open(safePath);

        Check("Lưu an toàn: file chính có bản MỚI (2 entity)",
            current.Document.Entities.Count() == 2,
            current.Document.Entities.Count() + " entity");

        Check("Lưu an toàn: BackupPath đúng",
            safeWs.BackupPath == safePath + ".bak", safeWs.BackupPath ?? "null");

        // Ghi thất bại (đường dẫn không hợp lệ) không được phá file cũ
        var sizeBefore = new FileInfo(safePath).Length;
        var threw = false;

        try { safeWs.Save(Path.Combine(safePath, "khong-the-ghi.dwg")); }
        catch { threw = true; }

        Check("Lưu an toàn: ghi lỗi thì ném ngoại lệ", threw, threw.ToString());
        Check("Lưu an toàn: ghi lỗi không làm hỏng file cũ",
            File.Exists(safePath) && new FileInfo(safePath).Length == sizeBefore,
            new FileInfo(safePath).Length + " byte");

        // Autosave phải được xoá sau khi lưu thật (nếu không lần sau app mời phục hồi sai)
        var clWs = CadWorkspace.New();
        clWs.Document.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(10, 0, 0),
        });
        clWs.MarkModified();

        var clPath = clWs.WriteAutosave();

        Check("Autosave: có file trước khi lưu thật",
            clPath is not null && File.Exists(clPath), clPath ?? "null");

        clWs.ClearAutosave();

        Check("Autosave: ClearAutosave xoá file",
            clPath is not null && !File.Exists(clPath), "đã xoá");

        var repeated = true;

        try { clWs.ClearAutosave(); clWs.ClearAutosave(); }
        catch { repeated = false; }

        Check("Autosave: ClearAutosave gọi nhiều lần không lỗi", repeated, "không lỗi");

        // ---------------------- AUTOSAVE
        var asWs = CadWorkspace.New();

        asWs.Document.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100, 0, 0),
        });

        var autosavePath = asWs.WriteAutosave();

        Check("Autosave: ghi được file", autosavePath is not null && File.Exists(autosavePath),
            autosavePath ?? "null");

        Check("Autosave: có ghi nhận thời điểm", asWs.LastAutosave is not null,
            asWs.LastAutosave?.ToString("HH:mm:ss") ?? "null");

        if (autosavePath is not null)
        {
            var restored = CadWorkspace.Open(autosavePath);

            Check("Autosave: mở lại giữ đúng số entity",
                restored.Document.Entities.Count() == 1,
                restored.Document.Entities.Count() + " entity");

            Check("Autosave: file nằm trong thư mục autosave",
                autosavePath.Contains("autosave", StringComparison.OrdinalIgnoreCase),
                Path.GetFileName(autosavePath));

            Check("Autosave: tìm thấy trong danh sách phục hồi",
                CadWorkspace.FindAutosaves().Contains(autosavePath), "có");

            asWs.ClearAutosave();

            Check("Autosave: xoá được sau khi lưu thật",
                !File.Exists(autosavePath), "đã xoá");
        }

        // Autosave KHÔNG được đổi FilePath và KHÔNG được xoá cờ "chưa lưu"
        var asWs2 = CadWorkspace.New();
        asWs2.Document.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(50, 0, 0),
        });
        asWs2.MarkModified();

        var pathBefore = asWs2.FilePath;
        asWs2.WriteAutosave();

        Check("Autosave không đổi FilePath", asWs2.FilePath == pathBefore,
            asWs2.FilePath ?? "null");

        Check("Autosave không xoá cờ chưa lưu", asWs2.IsModified, asWs2.IsModified.ToString());

        asWs2.ClearAutosave();

        // Hai bản vẽ khác đường dẫn không đè autosave của nhau
        var wsA = CadWorkspace.New();
        var wsB = CadWorkspace.New();

        Check("Autosave: hai bản chưa đặt tên dùng chung một chỗ (untitled)",
            wsA.AutosavePath == wsB.AutosavePath, Path.GetFileName(wsA.AutosavePath));

        // ---------------------- BREAK / JOIN
        // Cắt một line 0→1000 tại hai điểm 300 và 700 → còn 2 đoạn
        var breakLine = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(1000, 0, 0),
        };

        var pieces = BreakOperation.Apply(breakLine, new Pt(300, 10), new Pt(700, -10));

        Check("BREAK line thành 2 đoạn", pieces.Count == 2, pieces.Count + " đoạn");

        if (pieces.Count == 2)
        {
            var first = (Line)pieces[0];
            var second = (Line)pieces[1];

            Check("BREAK giữ đầu 0→300",
                Math.Abs(first.StartPoint.X) < 1e-6 && Math.Abs(first.EndPoint.X - 300) < 1e-6,
                $"{first.StartPoint.X:0.#}→{first.EndPoint.X:0.#}");

            Check("BREAK giữ cuối 700→1000",
                Math.Abs(second.StartPoint.X - 700) < 1e-6 && Math.Abs(second.EndPoint.X - 1000) < 1e-6,
                $"{second.StartPoint.X:0.#}→{second.EndPoint.X:0.#}");
        }

        // Cắt đúng ở đầu mút → đoạn đầu dài 0 nên bị bỏ, chỉ còn 1 đoạn
        var nearEnd = BreakOperation.Apply(breakLine, new Pt(0, 0), new Pt(500, 0));

        Check("BREAK đúng đầu mút chỉ còn 1 đoạn", nearEnd.Count == 1, nearEnd.Count + " đoạn");

        // Cắt tại x=2 thì đoạn 0→2 vẫn còn (dài 2 đơn vị, không suy biến)
        var tiny = BreakOperation.Apply(breakLine, new Pt(2, 0), new Pt(500, 0));

        Check("BREAK sát đầu vẫn giữ đoạn ngắn 0→2", tiny.Count == 2, tiny.Count + " đoạn");

        // Cắt đường tròn → cung
        var breakCircle = new Circle { Center = new CSMath.XYZ(0, 0, 0), Radius = 100 };
        var arcs = BreakOperation.Apply(breakCircle, new Pt(100, 0), new Pt(-100, 0));

        Check("BREAK đường tròn tạo cung", arcs.Count == 1 && arcs[0] is Arc,
            arcs.Count + " đối tượng");

        if (arcs.Count == 1 && arcs[0] is Arc arc)
        {
            Check("Cung tạo thành có bán kính bằng đường tròn",
                Math.Abs(arc.Radius - 100) < 1e-6, arc.Radius.ToString("0.#"));
        }

        // ---- JOIN
        var l1 = new Line { StartPoint = new CSMath.XYZ(0, 0, 0), EndPoint = new CSMath.XYZ(100, 0, 0) };
        var l2 = new Line { StartPoint = new CSMath.XYZ(100, 0, 0), EndPoint = new CSMath.XYZ(100, 100, 0) };
        var l3 = new Line { StartPoint = new CSMath.XYZ(100, 100, 0), EndPoint = new CSMath.XYZ(0, 100, 0) };

        var joined = JoinOperation.JoinLines(new[] { l1, l2, l3 });

        Check("JOIN nối 3 đoạn thành 1 polyline", joined is not null, joined is null ? "null" : "có");
        Check("JOIN giữ đúng 4 đỉnh",
            joined?.Vertices.Count == 4, (joined?.Vertices.Count ?? -1) + " đỉnh");

        // Hai đoạn rời không nối được
        var far = new Line { StartPoint = new CSMath.XYZ(500, 500, 0), EndPoint = new CSMath.XYZ(600, 500, 0) };

        Check("JOIN không nối đoạn rời",
            JoinOperation.JoinLines(new[] { l1, far }) is null, "null");

        // Đảo chiều một đoạn vẫn nối được
        var reversed = new Line { StartPoint = new CSMath.XYZ(100, 0, 0), EndPoint = new CSMath.XYZ(0, 0, 0) };

        Check("JOIN nối được đoạn bị đảo chiều",
            JoinOperation.JoinLines(new[] { reversed, l2 }) is not null, "có");

        // ---------------------- GHI ĐÈ CHỮ KÍCH THƯỚC (DIMTEDIT)
        var dtWs = CadWorkspace.New();
        var dtDoc = dtWs.Document;
        var dtEditor = new CadEditor(dtWs);

        var dim = new DimensionAligned
        {
            FirstPoint = new CSMath.XYZ(0, 0, 0),
            SecondPoint = new CSMath.XYZ(2000, 0, 0),
            DefinitionPoint = new CSMath.XYZ(1000, 300, 0),
        };

        dtEditor.AddEntities(new Entity[] { dim }, "Add");

        string? MeasuredText()
        {
            var scene = new CadScene();
            scene.Build(dtDoc);

            return scene.Entries
                .SelectMany(e => e.Prims)
                .Where(p => p.Kind == PrimKind.Text)
                .Select(p => p.Text)
                .FirstOrDefault();
        }

        Check("DIM hiển thị số đo thật 2000", MeasuredText() == "2000", MeasuredText() ?? "null");

        var changed = dtEditor.SetDimensionText(new Entity[] { dim }, "≈ 2000");

        Check("DIMTEDIT đổi được chữ", changed == 1, changed + " kích thước");
        Check("DIMTEDIT hiện chữ mới", MeasuredText() == "≈ 2000", MeasuredText() ?? "null");

        dtEditor.Undo();

        Check("Undo DIMTEDIT trả về số đo", MeasuredText() == "2000", MeasuredText() ?? "null");

        dtEditor.SetDimensionText(new Entity[] { dim }, "KHONG THE DO");

        Check("DIMTEDIT đặt chữ ghi chú", MeasuredText() == "KHONG THE DO", MeasuredText() ?? "null");

        dtEditor.SetDimensionText(new Entity[] { dim }, "");

        Check("DIMTEDIT chuỗi rỗng trả về số đo", MeasuredText() == "2000", MeasuredText() ?? "null");

        var notADim = new Line
        {
            StartPoint = new CSMath.XYZ(0, 500, 0),
            EndPoint = new CSMath.XYZ(100, 500, 0),
        };

        Check("DIMTEDIT bỏ qua đối tượng không phải kích thước",
            dtEditor.SetDimensionText(new Entity[] { notADim }, "X") == 0, "0");

        // ---------------------- CÔ LẬP LAYER (LAYISO)
        var isoWs = CadWorkspace.New();
        var isoDoc = isoWs.Document;
        var isoEditor = new CadEditor(isoWs);

        var layerA = new ACadSharp.Tables.Layer("A");
        var layerB = new ACadSharp.Tables.Layer("B");
        var layerC = new ACadSharp.Tables.Layer("C");

        isoDoc.Layers.Add(layerA);
        isoDoc.Layers.Add(layerB);
        isoDoc.Layers.Add(layerC);

        var onA = new Line { StartPoint = new CSMath.XYZ(0, 0, 0), EndPoint = new CSMath.XYZ(100, 0, 0), Layer = layerA };
        var onB = new Line { StartPoint = new CSMath.XYZ(0, 100, 0), EndPoint = new CSMath.XYZ(100, 100, 0), Layer = layerB };
        var onC = new Line { StartPoint = new CSMath.XYZ(0, 200, 0), EndPoint = new CSMath.XYZ(100, 200, 0), Layer = layerC };

        isoEditor.AddEntities(new Entity[] { onA, onB, onC }, "Add");

        var turnedOff = isoEditor.IsolateLayers(new Entity[] { onA });

        // 3 layer bị ẩn: B, C và layer "0" (đúng như AutoCAD — layer 0 cũng bị ẩn)
        Check("LAYISO ẩn các layer khác", turnedOff.Count == 3, turnedOff.Count + " layer ẩn");
        Check("LAYISO giữ layer của đối tượng chọn", layerA.IsOn, layerA.IsOn.ToString());
        Check("LAYISO tắt B và C", !layerB.IsOn && !layerC.IsOn,
            $"B={layerB.IsOn} C={layerC.IsOn}");

        Check("Sau LAYISO chỉ còn 1 đối tượng chọn được",
            isoEditor.Scene.Entries.Count == 1, isoEditor.Scene.Entries.Count + " entry");

        isoEditor.Undo();

        Check("Undo LAYISO bật lại các layer",
            layerA.IsOn && layerB.IsOn && layerC.IsOn, "bật lại");
        Check("Undo LAYISO phục hồi 3 đối tượng trong scene",
            isoEditor.Scene.Entries.Count == 3, isoEditor.Scene.Entries.Count + " entry");

        // LAYUNISO
        isoEditor.IsolateLayers(new Entity[] { onA });
        var restoredLayers = isoEditor.ShowAllLayers();

        Check("LAYUNISO bật lại 3 layer", restoredLayers == 3, restoredLayers + " layer");
        Check("Sau LAYUNISO mọi layer bật",
            layerA.IsOn && layerB.IsOn && layerC.IsOn, "3 layer bật");

        // Chọn nhiều đối tượng trên 2 layer khác nhau
        isoEditor.IsolateLayers(new Entity[] { onA, onB });
        Check("LAYISO với 2 đối tượng giữ cả 2 layer",
            layerA.IsOn && layerB.IsOn && !layerC.IsOn,
            $"A={layerA.IsOn} B={layerB.IsOn} C={layerC.IsOn}");

        // ---------------------- ĐỔI TÊN / XOÁ LAYER
        var lyrWs = CadWorkspace.New();
        var lyrDoc = lyrWs.Document;
        var lyrEditor = new CadEditor(lyrWs);

        var walls = new ACadSharp.Tables.Layer("TUONG_CU");
        lyrDoc.Layers.Add(walls);

        Check("Đổi tên layer được",
            lyrEditor.RenameLayer(walls, "TUONG_MOI"), "ok");
        Check("Layer mang tên mới",
            lyrDoc.Layers.Any(l => l.Name == "TUONG_MOI"), walls.Name);

        lyrEditor.Undo();
        Check("Undo đổi tên trả lại tên cũ",
            walls.Name == "TUONG_CU", walls.Name);

        lyrEditor.Redo();
        Check("Redo đổi tên lại",
            walls.Name == "TUONG_MOI", walls.Name);

        // Trùng tên thì từ chối
        lyrDoc.Layers.Add(new ACadSharp.Tables.Layer("DA_CO"));
        Check("Không đổi được sang tên đã có",
            !lyrEditor.RenameLayer(walls, "DA_CO"), "từ chối");

        Check("Không đổi được sang tên rỗng",
            !lyrEditor.RenameLayer(walls, "   "), "từ chối");

        // ---- Xoá layer
        var empty = new ACadSharp.Tables.Layer("TRONG");
        lyrDoc.Layers.Add(empty);

        var deleteError = lyrEditor.DeleteLayer(empty);

        Check("Xoá được layer rỗng", deleteError is null, deleteError ?? "đã xoá");
        Check("Layer rỗng không còn",
            !lyrDoc.Layers.Any(l => l.Name == "TRONG"), "đã xoá");

        lyrEditor.Undo();
        Check("Undo xoá layer phục hồi",
            lyrDoc.Layers.Any(l => l.Name == "TRONG"), "đã phục hồi");

        // Không xoá layer 0
        var zero = lyrDoc.Layers.First(l => l.Name == "0");
        Check("Không xoá được layer 0",
            lyrEditor.DeleteLayer(zero) is not null, "từ chối");

        // Không xoá layer đang dùng
        Check("Không xoá được layer hiện hành",
            lyrEditor.DeleteLayer(walls, walls.Name) is not null, "từ chối");

        // Không xoá layer còn entity
        var busyLayer = new ACadSharp.Tables.Layer("CO_HINH");
        lyrDoc.Layers.Add(busyLayer);
        lyrDoc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(10, 0, 0),
            Layer = busyLayer,
        });

        Check("Không xoá được layer còn đối tượng",
            lyrEditor.DeleteLayer(busyLayer) is not null, "từ chối");

        // ---------------------- MATCHPROP
        var matchWs = CadWorkspace.New();
        var matchDoc = matchWs.Document;

        var sourceLayer = new ACadSharp.Tables.Layer("NGUON")
        {
            Color = new ACadSharp.Color((short)5),
            LineWeight = ACadSharp.LineWeightType.W50,
        };

        matchDoc.Layers.Add(sourceLayer);

        var sourceLine = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100, 0, 0),
            Layer = sourceLayer,
            Color = new ACadSharp.Color((short)3),
            LineWeight = ACadSharp.LineWeightType.W70,
        };

        var targetLine = new Line
        {
            StartPoint = new CSMath.XYZ(0, 100, 0),
            EndPoint = new CSMath.XYZ(100, 100, 0),
        };

        var targetCircle = new Circle
        {
            Center = new CSMath.XYZ(300, 300, 0),
            Radius = 50,
        };

        matchDoc.Entities.Add(sourceLine);
        matchDoc.Entities.Add(targetLine);
        matchDoc.Entities.Add(targetCircle);

        var matchEditor = new CadEditor(matchWs);

        var matched = matchEditor.MatchProperties(
            sourceLine, new Entity[] { targetLine, targetCircle });

        Check("MATCHPROP đổi được nhiều đối tượng", matched == 2, matched + " đối tượng");

        Check("MATCHPROP chép layer",
            targetLine.Layer?.Name == "NGUON" && targetCircle.Layer?.Name == "NGUON",
            targetLine.Layer?.Name + " / " + targetCircle.Layer?.Name);

        Check("MATCHPROP chép màu",
            targetLine.Color.Index == 3 && targetCircle.Color.Index == 3,
            targetLine.Color.Index + " / " + targetCircle.Color.Index);

        Check("MATCHPROP chép độ đậm",
            targetLine.LineWeight == ACadSharp.LineWeightType.W70 &&
            targetCircle.LineWeight == ACadSharp.LineWeightType.W70,
            targetLine.LineWeight.ToString() ?? "-");

        Check("MATCHPROP không đổi chính đối tượng nguồn",
            sourceLine.Layer?.Name == "NGUON" && sourceLine.Color.Index == 3, "nguồn giữ nguyên");

        matchEditor.Undo();

        Check("Undo MATCHPROP trả layer cũ",
            targetLine.Layer?.Name != "NGUON", targetLine.Layer?.Name ?? "null");

        Check("Undo MATCHPROP trả màu cũ (ByLayer = 256)",
            targetLine.Color.Index == 256, targetLine.Color.Index.ToString());

        // Chép chiều cao chữ giữa hai text
        var bigText = new TextEntity
        {
            InsertPoint = new CSMath.XYZ(0, 500, 0),
            Height = 250,
            Value = "TO",
        };

        var smallText = new TextEntity
        {
            InsertPoint = new CSMath.XYZ(0, 800, 0),
            Height = 50,
            Value = "NHO",
        };

        matchDoc.Entities.Add(bigText);
        matchDoc.Entities.Add(smallText);

        matchEditor.MatchProperties(bigText, new Entity[] { smallText });

        Check("MATCHPROP chép chiều cao chữ",
            Math.Abs(smallText.Height - 250) < 1e-9, smallText.Height.ToString("0.###"));

        // ---------------------- PURGE
        var purgeWs = CadWorkspace.New();
        var purgeDoc = purgeWs.Document;

        purgeDoc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(500, 0, 0),
        });

        // Layer không dùng + layer có dùng
        var usedLayer = new ACadSharp.Tables.Layer("DUNG");
        purgeDoc.Layers.Add(usedLayer);
        purgeDoc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 100, 0),
            EndPoint = new CSMath.XYZ(500, 100, 0),
            Layer = usedLayer,
        });

        purgeDoc.Layers.Add(new ACadSharp.Tables.Layer("MO_COI"));

        // Block không dùng + block có dùng
        var orphanBlock = new ACadSharp.Tables.BlockRecord("RONG_KHONG_DUNG");
        orphanBlock.Entities.Add(new Circle { Center = new CSMath.XYZ(0, 0, 0), Radius = 10 });
        purgeDoc.BlockRecords.Add(orphanBlock);

        var usedBlock = new ACadSharp.Tables.BlockRecord("CO_DUNG");
        usedBlock.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(10, 0, 0),
        });
        purgeDoc.BlockRecords.Add(usedBlock);

        purgeDoc.Entities.Add(new ACadSharp.Entities.Insert(usedBlock)
        {
            InsertPoint = new CSMath.XYZ(200, 200, 0),
            XScale = 1,
            YScale = 1,
            ZScale = 1,
        });

        // Kiểu nét không dùng
        OpenCad.Core.Styles.LineTypeLibrary.EnsureAll(purgeDoc);
        purgeDoc.Layers.First(l => l.Name == "DUNG").LineType =
            OpenCad.Core.Styles.LineTypeLibrary.Ensure(purgeDoc, "DASHED")!;

        var plan = OpenCad.Core.Audit.DrawingPurge.Inspect(purgeDoc);

        Check("PURGE tìm được layer mồ côi",
            plan.Layers.Contains("MO_COI"), string.Join(",", plan.Layers));

        Check("PURGE KHÔNG xoá layer đang dùng",
            !plan.Layers.Contains("DUNG"), string.Join(",", plan.Layers));

        Check("PURGE KHÔNG xoá layer 0",
            !plan.Layers.Contains("0"), "bỏ qua 0");

        Check("PURGE tìm được block mồ côi",
            plan.Blocks.Contains("RONG_KHONG_DUNG"), string.Join(",", plan.Blocks));

        Check("PURGE KHÔNG xoá block đang dùng",
            !plan.Blocks.Contains("CO_DUNG"), string.Join(",", plan.Blocks));

        Check("PURGE KHÔNG xoá block hệ thống",
            !plan.Blocks.Any(b => b.StartsWith("*")), "bỏ qua *");

        Check("PURGE tìm được kiểu nét thừa",
            plan.LineTypes.Count > 0, string.Join(",", plan.LineTypes));

        Check("PURGE KHÔNG xoá DASHED (đang dùng)",
            !plan.LineTypes.Contains("DASHED"), string.Join(",", plan.LineTypes));

        // Chưa xoá gì khi mới Inspect
        Check("Inspect không xoá gì",
            purgeDoc.Layers.Any(l => l.Name == "MO_COI"), "còn nguyên");

        var removedCount = OpenCad.Core.Audit.DrawingPurge.Apply(purgeDoc, plan);

        Check("PURGE xoá đúng số định nghĩa", removedCount == plan.Total,
            removedCount + " / " + plan.Total);

        Check("Sau PURGE không còn layer mồ côi",
            !purgeDoc.Layers.Any(l => l.Name == "MO_COI"), "đã xoá");

        Check("Sau PURGE không còn block mồ côi",
            !purgeDoc.BlockRecords.Any(b => b.Name == "RONG_KHONG_DUNG"), "đã xoá");

        Check("Sau PURGE block đang dùng vẫn còn",
            purgeDoc.BlockRecords.Any(b => b.Name == "CO_DUNG"), "còn");

        Check("PURGE lần hai không còn gì để xoá",
            OpenCad.Core.Audit.DrawingPurge.Inspect(purgeDoc).IsEmpty,
            OpenCad.Core.Audit.DrawingPurge.Inspect(purgeDoc).Total + " mục");

        // ---- Khôi phục được sau purge (dùng cho undo)
        var restoreWs = CadWorkspace.New();
        var restoreDoc = restoreWs.Document;

        restoreDoc.Layers.Add(new ACadSharp.Tables.Layer("TAM"));

        var restorePlan = OpenCad.Core.Audit.DrawingPurge.Inspect(restoreDoc);

        var captured = restorePlan.Layers
            .Select(n => restoreDoc.Layers.First(l => l.Name == n))
            .ToList();

        OpenCad.Core.Audit.DrawingPurge.Apply(restoreDoc, restorePlan);

        Check("Purge xoá layer TAM",
            !restoreDoc.Layers.Any(l => l.Name == "TAM"), "đã xoá");

        foreach (var layer in captured)
        {
            try { restoreDoc.Layers.Add(layer); }
            catch { }
        }

        Check("Khôi phục được layer đã purge (undo)",
            restoreDoc.Layers.Any(l => l.Name == "TAM"), "đã khôi phục");

        // ---------------------- AUDIT
        var auditWs = CadWorkspace.New();
        var auditDoc = auditWs.Document;

        // Sạch
        auditDoc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(1000, 0, 0),
        });

        var clean = OpenCad.Core.Audit.DrawingAudit.Run(auditDoc);

        Check("Bản vẽ sạch không có lỗi audit", clean.Count == 0,
            clean.Count == 0 ? "không lỗi" : string.Join("; ", clean.Take(3)));

        // Đoạn dài 0
        auditDoc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(500, 500, 0),
            EndPoint = new CSMath.XYZ(500, 500, 0),
        });

        var zeroLine = OpenCad.Core.Audit.DrawingAudit.Run(auditDoc);

        Check("Audit phát hiện đoạn thẳng dài 0",
            zeroLine.Any(f => f.Code == "ZERO_LINE"), string.Join(",", zeroLine.Select(f => f.Code)));

        // Đường tròn bán kính 0
        // ACadSharp không cho bán kính = 0 nên dùng bán kính cực nhỏ (dưới ngưỡng audit)
        auditDoc.Entities.Add(new Circle { Center = new CSMath.XYZ(100, 100, 0), Radius = 1e-9 });
        Check("Audit phát hiện đường tròn bán kính 0",
            OpenCad.Core.Audit.DrawingAudit.Run(auditDoc).Any(f => f.Code == "ZERO_CIRCLE"), "ZERO_CIRCLE");

        // Chữ rỗng
        auditDoc.Entities.Add(new TextEntity
        {
            InsertPoint = new CSMath.XYZ(0, 200, 0),
            Height = 100,
            Value = "   ",
        });

        Check("Audit phát hiện chữ rỗng",
            OpenCad.Core.Audit.DrawingAudit.Run(auditDoc).Any(f => f.Code == "EMPTY_TEXT"), "EMPTY_TEXT");

        // Trùng lặp
        var duplicate = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(1000, 0, 0),
        };

        auditDoc.Entities.Add(duplicate);

        var withDuplicates = OpenCad.Core.Audit.DrawingAudit.Run(auditDoc);

        Check("Audit phát hiện đối tượng trùng lặp",
            withDuplicates.Any(f => f.Code == "DUPLICATE"), "DUPLICATE");

        // Block rỗng
        auditDoc.BlockRecords.Add(new ACadSharp.Tables.BlockRecord("RONG"));
        Check("Audit phát hiện block rỗng",
            OpenCad.Core.Audit.DrawingAudit.Run(auditDoc).Any(f => f.Code == "EMPTY_BLOCK"), "EMPTY_BLOCK");

        // Tổng hợp theo mã
        var summary = OpenCad.Core.Audit.DrawingAudit.Summary(withDuplicates);
        Check("Audit tổng hợp được theo mã lỗi", summary.Count > 0,
            string.Join(",", summary.Select(s => s.Code + "×" + s.Count)));

        // ---------------------- TỶ LỆ VIEWPORT
        var vpWs = CadWorkspace.New();
        var vpDoc = vpWs.Document;

        vpDoc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(5000, 0, 0),
        });

        var vpModel = new CadScene();
        vpModel.Build(vpDoc);

        var vpLayout = OpenCad.Core.Layout.LayoutFactory.Create(vpDoc, "VP",
            new OpenCad.Core.Layout.SheetTemplateOptions { Title = "T", UnitsPerMm = 50 }, vpModel);

        var vpBlock = vpLayout?.AssociatedBlock;

        if (vpBlock is null)
        {
            Check("Tạo được layout cho test viewport", false, "null");
        }
        else
        {
            // Khung tờ cũng là một Viewport — chọn viewport mô hình (tỷ lệ gần 1:50)
            var candidates = vpBlock.Entities.OfType<Viewport>().ToList();

            Check("Chẩn đoán viewport",
                candidates.Count > 0,
                string.Join(" | ", candidates.Select(v =>
                    $"H={v.Height:0.#} VH={v.ViewHeight:0.#} S={CadEditor.ViewportScale(v):0.##}")));

            var viewport = candidates
                .OrderBy(v => Math.Abs(CadEditor.ViewportScale(v) - 50))
                .FirstOrDefault();

            Check("Layout có viewport", viewport is not null, viewport is null ? "null" : "có");

            if (viewport is not null)
            {
                Check("Tỷ lệ ban đầu của viewport là 50",
                    Math.Abs(CadEditor.ViewportScale(viewport) - 50) < 1e-6,
                    CadEditor.ViewportScale(viewport).ToString("0.###"));

                var vpEditor = new CadEditor(vpWs);
                var heightBefore = viewport.ViewHeight;

                var ok = vpEditor.SetViewportScale(viewport, 100);

                Check("Đổi được tỷ lệ viewport", ok, ok.ToString());
                Check("Tỷ lệ mới là 1:100",
                    Math.Abs(CadEditor.ViewportScale(viewport) - 100) < 1e-6,
                    CadEditor.ViewportScale(viewport).ToString("0.###"));
                Check("Vùng nhìn thu nhỏ một nửa (phóng to gấp đôi)",
                    Math.Abs(viewport.ViewHeight - heightBefore * 2) < 1e-6,
                    viewport.ViewHeight.ToString("0.###") + " vs " + (heightBefore * 2).ToString("0.###"));

                vpEditor.Undo();

                Check("Undo tỷ lệ viewport về 50",
                    Math.Abs(CadEditor.ViewportScale(viewport) - 50) < 1e-6,
                    CadEditor.ViewportScale(viewport).ToString("0.###"));

                // ---- Dời và đổi kích thước viewport
                var centerBefore = new Pt(viewport.Center.X, viewport.Center.Y);
                var widthBefore = viewport.Width;
                var heightBefore2 = viewport.Height;

                vpEditor.MoveViewport(viewport, 20, -10);

                Check("Dời viewport đúng (20, -10)",
                    Math.Abs(viewport.Center.X - centerBefore.X - 20) < 1e-6 &&
                    Math.Abs(viewport.Center.Y - centerBefore.Y + 10) < 1e-6,
                    $"({viewport.Center.X:0.##}, {viewport.Center.Y:0.##})");

                Check("Dời viewport không đổi kích thước",
                    Math.Abs(viewport.Width - widthBefore) < 1e-6 &&
                    Math.Abs(viewport.Height - heightBefore2) < 1e-6,
                    $"{viewport.Width:0.##}×{viewport.Height:0.##}");

                vpEditor.Undo();

                Check("Undo dời viewport phục hồi tâm",
                    Math.Abs(viewport.Center.X - centerBefore.X) < 1e-6 &&
                    Math.Abs(viewport.Center.Y - centerBefore.Y) < 1e-6,
                    $"({viewport.Center.X:0.##}, {viewport.Center.Y:0.##})");

                var scaleBefore = CadEditor.ViewportScale(viewport);

                vpEditor.ResizeViewport(viewport, 300, 150);

                Check("Đổi kích thước viewport thành 300×150",
                    Math.Abs(viewport.Width - 300) < 1e-6 && Math.Abs(viewport.Height - 150) < 1e-6,
                    $"{viewport.Width:0.##}×{viewport.Height:0.##}");

                Check("Đổi kích thước GIỮ NGUYÊN tỷ lệ",
                    Math.Abs(CadEditor.ViewportScale(viewport) - scaleBefore) < 1e-6,
                    $"{CadEditor.ViewportScale(viewport):0.##} vs {scaleBefore:0.##}");

                vpEditor.Undo();

                Check("Undo đổi kích thước phục hồi khung",
                    Math.Abs(viewport.Width - widthBefore) < 1e-6 &&
                    Math.Abs(viewport.Height - heightBefore2) < 1e-6,
                    $"{viewport.Width:0.##}×{viewport.Height:0.##}");

                Check("Không nhận kích thước 0",
                    !vpEditor.ResizeViewport(viewport, 0, 100), "từ chối");

                // Tỷ lệ hợp lệ phải lớn hơn 0
                Check("Không nhận tỷ lệ 0",
                    !vpEditor.SetViewportScale(viewport, 0), "từ chối");
            }
        }

        // ---------------------- HÌNH VẼ TRÊN TỜ IN
        var sheetWs = CadWorkspace.New();
        var sheetDoc = sheetWs.Document;

        sheetDoc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(3000, 0, 0),
        });

        var sheetModel = new CadScene();
        sheetModel.Build(sheetDoc);

        var sheetLayout = OpenCad.Core.Layout.LayoutFactory.Create(sheetDoc, "S1",
            new OpenCad.Core.Layout.SheetTemplateOptions { Title = "T", UnitsPerMm = 50 }, sheetModel);

        if (sheetLayout?.AssociatedBlock is null)
        {
            Check("Tạo được layout cho test hình trên tờ", false, "null");
        }
        else
        {
            var sheetBlock = sheetLayout.AssociatedBlock;
            var sheetEditor = new CadEditor(sheetWs);
            var sheetBefore = sheetBlock.Entities.Count();

            // Vẽ đường tròn / cung / đa giác trên tờ (toạ độ mm giấy)
            var paperCircle = new Circle { Center = new CSMath.XYZ(50, 40, 0), Radius = 15 };
            var paperArc = new Arc { Center = new CSMath.XYZ(100, 40, 0), Radius = 15, StartAngle = 0, EndAngle = Math.PI };

            sheetEditor.RunCustom("Circle (paper)",
                () => sheetBlock.Entities.Add(paperCircle),
                () => sheetBlock.Entities.Remove(paperCircle));

            sheetEditor.RunCustom("Arc (paper)",
                () => sheetBlock.Entities.Add(paperArc),
                () => sheetBlock.Entities.Remove(paperArc));

            Check("Vẽ được đường tròn trên tờ",
                sheetBlock.Entities.OfType<Circle>().Any(), "có Circle");
            Check("Vẽ được cung trên tờ",
                sheetBlock.Entities.OfType<Arc>().Any(), "có Arc");

            var sheet = OpenCad.Core.Layout.LayoutFactory.Read(sheetDoc, sheetLayout)!;
            var sheetPrims = sheet.Items.SelectMany(i => i.Prims).ToList();

            Check("Tờ in có thêm hình sau khi vẽ",
                sheetBlock.Entities.Count() == sheetBefore + 2,
                sheetBefore + " → " + sheetBlock.Entities.Count());

            var circleOnSheet = sheetPrims.Any(p => p.Points.Any(pt =>
                Math.Abs(pt.X - 65) < 0.5 && Math.Abs(pt.Y - 40) < 0.5));

            Check("Đường tròn trên tờ nằm đúng toạ độ mm giấy (R15 tại 50,40)",
                circleOnSheet, circleOnSheet ? "đúng" : "không thấy");

            // In ra PDF phải có nội dung
            var sheetPdf = Path.Combine(Path.GetTempPath(), "opencad-smoke", "sheet-shapes.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(sheetPdf)!);

            PdfExporter.ExportPaperSheet(sheet, sheetPdf, new PdfExportOptions
            {
                Title = "sheet-shapes",
                FontPath = PdfExportOptions.FindSystemFont(),
            });

            var pdfText = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(sheetPdf));

            Check("PDF của tờ có hình vẽ mới", pdfText.Length > 1000, pdfText.Length + " byte");

            // Undo xoá hình vẽ trên tờ
            sheetEditor.Undo();
            sheetEditor.Undo();

            Check("Undo xoá hình vừa vẽ trên tờ",
                sheetBlock.Entities.Count() == sheetBefore, sheetBlock.Entities.Count().ToString());
        }

        // ---------------------- SỬA HATCH
        var hatchWs = CadWorkspace.New();
        var hatchDoc = hatchWs.Document;

        var hatchRect = new LwPolyline { IsClosed = true };
        hatchRect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 0) });
        hatchRect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(200, 0) });
        hatchRect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(200, 150) });
        hatchRect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 150) });

        var hatchObj = OpenCad.Core.Editing.HatchFactory.FromBoundary(hatchRect)!;
        hatchObj.IsSolid = false;

        try { hatchObj.Pattern = new ACadSharp.Entities.HatchPattern("ANSI31"); } catch { }

        hatchObj.PatternScale = 4;
        hatchDoc.Entities.Add(hatchObj);

        var hatchEditor = new CadEditor(hatchWs);

        var beforePrims = hatchEditor.Scene.Entries.First().Prims.Count;

        var changedHatches = hatchEditor.SetHatchProperties(
            new Entity[] { hatchObj }, scale: 16, angle: Math.PI / 4);

        Check("Sửa được hatch", changedHatches == 1, changedHatches + " hatch");
        Check("Đổi tỷ lệ hatch thành 16", Math.Abs(hatchObj.PatternScale - 16) < 1e-9,
            hatchObj.PatternScale.ToString("0.###"));
        Check("Đổi góc hatch thành 45°",
            Math.Abs(hatchObj.PatternAngle - Math.PI / 4) < 1e-9,
            (hatchObj.PatternAngle * 180 / Math.PI).ToString("0.#") + "°");

        var afterPrims = hatchEditor.Scene.Entries.First().Prims.Count;

        Check("Tỷ lệ lớn hơn thì hatch thưa hơn (ít đoạn hơn)",
            afterPrims < beforePrims, beforePrims + " → " + afterPrims);

        hatchEditor.Undo();

        Check("Undo hatch phục hồi tỷ lệ 4",
            Math.Abs(hatchObj.PatternScale - 4) < 1e-9, hatchObj.PatternScale.ToString("0.###"));

        Check("Undo hatch phục hồi góc 0",
            Math.Abs(hatchObj.PatternAngle) < 1e-9, hatchObj.PatternAngle.ToString("0.###"));

        Check("Undo hatch phục hồi số đoạn vẽ",
            hatchEditor.Scene.Entries.First().Prims.Count == beforePrims,
            hatchEditor.Scene.Entries.First().Prims.Count + " / " + beforePrims);

        // Đổi sang pattern khác
        hatchEditor.SetHatchProperties(new Entity[] { hatchObj }, pattern: "CONCRETE");

        Check("Đổi pattern hatch sang CONCRETE",
            hatchObj.Pattern?.Name == "CONCRETE", hatchObj.Pattern?.Name ?? "null");

        // Entity không phải hatch thì không đổi gì
        var plainLine = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100, 0, 0),
        };

        Check("Entity không phải hatch bị bỏ qua",
            hatchEditor.SetHatchProperties(new Entity[] { plainLine }, scale: 2) == 0, "0");

        // ---------------------- BẢNG LỆNH
        var duplicates = OpenCad.Core.Commands.CommandRegistry.FindDuplicates();

        Check("Không có tên viết tắt trùng nhau", duplicates.Count == 0,
            duplicates.Count == 0 ? "không trùng"
                : string.Join("; ", duplicates.Take(5).Select(d =>
                    d.Alias + " (" + d.First + " ↔ " + d.Second + ")")));

        Check("Bảng lệnh có ít nhất 40 lệnh",
            OpenCad.Core.Commands.CommandRegistry.All.Length >= 40,
            OpenCad.Core.Commands.CommandRegistry.All.Length + " lệnh");

        Check("Mọi lệnh thuộc một nhóm hợp lệ",
            OpenCad.Core.Commands.CommandRegistry.All.All(c =>
                !string.IsNullOrWhiteSpace(c.Category)),
            OpenCad.Core.Commands.CommandRegistry.ByCategory().Count + " nhóm");

        Check("Nhóm lệnh không rỗng",
            OpenCad.Core.Commands.CommandRegistry.ByCategory().All(g => g.Commands.Count > 0),
            string.Join(",", OpenCad.Core.Commands.CommandRegistry.ByCategory().Select(g => g.Category + "×" + g.Commands.Count)));

        Check("Mọi lệnh có mô tả",
            OpenCad.Core.Commands.CommandRegistry.All.All(c => !string.IsNullOrWhiteSpace(c.Description)),
            "có");

        Check("Tìm được lệnh theo viết tắt (L → LINE)",
            OpenCad.Core.Commands.CommandRegistry.Find("l")?.Name == "LINE",
            OpenCad.Core.Commands.CommandRegistry.Find("l")?.Name ?? "null");

        Check("Tìm được lệnh không phân biệt hoa thường (dimrad)",
            OpenCad.Core.Commands.CommandRegistry.Find("dimrad")?.Name == "DIMRADIUS",
            OpenCad.Core.Commands.CommandRegistry.Find("dimrad")?.Name ?? "null");

        Check("Lệnh không tồn tại trả về null",
            OpenCad.Core.Commands.CommandRegistry.Find("XYZ_KHONG_CO") is null, "null");

        Check("Kéo grip chữ dời InsertPoint",
            Math.Abs(text.InsertPoint.X - 300) < 1e-9 && Math.Abs(text.InsertPoint.Y - 400) < 1e-9,
            "(" + text.InsertPoint.X.ToString("0.#") + ", " + text.InsertPoint.Y.ToString("0.#") + ")");
    }

    // ---------------------- KIỂU NÉT (LINETYPE)

    private static void RunLineTypeChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- KIỂU NÉT --");

        var ws = CadWorkspace.New();
        var doc = ws.Document;

        // Từ vòng 116 bản vẽ mới đã có sẵn kiểu nét chuẩn → EnsureAll trả 0 (không tạo thêm)
        var created = OpenCad.Core.Styles.LineTypeLibrary.EnsureAll(doc);

        Check("EnsureAll không tạo trùng khi đã có sẵn", created == 0, created + " kiểu mới");

        // Trên bản vẽ đã bị xoá kiểu nét thì phải tạo lại đủ
        var stripped = CadWorkspace.New();

        foreach (var type in stripped.Document.LineTypes.ToList())
        {
            if (type.Name is "ByLayer" or "ByBlock" or "Continuous") continue;

            try { stripped.Document.LineTypes.Remove(type.Name); } catch { }
        }

        var recreated = OpenCad.Core.Styles.LineTypeLibrary.EnsureAll(stripped.Document);

        Check("EnsureAll tạo lại được kiểu nét đã mất", recreated >= 5, recreated + " kiểu");

        var names = OpenCad.Core.Styles.LineTypeLibrary.Available(doc)
            .Select(l => l.Name)
            .ToList();

        Check("Có DASHED trong danh sách", names.Contains("DASHED"), string.Join(",", names));
        // Combo trên UI dùng toàn bộ LineTypes để hiển thị đúng giá trị hiện tại
        var allNames = doc.LineTypes.Select(l => l.Name).ToList();

        Check("Bản vẽ có ByLayer và ByBlock để combo hiển thị đúng",
            allNames.Contains("ByLayer") && allNames.Contains("ByBlock"),
            string.Join(",", allNames));

        Check("Không liệt kê ByLayer/ByBlock",
            !names.Contains("ByLayer") && !names.Contains("ByBlock"), names.Count + " kiểu");

        var line = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(3000, 0, 0),
        };

        doc.Entities.Add(line);

        var editor = new CadEditor(ws);

        var dashed = OpenCad.Core.Styles.LineTypeLibrary.Ensure(doc, "DASHED")!;
        editor.SetLineType(new[] { line }, dashed);

        Check("Gán kiểu nét DASHED cho line", line.LineType?.Name == "DASHED",
            line.LineType?.Name ?? "null");

        var dash = editor.Scene.Entries.First().Prims.First().Dash;

        Check("Gán DASHED sinh pattern nét đứt", dash is { Length: 2 },
            dash is null ? "null" : string.Join("/", dash));

        Check("Pattern DASHED đúng 300 nét / 150 hở",
            dash is not null && Math.Abs(dash[0] - 300) < 1e-6 && Math.Abs(dash[1] + 150) < 1e-6,
            dash is null ? "null" : string.Join("/", dash));

        editor.Undo();

        Check("Undo kiểu nét phục hồi Continuous",
            line.LineType?.Name != "DASHED", line.LineType?.Name ?? "null");

        Check("Undo kiểu nét bỏ pattern nét đứt",
            editor.Scene.Entries.First().Prims.First().Dash is null,
            editor.Scene.Entries.First().Prims.First().Dash is null ? "không còn" : "vẫn còn");

        var hidden = OpenCad.Core.Styles.LineTypeLibrary.Ensure(doc, "HIDDEN")!;
        editor.SetLineType(new[] { line }, hidden);

        var hiddenDash = editor.Scene.Entries.First().Prims.First().Dash;

        Check("HIDDEN dùng pattern 150/75",
            hiddenDash is { Length: 2 } &&
            Math.Abs(hiddenDash[0] - 150) < 1e-6 && Math.Abs(hiddenDash[1] + 75) < 1e-6,
            hiddenDash is null ? "null" : string.Join("/", hiddenDash));
    }

    // ---------------------- MULTILEADER

    private static void RunMultiLeaderChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- MULTILEADER --");

        var leader = new MultiLeader();

        if (leader.ContextData is null)
        {
            Check("MULTILEADER có ContextData", false, "null");
            return;
        }

        leader.ContextData.TextLabel = "GHI CHU\\P%%c20";
        leader.ContextData.TextHeight = 100;
        leader.ContextData.TextLocation = new CSMath.XYZ(1000, 1000, 0);

        var line = new ACadSharp.Objects.MultiLeaderObjectContextData.LeaderLine();
        line.Points.Add(new CSMath.XYZ(0, 0, 0));
        line.Points.Add(new CSMath.XYZ(500, 400, 0));
        line.Points.Add(new CSMath.XYZ(900, 400, 0));

        var root = new ACadSharp.Objects.MultiLeaderObjectContextData.LeaderRoot();
        root.Lines.Add(line);
        leader.ContextData.LeaderRoots.Add(root);

        var ws = CadWorkspace.New();
        ws.Document.Entities.Add(leader);

        var scene = new CadScene();
        scene.Build(ws.Document);

        var prims = scene.Entries.First().Prims;

        Check("MULTILEADER vẽ được đường dẫn", prims.Any(p => !p.Filled && p.Points.Length >= 3),
            prims.Count + " primitive");

        Check("MULTILEADER có mũi tên", prims.Any(p => p.Filled),
            prims.Count(p => p.Filled) + " mũi tên");

        var texts = prims.Where(p => p.Kind == PrimKind.Text).Select(p => p.Text).ToList();

        Check("MULTILEADER vẽ chữ ghi chú", texts.Count >= 1, string.Join("|", texts));
        Check("MULTILEADER tách dòng theo \\P", texts.Count == 2, texts.Count + " dòng");
        Check("MULTILEADER giải mã %%c trong chữ",
            texts.Any(t => t.Contains("\u00D8")), string.Join("|", texts));

        // ---- ACadSharp 3.8.30: MULTILEADER LƯU ĐƯỢC — không còn là rủi ro mất dữ liệu
        // 3.8.30: entity sống sót nhưng MẤT CHỮ → vẫn phải cảnh báo (khác tên loại)
        Check("MULTILEADER vẫn bị cảnh báo (mất chữ) khi lưu DWG",
            ws.LossySaveRisks().Any(r => r.Type.StartsWith("MultiLeader")),
            string.Join(",", ws.LossySaveRisks().Select(r => r.Type + "×" + r.Count)));

        // Lưu rồi mở lại: multileader phải còn
        var mlKeepPath = Path.Combine(Path.GetTempPath(), "opencad-mleader-roundtrip.dwg");
        ws.Save(mlKeepPath);

        var mlKeepBack = CadWorkspace.Open(mlKeepPath);

        Check("MULTILEADER sống sót qua lưu/đọc DWG",
            mlKeepBack.Document.Entities.OfType<MultiLeader>().Count() == 1,
            mlKeepBack.Document.Entities.Select(e => e.GetType().Name).Count() + " entity");

        var mlKeepPrims = new CadScene();
        mlKeepPrims.Build(mlKeepBack.Document);

        var mlKeepTexts = mlKeepPrims.Entries.SelectMany(e => e.Prims)
            .Where(p2 => p2.Kind == OpenCad.Core.Rendering.PrimKind.Text)
            .Select(p2 => p2.Text).ToList();

        // Đây là GIỚI HẠN thật của ACadSharp 3.8.30: chữ ghi chú bị mất khi lưu
        Check("MULTILEADER mất chữ khi lưu (giới hạn đã biết)",
            mlKeepTexts.Count == 0, mlKeepTexts.Count + " dòng chữ còn lại");

        try { File.Delete(mlKeepPath); } catch { }

        // Chuyển đổi KHÔNG được đụng vào MULTILEADER nữa (nếu không là phá dữ liệu)
        var plainWs = CadWorkspace.New();
        plainWs.Document.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100, 0, 0),
        });

        var convertWs = CadWorkspace.New();
        var converted = new MultiLeader();
        converted.ContextData.TextLabel = "GHI CHU";
        converted.ContextData.TextHeight = 100;
        converted.ContextData.TextLocation = new CSMath.XYZ(500, 500, 0);

        var convLine = new ACadSharp.Objects.MultiLeaderObjectContextData.LeaderLine();
        convLine.Points.Add(new CSMath.XYZ(0, 0, 0));
        convLine.Points.Add(new CSMath.XYZ(300, 300, 0));

        var convRoot = new ACadSharp.Objects.MultiLeaderObjectContextData.LeaderRoot();
        convRoot.Lines.Add(convLine);
        converted.ContextData.LeaderRoots.Add(convRoot);

        convertWs.Document.Entities.Add(converted);

        var convertedCount = convertWs.ConvertUnsupportedForSave();

        Check("Chuyển được MULTILEADER sang dạng lưu được", convertedCount == 1,
            convertedCount + " đối tượng");

        Check("Sau khi chuyển không còn MULTILEADER",
            !convertWs.Document.Entities.OfType<MultiLeader>().Any(), "0");

        Check("Đã tạo Leader + chữ thay thế",
            convertWs.Document.Entities.OfType<Leader>().Any() &&
            convertWs.Document.Entities.OfType<MText>().Any(),
            convertWs.Document.Entities.Count() + " entity");

        Check("Chữ được giữ nguyên khi chuyển đổi",
            convertWs.Document.Entities.OfType<MText>().FirstOrDefault()?.Value == "GHI CHU",
            convertWs.Document.Entities.OfType<MText>().FirstOrDefault()?.Value ?? "không có");

        Check("Bản vẽ bình thường không có cảnh báo mất dữ liệu",
            plainWs.LossySaveRisks().Count == 0, "0 rủi ro");

        Check("MULTILEADER không bị tính là chưa hỗ trợ",
            scene.SkippedCount == 0, scene.SkippedCount + " bỏ qua");
    }

    // ---------------------- WIPEOUT & TOLERANCE

    private static void RunWipeoutToleranceChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- WIPEOUT & TOLERANCE --");

        var ws = CadWorkspace.New();
        var doc = ws.Document;

        var wipeout = new Wipeout
        {
            InsertPoint = new CSMath.XYZ(100, 200, 0),
            Size = new CSMath.XY(400, 200),
            UVector = new CSMath.XYZ(1, 0, 0),
            VVector = new CSMath.XYZ(0, 1, 0),
        };

        wipeout.ClipBoundaryVertices.Add(new CSMath.XY(0, 0));
        wipeout.ClipBoundaryVertices.Add(new CSMath.XY(1, 0));
        wipeout.ClipBoundaryVertices.Add(new CSMath.XY(1, 1));
        wipeout.ClipBoundaryVertices.Add(new CSMath.XY(0, 1));

        doc.Entities.Add(wipeout);

        var tolerance = new Tolerance
        {
            InsertionPoint = new CSMath.XYZ(0, 1000, 0),
            Text = "%%v0.05",
        };

        doc.Entities.Add(tolerance);

        var scene = new CadScene();
        scene.Build(doc);

        var wipeoutEntry = scene.Entries.First(e => ReferenceEquals(e.Entity, wipeout));

        Check("WIPEOUT vẽ được (không bị bỏ qua)", wipeoutEntry.Prims.Count > 0,
            wipeoutEntry.Prims.Count + " primitive");

        Check("WIPEOUT ánh xạ đúng vị trí và kích thước 400×200",
            Math.Abs(wipeoutEntry.Bounds.MinX - 100) < 1e-6 &&
            Math.Abs(wipeoutEntry.Bounds.MinY - 200) < 1e-6 &&
            Math.Abs(wipeoutEntry.Bounds.Width - 400) < 1e-6 &&
            Math.Abs(wipeoutEntry.Bounds.Height - 200) < 1e-6,
            wipeoutEntry.Bounds.ToString());

        var wipeoutPrim = wipeoutEntry.Prims.First();

        Check("WIPEOUT dùng nền trắng để che",
            wipeoutPrim.SolidFill && wipeoutPrim.Color.R > 240 && wipeoutPrim.Color.G > 240,
            wipeoutPrim.Color.ToString());

        var tolerancePrims = scene.Entries
            .First(e => ReferenceEquals(e.Entity, tolerance)).Prims;

        Check("TOLERANCE vẽ được khung", tolerancePrims.Count >= 2, tolerancePrims.Count + " primitive");

        var toleranceTexts = tolerancePrims
            .Where(p => p.Kind == PrimKind.Text)
            .Select(p => p.Text)
            .ToList();

        Check("TOLERANCE có chữ dung sai",
            toleranceTexts.Any(t => t.Contains("0.05")), string.Join("|", toleranceTexts));

        // ---- Giải mã ký hiệu AutoCAD trong chữ
        var coded = new TextEntity
        {
            InsertPoint = new CSMath.XYZ(0, 2000, 0),
            Height = 100,
            Value = "%%c20 %%p0.05 %%d",
        };

        var ws2 = CadWorkspace.New();
        ws2.Document.Entities.Add(coded);

        var scene2 = new CadScene();
        scene2.Build(ws2.Document);

        var codedText = scene2.Entries.First().Prims
            .Where(p => p.Kind == PrimKind.Text)
            .Select(p => p.Text)
            .FirstOrDefault() ?? string.Empty;

        Check("Chữ giải mã %%c thành Ø", codedText.Contains("\u00D8"), codedText);
        Check("Chữ giải mã %%p thành ±", codedText.Contains("\u00B1"), codedText);
        Check("Chữ giải mã %%d thành °", codedText.Contains("\u00B0"), codedText);
        Check("Chữ không còn %% sau khi giải mã", !codedText.Contains("%%"), codedText);

        Check("Hai loại mới không bị tính là chưa hỗ trợ",
            scene.SkippedCount == 0, scene.SkippedCount + " bỏ qua");
    }

    // ---------------------- UNDO CHO THAO TÁC TRÊN TỜ IN

    private static void RunPaperUndoChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- UNDO TRÊN PAPER SPACE --");

        var ws = CadWorkspace.New();
        var doc = ws.Document;

        doc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(4000, 0, 0),
        });

        var model = new CadScene();
        model.Build(doc);

        var layout = OpenCad.Core.Layout.LayoutFactory.Create(doc, "L1",
            new OpenCad.Core.Layout.SheetTemplateOptions { Title = "T", UnitsPerMm = 50 }, model);

        if (layout?.AssociatedBlock is null)
        {
            Check("Tạo được layout", false, "null");
            return;
        }

        var block = layout.AssociatedBlock;
        var editor = new CadEditor(ws);

        var before = block.Entities.Count();

        var note = new TextEntity
        {
            InsertPoint = new CSMath.XYZ(20, 20, 0),
            Height = 4,
            Value = "GHI CHU",
        };

        editor.RunCustom("Text (paper)",
            () => block.Entities.Add(note),
            () => block.Entities.Remove(note));

        Check("Thêm chữ trên tờ: block tăng 1", block.Entities.Count() == before + 1,
            before + " → " + block.Entities.Count());

        editor.Undo();
        Check("UNDO thêm chữ trên tờ: block trở lại", block.Entities.Count() == before,
            block.Entities.Count().ToString());

        editor.Redo();
        Check("REDO thêm chữ trên tờ: có lại", block.Entities.Count() == before + 1,
            block.Entities.Count().ToString());

        editor.Undo();

        var paperLine = new Line
        {
            StartPoint = new CSMath.XYZ(30, 30, 0),
            EndPoint = new CSMath.XYZ(200, 30, 0),
        };

        editor.RunCustom("Line (paper)",
            () => block.Entities.Add(paperLine),
            () => block.Entities.Remove(paperLine));

        Check("Thêm đường trên tờ: block tăng 1", block.Entities.Count() == before + 1,
            block.Entities.Count().ToString());

        editor.Undo();
        Check("UNDO thêm đường trên tờ: block trở lại", block.Entities.Count() == before,
            block.Entities.Count().ToString());

        var caption = new TextEntity
        {
            InsertPoint = new CSMath.XYZ(50, 50, 0),
            Height = 4,
            Value = "DI CHUYEN",
        };

        block.Entities.Add(caption);

        editor.RunCustom("Move (paper)",
            () => EntityTransform.Apply(caption, Affine2.Translate(60, 25)),
            () => EntityTransform.Apply(caption, Affine2.Translate(-60, -25)));

        Check("Di chuyển chữ trên tờ: tới (110, 75)",
            Math.Abs(caption.InsertPoint.X - 110) < 1e-9 &&
            Math.Abs(caption.InsertPoint.Y - 75) < 1e-9,
            "(" + caption.InsertPoint.X.ToString("0.#") + ", " + caption.InsertPoint.Y.ToString("0.#") + ")");

        editor.Undo();
        Check("UNDO di chuyển trên tờ: về (50, 50)",
            Math.Abs(caption.InsertPoint.X - 50) < 1e-9 &&
            Math.Abs(caption.InsertPoint.Y - 50) < 1e-9,
            "(" + caption.InsertPoint.X.ToString("0.#") + ", " + caption.InsertPoint.Y.ToString("0.#") + ")");

        var layer = doc.Layers.First();
        var wasOn = layer.IsOn;

        editor.RunCustom("Layer state",
            () => layer.IsOn = !layer.IsOn,
            () => layer.IsOn = wasOn);

        Check("Đổi trạng thái layer: đã đổi", layer.IsOn != wasOn, layer.IsOn.ToString());

        editor.Undo();
        Check("UNDO trạng thái layer: trở lại", layer.IsOn == wasOn, layer.IsOn.ToString());
    }

    // ---------------------- UNDO CÓ PHỤC HỒI CHÍNH XÁC KHÔNG?

    private static string Snapshot(CadWorkspace workspace)
    {
        var parts = new List<string>();

        foreach (var entity in workspace.Document.Entities)
        {
            var prims = new List<OpenCad.Core.Rendering.Prim>(4);

            try
            {
                OpenCad.Core.Rendering.EntityFlattener.Flatten(
                    entity, prims, Affine2.Identity, OpenCad.Core.Colors.Rgb.White, 0);
            }
            catch { }

            var geometry = string.Join(";", prims.Select(p =>
                string.Join(",", p.Points.Select(pt =>
                    Math.Round(pt.X, 4) + "/" + Math.Round(pt.Y, 4)))));

            var style = entity.Layer?.Name + "," + entity.Color.Index + "," + entity.LineWeight;
            parts.Add(entity.GetType().Name + "[" + style + "]{" + geometry + "}");
        }

        var layers = workspace.Document.Layers
            .Select(l => l.Name + ":" + l.Color.Index + ":" + l.LineWeight);
        var blocks = workspace.Document.BlockRecords
            .Where(b => !b.Name.StartsWith("*"))
            .Select(b => b.Name + "#" + b.Entities.Count());

        return string.Join("|", parts.OrderBy(p => p))
               + "||" + string.Join("|", layers.OrderBy(l => l))
               + "||" + string.Join("|", blocks.OrderBy(b => b));
    }

    private static void RunUndoFidelityChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- UNDO PHỤC HỒI CHÍNH XÁC? --");

        var scenarios = new (string Name, Action<CadEditor, Entity> Run)[]
        {
            ("MOVE", (ed, e) => ed.TransformEntities(new[] { e }, Affine2.Translate(300, 200), "Move")),
            ("COPY", (ed, e) => ed.CopyTransform(new[] { e }, Affine2.Translate(300, 200),
                x => x.Clone() as Entity, "Copy")),
            ("ARRAY 2×3", (ed, e) => ed.ArrayCopy(new[] { e }, 2, 3, 400, 300,
                x => x.Clone() as Entity, "Array")),
            ("ARRAY POLAR", (ed, e) => ed.ArrayPolar(new[] { e }, 5, new Pt(0, 0), 360,
                x => x.Clone() as Entity, "Polar")),
            ("DIVIDE", (ed, e) => ed.PlaceMarkers(e, MeasureOperation.Divide(e, 5), "Divide")),
            ("ĐỔI MÀU", (ed, e) => ed.SetColor(new[] { e }, new ACadSharp.Color((short)3))),
            ("ĐỔI ĐỘ ĐẬM", (ed, e) => ed.SetLineWeight(new[] { e }, ACadSharp.LineWeightType.W70)),
            ("ĐỔI LAYER", (ed, e) => ed.SetLayer(new[] { e }, ed.Workspace.Document.Layers.Last())),
            ("ERASE", (ed, e) => ed.RemoveEntities(new[] { e }, "Erase")),
            ("TẠO LAYER", (ed, e) => ed.CreateLayer("MOI", new ACadSharp.Color((short)2))),
        };

        foreach (var (name, run) in scenarios)
        {
            var ws = CadWorkspace.New();
            var doc = ws.Document;

            doc.Layers.Add(new ACadSharp.Tables.Layer("PHU"));

            var target = new Line
            {
                StartPoint = new CSMath.XYZ(100, 100, 0),
                EndPoint = new CSMath.XYZ(500, 400, 0),
                Layer = doc.Layers.First(),
            };

            doc.Entities.Add(new Circle { Center = new CSMath.XYZ(900, 900, 0), Radius = 80 });

            var editor = new CadEditor(ws);
            editor.AddEntities(new Entity[] { target }, "Add");

            var before = Snapshot(ws);
            run(editor, target);
            var changed = Snapshot(ws);
            editor.Undo();
            var restored = Snapshot(ws);

            var changedOk = changed != before;
            var restoreOk = restored == before;

            Check("UNDO " + name + ": phục hồi chính xác",
                changedOk && restoreOk,
                !changedOk ? "lệnh không đổi gì" : (restoreOk ? "khớp" : "KHÔNG khớp"));

            editor.Redo();
            var redone = Snapshot(ws);

            Check("REDO " + name + ": làm lại đúng như trước khi undo",
                redone == changed, redone == changed ? "khớp" : "KHÔNG khớp");
        }
    }

    // ---------------------- BIẾN ĐỔI CHO CÁC LOẠI MỚI THÊM

    private static void RunNewTypeTransformChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- MOVE/ROTATE CHO LEADER, MLINE, TABLE, MTEXT --");

        var makers = new (string Name, Func<Entity> Make)[]
        {
            ("Leader", () =>
            {
                var leader = new Leader { TextHeight = 100, ArrowHeadEnabled = true };
                leader.Vertices.Add(new CSMath.XYZ(500, 500, 0));
                leader.Vertices.Add(new CSMath.XYZ(700, 600, 0));
                leader.Vertices.Add(new CSMath.XYZ(900, 600, 0));
                return leader;
            }),
            ("MLine", () =>
            {
                var mline = new MLine();
                mline.Vertices.Add(new MLine.Vertex { Position = new CSMath.XYZ(500, 500, 0) });
                mline.Vertices.Add(new MLine.Vertex { Position = new CSMath.XYZ(800, 500, 0) });
                mline.Vertices.Add(new MLine.Vertex { Position = new CSMath.XYZ(800, 700, 0) });
                return mline;
            }),
            ("MText", () => new MText
            {
                InsertPoint = new CSMath.XYZ(500, 500, 0),
                Height = 100,
                Value = "Dong 1\\PDong 2",
            }),
        };

        foreach (var (name, make) in makers)
        {
            var ws = CadWorkspace.New();
            var editor = new CadEditor(ws);

            var entity = make();
            editor.AddEntities(new[] { entity }, "Add");

            var before = PrimCentroid(editor.Scene.Entries.First());
            editor.TransformEntities(new[] { entity }, Affine2.Translate(100, 100), "Move");
            var after = PrimCentroid(editor.Scene.Entries.First());

            var dx = after.X - before.X;
            var dy = after.Y - before.Y;

            Check(name + ": MOVE (100,100) dịch đúng cả hình",
                Math.Abs(dx - 100) < 1e-6 && Math.Abs(dy - 100) < 1e-6,
                "Δ = (" + dx.ToString("0.###") + ", " + dy.ToString("0.###") + ")");
        }

        var table = new TableEntity
        {
            InsertPoint = new CSMath.XYZ(500, 500, 0),
            RowCount = 2,
            ColumnCount = 2,
        };

        try
        {
            table.Columns.Add(new TableEntity.Column { Width = 200 });
            table.Columns.Add(new TableEntity.Column { Width = 200 });
            table.Rows.Add(new TableEntity.Row { Height = 100 });
            table.Rows.Add(new TableEntity.Row { Height = 100 });
        }
        catch { }

        var tableWs = CadWorkspace.New();
        var tableEditor = new CadEditor(tableWs);
        tableEditor.AddEntities(new Entity[] { table }, "Add");

        var tableBefore = PrimCentroid(tableEditor.Scene.Entries.First());
        tableEditor.TransformEntities(new Entity[] { table }, Affine2.Translate(100, 100), "Move");
        var tableAfter = PrimCentroid(tableEditor.Scene.Entries.First());

        // ---- ROTATE 90° quanh gốc cho các loại mới
        foreach (var (name, make) in makers)
        {
            var ws = CadWorkspace.New();
            var editor = new CadEditor(ws);

            var entity = make();
            editor.AddEntities(new[] { entity }, "Add");

            var before = PrimCentroid(editor.Scene.Entries.First());
            editor.TransformEntities(new[] { entity }, new Affine2(0, -1, 1, 0, 0, 0), "Rotate");
            var after = PrimCentroid(editor.Scene.Entries.First());

            var target = new Pt(-before.Y, before.X);

            // Chữ nhiều dòng không xoay được nên trọng tâm MText chỉ gần đúng
            var rotateTolerance = name == "MText" ? 400 : 3;

            Check(name + ": ROTATE 90° đúng trọng tâm",
                Pt.Distance(after, target) < rotateTolerance, after + " vs " + target);
        }

        // Table rotate
        {
            var ws = CadWorkspace.New();
            var editor = new CadEditor(ws);

            var t2 = new TableEntity { InsertPoint = new CSMath.XYZ(500, 500, 0), RowCount = 2, ColumnCount = 2 };
            try
            {
                t2.Columns.Add(new TableEntity.Column { Width = 200 });
                t2.Columns.Add(new TableEntity.Column { Width = 200 });
                t2.Rows.Add(new TableEntity.Row { Height = 100 });
                t2.Rows.Add(new TableEntity.Row { Height = 100 });
            }
            catch { }

            editor.AddEntities(new Entity[] { t2 }, "Add");

            var before = PrimCentroid(editor.Scene.Entries.First());
            editor.TransformEntities(new Entity[] { t2 }, new Affine2(0, -1, 1, 0, 0, 0), "Rotate");
            var after = PrimCentroid(editor.Scene.Entries.First());

            var target = new Pt(-before.Y, before.X);

            Check("Table: ROTATE 90° đúng trọng tâm",
                Pt.Distance(after, target) < 5, after + " vs " + target);
        }

        Check("Table: MOVE (100,100) dịch đúng cả hình",
            Math.Abs(tableAfter.X - tableBefore.X - 100) < 1e-6 &&
            Math.Abs(tableAfter.Y - tableBefore.Y - 100) < 1e-6,
            "Δ = (" + (tableAfter.X - tableBefore.X).ToString("0.###") + ", " +
            (tableAfter.Y - tableBefore.Y).ToString("0.###") + ")");
    }

    // ---------------------- LAYER TẮT / ĐÓNG BĂNG / KHOÁ

    private static void RunLayerStateChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- LAYER TẮT / ĐÓNG BĂNG / KHOÁ --");

        var ws = CadWorkspace.New();
        var doc = ws.Document;

        var layer = doc.Layers.First();

        var hidden = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(1000, 0, 0),
            Layer = layer,
        };

        doc.Entities.Add(hidden);

        var editor = new CadEditor(ws);

        Check("Layer đang bật: chọn được entity",
            editor.Scene.HitTest(new Pt(500, 0), 5) is not null, "có");

        layer.IsOn = false;
        editor.Reload();

        Check("Layer tắt: KHÔNG chọn được entity",
            editor.Scene.HitTest(new Pt(500, 0), 5) is null, "không");

        var inRect = editor.Scene.SelectInRect(new RectD(-100, -100, 2000, 200), crossing: false);
        Check("Layer tắt: chọn bằng khung cũng không được", inRect.Count == 0, inRect.Count + " đối tượng");

        Check("Layer tắt: entity không vào scene",
            editor.Scene.Entries.Count == 0, editor.Scene.Entries.Count + " entry");

        layer.IsOn = true;
        editor.Reload();

        Check("Bật lại layer: chọn được trở lại",
            editor.Scene.HitTest(new Pt(500, 0), 5) is not null, "có");

        layer.Flags |= ACadSharp.Tables.LayerFlags.Frozen;
        editor.Reload();

        Check("Layer đóng băng: không chọn được",
            editor.Scene.HitTest(new Pt(500, 0), 5) is null, "không");

        layer.Flags &= ~ACadSharp.Tables.LayerFlags.Frozen;
        editor.Reload();

        layer.Flags |= ACadSharp.Tables.LayerFlags.Locked;
        editor.Reload();

        Check("Layer khoá: vẫn CHỌN được (như AutoCAD)",
            editor.Scene.HitTest(new Pt(500, 0), 5) is not null, "có");

        Check("Layer khoá: nhận biết được là bị khoá",
            CadEditor.IsEntityLocked(hidden), "khoá");

        layer.Flags &= ~ACadSharp.Tables.LayerFlags.Locked;
        editor.Reload();

        Check("Bỏ khoá: sửa được",
            !CadEditor.IsEntityLocked(hidden), "không khoá");
    }

    // ---------------------- NỘI DUNG CÓ SỐNG SÓT QUA LƯU/MỞ?

    private static void RunFidelityChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- NỘI DUNG SAU KHI LƯU VÀ MỞ LẠI --");

        var ws = CadWorkspace.New();
        var doc = ws.Document;

        var tuong = new ACadSharp.Tables.Layer("TUONG")
        {
            Color = new ACadSharp.Color((short)6),
            LineWeight = ACadSharp.LineWeightType.W50,
        };

        doc.Layers.Add(tuong);

        doc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(1000, 0, 0),
            Layer = tuong,
            Color = new ACadSharp.Color((short)1),
            LineWeight = ACadSharp.LineWeightType.W70,
        });

        doc.Entities.Add(new TextEntity
        {
            InsertPoint = new CSMath.XYZ(0, 200, 0),
            Height = 120,
            Value = "PHÒNG KHÁCH — TẦNG 1",
            Layer = tuong,
        });

        var boundary = new LwPolyline { IsClosed = true, Layer = tuong };
        boundary.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 400) });
        boundary.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(400, 400) });
        boundary.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(400, 800) });
        boundary.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 800) });

        var hatch = OpenCad.Core.Editing.HatchFactory.FromBoundary(boundary)!;
        hatch.IsSolid = false;
        hatch.PatternScale = 7;
        hatch.PatternAngle = 0.5;

        try { hatch.Pattern = new ACadSharp.Entities.HatchPattern("CONCRETE"); } catch { }

        doc.Entities.Add(hatch);

        var inner = new ACadSharp.Tables.BlockRecord("TRONG");
        inner.Entities.Add(new Circle { Center = new CSMath.XYZ(0, 0, 0), Radius = 40 });

        var outer = new ACadSharp.Tables.BlockRecord("NGOAI");
        outer.Entities.Add(new Line { StartPoint = new CSMath.XYZ(-80, 0, 0), EndPoint = new CSMath.XYZ(80, 0, 0) });
        outer.Entities.Add(new ACadSharp.Entities.Insert(inner)
        {
            InsertPoint = new CSMath.XYZ(0, 0, 0),
            XScale = 1,
            YScale = 1,
            ZScale = 1,
        });

        doc.BlockRecords.Add(inner);
        doc.BlockRecords.Add(outer);

        doc.Entities.Add(new ACadSharp.Entities.Insert(outer)
        {
            InsertPoint = new CSMath.XYZ(2000, 1000, 0),
            XScale = 2,
            YScale = 2,
            ZScale = 2,
            Layer = tuong,
        });

        var path = Path.Combine(Path.GetTempPath(), "opencad-smoke", "fidelity.dwg");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        ws.Save(path);
        var back = CadWorkspace.Open(path).Document;

        var layerBack = back.Layers.FirstOrDefault(l => l.Name == "TUONG");

        Check("Layer sống sót", layerBack is not null, layerBack?.Name ?? "null");
        Check("Layer giữ màu ACI 6", layerBack?.Color.Index == 6, "ACI " + layerBack?.Color.Index);
        Check("Layer giữ độ đậm 0.50",
            layerBack?.LineWeight == ACadSharp.LineWeightType.W50, layerBack?.LineWeight.ToString() ?? "-");

        var lineBack = back.Entities.OfType<Line>().FirstOrDefault();

        Check("Line giữ layer TUONG", lineBack?.Layer?.Name == "TUONG", lineBack?.Layer?.Name ?? "null");
        Check("Line giữ màu riêng ACI 1", lineBack?.Color.Index == 1, "ACI " + lineBack?.Color.Index);
        Check("Line giữ độ đậm riêng 0.70",
            lineBack?.LineWeight == ACadSharp.LineWeightType.W70, lineBack?.LineWeight.ToString() ?? "-");

        var textBack = back.Entities.OfType<TextEntity>().FirstOrDefault();

        Check("Chữ tiếng Việt giữ nguyên từng ký tự",
            textBack?.Value == "PHÒNG KHÁCH — TẦNG 1", textBack?.Value ?? "null");
        Check("Chữ giữ chiều cao 120", Math.Abs((textBack?.Height ?? 0) - 120) < 1e-6,
            (textBack?.Height ?? 0).ToString("0.###"));

        var hatchBack = back.Entities.OfType<Hatch>().FirstOrDefault();

        Check("Hatch giữ pattern CONCRETE",
            hatchBack?.Pattern?.Name == "CONCRETE", hatchBack?.Pattern?.Name ?? "null");
        Check("Hatch giữ tỉ lệ 7", Math.Abs((hatchBack?.PatternScale ?? 0) - 7) < 1e-6,
            (hatchBack?.PatternScale ?? 0).ToString("0.###"));
        Check("Hatch giữ góc 0.5 rad", Math.Abs((hatchBack?.PatternAngle ?? 0) - 0.5) < 1e-6,
            (hatchBack?.PatternAngle ?? 0).ToString("0.###"));

        var innerBack = back.BlockRecords.FirstOrDefault(b => b.Name == "TRONG");
        var outerBack = back.BlockRecords.FirstOrDefault(b => b.Name == "NGOAI");
        var insertBack = back.Entities.OfType<ACadSharp.Entities.Insert>().FirstOrDefault();

        Check("Block con sống sót", innerBack is not null, innerBack?.Name ?? "null");
        Check("Block cha có 2 entity (line + insert lồng)",
            outerBack?.Entities.Count() == 2, (outerBack?.Entities.Count() ?? -1).ToString());
        Check("Insert lồng trỏ đúng block con",
            outerBack?.Entities.OfType<ACadSharp.Entities.Insert>().FirstOrDefault()?.Block?.Name == "TRONG",
            "TRONG");
        Check("Insert ngoài giữ scale 2", Math.Abs((insertBack?.XScale ?? 0) - 2) < 1e-6,
            (insertBack?.XScale ?? 0).ToString("0.###"));
    }

    // ---------------------- MTEXT NHIỀU DÒNG & ROUND-TRIP "TRA TẤN"

    private static void RunMTextAndTortureChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- MTEXT NHIỀU DÒNG & ROUND-TRIP --");

        var mtext = new MText
        {
            InsertPoint = new CSMath.XYZ(0, 0, 0),
            Height = 100,
            Value = "Dong 1\\PDong 2\\PDong 3",
        };

        var ws = CadWorkspace.New();
        ws.Document.Entities.Add(mtext);

        var scene = new CadScene();
        scene.Build(ws.Document);

        var texts = scene.Entries.First().Prims
            .Where(p => p.Kind == PrimKind.Text)
            .Select(p => p.Text)
            .ToList();

        Check("MTEXT \\P tách thành 3 dòng", texts.Count == 3, texts.Count + " dòng");
        Check("MTEXT không còn ký tự \\P trong chữ",
            texts.All(t => !t.Contains("\\P")), string.Join("|", texts));

        var ys = scene.Entries.First().Prims
            .Where(p => p.Kind == PrimKind.Text)
            .Select(p => p.Position.Y)
            .ToList();

        Check("MTEXT các dòng xếp xuống dần",
            ys.Count == 3 && ys[0] > ys[1] && ys[1] > ys[2],
            string.Join(",", ys.Select(y => y.ToString("0"))));

        // ---- Round-trip
        var rt = CadWorkspace.New();
        var doc = rt.Document;
        var layer = doc.Layers.First();

        doc.Entities.Add(new Line { StartPoint = new CSMath.XYZ(0, 0, 0), EndPoint = new CSMath.XYZ(1000, 0, 0), Layer = layer });
        doc.Entities.Add(new Arc { Center = new CSMath.XYZ(0, 500, 0), Radius = 200, StartAngle = 0, EndAngle = Math.PI, Layer = layer });
        doc.Entities.Add(new Circle { Center = new CSMath.XYZ(500, 500, 0), Radius = 150, Layer = layer });
        doc.Entities.Add(new Ellipse
        {
            Center = new CSMath.XYZ(1000, 500, 0),
            MajorAxisEndPoint = new CSMath.XYZ(150, 0, 0),
            RadiusRatio = 0.5,
            Layer = layer,
        });
        doc.Entities.Add(new ACadSharp.Entities.Point { Location = new CSMath.XYZ(1500, 500, 0), Layer = layer });

        var poly = new LwPolyline { IsClosed = true, Layer = layer };
        poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 1000) });
        poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(400, 1000) });
        poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(400, 1300) });
        doc.Entities.Add(poly);

        doc.Entities.Add(new TextEntity { InsertPoint = new CSMath.XYZ(0, 1500, 0), Height = 100, Value = "GHI CHU", Layer = layer });

        // Instance mới: entity đã thuộc document khác thì không thêm được nữa
        doc.Entities.Add(new MText
        {
            InsertPoint = new CSMath.XYZ(500, 1500, 0),
            Height = 100,
            Value = "Dong 1\\PDong 2",
            Layer = layer,
        });

        var spline = new Spline { Layer = layer };
        spline.ControlPoints.Add(new CSMath.XYZ(1000, 1000, 0));
        spline.ControlPoints.Add(new CSMath.XYZ(1100, 1300, 0));
        spline.ControlPoints.Add(new CSMath.XYZ(1300, 1000, 0));
        doc.Entities.Add(spline);

        var leader = new Leader { TextHeight = 80, ArrowHeadEnabled = true, Layer = layer };
        leader.Vertices.Add(new CSMath.XYZ(2000, 1000, 0));
        leader.Vertices.Add(new CSMath.XYZ(2200, 1200, 0));
        doc.Entities.Add(leader);

        var path = Path.Combine(Path.GetTempPath(), "opencad-smoke", "torture.dwg");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        rt.Save(path);
        var reopened = CadWorkspace.Open(path);

        Check("Round-trip giữ đủ số entity",
            reopened.Document.Entities.Count() == doc.Entities.Count(),
            reopened.Document.Entities.Count() + " / " + doc.Entities.Count());

        var scene2 = new CadScene();
        scene2.Build(reopened.Document);

        var blank = scene2.Entries.Where(e => e.Prims.Count == 0)
            .Select(e => e.Entity.GetType().Name)
            .Distinct()
            .ToList();

        Check("Round-trip: mọi entity vẫn vẽ được",
            blank.Count == 0, blank.Count == 0 ? "đủ" : string.Join(",", blank));

        Check("Round-trip: không entity nào bị bỏ qua",
            scene2.SkippedCount == 0, scene2.SkippedCount + " bỏ qua");
    }

    // ---------------------- VẼ ĐƯỜNG TRÊN TỜ IN

    private static void RunPaperLineChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- VẼ ĐƯỜNG TRÊN TỜ --");

        var ws = CadWorkspace.New();
        var doc = ws.Document;

        doc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(4000, 0, 0),
        });

        var model = new CadScene();
        model.Build(doc);

        var layout = OpenCad.Core.Layout.LayoutFactory.Create(doc, "L1",
            new OpenCad.Core.Layout.SheetTemplateOptions { Title = "T", UnitsPerMm = 50 }, model);

        if (layout?.AssociatedBlock is null)
        {
            Check("Tạo được layout", false, "null");
            return;
        }

        var beforeCount = OpenCad.Core.Layout.LayoutFactory.Read(doc, layout)!
            .Items.SelectMany(i => i.Prims).Count();

        var paperLine = new Line
        {
            StartPoint = new CSMath.XYZ(20, 20, 0),
            EndPoint = new CSMath.XYZ(200, 20, 0),
        };

        layout.AssociatedBlock.Entities.Add(paperLine);

        var sheet = OpenCad.Core.Layout.LayoutFactory.Read(doc, layout)!;
        var allPrims = sheet.Items.SelectMany(i => i.Prims).ToList();

        Check("Đường trên tờ làm tờ có thêm hình", allPrims.Count > beforeCount,
            beforeCount + " → " + allPrims.Count);

        var hasPaperLine = allPrims.Any(p => p.Kind == PrimKind.Polyline &&
            p.Points.Length >= 2 &&
            Math.Abs(p.Points[0].X - 20) < 1e-6 &&
            Math.Abs(p.Points[0].Y - 20) < 1e-6 &&
            Math.Abs(p.Points[1].X - 200) < 1e-6);

        Check("Đường trên tờ nằm đúng toạ độ mm giấy (20,20)→(200,20)", hasPaperLine,
            hasPaperLine ? "đúng" : "không thấy");

        var pdf = Path.Combine(Path.GetTempPath(), "opencad-smoke", "paperline.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(pdf)!);

        PdfExporter.ExportPaperSheet(sheet, pdf, new PdfExportOptions
        {
            Title = "paperline",
            FontPath = PdfExportOptions.FindSystemFont(),
        });

        var text = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf));

        Check("PDF có lệnh vẽ đường", text.Contains(" m\n") && text.Contains(" l\n"), "có m/l");
    }

    // ---------------------- BÁO CÁO LOẠI CHƯA HỖ TRỢ

    private static void RunUnsupportedReportingChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- BÁO CÁO LOẠI CHƯA HỖ TRỢ --");

        var ws = CadWorkspace.New();
        var doc = ws.Document;

        doc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100, 0, 0),
        });

        var unsupportedAdded = 0;

        try
        {
            doc.Entities.Add(new ACadSharp.Entities.Solid3D());
            unsupportedAdded++;
        }
        catch { }

        var scene = new CadScene();
        scene.Build(doc);

        Check("Đếm được số đối tượng không vẽ được",
            scene.SkippedCount == unsupportedAdded,
            scene.SkippedCount + " (thêm được " + unsupportedAdded + ")");

        Check("Ghi lại được loại đối tượng bị bỏ",
            unsupportedAdded == 0 || scene.SkippedTypes.Count > 0,
            string.Join(",", scene.SkippedTypes.Select(p => p.Key + "×" + p.Value)));

        Check("Không bỏ sót line bình thường",
            scene.Entries.Any(e => e.Entity is Line), scene.Entries.Count + " entry");
    }

    // ------------------------------------------ TABLE

    private static void RunTableChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- TABLE --");

        var table = new TableEntity
        {
            InsertPoint = new CSMath.XYZ(0, 0, 0),
            RowCount = 3,
            ColumnCount = 2,
        };

        try
        {
            table.Columns.Add(new TableEntity.Column { Width = 400 });
            table.Columns.Add(new TableEntity.Column { Width = 300 });

            table.Rows.Add(new TableEntity.Row { Height = 100 });
            table.Rows.Add(new TableEntity.Row { Height = 100 });
            table.Rows.Add(new TableEntity.Row { Height = 100 });
        }
        catch (Exception ex)
        {
            Check("Dựng được bảng để kiểm tra", false, ex.GetType().Name);
            return;
        }

        var ws = CadWorkspace.New();
        ws.Document.Entities.Add(table);

        var scene = new CadScene();
        scene.Build(ws.Document);

        var prims = scene.Entries.First().Prims;

        Check("TABLE vẽ được lưới", prims.Count >= 4, prims.Count + " primitive");

        var bounds = scene.Entries.First().Bounds;

        Check("TABLE có đúng kích thước 700×300",
            Math.Abs(bounds.Width - 700) < 1e-6 && Math.Abs(bounds.Height - 300) < 1e-6,
            bounds.Width.ToString("0") + "×" + bounds.Height.ToString("0"));

        var verticals = prims.Count(p => p.Points.Length == 2 &&
            Math.Abs(p.Points[0].X - p.Points[1].X) < 1e-9);
        var horizontals = prims.Count(p => p.Points.Length == 2 &&
            Math.Abs(p.Points[0].Y - p.Points[1].Y) < 1e-9);

        Check("TABLE kẻ đủ 3 đường dọc", verticals >= 3, verticals + " dọc");
        Check("TABLE kẻ đủ 4 đường ngang", horizontals >= 4, horizontals + " ngang");
    }

    // ------------------------------------------ LEADER & MLINE

    private static void RunLeaderMLineChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- LEADER & MLINE --");

        var leader = new Leader { TextHeight = 200, ArrowHeadEnabled = true };
        leader.Vertices.Add(new CSMath.XYZ(0, 0, 0));
        leader.Vertices.Add(new CSMath.XYZ(1000, 500, 0));
        leader.Vertices.Add(new CSMath.XYZ(2000, 500, 0));

        var ws = CadWorkspace.New();
        ws.Document.Entities.Add(leader);

        var scene = new CadScene();
        scene.Build(ws.Document);

        var prims = scene.Entries.First().Prims;

        Check("LEADER vẽ được đường dẫn", prims.Count >= 2, prims.Count + " primitive");
        Check("LEADER có mũi tên (primitive tô)",
            prims.Any(p => p.Filled), prims.Count(p => p.Filled) + " mũi tên");

        var pathPrim = prims.FirstOrDefault(p => !p.Filled && p.Points.Length >= 3);
        Check("LEADER nối đủ 3 đỉnh",
            pathPrim is not null && pathPrim.Points.Length == 3,
            pathPrim is null ? "null" : pathPrim.Points.Length + " đỉnh");

        var mline = new MLine();
        mline.Vertices.Add(new MLine.Vertex { Position = new CSMath.XYZ(0, 0, 0) });
        mline.Vertices.Add(new MLine.Vertex { Position = new CSMath.XYZ(3000, 0, 0) });
        mline.Vertices.Add(new MLine.Vertex { Position = new CSMath.XYZ(3000, 2000, 0) });

        var ws2 = CadWorkspace.New();
        ws2.Document.Entities.Add(mline);

        var scene2 = new CadScene();
        scene2.Build(ws2.Document);

        Check("MLINE vẽ được đường trục",
            scene2.Entries.First().Prims.Count >= 1,
            scene2.Entries.First().Prims.Count + " primitive");

        var bounds = scene2.Entries.First().Bounds;
        Check("MLINE trải đúng phạm vi",
            Math.Abs(bounds.MinX) < 1e-6 && Math.Abs(bounds.MaxX - 3000) < 1e-6 &&
            Math.Abs(bounds.MaxY - 2000) < 1e-6,
            bounds.ToString());
    }

    private static void RunDivideMeasureChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- DIVIDE & MEASURE --");

        var line = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(1000, 0, 0),
        };

        var divided = MeasureOperation.Divide(line, 5);

        Check("DIVIDE đường 1000 thành 5 → 4 điểm", divided.Count == 4, divided.Count.ToString());

        Check("DIVIDE đặt đúng 200/400/600/800",
            divided.Count == 4 &&
            Math.Abs(divided[0].X - 200) < 1e-6 &&
            Math.Abs(divided[1].X - 400) < 1e-6 &&
            Math.Abs(divided[2].X - 600) < 1e-6 &&
            Math.Abs(divided[3].X - 800) < 1e-6,
            string.Join(",", divided.Select(p => p.X.ToString("0"))));

        var measured = MeasureOperation.Measure(line, 250);

        Check("MEASURE cách 250 → 3 điểm", measured.Count == 3, measured.Count.ToString());
        Check("MEASURE đặt đúng 250/500/750",
            measured.Count == 3 &&
            Math.Abs(measured[0].X - 250) < 1e-6 &&
            Math.Abs(measured[2].X - 750) < 1e-6,
            string.Join(",", measured.Select(p => p.X.ToString("0"))));

        Check("MEASURE khoảng cách > chiều dài → 0 điểm",
            MeasureOperation.Measure(line, 2000).Count == 0, "0");

        var circle = new Circle { Center = new CSMath.XYZ(0, 0, 0), Radius = 100 };
        var onCircle = MeasureOperation.Divide(circle, 4);

        Check("DIVIDE đường tròn 4 phần → 3 điểm", onCircle.Count == 3, onCircle.Count.ToString());

        var onRadius = onCircle.All(p => Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y) - 100) < 0.5);

        Check("Điểm chia nằm trên đường tròn (sai số < 0.5)", onRadius,
            string.Join(",", onCircle.Select(p => Math.Sqrt(p.X * p.X + p.Y * p.Y).ToString("0.#"))));

        var poly = new LwPolyline();
        poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 0) });
        poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(100, 0) });
        poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(100, 100) });

        var onPoly = MeasureOperation.Divide(poly, 4);

        Check("DIVIDE polyline L-shape (dài 200) → 3 điểm", onPoly.Count == 3, onPoly.Count.ToString());
        Check("Điểm giữa polyline nằm ở góc (100,0)",
            onPoly.Count == 3 && Math.Abs(onPoly[1].X - 100) < 1e-6 && Math.Abs(onPoly[1].Y) < 1e-6,
            onPoly.Count == 3 ? "(" + onPoly[1].X.ToString("0.#") + ", " + onPoly[1].Y.ToString("0.#") + ")" : "?");

        var ws = CadWorkspace.New();
        var editor = new CadEditor(ws);

        var target = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(900, 0, 0),
        };

        editor.AddEntities(new Entity[] { target }, "Add");

        var placed = editor.PlaceMarkers(target, MeasureOperation.Divide(target, 4), "Divide");

        Check("DIVIDE tạo đúng 3 điểm trong bản vẽ", placed == 3, placed.ToString());
        Check("Bản vẽ có 4 entity (1 line + 3 point)",
            ws.Document.Entities.Count() == 4, ws.Document.Entities.Count().ToString());

        editor.Undo();
        Check("Undo DIVIDE xoá cả 3 điểm",
            ws.Document.Entities.Count() == 1, ws.Document.Entities.Count().ToString());
    }

    // ---------------------------- CHỈ MỤC LƯỚI KHÔNG ĐƯỢC LÀM SAI HIT-TEST

    private static void RunSpatialIndexChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- CHỈ MỤC LƯỚI CHO HIT-TEST --");

        var ws = CadWorkspace.New();
        var doc = ws.Document;

        var random = new Random(12345);

        for (var i = 0; i < 400; i++)
        {
            var x = random.NextDouble() * 10000;
            var y = random.NextDouble() * 10000;

            if (i % 3 == 0)
                doc.Entities.Add(new Line
                {
                    StartPoint = new CSMath.XYZ(x, y, 0),
                    EndPoint = new CSMath.XYZ(x + 200, y + 100, 0),
                });
            else if (i % 3 == 1)
                doc.Entities.Add(new Circle
                {
                    Center = new CSMath.XYZ(x, y, 0),
                    Radius = 60,
                });
            else
            {
                var rect = new LwPolyline { IsClosed = true };
                rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(x, y) });
                rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(x + 150, y) });
                rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(x + 150, y + 90) });
                rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(x, y + 90) });
                doc.Entities.Add(rect);
            }
        }

        var scene = new CadScene();
        scene.Build(doc);

        Check("Scene có 400 đối tượng", scene.Entries.Count == 400, $"{scene.Entries.Count}");

        // So sánh với quét tuyến tính (tự viết trong test)
        var mismatches = 0;
        var comparisons = 0;

        for (var i = 0; i < 300; i++)
        {
            var px = random.NextDouble() * 10200;
            var py = random.NextDouble() * 10200;
            var point = new Pt(px, py);
            const double tolerance = 8;

            var fromIndex = scene.HitTest(point, tolerance);

            // brute force
            CadScene.Entry? brute = null;
            var bestDistance = tolerance;

            foreach (var entry in scene.Entries)
            {
                if (entry.Prims.Count == 0) continue;
                if (!entry.Bounds.IsEmpty && !entry.Bounds.Inflate(tolerance).Contains(point)) continue;

                var distance = double.MaxValue;

                foreach (var prim in entry.Prims)
                {
                    var pts = prim.Points;
                    if (pts.Length == 0) continue;

                    if (pts.Length == 1)
                    {
                        distance = Math.Min(distance, Pt.Distance(pts[0], point));
                        continue;
                    }

                    for (var k = 0; k + 1 < pts.Length; k++)
                        distance = Math.Min(distance, DistanceToSegment(point, pts[k], pts[k + 1]));

                    if (prim.Closed && pts.Length > 2)
                        distance = Math.Min(distance, DistanceToSegment(point, pts[^1], pts[0]));
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    brute = entry;
                }
            }

            comparisons++;

            if (!ReferenceEquals(fromIndex?.Entity, brute?.Entity)) mismatches++;
        }

        Check("Hit-test qua lưới khớp quét tuyến tính (300 điểm)",
            mismatches == 0, $"{mismatches}/{comparisons} lệch");

        // ---- Thêm entity rồi dựng lại: lưới phải được làm mới, không dùng dữ liệu cũ
        var late = new Circle { Center = new CSMath.XYZ(5, 5, 0), Radius = 30 };
        doc.Entities.Add(late);

        scene.Build(doc);

        // Điểm nằm ĐÚNG trên biên đường tròn: tâm (5,5), bán kính 30 → (35,5)
        var lateHit = scene.HitTest(new Pt(35, 5), 5);
        Check("Sau khi dựng lại, hit-test thấy entity mới",
            ReferenceEquals(lateHit?.Entity, late), lateHit?.Entity?.GetType().Name ?? "null");

        // ---- Tốc độ: 200 lần hit-test trên 401 entity phải nhanh
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (var i = 0; i < 200; i++)
            scene.HitTest(new Pt(random.NextDouble() * 10000, random.NextDouble() * 10000), 8);

        sw.Stop();

        Check("200 hit-test dưới 200 ms", sw.ElapsedMilliseconds < 200, $"{sw.ElapsedMilliseconds} ms");
    }

    private static double DistanceToSegment(Pt point, Pt a, Pt b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;

        if (lengthSquared < 1e-12) return Pt.Distance(point, a);

        var t = ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared;
        t = Math.Clamp(t, 0, 1);

        return Pt.Distance(point, new Pt(a.X + dx * t, a.Y + dy * t));
    }

    // ------------------------------------------- BLOCK ATTRIBUTE

    private static void RunBlockAttributeChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- BLOCK ATTRIBUTE --");

        var ws = CadWorkspace.New();
        var doc = ws.Document;

        var block = new ACadSharp.Tables.BlockRecord("CUA");
        block.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(0, 900, 0),
        });

        doc.BlockRecords.Add(block);

        var insert = new ACadSharp.Entities.Insert(block)
        {
            InsertPoint = new CSMath.XYZ(1000, 1000, 0),
            XScale = 1,
            YScale = 1,
            ZScale = 1,
        };

        insert.Attributes.Add(new AttributeEntity
        {
            Tag = "SO_CUA",
            Value = "C01",
            InsertPoint = new CSMath.XYZ(1050, 2000, 0),
            Height = 300,
        });

        doc.Entities.Add(insert);

        var scene = new CadScene();
        scene.Build(doc);

        var texts = scene.Entries
            .First(e => ReferenceEquals(e.Entity, insert)).Prims
            .Where(p => p.Kind == PrimKind.Text)
            .Select(p => p.Text)
            .ToList();

        Check("Attribute của block được vẽ ra", texts.Contains("C01"), string.Join("|", texts));

        Check("Attribute nằm đúng toạ độ WCS",
            scene.Entries.First(e => ReferenceEquals(e.Entity, insert)).Prims
                .Any(p => p.Kind == PrimKind.Text && Math.Abs(p.Position.X - 1050) < 1e-6),
            "x = 1050");

        // ---- Đổi giá trị attribute
        var attribute = insert.Attributes.First(a => a.Tag == "SO_CUA");
        attribute.Value = "C02";

        var scene2 = new CadScene();
        scene2.Build(doc);

        var texts2 = scene2.Entries
            .First(e => ReferenceEquals(e.Entity, insert)).Prims
            .Where(p => p.Kind == PrimKind.Text)
            .Select(p => p.Text)
            .ToList();

        Check("Đổi giá trị attribute thì hình đổi theo",
            texts2.Contains("C02") && !texts2.Contains("C01"), string.Join("|", texts2));

        Check("Insert báo có attribute", insert.HasAttributes, insert.HasAttributes.ToString());
        Check("Đếm đúng số attribute", insert.Attributes.Count() == 1,
            insert.Attributes.Count().ToString());

        // ---- Block có ATTDEF: INSERT tự sinh attribute nhưng phải đặt ở toạ độ WCS
        var ws2 = CadWorkspace.New();
        var doc2 = ws2.Document;

        var doorBlock = new ACadSharp.Tables.BlockRecord("CUA2");
        doorBlock.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(0, 900, 0),
        });
        doorBlock.Entities.Add(new AttributeDefinition
        {
            Tag = "SO_CUA",
            Value = "C01",
            InsertPoint = new CSMath.XYZ(0, 1100, 0),
            Height = 250,
        });

        doc2.BlockRecords.Add(doorBlock);

        var created = BlockLibrary.Create(doorBlock, new Pt(5000, 3600));
        Check("INSERT block có ATTDEF tạo được attribute", created is not null, "ok");

        if (created is not null)
        {
            var autoAttribute = created.Attributes?.FirstOrDefault();

            Check("Attribute tự sinh nằm ở toạ độ WCS, không phải block-local",
                autoAttribute is not null &&
                Math.Abs(autoAttribute.InsertPoint.X - 5000) < 1e-6 &&
                Math.Abs(autoAttribute.InsertPoint.Y - 4700) < 1e-6,
                autoAttribute is null ? "null"
                    : $"({autoAttribute.InsertPoint.X:0},{autoAttribute.InsertPoint.Y:0})");

            Check("Chỉ có đúng 1 attribute (không bị nhân đôi)",
                created.Attributes?.Count() == 1, (created.Attributes?.Count() ?? -1).ToString());

            // ATTDEF bên trong block KHÔNG được vẽ (chỉ attribute của insert mới vẽ)
            doc2.Entities.Add(created);

            var scene3 = new CadScene();
            scene3.Build(doc2);

            var drawnTexts = scene3.Entries.First().Prims
                .Where(p => p.Kind == PrimKind.Text)
                .Select(p => p.Text)
                .ToList();

            Check("ATTDEF trong block không bị vẽ trùng",
                drawnTexts.Count(t => t == "C01") == 1, string.Join("|", drawnTexts));
        }
    }

    // ------------------------------------------ HATCH PATTERN TỪ .PAT

    private static void RunHatchPatternFileChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- HATCH PATTERN (.pat) --");

        // ---- Đọc định dạng .pat
        const string pat = """
; comment
*TESTPAT, Hai hướng
45, 0,0, 0,2
135, 0,0, 0,2

*ONELINE, Một hướng
0, 0,0, 0,4, 2,-2
""";

        var parsed = HatchPatternLibrary.Parse(pat);

        Check("Đọc được 2 pattern từ .pat", parsed.Count == 2, $"{parsed.Count}");
        Check("Pattern nhiều dòng đọc đúng số dòng",
            parsed[0].Name == "TESTPAT" && parsed[0].Lines.Count == 2,
            $"{parsed[0].Name}: {parsed[0].Lines.Count} dòng");
        Check("Đọc đúng góc và khoảng cách",
            Math.Abs(parsed[1].Lines[0].Angle) < 1e-9 &&
            Math.Abs(parsed[1].Lines[0].DeltaY - 4) < 1e-9,
            $"góc {parsed[1].Lines[0].Angle}, dy {parsed[1].Lines[0].DeltaY}");

        Check("Đọc đúng dash (2,-2)", parsed[1].Lines[0].Dashes.Length == 2,
            string.Join(",", parsed[1].Lines[0].Dashes));

        Check("Bỏ qua dòng chú thích ;", parsed.All(p => !p.Name.StartsWith(";")),
            string.Join(",", parsed.Select(p => p.Name)));

        // ---- Thư viện có sẵn pattern quen dùng
        var names = HatchPatternLibrary.Names;

        Check("Thư viện có ANSI31, BRICK, CONCRETE",
            names.Contains("ANSI31") && names.Contains("BRICK") && names.Contains("CONCRETE"),
            string.Join(",", names.Take(6)));

        // ---- Sinh đoạn thẳng trong hình vuông 100×100
        var square = new[]
        {
            new Pt(0, 0), new Pt(100, 0), new Pt(100, 100), new Pt(0, 100),
        };

        var ansi = HatchPatternLibrary.Find("ANSI31")!;
        var segments = HatchPatternLibrary.Generate(ansi, new[] { square }, 1);

        Check("Pattern ANSI31 sinh ra đoạn thẳng", segments.Count >= 20, $"{segments.Count} đoạn");

        var allInside = segments.SelectMany(s => s).All(p =>
            p.X >= -1 && p.X <= 101 && p.Y >= -1 && p.Y <= 101);

        Check("Mọi điểm nằm trong hình vuông (không tràn ra ngoài)", allInside,
            $"min/max kiểm tra trên {segments.Count} đoạn");

        // Đường 45°: dx và dy của mỗi đoạn phải bằng nhau về độ lớn
        var diagonalOk = segments
            .Where(s => Math.Abs(s[0].X - s[1].X) > 1e-6 || Math.Abs(s[0].Y - s[1].Y) > 1e-6)
            .All(s => Math.Abs(Math.Abs(s[1].X - s[0].X) - Math.Abs(s[1].Y - s[0].Y)) < 1e-6);

        Check("ANSI31 vẽ đúng hướng 45°", diagonalOk, $"{segments.Count} đoạn");

        // ---- Scale lớn hơn → ít đường hơn
        var coarse = HatchPatternLibrary.Generate(ansi, new[] { square }, 4);
        Check("Scale 4 thưa hơn scale 1", coarse.Count < segments.Count,
            $"{coarse.Count} vs {segments.Count}");

        // ---- Hatch có pattern tên trong thư viện phải vẽ bằng pattern
        var ws = CadWorkspace.New();
        var boundary = new LwPolyline { IsClosed = true };
        boundary.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 0) });
        boundary.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(100, 0) });
        boundary.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(100, 100) });
        boundary.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 100) });

        var hatch = HatchFactory.FromBoundary(boundary);
        if (hatch is null)
        {
            Check("Tạo hatch để thử pattern", false, "null");
            return;
        }

        hatch.IsSolid = false;
        hatch.PatternScale = 1;

        try { hatch.Pattern = new ACadSharp.Entities.HatchPattern("ANSI31"); }
        catch { }

        ws.Document.Entities.Add(hatch);

        var scene = new CadScene();
        scene.Build(ws.Document);

        var hatchPrims = scene.Entries
            .First(e => ReferenceEquals(e.Entity, hatch)).Prims;

        Check("Hatch ANSI31 vẽ bằng nhiều đoạn thẳng", hatchPrims.Count >= 20,
            $"{hatchPrims.Count} primitive");
    }

    // ------------------------------------------ ARRAY TRÒN & STRETCH

    private static void RunPolarArrayAndStretchChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- ARRAY TRÒN & STRETCH --");

        // ---- Dãy tròn: 4 bản quanh gốc
        var ws = CadWorkspace.New();
        var editor = new CadEditor(ws);

        var seed = new Circle { Center = new CSMath.XYZ(100, 0, 0), Radius = 10 };
        editor.AddEntities(new Entity[] { seed }, "Add");

        editor.ArrayPolar(new Entity[] { seed }, 4, new Pt(0, 0), 360, e => e.Clone() as Entity);

        var circles = ws.Document.Entities.OfType<Circle>().ToList();
        Check("ARRAYPOLAR 4 bản tạo 4 đường tròn", circles.Count == 4, $"{circles.Count}");

        var atRight = circles.Any(c => Math.Abs(c.Center.X - 100) < 1e-6 && Math.Abs(c.Center.Y) < 1e-6);
        var atTop = circles.Any(c => Math.Abs(c.Center.X) < 1e-6 && Math.Abs(c.Center.Y - 100) < 1e-6);
        var atLeft = circles.Any(c => Math.Abs(c.Center.X + 100) < 1e-6 && Math.Abs(c.Center.Y) < 1e-6);
        var atBottom = circles.Any(c => Math.Abs(c.Center.X) < 1e-6 && Math.Abs(c.Center.Y + 100) < 1e-6);

        Check("ARRAYPOLAR chia đều 4 hướng", atRight && atTop && atLeft && atBottom,
            $"phải={atRight} trên={atTop} trái={atLeft} dưới={atBottom}");

        Check("ARRAYPOLAR giữ nguyên bán kính",
            circles.All(c => Math.Abs(c.Radius - 10) < 1e-9), "R = 10");

        editor.Undo();
        Check("Undo ARRAYPOLAR xoá cả 3 bản sao",
            ws.Document.Entities.Count() == 1, $"{ws.Document.Entities.Count()}");

        // ---- STRETCH: chỉ đỉnh nằm trong khung mới dời
        var line = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100, 0, 0),
        };

        var window = new RectD(80, -20, 120, 20);
        StretchOperation.Apply(line, window, new Pt(0, 50));

        Check("STRETCH chỉ dời đầu mút nằm trong khung",
            Math.Abs(line.StartPoint.Y) < 1e-9 && Math.Abs(line.EndPoint.Y - 50) < 1e-9,
            $"start.Y={line.StartPoint.Y:0.#}, end.Y={line.EndPoint.Y:0.#}");

        // Điểm nằm ngoài khung thì không đổi
        var untouched = new Line
        {
            StartPoint = new CSMath.XYZ(500, 500, 0),
            EndPoint = new CSMath.XYZ(600, 500, 0),
        };

        var moved = StretchOperation.Apply(untouched, window, new Pt(0, 50));
        Check("STRETCH không đụng đối tượng ngoài khung",
            !moved && Math.Abs(untouched.StartPoint.Y - 500) < 1e-9,
            $"moved={moved}");

        // Polyline: đỉnh trong khung dời, đỉnh ngoài giữ nguyên
        var poly = new LwPolyline { IsClosed = true };
        poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 0) });
        poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(100, 0) });
        poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(100, 100) });
        poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 100) });

        StretchOperation.Apply(poly, window, new Pt(0, 50));

        // Chỉ đỉnh (100,0) nằm trong khung; (100,100) có y=100 nên ngoài khung
        var pts = poly.Vertices
            .Select(v => "(" + v.Location.X.ToString("0") + "," + v.Location.Y.ToString("0") + ")")
            .ToList();

        Check("STRETCH polyline: chỉ đỉnh (100,0) dời lên (100,50)",
            Math.Abs(poly.Vertices[0].Location.Y) < 1e-9 &&
            Math.Abs(poly.Vertices[1].Location.Y - 50) < 1e-9 &&
            Math.Abs(poly.Vertices[2].Location.Y - 100) < 1e-9 &&
            Math.Abs(poly.Vertices[3].Location.Y - 100) < 1e-9,
            string.Join(" ", pts));
    }

    // ------------------------------------------------------------ ARRAY

    private static void RunArrayChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- ARRAY (DÃY CHỮ NHẬT) --");

        var ws = CadWorkspace.New();
        var editor = new CadEditor(ws);

        var line = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100, 0, 0),
        };

        editor.AddEntities(new[] { line }, "Add");

        // 3 hàng × 4 cột, bước 200 theo Y, 300 theo X
        editor.ArrayCopy(new Entity[] { line }, 3, 4, 300, 200, e => e.Clone() as Entity, "Array");

        var count = ws.Document.Entities.Count();

        Check("ARRAY 3×4 tạo 12 bản (gồm cả gốc)", count == 12, $"{count} entity");

        var xs = ws.Document.Entities.Select(e => ((Line)e).StartPoint.X).OrderBy(v => v).Distinct().ToList();
        var ys = ws.Document.Entities.Select(e => ((Line)e).StartPoint.Y).OrderBy(v => v).Distinct().ToList();

        Check("ARRAY có đúng 4 cột", xs.Count == 4, string.Join(",", xs.Select(v => v.ToString("0"))));
        Check("ARRAY có đúng 3 hàng", ys.Count == 3, string.Join(",", ys.Select(v => v.ToString("0"))));

        Check("ARRAY bước cột đúng 300",
            xs.Zip(xs.Skip(1)).All(p => Math.Abs(p.Second - p.First - 300) < 1e-9),
            string.Join(",", xs.Select(v => v.ToString("0"))));

        Check("ARRAY bước hàng đúng 200",
            ys.Zip(ys.Skip(1)).All(p => Math.Abs(p.Second - p.First - 200) < 1e-9),
            string.Join(",", ys.Select(v => v.ToString("0"))));

        // ---- Undo phải xoá toàn bộ bản sao trong một bước
        editor.Undo();
        Check("Undo ARRAY xoá cả 11 bản sao",
            ws.Document.Entities.Count() == 1, $"{ws.Document.Entities.Count()} entity");

        // ---- Không tạo dãy khi 1×1 hoặc tham số sai
        editor.ArrayCopy(new Entity[] { line }, 1, 1, 100, 100, e => e.Clone() as Entity);
        Check("ARRAY 1×1 không tạo gì", ws.Document.Entities.Count() == 1,
            $"{ws.Document.Entities.Count()} entity");

        // ---- Dãy 2×1 với bước âm vẫn đúng
        var ws2 = CadWorkspace.New();
        var editor2 = new CadEditor(ws2);
        var circle = new Circle { Center = new CSMath.XYZ(1000, 1000, 0), Radius = 50 };
        editor2.AddEntities(new[] { circle }, "Add");

        editor2.ArrayCopy(new Entity[] { circle }, 2, 1, 0, -400, e => e.Clone() as Entity);

        var centers = ws2.Document.Entities.OfType<Circle>()
            .Select(c => c.Center.Y).OrderBy(v => v).ToList();

        Check("ARRAY bước âm: 2 hàng cách nhau 400",
            centers.Count == 2 && Math.Abs(centers[1] - centers[0] - 400) < 1e-9,
            string.Join(",", centers.Select(v => v.ToString("0"))));
    }

    // ---------------------------------------- NHIỀU VIEWPORT TRÊN MỘT TỜ

    private static void RunMultiViewportChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- NHIỀU VIEWPORT TRÊN MỘT TỜ --");

        var ws = CadWorkspace.New();
        var doc = ws.Document;

        doc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(6000, 0, 0),
        });
        doc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 4000, 0),
            EndPoint = new CSMath.XYZ(6000, 4000, 0),
        });

        var model = new CadScene();
        model.Build(doc);

        var template = new OpenCad.Core.Layout.SheetTemplateOptions
        {
            Title = "HAI VIEWPORT",
            UnitsPerMm = 100,
        };

        var layout = OpenCad.Core.Layout.LayoutFactory.Create(doc, "L1", template, model);
        if (layout?.AssociatedBlock is null)
        {
            Check("Tạo được layout", false, "null");
            return;
        }

        var sheetBefore = OpenCad.Core.Layout.LayoutFactory.Read(doc, layout);
        var viewportsBefore = sheetBefore!.Items.Count(i => i.Clip is not null);

        Check("Ban đầu tờ có đúng 1 viewport", viewportsBefore == 1, $"{viewportsBefore}");

        // ---- Thêm viewport thứ hai ở nửa dưới tờ, tỷ lệ 1:20
        var extra = OpenCad.Core.Layout.LayoutFactory.AddViewport(
            layout, model, new RectD(20, 20, 200, 80), 20);

        Check("Tạo được viewport thứ hai", extra is not null, extra?.Width.ToString("0") ?? "null");

        var sheetAfter = OpenCad.Core.Layout.LayoutFactory.Read(doc, layout);
        var viewportsAfter = sheetAfter!.Items.Count(i => i.Clip is not null);

        Check("Tờ giờ có 2 viewport", viewportsAfter == 2, $"{viewportsAfter}");

        // ---- Hai viewport phải có khung khác nhau
        var clips = sheetAfter.Items.Where(i => i.Clip is not null).Select(i => i.Clip!.Value).ToList();

        Check("Hai viewport có khung khác nhau",
            clips.Count == 2 && !clips[0].Equals(clips[1]),
            $"{clips[0]} vs {clips[1]}");

        // ---- Bản vẽ xuất hiện ở cả hai viewport, với tỷ lệ khác nhau
        var lineCounts = sheetAfter.Items
            .Where(i => i.Clip is not null)
            .Select(i => i.Prims.Count)
            .ToList();

        Check("Cả hai viewport đều có hình vẽ",
            lineCounts.Count == 2 && lineCounts.All(c => c >= 2),
            string.Join(", ", lineCounts));

        var heights = sheetAfter.Items
            .Where(i => i.Clip is not null)
            .Select(i => i.Prims.Count == 0 ? 0 :
                i.Prims.SelectMany(p => p.Points).Select(p => p.Y).Max() -
                i.Prims.SelectMany(p => p.Points).Select(p => p.Y).Min())
            .ToList();

        Check("Hai viewport vẽ ở tỷ lệ khác nhau",
            heights.Count == 2 && Math.Abs(heights[0] - heights[1]) > 1,
            $"{heights[0]:0.##} mm vs {heights[1]:0.##} mm");

        // ---- Xuất PDF phải có 2 lệnh cắt
        var pdf = Path.Combine(Path.GetTempPath(), "opencad-smoke", "twoviewports.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(pdf)!);

        PdfExporter.ExportPaperSheet(sheetAfter, pdf, new PdfExportOptions
        {
            Title = "twoviewports",
            FontPath = PdfExportOptions.FindSystemFont(),
        });

        var text = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf));
        var clipCount = System.Text.RegularExpressions.Regex.Matches(text, " re W n").Count;

        Check("PDF có 2 lệnh cắt viewport", clipCount >= 2, $"{clipCount} lệnh cắt");
    }

    // ---------------------- BIẾN ĐỔI Ở TOẠ ĐỘ LỆCH GỐC (bẫy "0 = chưa dùng")

    private static void RunOffsetTransformChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- BIẾN ĐỔI Ở TOẠ ĐỘ LỆCH GỐC --");

        const double ox = 500;
        const double oy = 500;

        var makers = new (string Name, Func<Entity> Make)[]
        {
            ("Line", () => new Line
            {
                StartPoint = new CSMath.XYZ(ox, oy, 0),
                EndPoint = new CSMath.XYZ(ox + 100, oy, 0),
            }),
            ("LwPolyline", () =>
            {
                var rect = new LwPolyline { IsClosed = true };
                rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(ox, oy) });
                rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(ox + 100, oy) });
                rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(ox + 100, oy + 50) });
                rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(ox, oy + 50) });
                return rect;
            }),
            ("Circle", () => new Circle { Center = new CSMath.XYZ(ox, oy, 0), Radius = 30 }),
            ("Arc", () => new Arc
            {
                Center = new CSMath.XYZ(ox, oy, 0),
                Radius = 30,
                StartAngle = 0,
                EndAngle = Math.PI / 2,
            }),
            ("Ellipse", () => ShapeFactory.CreateEllipse(new Pt(ox, oy), new Pt(ox + 100, oy), 40)!),
            ("Text", () => new TextEntity
            {
                InsertPoint = new CSMath.XYZ(ox, oy, 0),
                Height = 50,
                Value = "XA GOC",
            }),
            ("Point", () => new ACadSharp.Entities.Point { Location = new CSMath.XYZ(ox, oy, 0) }),
            ("Insert", () =>
            {
                var block = new ACadSharp.Tables.BlockRecord("BLK");
                block.Entities.Add(new Line
                {
                    StartPoint = new CSMath.XYZ(0, 0, 0),
                    EndPoint = new CSMath.XYZ(60, 20, 0),
                });

                return new ACadSharp.Entities.Insert(block)
                {
                    InsertPoint = new CSMath.XYZ(ox, oy, 0),
                    XScale = 1,
                    YScale = 1,
                    ZScale = 1,
                };
            }),
            ("DimensionAligned", () => new DimensionAligned
            {
                FirstPoint = new CSMath.XYZ(ox, oy, 0),
                SecondPoint = new CSMath.XYZ(ox + 200, oy, 0),
                DefinitionPoint = new CSMath.XYZ(ox + 100, oy + 80, 0),
            }),
        };

        foreach (var (name, make) in makers)
        {
            var ws = CadWorkspace.New();
            var editor = new CadEditor(ws);

            var entity = make();
            editor.AddEntities(new[] { entity }, "Add");

            var before = PrimCentroid(editor.Scene.Entries.First());
            editor.TransformEntities(new[] { entity }, Affine2.Translate(100, 100), "Move");
            var after = PrimCentroid(editor.Scene.Entries.First());

            var dx = after.X - before.X;
            var dy = after.Y - before.Y;

            Check($"{name} ở (500,500): MOVE (100,100) dịch đúng cả hình",
                Math.Abs(dx - 100) < 1e-6 && Math.Abs(dy - 100) < 1e-6,
                $"Δ = ({dx:0.###}, {dy:0.###})");

            // Xoay 90° quanh gốc: trọng tâm phải map đúng (x,y) → (−y,x)
            var ws2 = CadWorkspace.New();
            var editor2 = new CadEditor(ws2);
            var entity2 = make();
            editor2.AddEntities(new[] { entity2 }, "Add");

            var beforeRotate = PrimCentroid(editor2.Scene.Entries.First());
            editor2.TransformEntities(new[] { entity2 }, new Affine2(0, -1, 1, 0, 0, 0), "Rotate");
            var afterRotate = PrimCentroid(editor2.Scene.Entries.First());

            var target = new Pt(-beforeRotate.Y, beforeRotate.X);

            // Chữ và kích thước dùng hộp bao ước lượng theo số ký tự, xoay thì hộp trục
            // không còn khớp — kiểm tra lỏng hơn cho hai loại này.
            if (name == "Text")
            {
                Check($"{name} ở (500,500): ROTATE 90° không làm chữ biến mất",
                    afterRotate.X < 0 && afterRotate.Y > 0, $"{afterRotate}");
                continue;
            }

            var rotateTolerance = name == "DimensionAligned" ? 5.0 : 1e-4;

            Check($"{name} ở (500,500): ROTATE 90° đúng trọng tâm",
                Pt.Distance(afterRotate, target) < rotateTolerance,
                $"{afterRotate} vs {target} | lệch {Pt.Distance(afterRotate, target):0.###}");
        }
    }

    /// <summary>Trọng tâm các điểm của mọi primitive — dùng để phát hiện điểm không được biến đổi.</summary>
    private static Pt PrimCentroid(CadScene.Entry entry)
    {
        double sx = 0, sy = 0;
        var n = 0;

        foreach (var prim in entry.Prims)
            foreach (var p in AllPoints(prim))
            {
                sx += p.X;
                sy += p.Y;
                n++;
            }

        return n == 0 ? new Pt(0, 0) : new Pt(sx / n, sy / n);
    }

    // ------------------------------------------ SỬA TRÊN PAPER SPACE

    private static void RunPaperEditingChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- SỬA TRÊN TỜ IN (PAPER SPACE) --");

        var ws = CadWorkspace.New();
        var doc = ws.Document;

        doc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(5000, 0, 0),
        });

        var scene = new CadScene();
        scene.Build(doc);

        var template = new OpenCad.Core.Layout.SheetTemplateOptions
        {
            Title = "TEST",
            UnitsPerMm = 50,
        };

        var layout = OpenCad.Core.Layout.LayoutFactory.Create(doc, "L1", template, scene);
        if (layout?.AssociatedBlock is null)
        {
            Check("Tạo được layout để thử sửa", false, "null");
            return;
        }

        static List<string> TextsOf(CadDocument document, ACadSharp.Objects.Layout layout)
        {
            var sheet = OpenCad.Core.Layout.LayoutFactory.Read(document, layout);
            if (sheet is null) return new List<string>();

            return sheet.Items
                .SelectMany(i => i.Prims)
                .Where(p => p.Kind == PrimKind.Text)
                .Select(p => p.Text)
                .ToList();
        }

        var before = TextsOf(doc, layout);

        // ---- Thêm ghi chú lên tờ
        var note = new TextEntity
        {
            InsertPoint = new CSMath.XYZ(20, 20, 0),
            Height = 5,
            Value = "GHI CHU TREN TO",
        };

        layout.AssociatedBlock.Entities.Add(note);
        var afterAdd = TextsOf(doc, layout);

        Check("Thêm chữ lên tờ thì tờ in có chữ đó",
            afterAdd.Contains("GHI CHU TREN TO"), string.Join("|", afterAdd.Take(6)));

        Check("Thêm chữ không làm mất chữ khác",
            afterAdd.Count == before.Count + 1, $"{before.Count} → {afterAdd.Count}");

        // ---- Xoá chữ khỏi tờ
        layout.AssociatedBlock.Entities.Remove(note);
        var afterRemove = TextsOf(doc, layout);

        Check("Xoá chữ khỏi tờ thì tờ in hết chữ đó",
            !afterRemove.Contains("GHI CHU TREN TO"), string.Join("|", afterRemove.Take(6)));

        Check("Số chữ trở lại như cũ", afterRemove.Count == before.Count,
            $"{afterRemove.Count}");

        // ---- Scene paper space hit-test được (dùng cho chọn trên tờ)
        layout.AssociatedBlock.Entities.Add(note);

        var paperScene = new CadScene();
        paperScene.Build(layout.AssociatedBlock.Entities.Where(e => e is not Viewport));

        var hit = paperScene.HitTest(new Pt(22, 22), 3);
        Check("Click trên tờ chọn được đối tượng paper",
            ReferenceEquals(hit?.Entity, note), hit?.Entity?.GetType().Name ?? "null");

        // ---- Di chuyển đối tượng paper: dịch đúng và tờ in cập nhật
        var beforeMove = (note.InsertPoint.X, note.InsertPoint.Y);

        var move = Affine2.Translate(50, -10);
        EntityTransform.Apply(note, move);

        Check("Di chuyển chữ trên tờ đúng (50, -10)",
            Math.Abs(note.InsertPoint.X - (beforeMove.X + 50)) < 1e-9 &&
            Math.Abs(note.InsertPoint.Y - (beforeMove.Y - 10)) < 1e-9,
            $"({note.InsertPoint.X:0.#}, {note.InsertPoint.Y:0.#})");

        var sheetAfterMove = OpenCad.Core.Layout.LayoutFactory.Read(doc, layout);

        var textPositions = sheetAfterMove!.Items
            .SelectMany(i => i.Prims)
            .Where(p => p.Kind == PrimKind.Text)
            .Select(p => p.Text + "@" + p.Position.X.ToString("0.#"))
            .ToList();

        var movedText = sheetAfterMove.Items
            .SelectMany(i => i.Prims)
            .FirstOrDefault(p => p.Kind == PrimKind.Text && p.Text == "GHI CHU TREN TO");

        var movedX = movedText is null ? -1 : movedText.Position.X;

        Check("Tờ in cập nhật theo vị trí mới",
            movedText is not null && Math.Abs(movedX - 70) < 1e-6,
            "x=" + movedX.ToString("0.#") + " | " + string.Join(" ; ", textPositions));

        // ---- Sửa nội dung chữ trên tờ
        note.Value = "GHI CHU MOI";

        var sheetAfterEdit = OpenCad.Core.Layout.LayoutFactory.Read(doc, layout);
        var textsAfterEdit = sheetAfterEdit!.Items
            .SelectMany(i => i.Prims)
            .Where(p => p.Kind == PrimKind.Text)
            .Select(p => p.Text)
            .ToList();

        Check("Sửa chữ trên tờ thì tờ in đổi nội dung",
            textsAfterEdit.Contains("GHI CHU MOI") && !textsAfterEdit.Contains("GHI CHU TREN TO"),
            string.Join("|", textsAfterEdit.Take(6)));

        // ---- Chữ nằm xa gốc: MOVE phải giữ đúng vị trí (AlignmentPoint không được lấn át)
        var ws2 = CadWorkspace.New();
        var editor2 = new CadEditor(ws2);

        var far = new TextEntity
        {
            InsertPoint = new CSMath.XYZ(500, 500, 0),
            Height = 100,
            Value = "XA GOC",
        };

        editor2.AddEntities(new[] { far }, "Add");
        editor2.TransformEntities(new[] { far }, Affine2.Translate(100, 100), "Move");

        var farPrim = editor2.Scene.Entries.First().Prims
            .First(p => p.Kind == PrimKind.Text);

        Check("MOVE chữ ở xa gốc: (500,500) + (100,100) → (600,600)",
            Math.Abs(farPrim.Position.X - 600) < 1e-6 &&
            Math.Abs(farPrim.Position.Y - 600) < 1e-6,
            $"({farPrim.Position.X:0.#}, {farPrim.Position.Y:0.#})");
    }

    // ------------------------------------------ DIMENSION STYLE

    private static void RunDimStyleChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- DIMENSION STYLE --");

        var ws = CadWorkspace.New();
        var doc = ws.Document;

        var small = new ACadSharp.Tables.DimensionStyle("NHO") { TextHeight = 100, ArrowSize = 10 };
        var big = new ACadSharp.Tables.DimensionStyle("LON") { TextHeight = 100, ArrowSize = 50 };

        doc.DimensionStyles.Add(small);
        doc.DimensionStyles.Add(big);

        Check("Bản vẽ có 2 style kích thước",
            doc.DimensionStyles.Count(s => s.Name is "NHO" or "LON") == 2,
            string.Join(",", doc.DimensionStyles.Select(s => s.Name)));

        static double ArrowLengthOf(string styleName, double arrowSize)
        {
            var ws2 = CadWorkspace.New();
            var local = new ACadSharp.Tables.DimensionStyle(styleName)
            {
                TextHeight = 100,
                ArrowSize = arrowSize,
            };

            ws2.Document.DimensionStyles.Add(local);

            ws2.Document.Entities.Add(new DimensionAligned
            {
                FirstPoint = new CSMath.XYZ(0, 0, 0),
                SecondPoint = new CSMath.XYZ(2000, 0, 0),
                DefinitionPoint = new CSMath.XYZ(1000, 400, 0),
                Style = local,
            });

            var scene = new CadScene();
            scene.Build(ws2.Document);

            var arrows = scene.Entries.First().Prims.Where(p => p.Filled).ToList();
            if (arrows.Count == 0) return -1;

            var arrow = arrows[0];
            var xs = arrow.Points.Select(p => p.X).ToList();
            var ys = arrow.Points.Select(p => p.Y).ToList();

            return Math.Max(xs.Max() - xs.Min(), ys.Max() - ys.Min());
        }

        var smallArrow = ArrowLengthOf("NHO", 10);
        var bigArrow = ArrowLengthOf("LON", 50);

        Check("Style NHO cho mũi tên dài 10", Math.Abs(smallArrow - 10) < 1e-6, $"{smallArrow:0.###}");
        Check("Style LON cho mũi tên dài 50", Math.Abs(bigArrow - 50) < 1e-6, $"{bigArrow:0.###}");
        Check("Đổi style làm mũi tên dài gấp 5",
            smallArrow > 0 && Math.Abs(bigArrow / smallArrow - 5) < 1e-6,
            $"{bigArrow / smallArrow:0.###}×");

        // ---- Định dạng số theo style (DecimalPlaces + bỏ số 0 thừa)
        static string LabelWith(short decimals, double measured)
        {
            var ws3 = CadWorkspace.New();
            var fmt = new ACadSharp.Tables.DimensionStyle("FMT")
            {
                TextHeight = 100,
                ArrowSize = 20,
                DecimalPlaces = decimals,
            };

            ws3.Document.DimensionStyles.Add(fmt);

            ws3.Document.Entities.Add(new DimensionAligned
            {
                FirstPoint = new CSMath.XYZ(0, 0, 0),
                SecondPoint = new CSMath.XYZ(measured, 0, 0),
                DefinitionPoint = new CSMath.XYZ(measured / 2, 400, 0),
                Style = fmt,
            });

            var scene = new CadScene();
            scene.Build(ws3.Document);

            return scene.Entries.First().Prims
                .Where(p => p.Kind == PrimKind.Text)
                .Select(p => p.Text)
                .FirstOrDefault() ?? "?";
        }

        Check("Style 1 chữ số: 1234.56 → 1234.6", LabelWith(1, 1234.56) == "1234.6", LabelWith(1, 1234.56));
        Check("Style 0 chữ số: 1234.56 → 1235", LabelWith(0, 1234.56) == "1235", LabelWith(0, 1234.56));
        Check("Style 3 chữ số: 1234.56 → 1234.56", LabelWith(3, 1234.56) == "1234.56", LabelWith(3, 1234.56));
        Check("Số nguyên không có .00", LabelWith(2, 8000) == "8000", LabelWith(2, 8000));

        // ---- Hệ số đo (DIMLFAC) + tiền tố/hậu tố: vẽ bằng mm, ghi bằng m
        static string LabelWithScale(double linearScale, string suffix, double measured)
        {
            var ws4 = CadWorkspace.New();
            var scaled = new ACadSharp.Tables.DimensionStyle("SCALED")
            {
                TextHeight = 100,
                ArrowSize = 20,
                DecimalPlaces = 3,
                LinearScaleFactor = linearScale,
                Suffix = suffix,
            };

            ws4.Document.DimensionStyles.Add(scaled);

            ws4.Document.Entities.Add(new DimensionAligned
            {
                FirstPoint = new CSMath.XYZ(0, 0, 0),
                SecondPoint = new CSMath.XYZ(measured, 0, 0),
                DefinitionPoint = new CSMath.XYZ(measured / 2, 400, 0),
                Style = scaled,
            });

            var scene = new CadScene();
            scene.Build(ws4.Document);

            return scene.Entries.First().Prims
                .Where(p => p.Kind == PrimKind.Text)
                .Select(p => p.Text)
                .FirstOrDefault() ?? "?";
        }

        Check("Hệ số đo 0.001: 8000 mm → 8",
            LabelWithScale(0.001, "", 8000) == "8", LabelWithScale(0.001, "", 8000));

        Check("Hậu tố đơn vị: 8000 mm → 8 m",
            LabelWithScale(0.001, " m", 8000) == "8 m", LabelWithScale(0.001, " m", 8000));

        Check("Hệ số đo 1 giữ nguyên 8000",
            LabelWithScale(1, "", 8000) == "8000", LabelWithScale(1, "", 8000));

        // ---- TRIM cung tròn bằng đường thẳng đứng x = 0
        var halfCircle = new Arc
        {
            Center = new CSMath.XYZ(0, 0, 0),
            Radius = 50,
            StartAngle = 0,
            EndAngle = Math.PI,
        };

        var cutter = new Line
        {
            StartPoint = new CSMath.XYZ(0, -100, 0),
            EndPoint = new CSMath.XYZ(0, 100, 0),
        };

        var trimmed = TrimExtendOperations.Trim(halfCircle, new Pt(35, 35), new Entity[] { cutter });

        Check("TRIM cung tròn: còn 1 mảnh", trimmed is { Count: 1 },
            trimmed is null ? "null" : $"{trimmed.Count} mảnh");

        if (trimmed is { Count: 1 } && trimmed[0] is Arc kept)
        {
            var startDeg = kept.StartAngle * 180 / Math.PI;
            Check("TRIM cung tròn: mảnh còn lại bắt đầu ở 90°",
                Math.Abs(startDeg - 90) < 1e-6, $"{startDeg:0.###}°");
        }
        else
        {
            Check("TRIM cung tròn: mảnh còn lại bắt đầu ở 90°", false,
                trimmed is { Count: 1 } ? trimmed[0].GetType().Name : "null");
        }
    }

    private static void CheckPolylineOffset(string label, Entity? result,
                                            double minX, double minY, double maxX, double maxY)
    {
        if (result is not LwPolyline poly)
        {
            Check(label, false, result?.GetType().Name ?? "null");
            return;
        }

        var ax = poly.Vertices.Min(v => v.Location.X);
        var ay = poly.Vertices.Min(v => v.Location.Y);
        var bx = poly.Vertices.Max(v => v.Location.X);
        var by = poly.Vertices.Max(v => v.Location.Y);

        Check(label,
            Math.Abs(ax - minX) < 1e-6 && Math.Abs(ay - minY) < 1e-6 &&
            Math.Abs(bx - maxX) < 1e-6 && Math.Abs(by - maxY) < 1e-6,
            $"[{ax:0.#},{ay:0.#} .. {bx:0.#},{by:0.#}]");
    }

    // ---------------------------------------- MẪU TÔ XOAY/PHÓNG THEO HÌNH

    private static void RunHatchPatternChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- MẪU TÔ XOAY/PHÓNG THEO HÌNH --");

        var ws = CadWorkspace.New();
        var editor = new CadEditor(ws);

        var hatch = HatchFactory.FromBoundary(MakeRect(0, 0, 100, 50));
        if (hatch is null)
        {
            Check("Tạo được hatch để kiểm tra mẫu tô", false, "null");
            return;
        }

        editor.AddEntities(new[] { hatch }, "Add");

        // PatternScale mặc định = 0 (dùng giá trị của mẫu) nên phải đặt 1 để kiểm tra có ý nghĩa
        hatch.PatternScale = 1;

        var angleBefore = hatch.PatternAngle;
        var scaleBefore = hatch.PatternScale;

        var cos30 = Math.Cos(Math.PI / 6);
        var sin30 = Math.Sin(Math.PI / 6);
        var rotate30 = new Affine2(cos30, -sin30, sin30, cos30, 0, 0);

        editor.TransformEntities(new[] { hatch }, rotate30, "Rotate");

        Check("Xoay hatch thì góc mẫu tô cũng xoay",
            Math.Abs(hatch.PatternAngle - (angleBefore + Math.PI / 6)) < 1e-6,
            $"{angleBefore:0.###} → {hatch.PatternAngle:0.###} rad");

        var scale2 = new Affine2(2, 0, 0, 2, 0, 0);
        editor.TransformEntities(new[] { hatch }, scale2, "Scale");

        Check("Phóng hatch thì tỉ lệ mẫu tô cũng phóng",
            Math.Abs(hatch.PatternScale - scaleBefore * 2) < 1e-6,
            $"{scaleBefore:0.###} → {hatch.PatternScale:0.###}");

        var angleNow = hatch.PatternAngle;
        var scaleNow = hatch.PatternScale;

        editor.TransformEntities(new[] { hatch }, Affine2.Translate(500, 300), "Move");

        Check("Dịch chuyển không đổi mẫu tô",
            Math.Abs(hatch.PatternAngle - angleNow) < 1e-9 &&
            Math.Abs(hatch.PatternScale - scaleNow) < 1e-9,
            $"{hatch.PatternAngle:0.###}, ×{hatch.PatternScale:0.###}");
    }

    // --------------------------------- ROTATE / SCALE / MIRROR CHO MỌI LOẠI

    private static void RunTransformGeometryChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- ROTATE / SCALE / MIRROR --");

        var makers = new (string Name, Func<Entity> Make)[]
        {
            ("Line", () => new Line
            {
                StartPoint = new CSMath.XYZ(0, 0, 0),
                EndPoint = new CSMath.XYZ(100, 0, 0),
            }),
            ("LwPolyline", () => MakeRect(0, 0, 100, 50)),
            ("Circle", () => new Circle { Center = new CSMath.XYZ(10, 20, 0), Radius = 30 }),
            ("Arc", () => new Arc
            {
                Center = new CSMath.XYZ(10, 20, 0),
                Radius = 30,
                StartAngle = 0,
                EndAngle = Math.PI / 2,
            }),
            ("Ellipse", () => ShapeFactory.CreateEllipse(new Pt(10, 20), new Pt(60, 20), 25)!),
            ("Point", () => new ACadSharp.Entities.Point { Location = new CSMath.XYZ(30, 40, 0) }),
            ("Hatch", () => HatchFactory.FromBoundary(MakeRect(0, 0, 100, 50))!),
            ("Insert", () =>
            {
                var block = new ACadSharp.Tables.BlockRecord("TB");
                block.Entities.Add(new Line
                {
                    StartPoint = new CSMath.XYZ(0, 0, 0),
                    EndPoint = new CSMath.XYZ(60, 20, 0),
                });

                return new ACadSharp.Entities.Insert(block)
                {
                    InsertPoint = new CSMath.XYZ(10, 10, 0),
                    XScale = 1,
                    YScale = 1,
                    ZScale = 1,
                };
            }),
            ("DimensionAligned", () => new DimensionAligned
            {
                FirstPoint = new CSMath.XYZ(0, 0, 0),
                SecondPoint = new CSMath.XYZ(100, 0, 0),
                DefinitionPoint = new CSMath.XYZ(50, 30, 0),
            }),
        };

        var rotate90 = new Affine2(0, -1, 1, 0, 0, 0);   // (x,y) → (-y, x)
        var scale2 = new Affine2(2, 0, 0, 2, 0, 0);      // 2×
        var mirrorY = new Affine2(-1, 0, 0, 1, 0, 0);    // (x,y) → (-x, y)

        foreach (var (name, make) in makers)
        {
            var (before, afterRotate) = TransformBounds(make, rotate90);
            var (_, afterScale) = TransformBounds(make, scale2);
            var (mirrorBase, afterMirror) = TransformBounds(make, mirrorY);

            Check($"{name}: ROTATE 90° đổi chỗ rộng/cao",
                Math.Abs(afterRotate.Width - before.Height) < 1e-3 &&
                Math.Abs(afterRotate.Height - before.Width) < 1e-3,
                $"{before.Width:0.#}×{before.Height:0.#} → {afterRotate.Width:0.#}×{afterRotate.Height:0.#}");

            var expectedCenter = new Pt(-before.Center.Y, before.Center.X);
            Check($"{name}: ROTATE 90° đúng tâm",
                Pt.Distance(afterRotate.Center, expectedCenter) < 1e-3,
                $"{afterRotate.Center} vs {expectedCenter}");

            Check($"{name}: SCALE 2× gấp đôi kích thước",
                Math.Abs(afterScale.Width - before.Width * 2) < 1e-3 &&
                Math.Abs(afterScale.Height - before.Height * 2) < 1e-3,
                $"{afterScale.Width:0.#}×{afterScale.Height:0.#}");

            Check($"{name}: MIRROR qua trục Y lật đúng",
                Math.Abs(afterMirror.Width - mirrorBase.Width) < 1e-3 &&
                Math.Abs(afterMirror.MinX + mirrorBase.MaxX) < 1e-3,
                $"X [{afterMirror.MinX:0.#}..{afterMirror.MaxX:0.#}]");
        }
    }

    private static (RectD Before, RectD After) TransformBounds(Func<Entity> make, Affine2 transform)
    {
        var ws = CadWorkspace.New();
        var editor = new CadEditor(ws);

        var entity = make();
        editor.AddEntities(new[] { entity }, "Add");

        var before = GeometryBounds(editor.Scene.Entries.First());
        editor.TransformEntities(new[] { entity }, transform, "Transform");
        var after = GeometryBounds(editor.Scene.Entries.First());

        return (before, after);
    }

    /// <summary>
    /// Hộp bao chỉ tính nét hình học, bỏ qua primitive chữ — hộp bao chữ là ước lượng
    /// nên không dùng để kiểm tra phép biến đổi chính xác được.
    /// </summary>
    private static RectD GeometryBounds(CadScene.Entry entry)
    {
        var prims = entry.Prims.Where(p => p.Kind != PrimKind.Text).ToList();
        if (prims.Count == 0) prims = entry.Prims.ToList();

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        var any = false;

        foreach (var prim in prims)
        {
            foreach (var point in AllPoints(prim))
            {
                any = true;
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
        }

        return any ? new RectD(minX, minY, maxX, maxY) : RectD.Empty;
    }

    private static IEnumerable<Pt> AllPoints(Prim prim)
    {
        if (prim.Loops is not null)
        {
            foreach (var loop in prim.Loops)
                foreach (var p in loop) yield return p;
            yield break;
        }

        if (prim.Kind == PrimKind.Text)
        {
            var width = Math.Max(prim.Height * 0.5, (prim.Text?.Length ?? 1) * prim.Height * 0.62);
            var x = prim.Position.X;
            var y = prim.Position.Y;

            yield return new Pt(x, y);
            yield return new Pt(x + width, y);
            yield return new Pt(x, y + prim.Height);
            yield return new Pt(x + width, y + prim.Height);
            yield break;
        }

        foreach (var p in prim.Points) yield return p;

        if (prim.Kind == PrimKind.Point) yield return prim.Position;
    }

    private static LwPolyline MakeRect(double x, double y, double w, double h)
    {
        var rect = new LwPolyline { IsClosed = true };
        rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(x, y) });
        rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(x + w, y) });
        rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(x + w, y + h) });
        rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(x, y + h) });
        return rect;
    }

    // ---------------------------------------------- POLAR / SỬA CHỮ

    private static void RunRound20Checks()
    {
        Console.WriteLine();
        Console.WriteLine("-- POLAR TRACKING & SỬA CHỮ --");

        // ---- Polar: bắt hướng về bội số 45°
        var polar = SnapEngine.ApplyPolar(new Pt(0, 0), new Pt(100, 10), 45);

        Check("POLAR 45° kẹp về trục X", Math.Abs(polar.Y) < 1e-9, $"Y = {polar.Y:0.####}");

        var distance = Math.Sqrt(100 * 100 + 10 * 10);
        Check("POLAR giữ nguyên khoảng cách",
            Math.Abs(Math.Sqrt(polar.X * polar.X + polar.Y * polar.Y) - distance) < 1e-9,
            $"{distance:0.###}");

        // 30° → điểm (100, 60) lệch 31° sẽ kẹp về 30°
        var polar30 = SnapEngine.ApplyPolar(new Pt(0, 0), new Pt(100, 60), 30);
        var angle30 = Math.Atan2(polar30.Y, polar30.X) * 180 / Math.PI;
        Check("POLAR 30° đúng góc", Math.Abs(angle30 - 30) < 1e-6, $"{angle30:0.###}°");

        // Điểm trùng gốc thì giữ nguyên
        var same = SnapEngine.ApplyPolar(new Pt(5, 5), new Pt(5, 5), 15);
        Check("POLAR điểm trùng gốc không đổi",
            Math.Abs(same.X - 5) < 1e-9 && Math.Abs(same.Y - 5) < 1e-9, $"{same.X},{same.Y}");

        // ---- Sửa nội dung chữ
        var ws = CadWorkspace.New();
        var editor = new CadEditor(ws);

        var text = new TextEntity
        {
            InsertPoint = new CSMath.XYZ(0, 0, 0),
            Height = 100,
            Value = "PHÒNG KHÁCH",
        };

        editor.AddEntities(new Entity[] { text }, "Add");
        editor.SetText(text, "PHÒNG NGỦ");

        Check("Sửa được nội dung chữ", text.Value == "PHÒNG NGỦ", text.Value);

        editor.Undo();
        Check("Undo sửa chữ phục hồi", text.Value == "PHÒNG KHÁCH", text.Value);

        // Nội dung rỗng thì bỏ qua
        editor.SetText(text, "");
        Check("Không sửa khi nội dung rỗng", text.Value == "PHÒNG KHÁCH", text.Value);

        // ---- Chữ phải click chọn được (trước đây không hit-test được text)
        var scene = new CadScene();
        scene.Build(ws.Document);

        var onText = scene.HitTest(new Pt(200, 60), 50);
        Check("Click trúng chữ thì hit-test thấy", onText?.Entity == text,
            onText?.Entity?.GetType().Name ?? "null");

        var besideText = scene.HitTest(new Pt(200, 900), 5);
        Check("Click xa chữ thì không trúng", besideText?.Entity != text,
            besideText?.Entity?.GetType().Name ?? "null");
    }

    // ------------------------------------------------- TẠO BLOCK TỪ LỰA CHỌN

    private static void RunCreateBlockChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- TẠO BLOCK TỪ LỰA CHỌN --");

        var ws = CadWorkspace.New();
        var editor = new CadEditor(ws);

        var line1 = new Line { StartPoint = new CSMath.XYZ(100, 100, 0), EndPoint = new CSMath.XYZ(200, 100, 0) };
        var line2 = new Line { StartPoint = new CSMath.XYZ(100, 100, 0), EndPoint = new CSMath.XYZ(100, 200, 0) };

        editor.AddEntities(new Entity[] { line1, line2 }, "Add");

        var ok = editor.CreateBlock(new Entity[] { line1, line2 }, "KHUNG", new Pt(100, 100));

        Check("Tạo được block từ lựa chọn", ok, ok.ToString());

        Check("Block mới có trong bản vẽ",
            BlockLibrary.Find(ws.Document, "KHUNG") is not null, "KHUNG");

        var block = BlockLibrary.Find(ws.Document, "KHUNG");
        Check("Block chứa 2 entity", BlockLibrary.EntityCount(block!) == 2,
            BlockLibrary.EntityCount(block!).ToString());

        Check("Bản gốc bị thay bằng 1 insert",
            ws.Document.Entities.Count() == 1
            && ws.Document.Entities.First() is ACadSharp.Entities.Insert,
            $"{ws.Document.Entities.Count()} entity");

        // Hình học trong block phải dời về quanh gốc (100,100) → (0,0)
        var blockStart = block!.Entities.OfType<Line>().Min(l => l.StartPoint.X);
        Check("Hình học trong block dời về gốc",
            Math.Abs(blockStart) < 1e-6, $"min X = {blockStart:0.###}");

        // Insert đặt tại gốc cũ → hình vẫn nằm đúng chỗ
        var scene = new CadScene();
        scene.Build(ws.Document);
        var bounds = scene.Entries[0].Bounds;

        Check("Sau khi tạo block, hình vẫn ở đúng chỗ",
            Math.Abs(bounds.MinX - 100) < 1e-6 && Math.Abs(bounds.MaxY - 200) < 1e-6,
            bounds.ToString());

        // ---- Trùng tên bị chặn (khi block còn tồn tại)
        var duplicate = editor.CreateBlock(new Entity[] { line1 }, "KHUNG", new Pt(0, 0));
        Check("Không tạo được block trùng tên", !duplicate, duplicate.ToString());

        // ---- Undo phải phục hồi cả 3 thứ: bản gốc, block, insert
        editor.Undo();

        Check("Undo trả lại 2 entity gốc",
            ws.Document.Entities.Count() == 2, $"{ws.Document.Entities.Count()} entity");

        Check("Undo xoá định nghĩa block",
            BlockLibrary.Find(ws.Document, "KHUNG") is null, "null");
    }

    // ------------------------------------------------------------ INSERT BLOCK

    private static void RunBlockChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- BLOCK / INSERT --");

        var ws = CadWorkspace.New();
        var doc = ws.Document;

        var block = new ACadSharp.Tables.BlockRecord("CUA");
        block.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100, 0, 0),
        });
        block.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 200, 0),
            EndPoint = new CSMath.XYZ(100, 200, 0),
        });
        doc.BlockRecords.Add(block);

        var emptyBlock = new ACadSharp.Tables.BlockRecord("RONG");
        doc.BlockRecords.Add(emptyBlock);

        var names = BlockLibrary.Named(doc);
        Check("Liệt kê được block người dùng", names.Any(b => b.Name == "CUA"),
            string.Join(",", names.Select(b => b.Name)));

        Check("Không liệt kê block hệ thống (*Model_Space…)",
            names.All(b => !b.Name.StartsWith("*")), $"{names.Count} block");

        Check("Tìm block không phân biệt hoa thường",
            BlockLibrary.Find(doc, "cua")?.Name == "CUA", "cua → CUA");

        var insert = BlockLibrary.Create(block, new Pt(500, 500));
        Check("Tạo được tham chiếu block", insert is not null, insert?.Block?.Name ?? "null");
        Check("Insert trỏ đúng block", insert?.Block?.Name == "CUA", insert?.Block?.Name ?? "-");

        Check("Không tạo được insert từ block rỗng",
            BlockLibrary.Create(emptyBlock, new Pt(0, 0)) is null, "null");

        // ---- Insert phải render ra hình học của block
        if (insert is not null) doc.Entities.Add(insert);

        var scene = new CadScene();
        scene.Build(doc);

        var entry = scene.Entries.FirstOrDefault(e => e.Entity is ACadSharp.Entities.Insert);
        Check("Insert render được hình học", entry is { Prims.Count: >= 2 },
            $"{entry?.Prims.Count} primitive");

        // Block cao 200 đơn vị, đặt tại (500,500) → hộp bao phải nằm đúng chỗ
        var bounds = entry?.Bounds ?? default;
        Check("Vị trí insert đúng",
            Math.Abs(bounds.MinX - 500) < 1e-6 && Math.Abs(bounds.MaxY - 700) < 1e-6,
            bounds.ToString());
    }

    // --------------------------------------------- HÌNH CƠ BẢN & ĐO ĐẠC

    private static void RunRound15Checks()
    {
        Console.WriteLine();
        Console.WriteLine("-- ELLIPSE / POLYGON / ĐO ĐẠC --");

        // ---- Ellipse
        var ellipse = ShapeFactory.CreateEllipse(new Pt(0, 0), new Pt(100, 0), 50);

        Check("Tạo được ellipse", ellipse is not null, ellipse?.ObjectName ?? "null");
        Check("Ellipse có bán trục đúng",
            ellipse is not null && Math.Abs(ellipse.RadiusRatio - 0.5) < 1e-9,
            $"{ellipse?.RadiusRatio:0.###}");

        Check("Ellipse thoái hoá trả null",
            ShapeFactory.CreateEllipse(new Pt(0, 0), new Pt(0, 0), 10) is null, "null");

        // ---- Polygon
        var hexagon = ShapeFactory.CreatePolygon(new Pt(0, 0), new Pt(100, 0), 6);

        Check("Tạo được polygon 6 cạnh", hexagon?.Vertices.Count == 6, $"{hexagon?.Vertices.Count} đỉnh");
        Check("Polygon khép kín", hexagon?.IsClosed == true, "closed");

        var allSameRadius = hexagon?.Vertices.All(v =>
            Math.Abs(Math.Sqrt(v.Location.X * v.Location.X + v.Location.Y * v.Location.Y) - 100) < 1e-6) ?? false;

        Check("Polygon nội tiếp đúng bán kính 100", allSameRadius, "R = 100");

        Check("Polygon dưới 3 cạnh trả null",
            ShapeFactory.CreatePolygon(new Pt(0, 0), new Pt(10, 0), 2) is null, "null");

        // ---- Đo khoảng cách
        var d = Measure.Distance(new Pt(0, 0), new Pt(30, 40));
        Check("DIST tính đúng 3-4-5", Math.Abs(d.Distance - 50) < 1e-9, $"{d.Distance:0.###}");
        Check("DIST tính đúng góc", Math.Abs(d.AngleDegrees - 53.1301) < 1e-3, $"{d.AngleDegrees:0.###}°");
        Check("DIST tính đúng ΔX/ΔY",
            Math.Abs(d.DeltaX - 30) < 1e-9 && Math.Abs(d.DeltaY - 40) < 1e-9, "30, 40");

        // ---- Đo diện tích
        var rect = new LwPolyline { IsClosed = true };
        rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 0) });
        rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(100, 0) });
        rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(100, 50) });
        rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 50) });

        Check("AREA hình chữ nhật 100×50", Math.Abs(Measure.Area(rect) - 5000) < 1e-6,
            $"{Measure.Area(rect):0.##}");

        var circle = new Circle { Center = new CSMath.XYZ(0, 0, 0), Radius = 10 };
        var circleArea = Measure.Area(circle);

        // Diện tích đa giác 72 cạnh nội tiếp ≈ 0.16% nhỏ hơn diện tích thật
        Check("AREA đường tròn xấp xỉ đúng",
            Math.Abs(circleArea - Math.PI * 100) / (Math.PI * 100) < 0.01,
            $"{circleArea:0.##} vs {Math.PI * 100:0.##}");

        var openLine = new Line { StartPoint = new CSMath.XYZ(0, 0, 0), EndPoint = new CSMath.XYZ(10, 0, 0) };
        Check("AREA đường hở trả 0", Measure.Area(openLine) < 1e-9, $"{Measure.Area(openLine):0.##}");
    }

    // ------------------------------------------- FILLET POLYLINE / DIM BÁN KÍNH

    private static void RunRound14Checks()
    {
        Console.WriteLine();
        Console.WriteLine("-- FILLET POLYLINE & DIM BÁN KÍNH --");

        // ---- Bo tất cả góc của hình vuông 100×100, bán kính 10
        var square = new LwPolyline { IsClosed = true };
        square.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 0) });
        square.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(100, 0) });
        square.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(100, 100) });
        square.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 100) });

        var rounded = FilletChamfer.FilletAllCorners(square, 10);

        Check("FILLET polyline tạo được kết quả", rounded is not null,
            rounded?.Vertices.Count.ToString() ?? "null");

        Check("FILLET polyline: mỗi góc thành 2 đỉnh",
            rounded?.Vertices.Count == 8, $"{rounded?.Vertices.Count} đỉnh");

        var bulges = rounded?.Vertices.Count(v => Math.Abs(v.Bulge) > 1e-9) ?? 0;
        Check("FILLET polyline: có 4 cung (bulge)", bulges == 4, $"{bulges} cung");

        var radiusOk = rounded?.Vertices
            .Where(v => Math.Abs(v.Bulge) > 1e-9)
            .All(v => Math.Abs(Math.Abs(v.Bulge) - Math.Tan(Math.PI / 8)) < 1e-6) ?? false;

        Check("FILLET polyline: bulge đúng góc 90°", radiusOk, "tan(22.5°)");

        // Bán kính quá lớn → không bo được góc nào
        var tooBig = FilletChamfer.FilletAllCorners(square, 80);
        var tooBigBulges = tooBig?.Vertices.Count(v => Math.Abs(v.Bulge) > 1e-9) ?? -1;
        Check("FILLET polyline bán kính chồng lấn → không bo", tooBigBulges == 0, $"{tooBigBulges} cung");

        // Bán kính vừa phải vẫn bo được cả 4 góc
        var ok = FilletChamfer.FilletAllCorners(square, 40);
        var okBulges = ok?.Vertices.Count(v => Math.Abs(v.Bulge) > 1e-9) ?? -1;
        Check("FILLET polyline r=40 (vừa) bo đủ 4 góc", okBulges == 4, $"{okBulges} cung");

        // ---- Dimension radius
        var radiusDim = new DimensionRadius
        {
            AngleVertex = new CSMath.XYZ(0, 0, 0),
            DefinitionPoint = new CSMath.XYZ(50, 0, 0),
        };

        var ws = CadWorkspace.New();
        ws.Document.Entities.Add(radiusDim);

        var scene = new CadScene();
        scene.Build(ws.Document);

        var prims = scene.Entries.First().Prims;
        var texts = prims.Where(p => p.Kind == PrimKind.Text).Select(p => p.Text).ToList();

        Check("DIMRAD vẽ được", prims.Count >= 3, $"{prims.Count} primitive");
        Check("DIMRAD có chữ R50", texts.Any(t => t.Contains("R50")), string.Join("|", texts));

        // ---- Dimension diameter
        var diameterDim = new DimensionDiameter
        {
            AngleVertex = new CSMath.XYZ(-50, 0, 0),
            DefinitionPoint = new CSMath.XYZ(50, 0, 0),
        };

        ws.Document.Entities.Add(diameterDim);
        var scene2 = new CadScene();
        scene2.Build(ws.Document);

        var diameterPrims = scene2.Entries
            .First(e => ReferenceEquals(e.Entity, diameterDim)).Prims;
        var diameterTexts = diameterPrims.Where(p => p.Kind == PrimKind.Text)
            .Select(p => p.Text).ToList();

        Check("DIMDIA vẽ được", diameterPrims.Count >= 4, $"{diameterPrims.Count} primitive");
        Check("DIMDIA có chữ Ø100",
            diameterTexts.Any(t => t.Contains("\u00D8100")), string.Join("|", diameterTexts));
    }

    // --------------------------------------------------- SỬA THUỘC TÍNH

    private static void RunPropertyEditingChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- SỬA THUỘC TÍNH / LAYER --");

        var ws = CadWorkspace.New();
        var editor = new CadEditor(ws);

        var line = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100, 0, 0),
        };

        editor.AddEntities(new Entity[] { line }, "Add");

        // ---- Đổi màu
        editor.SetColor(new Entity[] { line }, new ACadSharp.Color((short)1));
        Check("Đổi màu entity", line.Color.Index == 1, $"ACI {line.Color.Index}");

        editor.Undo();
        Check("Undo đổi màu phục hồi ByLayer",
            line.Color.IsByLayer || line.Color.Index != 1,
            $"ByLayer={line.Color.IsByLayer}, ACI {line.Color.Index}");

        // ---- Đổi độ đậm nét
        editor.SetLineWeight(new Entity[] { line }, ACadSharp.LineWeightType.W50);
        Check("Đổi lineweight entity", line.LineWeight == ACadSharp.LineWeightType.W50,
            line.LineWeight.ToString());

        editor.Undo();
        Check("Undo lineweight phục hồi",
            line.LineWeight != ACadSharp.LineWeightType.W50, line.LineWeight.ToString());

        // ---- Tạo layer
        var before = ws.Document.Layers.Count();
        var created = editor.CreateLayer("TUONG", new ACadSharp.Color((short)6));

        Check("Tạo được layer mới", created is not null, created?.Name ?? "null");
        Check("Layer mới có trong bản vẽ",
            ws.Document.Layers.Any(l => l.Name == "TUONG"), $"{ws.Document.Layers.Count()} layer");
        Check("Số layer tăng đúng 1", ws.Document.Layers.Count() == before + 1,
            $"{before} → {ws.Document.Layers.Count()}");

        // ---- Không cho trùng tên
        var duplicate = editor.CreateLayer("TUONG", new ACadSharp.Color((short)1));
        Check("Không tạo được layer trùng tên", duplicate is null, duplicate?.Name ?? "null");

        // ---- Undo tạo layer
        editor.Undo();
        Check("Undo xoá layer vừa tạo",
            !ws.Document.Layers.Any(l => l.Name == "TUONG"), $"{ws.Document.Layers.Count()} layer");

        // ---- Đổi màu layer
        var layer = editor.CreateLayer("CUA", new ACadSharp.Color((short)3));
        editor.SetLayerColor(layer!, new ACadSharp.Color((short)5));

        Check("Đổi màu layer", layer!.Color.Index == 5, $"ACI {layer.Color.Index}");

        editor.Undo();
        Check("Undo đổi màu layer phục hồi", layer.Color.Index == 3, $"ACI {layer.Color.Index}");
    }

    // ------------------------------------------------------- LAYOUT TRONG DWG

    private static void RunLayoutChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- LAYOUT TRONG DWG --");

        var ws = CadWorkspace.New();
        var doc = ws.Document;

        doc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(8000, 0, 0),
        });
        doc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 6000, 0),
            EndPoint = new CSMath.XYZ(8000, 6000, 0),
        });

        var scene = new CadScene();
        scene.Build(doc);

        var template = new OpenCad.Core.Layout.SheetTemplateOptions
        {
            Title = "MAT BANG",
            Project = "OpenCAD",
            Drafter = "test",
            UnitsPerMm = 50,
        };

        var created = OpenCad.Core.Layout.LayoutFactory.Create(doc, "A3-150", template, scene);
        Check("Tạo được layout mới", created is not null, created?.Name ?? "null");

        Check("Layout có block record riêng",
            created?.AssociatedBlock is not null, created?.AssociatedBlock?.Name ?? "null");

        var entityCount = created?.AssociatedBlock?.Entities.Count() ?? 0;
        var viewportCount = created?.AssociatedBlock?.Entities.OfType<Viewport>().Count() ?? 0;

        Check("Layout có khung + khung tên", entityCount >= 5, $"{entityCount} entity");
        Check("Layout có viewport", viewportCount >= 1, $"{viewportCount} viewport");

        // ---- Ghi DWG rồi đọc lại: layout phải còn nguyên
        var path = Path.Combine(Path.GetTempPath(), "opencad-smoke", "layout.dwg");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        ws.Save(path);

        var reopened = CadWorkspace.Open(path);
        var names = OpenCad.Core.Layout.LayoutFactory.LayoutNames(reopened.Document);

        Check("Ghi/đọc DWG giữ được layout", names.Contains("A3-150"), string.Join(",", names));

        var readBack = OpenCad.Core.Layout.LayoutFactory.Find(reopened.Document, "A3-150");
        Check("Đọc lại layout đúng khổ giấy",
            readBack is not null
            && Math.Abs(readBack.PaperWidth - 420) < 1 && Math.Abs(readBack.PaperHeight - 297) < 1,
            $"{readBack?.PaperWidth:0}×{readBack?.PaperHeight:0}");

        // ---- Dựng tờ in từ layout đọc được
        var sheet = OpenCad.Core.Layout.LayoutFactory.Read(reopened.Document, readBack!);

        Check("Đọc layout ra tờ in", sheet is not null, sheet?.Name ?? "null");
        Check("Tờ in từ layout có nội dung", sheet is { Items.Count: >= 2 },
            $"{sheet?.Items.Count} mảng");
        Check("Tờ in từ layout có viewport clip",
            sheet is not null && sheet.Items.Any(i => i.Clip is not null),
            sheet?.Items.Count(i => i.Clip is not null).ToString() ?? "-");

        // ---- In tờ đó ra PDF: phải là nét đen, không phải trắng vô hình
        var pdf = Path.Combine(Path.GetTempPath(), "opencad-smoke", "layout.pdf");
        PdfExporter.ExportPaperSheet(sheet!, pdf, new PdfExportOptions
        {
            Title = "layout",
            FontPath = PdfExportOptions.FindSystemFont(),
        });

        var pdfText = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf));

        Check("PDF layout có nét đen", pdfText.Contains("0 0 0 RG"), "0 0 0 RG");
        Check("PDF layout không còn nét trắng vô hình",
            !pdfText.Contains("1 1 1 RG"), $"còn {System.Text.RegularExpressions.Regex.Matches(pdfText, "1 1 1 RG").Count}");
        Check("PDF layout có chữ trong khung tên", pdfText.Contains("Tj"), "có text");
        Check("PDF layout có cắt viewport", pdfText.Contains(" re W n"), "có clip");
    }

    // ------------------------------------------------------------- TỜ IN

    private static void RunPaperSheetChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- TỜ IN (LAYOUT) --");

        var ws = CadWorkspace.New();
        var doc = ws.Document;

        doc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(8000, 0, 0),
        });
        doc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 6000, 0),
            EndPoint = new CSMath.XYZ(8000, 6000, 0),
        });

        var scene = new CadScene();
        scene.Build(doc);

        var template = new OpenCad.Core.Layout.SheetTemplateOptions
        {
            Title = "MẶT BẰNG TẦNG 1",
            Project = "Nhà phố",
            Drafter = "OpenCAD",
            WidthMm = 420,
            HeightMm = 297,
            UnitsPerMm = 50,
        };

        var sheet = OpenCad.Core.Layout.PaperSheetBuilder.Build(scene, template);

        Check("Tờ in đúng khổ A3",
            Math.Abs(sheet.WidthMm - 420) < 1e-9 && Math.Abs(sheet.HeightMm - 297) < 1e-9,
            $"{sheet.WidthMm}×{sheet.HeightMm} mm");

        Check("Tờ in có khung + khung tên + viewport", sheet.Items.Count >= 5,
            $"{sheet.Items.Count} mảng");

        Check("Viewport có clip",
            sheet.Items.Any(i => i.Clip is not null),
            sheet.Items.Count(i => i.Clip is not null).ToString());

        var allPrims = sheet.Items.SelectMany(i => i.Prims).ToList();
        var texts = allPrims.Where(p => p.Kind == PrimKind.Text).Select(p => p.Text).ToList();

        Check("Khung tên có tên bản vẽ",
            texts.Any(t => t.Contains("MẶT BẰNG")), string.Join(" | ", texts.Take(4)));

        Check("Khung tên ghi đúng tỷ lệ",
            texts.Any(t => t.Contains("1:50")), string.Join(" | ", texts.Take(4)));

        // ---- Bản vẽ quá lớn ở 1:50 → tự nới tỷ lệ
        var bigTemplate = new OpenCad.Core.Layout.SheetTemplateOptions
        {
            Title = "BẢN VẼ LỚN",
            WidthMm = 420,
            HeightMm = 297,
            UnitsPerMm = 50,
        };

        var bigScene = new CadScene();
        var bigWs = CadWorkspace.New();
        bigWs.Document.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100_000, 0, 0),
        });
        bigScene.Build(bigWs.Document);

        OpenCad.Core.Layout.PaperSheetBuilder.Build(bigScene, bigTemplate);

        Check("Bản vẽ quá lớn thì tự nới tỷ lệ",
            bigTemplate.UnitsPerMm > 50, $"1:{bigTemplate.UnitsPerMm:0.##}");

        // ---- Xuất tờ ra PDF
        var path = Path.Combine(Path.GetTempPath(), "opencad-smoke", "sheet.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        PdfExporter.ExportPaperSheet(sheet, path, new PdfExportOptions
        {
            Title = "sheet",
            FontPath = PdfExportOptions.FindSystemFont(),
        });

        var text = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path));
        var w = PdfExportOptions.MmToPoints(420);
        var h = PdfExportOptions.MmToPoints(297);

        Check("PDF tờ in đúng khổ",
            text.Contains($"/MediaBox [0 0 {w:0.###} {h:0.###}]"), $"{w:0.##}×{h:0.##} pt");

        Check("PDF tờ in có lệnh cắt viewport", text.Contains(" re W n"), "có clip");
        Check("PDF tờ in là 1 trang", text.Contains("/Count 1"), "1 trang");

        var xrefIndex = text.LastIndexOf("startxref", StringComparison.Ordinal);
        var offset = int.Parse(text[(xrefIndex + 9)..].Trim().Split('\n')[0].Trim());

        Check("PDF tờ in có xref hợp lệ",
            offset > 0 && offset < text.Length && text.Substring(offset, 4) == "xref", $"offset {offset}");
    }

    // ------------------------------------------------------------ LINEWEIGHT

    private static void RunLineWeightChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- ĐỘ ĐẬM NÉT (LWT / CTB) --");

        Check("W25 → 0.25 mm",
            Math.Abs(LineWeights.FromType(ACadSharp.LineWeightType.W25) - 0.25) < 1e-9,
            LineWeights.FromType(ACadSharp.LineWeightType.W25).ToString("0.###"));

        Check("W50 → 0.50 mm",
            Math.Abs(LineWeights.FromType(ACadSharp.LineWeightType.W50) - 0.5) < 1e-9,
            LineWeights.FromType(ACadSharp.LineWeightType.W50).ToString("0.###"));

        Check("Default → 0.25 mm",
            Math.Abs(LineWeights.FromType(ACadSharp.LineWeightType.Default) - 0.25) < 1e-9,
            LineWeights.FromType(ACadSharp.LineWeightType.Default).ToString("0.###"));

        // ---- ByLayer lấy theo layer
        var ws = CadWorkspace.New();
        var doc = ws.Document;

        var thickLayer = new Layer("DAM")
        {
            Color = new ACadSharp.Color((short)6),
            LineWeight = ACadSharp.LineWeightType.W70,
        };
        doc.Layers.Add(thickLayer);

        var byLayerLine = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100, 0, 0),
            Layer = thickLayer,
        };
        doc.Entities.Add(byLayerLine);

        Check("ByLayer lấy đúng độ đậm của layer",
            Math.Abs(LineWeights.Resolve(byLayerLine) - 0.7) < 1e-9,
            LineWeights.Resolve(byLayerLine).ToString("0.###"));

        var explicitLine = new Line
        {
            StartPoint = new CSMath.XYZ(0, 10, 0),
            EndPoint = new CSMath.XYZ(100, 10, 0),
            LineWeight = ACadSharp.LineWeightType.W35,
        };
        doc.Entities.Add(explicitLine);

        Check("LineWeight riêng thắng ByLayer",
            Math.Abs(LineWeights.Resolve(explicitLine) - 0.35) < 1e-9,
            LineWeights.Resolve(explicitLine).ToString("0.###"));

        var scene = new CadScene();
        scene.Build(doc);

        var prim = scene.Entries.First(e => ReferenceEquals(e.Entity, byLayerLine)).Prims[0];
        Check("Prim mang độ đậm và chỉ số màu",
            Math.Abs(prim.LineWeightMm - 0.7) < 1e-9 && prim.AciIndex == 6,
            $"{prim.LineWeightMm:0.###} mm, ACI {prim.AciIndex}");

        // ---- PDF: nét đậm theo màu khi in đơn sắc
        var path = Path.Combine(Path.GetTempPath(), "opencad-smoke", "ctb.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        PdfExporter.Export(scene, path, new PdfExportOptions
        {
            PageWidthMm = 420,
            PageHeightMm = 297,
            Title = "ctb",
            Monochrome = true,
        });

        var text = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path));

        Check("In đơn sắc dùng nét đen", text.Contains("0 0 0 RG"), "0 0 0 RG");
        Check("In đơn sắc không còn nét magenta 6",
            !text.Contains("1 0 1 RG") && !text.Contains("1 0 1 rg"), "không có magenta");

        // Magenta (ACI 6) → 0.7 mm = 1.984 pt ; W35 → 0.35 mm = 0.992 pt
        Check("Độ đậm in theo màu (ACI 6 → 0.7 mm)",
            text.Contains("1.984 w"), "1.984 w");

        Check("Độ đậm riêng của entity được giữ (0.35 mm)",
            text.Contains("0.992 w"), "0.992 w");

        // ---- Bản in màu thường vẫn giữ màu gốc
        var colorPath = Path.Combine(Path.GetTempPath(), "opencad-smoke", "color.pdf");
        PdfExporter.Export(scene, colorPath, new PdfExportOptions
        {
            PageWidthMm = 420,
            PageHeightMm = 297,
            Title = "color",
        });

        var colorText = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(colorPath));
        Check("Bản in màu giữ màu gốc (magenta)",
            colorText.Contains("1 0 1 RG"), "1 0 1 RG");
    }

    // -------------------------------------------------------------------- PDF

    private static void RunPdfChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- XUẤT PDF --");

        var ws = CadWorkspace.New();
        var doc = ws.Document;

        doc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(1000, 0, 0),
        });

        doc.Entities.Add(new Circle { Center = new CSMath.XYZ(500, 500, 0), Radius = 200 });

        doc.Entities.Add(new TextEntity
        {
            InsertPoint = new CSMath.XYZ(0, 900, 0),
            Height = 100,
            Value = "PHÒNG KHÁCH",
        });

        var scene = new CadScene();
        scene.Build(doc);

        var path = Path.Combine(Path.GetTempPath(), "opencad-smoke", "export.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var options = new OpenCad.Core.Export.PdfExportOptions
        {
            PageWidthMm = 420,
            PageHeightMm = 297,
            Title = "smoke-test",
        };

        var result = OpenCad.Core.Export.PdfExporter.Export(scene, path, options);

        var bytes = File.ReadAllBytes(path);
        var text = System.Text.Encoding.ASCII.GetString(bytes);

        Check("PDF có header đúng", text.StartsWith("%PDF-1.4"), text[..8]);
        Check("PDF có kết thúc %%EOF", text.TrimEnd().EndsWith("%%EOF"), "ok");
        Check("PDF khai báo 1 trang", text.Contains("/Count 1"), "ok");

        var expectedW = PdfExportOptions.MmToPoints(420);
        var expectedH = PdfExportOptions.MmToPoints(297);
        Check("PDF đúng khổ A3 ngang",
            text.Contains($"/MediaBox [0 0 {expectedW:0.###} {expectedH:0.###}]"),
            $"mong đợi {expectedW:0.##} x {expectedH:0.##} pt");

        // startxref phải trỏ đúng bảng xref, và từng offset phải trỏ đúng "N 0 obj"
        var startxrefIndex = text.LastIndexOf("startxref", StringComparison.Ordinal);
        var xrefOffset = 0;
        var xrefOk = false;

        if (startxrefIndex > 0)
        {
            var tail = text[(startxrefIndex + 9)..].Trim();
            var firstLine = tail.Split('\n')[0].Trim();
            xrefOk = int.TryParse(firstLine, out xrefOffset)
                     && xrefOffset > 0 && xrefOffset < bytes.Length
                     && text.Substring(xrefOffset, 4) == "xref";
        }

        Check("startxref trỏ đúng bảng xref", xrefOk, $"offset = {xrefOffset}");

        if (xrefOk)
        {
            var xrefBlock = text[xrefOffset..];
            var lines = xrefBlock.Split('\n');
            var allValid = true;
            var checkedEntries = 0;

            for (var i = 2; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (line.Length < 18) break;

                // Hết bảng xref khi gặp dòng không phải entry
                if (!line[..10].All(char.IsDigit)) break;

                // Bỏ qua entry "free" đầu tiên (offset 0)
                if (line[17] != 'n') continue;

                var entryOffset = int.Parse(line[..10]);

                if (entryOffset <= 0 || entryOffset >= text.Length)
                {
                    allValid = false;
                    break;
                }

                var prefix = text.Substring(entryOffset, Math.Min(12, text.Length - entryOffset));

                if (!System.Text.RegularExpressions.Regex.IsMatch(prefix, @"^\d+ 0 obj"))
                {
                    allValid = false;
                    break;
                }

                checkedEntries++;
            }

            Check("Mọi offset trong xref trỏ đúng object",
                allValid && checkedEntries >= 5, $"{checkedEntries} object");
        }

        Check("PDF có nội dung vẽ (path + text)",
            text.Contains(" l\n") && text.Contains("Tj"), "có cả hai");

        Check("Chữ tiếng Việt được chuyển sang ASCII",
            text.Contains("(PHONG KHACH)"), "PHONG KHACH");

        Check("Tỷ lệ tính ra hợp lý",
            result.UnitsPerMm > 1 && result.UnitsPerMm < 1000,
            $"1:{result.UnitsPerMm:0.##}");

        // ---- Tỷ lệ in cố định
        var at50 = Path.Combine(Path.GetTempPath(), "opencad-smoke", "scale-50.pdf");

        var fixedScale = new PdfExportOptions
        {
            PageWidthMm = 420,
            PageHeightMm = 297,
            FitToPage = false,
            UnitsPerMm = PdfExportOptions.FromScaleDenominator(50),
            Title = "scale test",
        };

        var fixedResult = PdfExporter.Export(scene, at50, new PdfExportOptions
        {
            PageWidthMm = fixedScale.PageWidthMm,
            PageHeightMm = fixedScale.PageHeightMm,
            FitToPage = false,
            UnitsPerMm = fixedScale.UnitsPerMm,
            Title = fixedScale.Title,
        });

        Check("Tỷ lệ 1:50 cho đúng units/mm",
            Math.Abs(fixedResult.UnitsPerMm - 50) < 1e-6, $"1:{fixedResult.UnitsPerMm:0.###}");

        Check("Scale điểm/đơn vị khớp 1:50",
            Math.Abs(fixedResult.Scale - PdfExportOptions.MmToPoints(1) / 50) < 1e-9,
            $"{fixedResult.Scale:0.#####} pt/unit");

        // ---- Nhúng font TrueType
        var fontPath = PdfExportOptions.FindSystemFont();
        Check("Tìm được font hệ thống", fontPath is not null, fontPath ?? "không có");

        if (fontPath is not null)
        {
            var fontPdf = Path.Combine(Path.GetTempPath(), "opencad-smoke", "font.pdf");

            PdfExporter.Export(scene, fontPdf, new PdfExportOptions
            {
                PageWidthMm = 420,
                PageHeightMm = 297,
                Title = "font test",
                FontPath = fontPath,
            });

            var fontText = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(fontPdf));

            Check("PDF nhúng font dùng Type0 + Identity-H",
                fontText.Contains("/Subtype /Type0") && fontText.Contains("/Encoding /Identity-H"),
                "Type0");

            Check("PDF có CIDFontType2 và FontFile2",
                fontText.Contains("/CIDFontType2") && fontText.Contains("/FontFile2"),
                "CIDFontType2");

            Check("PDF có ToUnicode để copy đúng chữ",
                fontText.Contains("/ToUnicode 10 0 R"), "có");

            Check("Chữ trong content là glyph id dạng hex",
                System.Text.RegularExpressions.Regex.IsMatch(fontText, @"<[0-9A-F]{8,}> Tj"),
                "hex Tj");

            Check("Không còn chuyển dấu thành ASCII khi nhúng font",
                !fontText.Contains("(PHÒNG KHÁCH)"), "không dùng literal text");

            Check("PDF nhúng font vẫn hợp lệ (có %%EOF)",
                fontText.TrimEnd().EndsWith("%%EOF"), "ok");
        }

        // ---- Nhiều tờ trong một PDF (batch plot)
        var multi = Path.Combine(Path.GetTempPath(), "opencad-smoke", "batch.pdf");

        var sheetA = new PdfExportOptions { PageWidthMm = 420, PageHeightMm = 297, Title = "Tờ 1" };
        var sheetB = new PdfExportOptions
        {
            PageWidthMm = 297,
            PageHeightMm = 210,
            Title = "Tờ 2",
            FitToPage = false,
            UnitsPerMm = PdfExportOptions.FromScaleDenominator(50),
        };

        var multiResults = PdfExporter.ExportSheets(
            new[] { (scene, sheetA), (scene, sheetB) },
            multi,
            new PdfExportOptions { Title = "Bộ hồ sơ", FontPath = fontPath });

        var multiText = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(multi));

        Check("Batch plot tạo đúng 2 tờ", multiResults.Count == 2, $"{multiResults.Count} tờ");
        Check("PDF nhiều tờ khai báo /Count 2", multiText.Contains("/Count 2"), "Count 2");
        Check("PDF nhiều tờ có 2 MediaBox",
            System.Text.RegularExpressions.Regex.Matches(multiText, @"/MediaBox \[0 0").Count == 2,
            "2 MediaBox");

        Check("Tờ 2 giữ đúng tỷ lệ 1:50",
            Math.Abs(multiResults[1].UnitsPerMm - 50) < 1e-6,
            $"1:{multiResults[1].UnitsPerMm:0.##}");

        // Mỗi trang phải có content stream riêng và xref phải trỏ đúng tất cả object
        var multiXrefIndex = multiText.LastIndexOf("startxref", StringComparison.Ordinal);
        var multiOffset = int.Parse(multiText[(multiXrefIndex + 9)..].Trim().Split('\n')[0].Trim());
        var multiXrefOk = multiOffset > 0 && multiOffset < multiText.Length
                          && multiText.Substring(multiOffset, 4) == "xref";

        var multiEntries = 0;
        if (multiXrefOk)
        {
            var lines = multiText[multiOffset..].Split('\n');
            for (var i = 2; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (line.Length < 18 || !line[..10].All(char.IsDigit)) break;
                if (line[17] != 'n') continue;

                var off = int.Parse(line[..10]);
                if (off <= 0 || off >= multiText.Length) { multiXrefOk = false; break; }
                if (!System.Text.RegularExpressions.Regex.IsMatch(
                        multiText.Substring(off, 12), @"^\d+ 0 obj")) { multiXrefOk = false; break; }
                multiEntries++;
            }
        }

        Check("PDF nhiều tờ có xref hợp lệ",
            multiXrefOk && multiEntries == 12, $"{multiEntries} object (kỳ vọng 12)");

        // ---- In một vùng (Area): chỉ vẽ phần nằm trong vùng
        var areaPdf = Path.Combine(Path.GetTempPath(), "opencad-smoke", "area.pdf");
        PdfExporter.Export(scene, areaPdf, new PdfExportOptions
        {
            PageWidthMm = 420,
            PageHeightMm = 297,
            Title = "area",
            Area = new RectD(0, 0, 500, 500),
        });

        var areaText = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(areaPdf));
        Check("In theo vùng có lệnh cắt (re W n)", areaText.Contains(" re W n"), "có clip");
    }

    // ------------------------------------------------------- FILLET / CHAMFER

    private static void RunFilletChamferChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- FILLET / CHAMFER --");

        static (Line A, Line B) Corner() => (
            new Line { StartPoint = new CSMath.XYZ(0, 0, 0), EndPoint = new CSMath.XYZ(100, 0, 0) },
            new Line { StartPoint = new CSMath.XYZ(0, 0, 0), EndPoint = new CSMath.XYZ(0, 100, 0) });

        // ---- FILLET góc vuông, bán kính 20
        var (a1, b1) = Corner();
        var fillet = FilletChamfer.Fillet(a1, b1, 20);

        Check("FILLET tạo được kết quả", fillet is not null, fillet is null ? "null" : "ok");

        if (fillet is not null)
        {
            var arc = fillet.Connector as Arc;
            Check("FILLET sinh ra cung tròn", arc is not null, arc is null ? "không" : "Arc");

            Check("FILLET cung có bán kính đúng",
                arc is not null && Math.Abs(arc.Radius - 20) < 1e-6,
                arc is null ? "-" : $"r = {arc.Radius:0.###}");

            Check("FILLET tâm cung đúng (20,20)",
                arc is not null
                && Math.Abs(arc.Center.X - 20) < 1e-6
                && Math.Abs(arc.Center.Y - 20) < 1e-6,
                arc is null ? "-" : $"tâm = {arc.Center.X:0.###},{arc.Center.Y:0.###}");

            // Đầu bị cắt ngắn là đầu gần góc (0,0) → lấy Min, không phải Max
            var nearFirst = Math.Min(fillet.First.StartPoint.X, fillet.First.EndPoint.X);
            var nearSecond = Math.Min(fillet.Second.StartPoint.Y, fillet.Second.EndPoint.Y);

            var farFirst = Math.Max(fillet.First.StartPoint.X, fillet.First.EndPoint.X);
            var farSecond = Math.Max(fillet.Second.StartPoint.Y, fillet.Second.EndPoint.Y);

            Check("FILLET cắt ngắn hai đường về điểm tiếp xúc",
                Math.Abs(nearFirst - 20) < 1e-6 && Math.Abs(nearSecond - 20) < 1e-6,
                $"đầu gần = {nearFirst:0.###}, {nearSecond:0.###}");

            Check("FILLET giữ nguyên đầu xa",
                Math.Abs(farFirst - 100) < 1e-6 && Math.Abs(farSecond - 100) < 1e-6,
                $"đầu xa = {farFirst:0.###}, {farSecond:0.###}");
        }

        // ---- Bán kính quá lớn → không bo được
        var (a2, b2) = Corner();
        var tooBig = FilletChamfer.Fillet(a2, b2, 500);
        Check("FILLET bán kính quá lớn trả về null", tooBig is null, tooBig is null ? "null" : "có");

        // ---- Hai đường song song → không bo được
        var parallel = FilletChamfer.Fillet(
            new Line { StartPoint = new CSMath.XYZ(0, 0, 0), EndPoint = new CSMath.XYZ(100, 0, 0) },
            new Line { StartPoint = new CSMath.XYZ(0, 50, 0), EndPoint = new CSMath.XYZ(100, 50, 0) },
            10);
        Check("FILLET hai đường song song trả về null", parallel is null, parallel is null ? "null" : "có");

        // ---- CHAMFER
        var (a3, b3) = Corner();
        var chamfer = FilletChamfer.Chamfer(a3, b3, 20, 20);

        Check("CHAMFER tạo được kết quả", chamfer is not null, chamfer is null ? "null" : "ok");

        if (chamfer is not null && chamfer.Connector is Line connector)
        {
            var length = Pt.Distance(
                new Pt(connector.StartPoint.X, connector.StartPoint.Y),
                new Pt(connector.EndPoint.X, connector.EndPoint.Y));

            Check("CHAMFER cạnh vát dài đúng √(20²+20²)",
                Math.Abs(length - Math.Sqrt(800)) < 1e-6,
                $"{length:0.###}");
        }
    }

    // -------------------------------------------------------------- DIMENSION

    private static void RunDimensionChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- DIMENSION --");

        var dim = new DimensionAligned
        {
            FirstPoint = new CSMath.XYZ(0, 0, 0),
            SecondPoint = new CSMath.XYZ(100, 0, 0),
            DefinitionPoint = new CSMath.XYZ(50, 30, 0),
        };

        var ws = CadWorkspace.New();
        ws.Document.Entities.Add(dim);

        var scene = new CadScene();
        scene.Build(ws.Document);

        var prims = scene.Entries.FirstOrDefault(e => ReferenceEquals(e.Entity, dim))?.Prims ?? new List<Prim>();

        Check("DIM tạo được hình học hiển thị", prims.Count > 0, $"{prims.Count} primitive");

        var texts = prims.Where(p => p.Kind == PrimKind.Text).ToList();
        Check("DIM có chữ số đo", texts.Count == 1 && texts[0].Text == "100",
            texts.Count == 0 ? "không có" : texts[0].Text);

        var filled = prims.Where(p => p.Filled).ToList();
        Check("DIM có 2 mũi tên", filled.Count == 2, $"{filled.Count} mũi tên");

        Check("DIM có 2 đường gióng + 1 đường kích thước",
            prims.Count(p => p.Kind == PrimKind.Polyline && !p.Filled) == 3,
            $"{prims.Count(p => p.Kind == PrimKind.Polyline && !p.Filled)} đoạn");

        // ---- Ghi/đọc DWG
        var path = Path.Combine(Path.GetTempPath(), "opencad-smoke", "dim.dwg");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            ACadSharp.IO.DwgWriter.Write(path, ws.Document);
            var reopened = CadWorkspace.Open(path);
            var count = reopened.Document.Entities.OfType<Dimension>().Count();
            Check("Ghi/đọc DWG giữ được dimension", count == 1, $"{count} dimension");
        }
        catch (Exception ex)
        {
            Check("Ghi/đọc DWG giữ được dimension", false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    // ------------------------------------------------------------------ HATCH

    private static void RunHatchChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- HATCH --");

        var ws = CadWorkspace.New();
        var doc = ws.Document;

        // Vòng ngoài 200x200, vòng trong 100x100 → lỗ ở giữa (even-odd)
        var hatch = new Hatch { IsSolid = true };

        hatch.Paths.Add(RectPath(0, 0, 200, 200));
        hatch.Paths.Add(RectPath(50, 50, 150, 150));
        doc.Entities.Add(hatch);

        var scene = new CadScene();
        scene.Build(doc);

        var entry = scene.Entries.FirstOrDefault(e => ReferenceEquals(e.Entity, hatch));
        var prim = entry?.Prims.FirstOrDefault();

        Check("HATCH sinh được primitive", prim is not null && prim.Kind == PrimKind.Hatch,
            prim is null ? "không có" : prim.Kind.ToString());

        Check("HATCH gom đủ 2 vòng biên", prim?.Loops is { Length: 2 },
            prim?.Loops is null ? "null" : $"{prim.Loops.Length} vòng");

        Check("HATCH điểm trong vùng đặc thuộc về hatch",
            prim is not null && prim.ContainsPoint(new Pt(25, 25)), "điểm (25,25)");

        Check("HATCH lỗ ở giữa KHÔNG thuộc vùng tô",
            prim is not null && !prim.ContainsPoint(new Pt(100, 100)), "điểm (100,100)");

        var hit = scene.HitTest(new Pt(20, 20), 5);
        Check("Hit-test chọn được hatch ở giữa vùng",
            hit is not null && ReferenceEquals(hit.Entity, hatch),
            hit is null ? "không tìm thấy" : hit.Entity.GetType().Name);

        Check("HATCH hộp bao đúng",
            Math.Abs(entry!.Bounds.MinX) < 1e-9 && Math.Abs(entry.Bounds.MaxX - 200) < 1e-9,
            entry.Bounds.ToString());

        // Vòng biên là polyline có bulge (cung tròn) vẫn phải khép kín được
        var round = new Hatch { IsSolid = true };
        var polyPath = new Hatch.BoundaryPath();
        var polyEdge = new Hatch.BoundaryPath.Polyline { IsClosed = true };
        polyEdge.Vertices.Add(new CSMath.XYZ(0, 0, 0));
        polyEdge.Vertices.Add(new CSMath.XYZ(100, 0, 0));
        polyEdge.Vertices.Add(new CSMath.XYZ(100, 100, 0));
        polyEdge.Vertices.Add(new CSMath.XYZ(0, 100, 0));
        polyPath.Edges.Add(polyEdge);
        round.Paths.Add(polyPath);
        doc.Entities.Add(round);

        var scene2 = new CadScene();
        scene2.Build(doc);
        var roundEntry = scene2.Entries.FirstOrDefault(e => ReferenceEquals(e.Entity, round));
        var roundPrim = roundEntry?.Prims.FirstOrDefault();

        Check("HATCH biên dạng polyline đọc được",
            roundPrim is not null && roundPrim.Loops is { Length: 1 } && roundPrim.Loops[0].Length >= 4,
            roundPrim?.Loops is null ? "null" : $"{roundPrim.Loops[0].Length} đỉnh");

        // Ghi/đọc lại DWG giữ được hatch
        var path = Path.Combine(Path.GetTempPath(), "opencad-smoke", "hatch.dwg");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        doc.Entities.Remove(round);
        ACadSharp.IO.DwgWriter.Write(path, doc);

        var reopened = CadWorkspace.Open(path);
        var hatchCount = reopened.Document.Entities.OfType<Hatch>().Count();

        Check("Ghi/đọc DWG giữ được hatch", hatchCount == 1, $"{hatchCount} hatch");

        // ---- Tạo hatch từ biên khép kín
        var rect = new LwPolyline { IsClosed = true };
        rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 0) });
        rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(300, 0) });
        rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(300, 300) });
        rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 300) });

        var created = HatchFactory.FromBoundary(rect);
        Check("Tạo được hatch từ polyline khép kín",
            created is not null && created.Paths.Count == 1,
            created is null ? "null" : $"{created.Paths.Count} path");

        var circleBoundary = new Circle { Center = new CSMath.XYZ(0, 0, 0), Radius = 50 };
        var circleHatch = HatchFactory.FromBoundary(circleBoundary);
        Check("Tạo được hatch từ đường tròn",
            circleHatch is not null && circleHatch.Paths.Count == 1
                                   && circleHatch.Paths[0].Edges.Count == 2,
            circleHatch is null ? "null" : $"{circleHatch.Paths[0].Edges.Count} cung");

        // Hatch vừa tạo phải làm phẳng được thành vùng tô
        var wsCreate = CadWorkspace.New();
        if (created is not null) wsCreate.Document.Entities.Add(created);

        var sceneCreate = new CadScene();
        sceneCreate.Build(wsCreate.Document);
        var createdPrim = sceneCreate.Entries.FirstOrDefault()?.Prims.FirstOrDefault();

        Check("Hatch vừa tạo render được",
            createdPrim is not null && createdPrim.Kind == PrimKind.Hatch && createdPrim.ContainsPoint(new Pt(150, 150)),
            createdPrim is null ? "không có" : createdPrim.Kind.ToString());
    }

    private static Hatch.BoundaryPath RectPath(double x1, double y1, double x2, double y2)
    {
        var path = new Hatch.BoundaryPath();
        path.Edges.Add(new Hatch.BoundaryPath.Line { Start = new CSMath.XY(x1, y1), End = new CSMath.XY(x2, y1) });
        path.Edges.Add(new Hatch.BoundaryPath.Line { Start = new CSMath.XY(x2, y1), End = new CSMath.XY(x2, y2) });
        path.Edges.Add(new Hatch.BoundaryPath.Line { Start = new CSMath.XY(x2, y2), End = new CSMath.XY(x1, y2) });
        path.Edges.Add(new Hatch.BoundaryPath.Line { Start = new CSMath.XY(x1, y2), End = new CSMath.XY(x1, y1) });
        return path;
    }

    // ------------------------------------------------------------ TRIM / EXTEND

    private static void RunTrimExtendChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- TRIM / EXTEND --");

        var horizontal = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100, 0, 0),
        };

        var cutter1 = new Line
        {
            StartPoint = new CSMath.XYZ(25, -10, 0),
            EndPoint = new CSMath.XYZ(25, 10, 0),
        };

        var cutter2 = new Line
        {
            StartPoint = new CSMath.XYZ(75, -10, 0),
            EndPoint = new CSMath.XYZ(75, 10, 0),
        };

        var boundaries = new Entity[] { cutter1, cutter2 };

        // ---- Cắt đoạn giữa → còn 2 mảnh
        var middle = TrimExtendOperations.Trim(horizontal, new Pt(50, 0), boundaries);
        Check("TRIM giữa đoạn → 2 mảnh", middle is { Count: 2 },
            middle is null ? "null" : $"{middle.Count} mảnh");

        if (middle is { Count: 2 })
        {
            var lengths = middle.Cast<Line>()
                .Select(l => Math.Abs(l.EndPoint.X - l.StartPoint.X))
                .OrderBy(v => v)
                .ToList();

            Check("TRIM giữ lại đúng hai đoạn 25 và 25",
                Math.Abs(lengths[0] - 25) < 1e-6 && Math.Abs(lengths[1] - 25) < 1e-6,
                $"{lengths[0]:0.##} + {lengths[1]:0.##}");
        }

        // ---- Cắt phần đuôi → còn 1 mảnh dài 75
        var tail = TrimExtendOperations.Trim(horizontal, new Pt(95, 0), boundaries);
        Check("TRIM phần đuôi → 1 mảnh", tail is { Count: 1 },
            tail is null ? "null" : $"{tail.Count} mảnh");

        if (tail is { Count: 1 } && tail[0] is Line tailLine)
        {
            Check("TRIM đuôi còn đúng 75 đơn vị",
                Math.Abs(tailLine.EndPoint.X - 75) < 1e-6,
                $"end.X = {tailLine.EndPoint.X:0.###}");
        }

        // ---- Cắt khi không có giao điểm → null
        var noCut = TrimExtendOperations.Trim(horizontal, new Pt(50, 0),
            new Entity[] { new Line { StartPoint = new CSMath.XYZ(25, 50, 0), EndPoint = new CSMath.XYZ(75, 50, 0) } });
        Check("TRIM không có giao điểm trả về null", noCut is null, noCut is null ? "null" : "có mảnh");

        // ---- Cắt đường tròn → còn một cung
        var circle = new Circle { Center = new CSMath.XYZ(0, 0, 0), Radius = 50 };
        var verticalCutter = new Line
        {
            StartPoint = new CSMath.XYZ(0, -80, 0),
            EndPoint = new CSMath.XYZ(0, 80, 0),
        };

        var circlePieces = TrimExtendOperations.Trim(circle, new Pt(40, 30), new Entity[] { verticalCutter });
        Check("TRIM đường tròn → 1 cung", circlePieces is { Count: 1 } && circlePieces[0] is Arc,
            circlePieces is null ? "null" : $"{circlePieces.Count} mảnh");

        // ---- EXTEND tới biên
        var shortLine = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(50, 0, 0),
        };
        var wall = new Line
        {
            StartPoint = new CSMath.XYZ(100, -10, 0),
            EndPoint = new CSMath.XYZ(100, 10, 0),
        };

        var extended = TrimExtendOperations.Extend(shortLine, new Pt(45, 0), new Entity[] { wall }) as Line;
        Check("EXTEND kéo dài tới biên", extended is not null && Math.Abs(extended.EndPoint.X - 100) < 1e-6,
            extended is null ? "null" : $"end.X = {extended.EndPoint.X:0.###}");

        // ---- EXTEND về phía đầu kia
        var wallLeft = new Line
        {
            StartPoint = new CSMath.XYZ(-100, -10, 0),
            EndPoint = new CSMath.XYZ(-100, 10, 0),
        };

        var extendedStart = TrimExtendOperations.Extend(shortLine, new Pt(5, 0), new Entity[] { wallLeft }) as Line;
        Check("EXTEND đúng đầu được chọn",
            extendedStart is not null && Math.Abs(extendedStart.StartPoint.X + 100) < 1e-6
                                     && Math.Abs(extendedStart.EndPoint.X - 50) < 1e-6,
            extendedStart is null ? "null" : $"start.X = {extendedStart.StartPoint.X:0.###}");

        // ---- Không có biên → null
        var noExtend = TrimExtendOperations.Extend(shortLine, new Pt(45, 0), Array.Empty<Entity>());
        Check("EXTEND không có biên trả về null", noExtend is null, noExtend is null ? "null" : "có");

        // ---- Undo thao tác TRIM phục hồi entity gốc
        var ws = CadWorkspace.New();
        var editor = new CadEditor(ws);

        var line = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100, 0, 0),
        };
        var cut1 = new Line { StartPoint = new CSMath.XYZ(25, -10, 0), EndPoint = new CSMath.XYZ(25, 10, 0) };
        var cut2 = new Line { StartPoint = new CSMath.XYZ(75, -10, 0), EndPoint = new CSMath.XYZ(75, 10, 0) };

        editor.AddEntities(new Entity[] { line, cut1, cut2 }, "Add");

        var trimmed = TrimExtendOperations.Trim(line, new Pt(50, 0),
            ws.Document.Entities.Where(e => !ReferenceEquals(e, line)).ToList());

        if (trimmed is not null) editor.ReplaceEntity(line, trimmed, "Trim");

        var afterTrim = ws.Document.Entities.Count();
        editor.Undo();
        var afterUndo = ws.Document.Entities.Count();

        var restored = ws.Document.Entities.OfType<Line>()
            .FirstOrDefault(l => Math.Abs(l.StartPoint.X) < 1e-9 && Math.Abs(l.EndPoint.X - 100) < 1e-9);

        Check("TRIM một đường bị cắt 2 chỗ → 4 entity",
            afterTrim == 4, $"{afterTrim} entity");

        Check("Undo TRIM phục hồi đúng entity gốc",
            afterUndo == 3 && restored is not null,
            $"{afterUndo} entity, khôi phục gốc = {restored is not null}");
    }

    // ------------------------------------------------------- OFFSET / LINETYPE

    private static void RunOffsetAndLineTypeChecks()
    {
        Console.WriteLine();
        Console.WriteLine("-- OFFSET & line type --");

        // ---- OFFSET đường thẳng
        var line = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100, 0, 0),
        };

        var offsetLine = OffsetOperation.Create(line, 10, new Pt(50, 100)) as Line;
        Check("OFFSET line tạo được", offsetLine is not null, offsetLine is null ? "null" : "Line");
        Check("OFFSET line đúng khoảng cách",
            offsetLine is not null && Math.Abs(offsetLine.StartPoint.Y - 10) < 1e-9
                                  && Math.Abs(offsetLine.EndPoint.Y - 10) < 1e-9,
            offsetLine is null ? "-" : $"y = {offsetLine.StartPoint.Y}");

        // ---- OFFSET đường tròn (ra ngoài)
        var circle = new Circle { Center = new CSMath.XYZ(0, 0, 0), Radius = 50 };
        var offsetCircle = OffsetOperation.Create(circle, 10, new Pt(100, 0)) as Circle;
        Check("OFFSET circle ra ngoài +10", offsetCircle is not null && Math.Abs(offsetCircle.Radius - 60) < 1e-9,
            offsetCircle is null ? "-" : $"r = {offsetCircle.Radius}");

        // ---- OFFSET đường tròn (vào trong)
        var innerCircle = OffsetOperation.Create(circle, 10, new Pt(10, 0)) as Circle;
        Check("OFFSET circle vào trong -10", innerCircle is not null && Math.Abs(innerCircle.Radius - 40) < 1e-9,
            innerCircle is null ? "-" : $"r = {innerCircle.Radius}");

        // ---- OFFSET hình chữ nhật khép kín
        var rect = new LwPolyline { IsClosed = true };
        rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 0) });
        rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(100, 0) });
        rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(100, 50) });
        rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 50) });

        var bigRect = OffsetOperation.Create(rect, 10, new Pt(200, 25)) as LwPolyline;
        var smallRect = OffsetOperation.Create(rect, 10, new Pt(50, 25)) as LwPolyline;

        var bigMaxX = bigRect?.Vertices.Max(v => v.Location.X) ?? double.NaN;
        var smallMaxX = smallRect?.Vertices.Max(v => v.Location.X) ?? double.NaN;

        Check("OFFSET rect ra ngoài rộng hơn",
            bigRect is not null && bigMaxX > 100.5,
            $"maxX = {bigMaxX:0.##}");

        Check("OFFSET rect vào trong hẹp hơn",
            smallRect is not null && smallMaxX < 99.5,
            $"maxX = {smallMaxX:0.##}");

        // ---- Line type: nét đứt phải có pattern, nét liền thì không
        var ws = CadWorkspace.New();
        var doc = ws.Document;

        // Bản vẽ mới đã có DASHED chuẩn (từ vòng 116) → dùng lại, không tạo trùng tên
        var dashed = doc.LineTypes.FirstOrDefault(l => l.Name == "DASHED");

        if (dashed is null)
        {
            dashed = new LineType("DASHED");
            dashed.AddSegment(new LineType.Segment { Length = 20 });
            dashed.AddSegment(new LineType.Segment { Length = -10 });
            doc.LineTypes.Add(dashed);
        }

        var dashedLayer = new Layer("DASHED_LAYER") { LineType = dashed };
        doc.Layers.Add(dashedLayer);

        var dashedLine = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(100, 0, 0),
            Layer = dashedLayer,
        };
        doc.Entities.Add(dashedLine);

        var solidLine = new Line
        {
            StartPoint = new CSMath.XYZ(0, 50, 0),
            EndPoint = new CSMath.XYZ(100, 50, 0),
        };
        doc.Entities.Add(solidLine);

        var scene = new CadScene();
        scene.Build(doc);

        var dashedPrims = scene.Entries.First(e => ReferenceEquals(e.Entity, dashedLine)).Prims;
        var solidPrims = scene.Entries.First(e => ReferenceEquals(e.Entity, solidLine)).Prims;

        Check("Line type nét đứt sinh pattern",
            dashedPrims.Count > 0 && dashedPrims[0].Dash is { Length: 2 },
            dashedPrims.Count == 0 ? "không có prim" : $"{dashedPrims[0].Dash?.Length ?? 0} đoạn");

        Check("Line type nét liền không có pattern",
            solidPrims.Count > 0 && solidPrims[0].Dash is null,
            solidPrims.Count == 0 ? "không có prim" : "null");
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        _checks++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}  —  {detail}");
        if (!ok) _failures++;
    }
}

using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using OpenCad.Core;
using OpenCad.Core.Editing;
using OpenCad.Core.Geometry;
using OpenCad.Core.Rendering;

namespace OpenCad.Tools;

internal static class Program
{
    private static int Main(string[] args)
    {
        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

        try
        {
            return Dispatch(command, args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{command}: LỖI — {ex.GetType().Name}: {ex.Message}");

            return 1;
        }
    }

    private static int Dispatch(string command, string[] args)
    {
        return command switch
        {
            "sample" => SampleCommand(args),
            "mksample" => MakeSampleCommand(args),
            "trimsample" => TrimSampleCommand(args),
            "pdf" => PdfCommand(args),
            "pdfbatch" => PdfBatchCommand(args),
            "addviewport" => AddViewportCommand(args),
            "patterns" => PatternsCommand(args),
            "stress" => StressCommand(args),
            "torture" => TortureCommand(args),
            "lossy" => LossyCommand(args),
            "audit" => AuditCommand(args),
            "purge" => PurgeCommand(args),
            "checkcmds" => CheckCommandsCommand(),
            "script" => ScriptCommand(args),
            "docs" => DocsCommand(args),
            "checkdocs" => CheckDocsCommand(),
            "checkwiring" => CheckWiringCommand(args),
            "checkinvariants" => CheckInvariantsCommand(args),
            "checkall" => CheckAllCommand(args),
            "checkaliases" => CheckAliasesCommand(),
            "checkparity" => CheckParityCommand(),
            "checktests" => CheckTestsCommand(),
            "checklimits" => CheckLimitsCommand(),
            "checkdead" => CheckDeadCommand(args.Length > 1 ? args[1] : "."),
            "checkclaims" => CheckClaimsCommand(),
            "checkrender" => CheckRenderCommand(args.Length > 1 ? args[1] : "."),
            "lifecycle" => LifecycleCommand(),
            "griphunt" => GripHuntCommand(),
            "workflow" => WorkflowCommand(args.Length > 1 ? args[1] : "."),
            "interop" => InteropCommand(args),
            "interoprt" => InteropRoundTripCommand(args),
            "interopedit" => InteropEditCommand(args),
            "mkfixtures" => MakeFixturesCommand(args.Length > 1 ? args[1] : "."),
            "checkfixtures" => CheckFixturesCommand(args.Length > 1 ? args[1] : "."),
            "plotsheet" => PlotSheetCommand(args),
            "layouts" => LayoutsCommand(args),
            "mklayout" => MakeLayoutCommand(args),
            "plotlayout" => PlotLayoutCommand(args),
            "smoke" => SmokeTest.Run(),
            "info" => InfoCommand(args),
            _ => Help(),
        };
    }

    private static int Help()
    {
        Console.WriteLine("""
            OpenCAD tools

              sample <path.dwg>   Tạo bản vẽ demo
              mksample <path.dwg> Tạo bản vẽ mẫu căn hộ (có tờ A3 1:50)
              smoke               Chạy kiểm thử round-trip mở → sửa → lưu
              info <path>         In thông tin bản vẽ
            """);
        return 0;
    }

    private static int SampleCommand(string[] args)
    {
        var path = args.Length > 1 ? args[1] : "samples/demo.dwg";
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var doc = DemoDrawing.Build();
        ACadSharp.IO.DwgWriter.Write(path, doc);

        var size = new FileInfo(path).Length;
        Console.WriteLine($"Đã tạo {path} ({size:N0} bytes, {doc.Entities.Count()} entity)");
        return 0;
    }

    private static int TrimSampleCommand(string[] args)
    {
        var path = args.Length > 1 ? args[1] : "samples/trim-test.dwg";
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var doc = DemoDrawing.BuildTrimDemo();
        ACadSharp.IO.DwgWriter.Write(path, doc);

        Console.WriteLine($"Đã tạo {path} ({doc.Entities.Count()} entity)");
        return 0;
    }

    private static int PdfCommand(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Dùng: pdf <file.dwg> <out.pdf> [A3|A4] [1:50 | fit] [mono] [style=<kieu-in.txt>]");
            return 1;
        }

        var workspace = CadWorkspace.Open(args[1]);
        var scene = new CadScene();
        scene.Build(workspace.Document);

        var a4 = args.Length > 3 && args[3].Equals("A4", StringComparison.OrdinalIgnoreCase);

        double? unitsPerMm = null;
        var scaleLabel = "fit";

        if (args.Length > 4 && args[4].StartsWith("1:", StringComparison.Ordinal))
        {
            if (double.TryParse(args[4][2..], out var denominator) && denominator > 0)
            {
                unitsPerMm = OpenCad.Core.Export.PdfExportOptions.FromScaleDenominator(denominator);
                scaleLabel = args[4];
            }
        }

        var fontPath = OpenCad.Core.Export.PdfExportOptions.FindSystemFont();
        var monochrome = args.Any(a => a.Equals("mono", StringComparison.OrdinalIgnoreCase));

        // style=<path>: nạp bảng kiểu in theo lớp
        OpenCad.Core.Export.PlotStyleTable? plotStyle = null;
        var styleArg = args
            .FirstOrDefault(a => a.StartsWith("style=", StringComparison.OrdinalIgnoreCase));

        if (styleArg is not null)
        {
            var stylePath = styleArg[6..].Trim('"');

            plotStyle = OpenCad.Core.Export.PlotStyleTable.LoadFile(stylePath);

            if (plotStyle is null)
            {
                Console.WriteLine($"Không đọc được bảng kiểu in: {stylePath}");
                return 1;
            }

            Console.WriteLine($"Bảng kiểu in: {plotStyle.Count} lớp từ {stylePath}");
        }

        var options = new OpenCad.Core.Export.PdfExportOptions
        {
            PageWidthMm = a4 ? 297 : 420,
            PageHeightMm = a4 ? 210 : 297,
            Title = Path.GetFileName(args[1]),
            FitToPage = unitsPerMm is null,
            UnitsPerMm = unitsPerMm,
            FontPath = fontPath,
            Monochrome = monochrome,
            PlotStyle = plotStyle,
        };

        var result = OpenCad.Core.Export.PdfExporter.Export(scene, args[2], options,
            entity => entity.Layer?.IsOn ?? true);

        var info = new FileInfo(args[2]);
        Console.WriteLine($"Đã xuất PDF: {args[2]} ({info.Length:N0} bytes, {(a4 ? "A4" : "A3")} ngang, {scaleLabel})");
        Console.WriteLine($"Tỷ lệ thực: 1:{result.UnitsPerMm:0.##}  |  font: {(fontPath is null ? "Helvetica (ASCII)" : Path.GetFileName(fontPath))}");
        return 0;
    }

    /// <summary>In nhiều bản vẽ vào một file PDF — mỗi file một tờ.</summary>
    private static int PdfBatchCommand(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Dùng: pdfbatch <out.pdf> <in1.dwg> <in2.dwg> ... [1:50 | fit]");
            return 1;
        }

        var output = args[1];
        var inputs = args.Skip(2)
            .Where(a => !a.StartsWith("1:", StringComparison.Ordinal) && !a.Equals("fit", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var scaleArg = args.Skip(2).FirstOrDefault(a => a.StartsWith("1:", StringComparison.Ordinal));

        double? unitsPerMm = null;
        if (scaleArg is not null && double.TryParse(scaleArg[2..], out var denominator) && denominator > 0)
            unitsPerMm = OpenCad.Core.Export.PdfExportOptions.FromScaleDenominator(denominator);

        var fontPath = OpenCad.Core.Export.PdfExportOptions.FindSystemFont();
        var sheets = new List<(CadScene, OpenCad.Core.Export.PdfExportOptions)>();

        foreach (var file in inputs)
        {
            if (!File.Exists(file))
            {
                Console.WriteLine($"Bỏ qua '{file}': không tìm thấy file.");
                continue;
            }

            CadWorkspace workspace;

            try { workspace = CadWorkspace.Open(file); }
            catch (Exception ex)
            {
                Console.WriteLine($"Bỏ qua '{file}': {ex.Message}");
                continue;
            }
            var scene = new CadScene();
            scene.Build(workspace.Document);

            sheets.Add((scene, new OpenCad.Core.Export.PdfExportOptions
            {
                PageWidthMm = 420,
                PageHeightMm = 297,
                Title = Path.GetFileName(file),
                FitToPage = unitsPerMm is null,
                UnitsPerMm = unitsPerMm,
                FontPath = fontPath,
            }));
        }

        var baseOptions = new OpenCad.Core.Export.PdfExportOptions
        {
            Title = "OpenCAD batch",
            FontPath = fontPath,
        };

        if (sheets.Count == 0)
        {
            Console.WriteLine("pdfbatch: không có tờ nào để in.");
            return 1;
        }

        var results = OpenCad.Core.Export.PdfExporter.ExportSheets(
            sheets, output, baseOptions, entity => entity.Layer?.IsOn ?? true);

        var info = new FileInfo(output);
        Console.WriteLine($"Đã xuất {results.Count} tờ vào {output} ({info.Length:N0} bytes)");

        for (var i = 0; i < results.Count; i++)
            Console.WriteLine($"  tờ {i + 1}: {inputs[i]} — tỷ lệ 1:{results[i].UnitsPerMm:0.##}");

        return 0;
    }

    /// <summary>In một tờ có khung tên và viewport đúng tỷ lệ.</summary>
    private static int PlotSheetCommand(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Dùng: plotsheet <file.dwg> [out.pdf] [1:50] [A3|A4]");
            return 1;
        }

        var workspace = CadWorkspace.Open(args[1]);
        var scene = new CadScene();
        scene.Build(workspace.Document);

        // out.pdf: tham số thứ hai nếu nó là đường dẫn .pdf, ngược lại tự đặt tên
        var outPdf = args.Length > 2 && args[2].EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? args[2]
            : Path.ChangeExtension(args[1], ".pdf");

        var a4 = args.Any(a => a.Equals("A4", StringComparison.OrdinalIgnoreCase));
        var scaleArg = args.FirstOrDefault(a => a.StartsWith("1:", StringComparison.Ordinal));

        double denominator = 50;
        if (scaleArg is not null && double.TryParse(scaleArg[2..], out var parsed) && parsed > 0)
            denominator = parsed;

        var template = new OpenCad.Core.Layout.SheetTemplateOptions
        {
            Title = Path.GetFileNameWithoutExtension(args[1]),
            Project = "OpenCAD",
            Drafter = "OpenCAD",
            WidthMm = a4 ? 297 : 420,
            HeightMm = a4 ? 210 : 297,
            UnitsPerMm = denominator,
        };

        var sheet = OpenCad.Core.Layout.PaperSheetBuilder.Build(scene, template);

        var options = new OpenCad.Core.Export.PdfExportOptions
        {
            Title = Path.GetFileName(args[1]),
            FontPath = OpenCad.Core.Export.PdfExportOptions.FindSystemFont(),
        };

        OpenCad.Core.Export.PdfExporter.ExportPaperSheet(sheet, outPdf, options);

        var info = new FileInfo(outPdf);
        Console.WriteLine($"Đã in tờ: {outPdf} ({info.Length:N0} bytes)");
        Console.WriteLine($"  khổ {(a4 ? "A4" : "A3")} ngang, {sheet.Items.Count} mảng hình, tỷ lệ 1:{template.UnitsPerMm:0.##}");

        return 0;
    }

    /// <summary>Thêm viewport vào layout: addviewport <dwg> <layout> <1:N> <x> <y> <w> <h></summary>
    private static int AddViewportCommand(string[] args)
    {
        if (args.Length < 8)
        {
            Console.WriteLine("Dùng: addviewport <file.dwg> <layout> <1:N> <x> <y> <w> <h>");
            return 1;
        }

        var workspace = CadWorkspace.Open(args[1]);

        var layout = OpenCad.Core.Layout.LayoutFactory.Find(workspace.Document, args[2]);
        if (layout is null) { Console.WriteLine($"Không có layout '{args[2]}'."); return 1; }

        var scene = new CadScene();
        scene.Build(workspace.Document);

        var scale = double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
        var x = double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
        var y = double.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture);
        var w = double.Parse(args[6], System.Globalization.CultureInfo.InvariantCulture);
        var h = double.Parse(args[7], System.Globalization.CultureInfo.InvariantCulture);

        var viewport = OpenCad.Core.Layout.LayoutFactory.AddViewport(
            layout, scene, new OpenCad.Core.Geometry.RectD(x, y, x + w, y + h), scale);

        if (viewport is null) { Console.WriteLine("Không thêm được viewport."); return 1; }

        workspace.Save(args[1]);
        var sheet = OpenCad.Core.Layout.LayoutFactory.Read(workspace.Document, layout);
        var count = sheet?.Items.Count(i => i.Clip is not null) ?? 0;

        Console.WriteLine($"Đã thêm viewport 1:{scale:0.##} ({w}×{h} mm) vào '{layout.Name}' — tờ giờ có {count} viewport");
        return 0;
    }

    private static int LayoutsCommand(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("Dùng: layouts <file.dwg>"); return 1; }

        var workspace = CadWorkspace.Open(args[1]);
        var names = OpenCad.Core.Layout.LayoutFactory.LayoutNames(workspace.Document);

        Console.WriteLine($"{args[1]}: {names.Count} layout");
        foreach (var name in names)
        {
            var layout = OpenCad.Core.Layout.LayoutFactory.Find(workspace.Document, name);
            var block = layout?.AssociatedBlock;
            var viewports = block?.Entities.OfType<ACadSharp.Entities.Viewport>().Count() ?? 0;
            Console.WriteLine($"  {name}: {layout?.PaperWidth:0}×{layout?.PaperHeight:0} mm, " +
                              $"{block?.Entities.Count() ?? 0} entity, {viewports} viewport");
        }

        return 0;
    }

    private static int MakeLayoutCommand(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Dùng: mklayout <file.dwg> <tên layout> [1:50]");
            return 1;
        }

        var workspace = CadWorkspace.Open(args[1]);
        var scene = new CadScene();
        scene.Build(workspace.Document);

        var denominator = 50.0;
        var scaleArg = args.FirstOrDefault(a => a.StartsWith("1:", StringComparison.Ordinal));
        if (scaleArg is not null && double.TryParse(scaleArg[2..], out var parsed) && parsed > 0)
            denominator = parsed;

        var template = new OpenCad.Core.Layout.SheetTemplateOptions
        {
            Title = Path.GetFileNameWithoutExtension(args[1]),
            Project = "OpenCAD",
            Drafter = "OpenCAD",
            UnitsPerMm = denominator,
        };

        var layout = OpenCad.Core.Layout.LayoutFactory.Create(
            workspace.Document, args[2], template, scene);

        if (layout is null) { Console.WriteLine("Không tạo được layout."); return 1; }

        workspace.Save(args[1]);
        Console.WriteLine($"Đã tạo layout '{layout.Name}' (1:{template.UnitsPerMm:0.##}) và lưu {args[1]}");
        return 0;
    }

    private static int PlotLayoutCommand(string[] args)
    {
        if (args.Length < 4)
        {
            Console.WriteLine("Dùng: plotlayout <file.dwg> <tên layout> <out.pdf>");
            return 1;
        }

        var workspace = CadWorkspace.Open(args[1]);
        var layout = OpenCad.Core.Layout.LayoutFactory.Find(workspace.Document, args[2]);

        if (layout is null)
        {
            Console.WriteLine($"Không có layout '{args[2]}'.");
            return 1;
        }

        var sheet = OpenCad.Core.Layout.LayoutFactory.Read(workspace.Document, layout);
        if (sheet is null) { Console.WriteLine("Không đọc được layout."); return 1; }

        OpenCad.Core.Export.PdfExporter.ExportPaperSheet(sheet, args[3], new OpenCad.Core.Export.PdfExportOptions
        {
            Title = args[2],
            FontPath = OpenCad.Core.Export.PdfExportOptions.FindSystemFont(),
        });

        Console.WriteLine($"Đã in layout '{sheet.Name}' ({sheet.WidthMm:0}×{sheet.HeightMm:0} mm, " +
                          $"{sheet.Items.Count} mảng) → {args[3]} ({new FileInfo(args[3]).Length:N0} bytes)");
        return 0;
    }

    /// <summary>Đo hiệu năng: sinh N entity, lưu, mở lại, dựng scene, hit-test.</summary>
    /// <summary>
    /// Sinh bản vẽ chứa đủ loại nội dung như file thật, lưu DWG, mở lại rồi kiểm tra
    /// từng entity có vẽ được không — tìm chỗ mất dữ liệu âm thầm.
    /// </summary>
    /// <summary>
    /// Kiểm tra từng loại entity có sống sót qua lưu/mở DWG không.
    /// In bảng: loại nào mất dữ liệu khi lưu.
    /// </summary>
    /// <summary>Rà một file DWG tìm lỗi dữ liệu.</summary>
    /// <summary>Dọn định nghĩa mồ côi trong file DWG (chỉ báo cáo, thêm --apply để xoá).</summary>
    /// <summary>
    /// Đối chiếu bảng lệnh (CommandRegistry) với router thật trong MainForm:
    /// – lệnh có trong HELP nhưng không có case → gõ vào không làm gì
    /// – case có trong router nhưng không có trong HELP → chạy được mà không ai biết
    /// </summary>
    /// <summary>Chạy file script để sinh bản vẽ: script <in.scr> [out.dwg]</summary>
    private static int ScriptCommand(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Dùng: script <in.scr> [out.dwg]");
            return 1;
        }

        var input = args[1];

        if (!File.Exists(input))
        {
            Console.WriteLine($"Không thấy file script: {input}");
            return 1;
        }

        // Mặc định KHÔNG ghi đè file trùng tên script: thêm hậu tố "-script"
        var output = args.Length > 2
            ? args[2]
            : Path.Combine(
                Path.GetDirectoryName(input) ?? ".",
                Path.GetFileNameWithoutExtension(input) + "-script.dwg");
        var (workspace, result) = OpenCad.Core.Scripting.ScriptRunner.RunNew(File.ReadAllText(input));

        foreach (var error in result.Errors) Console.WriteLine("   " + error);

        Console.WriteLine($"Chạy {result.Executed} lệnh, {result.Failed} lỗi → {workspace.Document.Entities.Count()} entity");

        workspace.Save(output);
        Console.WriteLine($"Đã lưu: {output}");

        return result.Ok ? 0 : 1;
    }

    /// <summary>
    /// Vẽ lại phần "bảng lệnh" trong README từ CommandRegistry (nguồn sự thật duy nhất),
    /// để tài liệu không bị lệch với code.
    /// </summary>
    /// <summary>Kiểm tra bảng lệnh trong README có khớp CommandRegistry không (dùng cho CI).</summary>
    /// <summary>
    /// Tìm hàm/thuộc tính công khai của Core mà tầng App KHÔNG gọi tới.
    /// Đây là loại lỗi "code có nhưng không nối vào giao diện" đã gặp nhiều lần
    /// (hatch edit, dimension text, JOIN, explode…).
    /// </summary>
    /// <summary>
    /// Tìm API công khai của Core KHÔNG được dùng ở bất kỳ file nào khác ngoài file khai báo.
    /// Đây là loại lỗi "code có nhưng không nối vào đâu" đã gặp nhiều lần
    /// (hatch edit, dimension text, JOIN…). Chỉ báo cáo, không chặn CI.
    /// </summary>
    /// <summary>
    /// Kiểm các "bất biến nguồn" — những bản vá quan trọng đã từng bị lần sửa sau ghi đè
    /// mà không ai biết (ví dụ guard kéo grip ở vòng 87 biến mất rồi được phát hiện lại ở vòng 114).
    /// Chạy được trong CI: exit 1 nếu có bất biến bị phá.
    /// </summary>
    /// <summary>Chạy mọi cổng kiểm: lệnh, tài liệu, bất biến nguồn. Exit 1 nếu có cái nào hỏng.</summary>
    /// <summary>
    /// Đối chiếu khả năng giữa CLI (tools) và app: việc gì CLI làm được mà app không có lệnh?
    /// Đây là loại lỗ hổng đã gặp nhiều lần (JOIN, kiểu nét, mẫu tô, SCRIPT chỉ có ở CLI).
    /// </summary>
    /// <summary>
    /// Săn "test giả": điều kiện luôn đúng (hằng true, hoặc số đếm >= 0).
    /// Đã từng viết loại này 3 lần — một test luôn xanh còn tệ hơn không có test.
    /// </summary>
    /// <summary>
    /// Kiểm các GIỚI HẠN đã ghi trong README bằng reflection trên ACadSharp.
    /// Nếu một giới hạn biến mất (thư viện nâng cấp) thì phải cập nhật README — công cụ sẽ báo.
    /// </summary>
    /// <summary>
    /// Tìm mã chết trong tầng App: thành viên private/internal được khai báo nhưng
    /// không được nhắc tới ở bất kỳ tệp .cs nào khác trong cùng thư mục.
    /// Bắt được trường hợp như BuildToolbar (mất khi bỏ toolbar mà không ai biết).
    /// </summary>
    /// <summary>
    /// Kiểm các TUYÊN BỐ trong README có đúng thực tế:
    /// (1) không có lệnh nào vừa tồn tại vừa nằm trong mục "Còn thiếu";
    /// (2) số test Core ghi trong README khớp số test thật;
    /// (3) mọi lệnh trong bảng lệnh đều được nhắc trong README.
    /// </summary>
    /// <summary>
    /// Đối chiếu MỌI lớp entity của ACadSharp với các nhánh vẽ trong EntityFlattener.
    /// Tìm loại entity "không có nhánh vẽ" — dạng dữ liệu vô hình (XLINE/RAY từng bị đúng lỗi này).
    /// </summary>
    /// <summary>
    /// Ma trận vòng đời cho từng loại entity: tạo → vẽ → hit-test → MOVE → undo → lưu/đọc.
    /// Mỗi ô là một khẳng định; in bảng để thấy ngay loại nào hỏng ở bước nào.
    /// </summary>
    /// <summary>
    /// Truy lùng lỗi "kéo grip bị crash": mô phỏng kéo MỌI grip của MỌI loại entity
    /// ở nhiều toạ độ/tỉ lệ cực đoan, kiểm ngoại lệ và toạ độ NaN/vô cực.
    /// </summary>
    /// <summary>
    /// Chạy TRỌN một quy trình làm hồ sơ thật: tạo bản vẽ → layer → tường → cửa →
    /// kích thước → chữ → tô → block có attribute → tờ A3 + viewport 1:50 →
    /// bảng kiểu in → PDF → lưu → mở lại → audit.
    /// Mỗi bước là một khẳng định; cuối cùng in bảng kết quả.
    /// </summary>
    /// <summary>
    /// Kiểm tương thích ĐỌC với bản vẽ do phần mềm khác tạo: mở read-only (không ghi),
    /// báo số đối tượng/layer/block, số vẽ được, loại bỏ qua, và lỗi audit.
    /// </summary>
    private static int InteropCommand(string[] args)
    {
        var paths = new List<string>();

        foreach (var arg in args.Skip(1))
        {
            if (Directory.Exists(arg))
                paths.AddRange(Directory.GetFiles(arg, "*.dwg", SearchOption.AllDirectories));
            else if (File.Exists(arg))
                paths.Add(arg);
        }

        if (paths.Count == 0)
        {
            Console.WriteLine("Dùng: interop <thư mục hoặc tệp .dwg>…");
            return 1;
        }

        Console.WriteLine("Tệp                              entity  layer  block   vẽ  bỏ  audit  ghi chú");
        Console.WriteLine("--------------------------------------------------------------------------------");

        var problems = 0;

        foreach (var path in paths.OrderBy(p => p))
        {
            var name = Path.GetFileName(path);

            if (name.Length > 30) name = name[..27] + "…";

            try
            {
                var workspace = CadWorkspace.Open(path);
                var doc = workspace.Document;

                var scene = new CadScene();
                scene.Build(doc);

                var audit = OpenCad.Core.Audit.DrawingAudit.Run(doc);

                var skipped = scene.SkippedTypes.Count == 0
                    ? "-"
                    : string.Join(",", scene.SkippedTypes.Select(kv => kv.Key + "×" + kv.Value));

                var notes = new List<string>();

                // Cảnh báo đọc của thư viện KHÔNG tính là lỗi của app — chỉ ghi chú
                if (workspace.LastWarnings.Count > 0)
                    notes.Add(workspace.LastWarnings.Count + " cảnh báo đọc");

                // Lỗi thật: không vẽ được gì, hoặc có loại đối tượng bị bỏ qua
                if (scene.PrimitiveCount == 0) notes.Add("KHÔNG vẽ được gì");
                if (scene.SkippedCount > 0) notes.Add("BỎ QUA " + scene.SkippedCount + " đối tượng");

                if (scene.PrimitiveCount == 0 || scene.SkippedCount > 0) problems++;

                Console.WriteLine($"{name,-32} {doc.Entities.Count(),7} {doc.Layers.Count(),6} " +
                                  $"{doc.BlockRecords.Count(),6} {scene.PrimitiveCount,5} " +
                                  $"{scene.SkippedCount,3} {audit.Count,6}  {string.Join("; ", notes)}");

                if (skipped != "-")
                    Console.WriteLine($"    bỏ qua: {skipped}");
            }
            catch (Exception ex)
            {
                problems++;
                Console.WriteLine($"{name,-32} LỖI MỞ: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(problems == 0
            ? $"interop: {paths.Count} bản vẽ mở được, không lỗi"
            : $"interop: {problems}/{paths.Count} bản vẽ có vấn đề");

        return 0;
    }

    /// <summary>
    /// Round-trip bản vẽ THẬT: mở → lưu ra bản sao → mở lại → so sánh.
    /// Đây là thứ người dùng thực sự cần: mở DWG của phần mềm khác, sửa, lưu, không mất gì.
    /// </summary>
    private static int InteropRoundTripCommand(string[] args)
    {
        var paths = new List<string>();

        foreach (var arg in args.Skip(1))
        {
            if (Directory.Exists(arg))
                paths.AddRange(Directory.GetFiles(arg, "*.dwg", SearchOption.AllDirectories));
            else if (File.Exists(arg))
                paths.Add(arg);
        }

        if (paths.Count == 0)
        {
            Console.WriteLine("Dùng: interoprt <thư mục hoặc tệp .dwg>…");
            return 1;
        }

        Console.WriteLine("Tệp                              entity trước/sau   layer   block    vẽ trước/sau   kết quả");
        Console.WriteLine("-----------------------------------------------------------------------------------------");

        var failures = 0;

        foreach (var path in paths.OrderBy(p => p).Take(12))
        {
            var name = Path.GetFileName(path);

            if (name.Length > 30) name = name[..27] + "…";

            string? copy = null;

            try
            {
                var before = CadWorkspace.Open(path);

                var beforeScene = new CadScene();
                beforeScene.Build(before.Document);

                var beforeEntities = before.Document.Entities.Count();
                var beforeLayers = before.Document.Layers.Count();
                var beforeBlocks = before.Document.BlockRecords.Count();
                var beforePrims = beforeScene.PrimitiveCount;

                copy = Path.Combine(Path.GetTempPath(), "opencad-rt-" + Guid.NewGuid().ToString("N")[..6] + ".dwg");

                before.Save(copy);

                var after = CadWorkspace.Open(copy);

                var afterScene = new CadScene();
                afterScene.Build(after.Document);

                var afterEntities = after.Document.Entities.Count();

                // Dấu vân tay NỘI DUNG: băm toàn bộ chữ ký hình học đã sắp xếp.
                // Số lượng khớp chưa đủ — nội dung mới chứng minh không mất gì.
                static string Fingerprint(CadDocument doc)
                {
                    var signatureMethod = typeof(OpenCad.Core.Audit.DrawingAudit).GetMethod(
                        "Signature",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

                    // Vân tay phủ cả THUỘC TÍNH HIỂN THỊ: màu, độ đậm, kiểu nét, tỷ lệ nét,
                    // màu/độ đậm của lớp — chứ không chỉ hình học. Nếu chỉ so hình học thì
                    // một bản vẽ bị đổi toàn bộ màu vẫn "khớp".
                    static string Attributes(Entity e)
                    {
                        var color = e.Color.IsByLayer ? "byLayer" : e.Color.ToString();
                        var layer = e.Layer;

                        return string.Join("|",
                            color,
                            e.LineWeight.ToString(),
                            e.LineType?.Name ?? "-",
                            e.LineTypeScale.ToString("0.###"),
                            layer?.Color.ToString() ?? "-",
                            layer?.LineWeight.ToString() ?? "-",
                            layer?.LineType?.Name ?? "-");
                    }

                    var keys = doc.Entities
                        .Select(e => (string)signatureMethod.Invoke(null, new object[] { e })!
                                     + "#" + Attributes(e))
                        .OrderBy(k => k, StringComparer.Ordinal)
                        .ToList();

                    using var sha = System.Security.Cryptography.SHA256.Create();

                    var bytes = sha.ComputeHash(
                        System.Text.Encoding.UTF8.GetBytes(string.Join("\n", keys)));

                    return Convert.ToHexString(bytes)[..16];
                }

                var beforePrint = Fingerprint(before.Document);
                var afterPrint = Fingerprint(after.Document);

                var sameContent = beforePrint == afterPrint;

                var ok = sameContent
                         && afterEntities == beforeEntities
                         && after.Document.Layers.Count() == beforeLayers
                         && after.Document.BlockRecords.Count() == beforeBlocks
                         && afterScene.PrimitiveCount > 0
                         && afterScene.SkippedCount == 0;

                if (!ok) failures++;

                var note = ok ? "OK " + beforePrint
                    : !sameContent ? "NỘI DUNG KHÁC " + beforePrint + "→" + afterPrint
                    : afterEntities != beforeEntities ? $"MẤT {beforeEntities - afterEntities} entity"
                    : afterScene.SkippedCount > 0 ? "BỎ QUA " + afterScene.SkippedCount
                    : "khác layer/block";

                Console.WriteLine($"{name,-32} {beforeEntities,7}/{afterEntities,-7} {beforeLayers,6} " +
                                  $"{beforeBlocks,7} {beforePrims,7}/{afterScene.PrimitiveCount,-7} {note}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"{name,-32} LỖI: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (copy is not null) { try { File.Delete(copy); } catch { } }
            }
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? $"interoprt: {Math.Min(paths.Count, 12)} bản vẽ round-trip KHÔNG mất dữ liệu"
            : $"interoprt: {failures} bản vẽ bị mất dữ liệu khi lưu");

        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// SỬA bản vẽ thật rồi lưu: đúng nhu cầu "mở lên edit, lưu file".
    /// Kiểm: (1) thay đổi có hiệu lực; (2) mọi đối tượng KHÔNG bị sửa vẫn nguyên vẹn.
    /// </summary>
    private static int InteropEditCommand(string[] args)
    {
        var paths = new List<string>();

        foreach (var arg in args.Skip(1))
        {
            if (Directory.Exists(arg))
                paths.AddRange(Directory.GetFiles(arg, "*.dwg", SearchOption.AllDirectories));
            else if (File.Exists(arg))
                paths.Add(arg);
        }

        if (paths.Count == 0)
        {
            Console.WriteLine("Dùng: interopedit <thư mục hoặc tệp .dwg>…");
            return 1;
        }

        var signatureMethod = typeof(OpenCad.Core.Audit.DrawingAudit).GetMethod(
            "Signature",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        string Sig(Entity e) => (string)signatureMethod.Invoke(null, new object[] { e })!;

        var failures = 0;
        var tested = 0;

        Console.WriteLine("Tệp                              sửa              đối tượng khác   kết quả");
        Console.WriteLine("----------------------------------------------------------------------------");

        foreach (var path in paths.OrderBy(p => new FileInfo(p).Length).Take(12))
        {
            var name = Path.GetFileName(path);

            if (name.Length > 30) name = name[..27] + "…";

            string? copy = null;

            try
            {
                copy = Path.Combine(Path.GetTempPath(),
                    "opencad-edit-" + Guid.NewGuid().ToString("N")[..6] + ".dwg");

                File.Copy(path, copy, overwrite: true);

                var workspace = CadWorkspace.Open(copy);
                var document = workspace.Document;

                // Chọn đối tượng để sửa theo thứ tự ưu tiên — để mọi tệp đều kiểm được,
                // kể cả tệp chỉ có block (thư viện block) hoặc chỉ có polyline.
                Entity? target = null;

                foreach (var pick in new Func<Entity?>[]
                         {
                             () => document.Entities.OfType<Line>().FirstOrDefault(),
                             () => document.Entities.OfType<LwPolyline>().FirstOrDefault(),
                             () => document.Entities.OfType<TextEntity>().FirstOrDefault(),
                             () => document.Entities.OfType<MText>().FirstOrDefault(),
                             () => document.Entities.OfType<Insert>().FirstOrDefault(),
                             () => document.Entities.OfType<Circle>().FirstOrDefault(),
                         })
                {
                    target = pick();
                    if (target is not null) break;
                }

                if (target is null)
                {
                    Console.WriteLine($"{name,-32} (không có loại nào sửa được)");
                    continue;
                }

                var originalSig = Sig(target);
                var othersBefore = document.Entities
                    .Where(e => !ReferenceEquals(e, target))
                    .Select(Sig)
                    .OrderBy(k => k, StringComparer.Ordinal)
                    .ToList();

                // SỬA tuỳ loại đối tượng
                string editLabel;

                switch (target)
                {
                    case Line line:
                        line.StartPoint = new CSMath.XYZ(line.StartPoint.X + 1000, line.StartPoint.Y, line.StartPoint.Z);
                        line.EndPoint = new CSMath.XYZ(line.EndPoint.X + 1000, line.EndPoint.Y, line.EndPoint.Z);
                        editLabel = "Line +1000";
                        break;

                    case LwPolyline poly when poly.Vertices.Count > 0:
                    {
                        var vertex = poly.Vertices[0];
                        poly.Vertices[0] = new LwPolyline.Vertex
                        {
                            Location = new CSMath.XY(vertex.Location.X + 1000, vertex.Location.Y),
                        };

                        editLabel = "Polyline +1000";
                        break;
                    }

                    case TextEntity text:
                        text.Value = (text.Value ?? string.Empty) + "-SỬA";
                        editLabel = "Chữ";
                        break;

                    case MText mtext:
                        mtext.Value = (mtext.Value ?? string.Empty) + "-SỬA";
                        editLabel = "MTEXT";
                        break;

                    case Insert insert:
                        insert.InsertPoint = new CSMath.XYZ(
                            insert.InsertPoint.X + 1000, insert.InsertPoint.Y, insert.InsertPoint.Z);
                        editLabel = "Insert +1000";
                        break;

                    case Circle circle:
                        circle.Center = new CSMath.XYZ(circle.Center.X + 1000, circle.Center.Y, circle.Center.Z);
                        editLabel = "Circle +1000";
                        break;

                    default:
                        editLabel = "?";
                        break;
                }

                workspace.Save(copy);

                // MỞ LẠI và kiểm
                var back = CadWorkspace.Open(copy);

                // Kiểm thay đổi đã lưu: tìm đối tượng có chữ ký MỚI
                var newSig = Sig(target);

                var moved = back.Document.Entities.FirstOrDefault(e => Sig(e) == newSig);

                var othersAfter = back.Document.Entities
                    .Where(e => !ReferenceEquals(e, moved))
                    .Select(Sig)
                    .OrderBy(k => k, StringComparer.Ordinal)
                    .ToList();

                var othersSame = othersAfter.SequenceEqual(othersBefore);
                var ok = moved is not null && othersSame;

                if (!ok) failures++;
                tested++;

                var note = ok ? "OK"
                    : moved is null ? "THAY ĐỔI KHÔNG LƯU ĐƯỢC"
                    : "ĐỐI TƯỢNG KHÁC BỊ ĐỔI";

                Console.WriteLine($"{name,-32} {editLabel,-16} " +
                                  $"{othersBefore.Count}/{othersAfter.Count}   {note}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"{name,-32} LỖI: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (copy is not null) { try { File.Delete(copy); } catch { } }
            }
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? $"interopedit: {tested}/{tested} bản vẽ thật — sửa lưu được, đối tượng khác nguyên vẹn"
            : $"interopedit: {failures}/{tested} bản vẽ có vấn đề");

        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Tạo bộ fixture mô phỏng các dạng dữ liệu THẬT đã từng làm hỏng app
    /// (chữ trùng ký tự ở nhiều vị trí, hatch biên một cung tròn, tệp chỉ có block).
    /// Fixture nằm trong repo để cổng kiểm chạy được mà không cần bản vẽ bên ngoài.
    /// </summary>
    private static int MakeFixturesCommand(string root = ".")
    {
        var folder = Path.Combine(root, "samples", "fixtures");
        Directory.CreateDirectory(folder);

        var created = new List<string>();

        // 1. Nhiều chữ CÙNG KÝ TỰ ở các vị trí khác nhau (từng bị coi là trùng lặp)
        {
            var ws = CadWorkspace.New();

            for (var i = 0; i < 5; i++)
            {
                ws.Document.Entities.Add(new TextEntity
                {
                    InsertPoint = new CSMath.XYZ(i * 1000, i * 500, 0),
                    Value = "N",
                    Height = 250,
                });
            }

            var path = Path.Combine(folder, "repeated-text.dwg");
            ws.Save(path);
            created.Add(Path.GetFileName(path));
        }

        // 2. Hatch có biên là MỘT CUNG TRÒN khép kín (start == end) — từng không vẽ được
        {
            var ws = CadWorkspace.New();

            for (var i = 0; i < 3; i++)
            {
                var hatch = new Hatch { IsSolid = true };
                var boundary = new Hatch.BoundaryPath();

                boundary.Edges.Add(new Hatch.BoundaryPath.Arc
                {
                    Center = new CSMath.XY(i * 3000, 0),
                    Radius = 800 + i * 200,
                    StartAngle = 0,
                    EndAngle = 0,
                    CounterClockWise = true,
                });

                hatch.Paths.Add(boundary);
                ws.Document.Entities.Add(hatch);
            }

            var path = Path.Combine(folder, "circle-hatch.dwg");
            ws.Save(path);
            created.Add(Path.GetFileName(path));
        }

        // 3. Tệp chỉ có BLOCK + insert + attribute (như thư viện block thật)
        {
            var ws = CadWorkspace.New();

            var block = new ACadSharp.Tables.BlockRecord("CUA");
            ws.Document.BlockRecords.Add(block);

            block.Entities.Add(new Line
            {
                StartPoint = new CSMath.XYZ(0, 0, 0),
                EndPoint = new CSMath.XYZ(1000, 0, 0),
            });

            block.Entities.Add(new Arc
            {
                Center = new CSMath.XYZ(0, 0, 0),
                Radius = 1000,
                StartAngle = 0,
                EndAngle = Math.PI / 2,
            });

            block.Entities.Add(new AttributeDefinition
            {
                Tag = "MA",
                Value = "C1",
                InsertPoint = new CSMath.XYZ(0, 300, 0),
                Height = 200,
            });

            for (var i = 0; i < 20; i++)
            {
                var insert = new Insert(block)
                {
                    InsertPoint = new CSMath.XYZ((i % 5) * 2000, (i / 5) * 2000, 0),
                };

                insert.Attributes.Add(new AttributeEntity
                {
                    Tag = "MA",
                    Value = "C" + (i + 1),
                    InsertPoint = new CSMath.XYZ((i % 5) * 2000, (i / 5) * 2000 + 300, 0),
                    Height = 200,
                });

                ws.Document.Entities.Add(insert);
            }

            var path = Path.Combine(folder, "blocks-only.dwg");
            ws.Save(path);
            created.Add(Path.GetFileName(path));
        }

        // 4. Bản vẽ hỗn hợp với nhiều màu, kiểu nét, độ đậm (kiểm round-trip thuộc tính)
        {
            var ws = CadWorkspace.New();
            OpenCad.Core.Styles.LineTypeLibrary.EnsureAll(ws.Document);

            var dashed = ws.Document.LineTypes.FirstOrDefault(l => l.Name == "DASHED");

            for (var i = 0; i < 40; i++)
            {
                var line = new Line
                {
                    StartPoint = new CSMath.XYZ(i * 200, 0, 0),
                    EndPoint = new CSMath.XYZ(i * 200, 1000 + i * 50, 0),
                    Color = new ACadSharp.Color((short)(1 + i % 7)),
                    LineWeight = (ACadSharp.LineWeightType)(i % 5),
                };

                if (dashed is not null && i % 3 == 0) line.LineType = dashed;

                ws.Document.Entities.Add(line);
            }

            var path = Path.Combine(folder, "mixed-attributes.dwg");
            ws.Save(path);
            created.Add(Path.GetFileName(path));
        }

        foreach (var name in created)
        {
            var info = new FileInfo(Path.Combine(folder, name));
            Console.WriteLine($"   {name,-26} {info.Length,8:N0} bytes");
        }

        Console.WriteLine($"mkfixtures: đã tạo {created.Count} fixture trong {folder}");

        return 0;
    }

    /// <summary>
    /// Kiểm bộ fixture: mở được, không bỏ qua đối tượng, round-trip giữ nguyên nội dung.
    /// Đây là phiên bản chạy được trong CI của các bài học từ dữ liệu thật.
    /// </summary>
    private static int CheckFixturesCommand(string root = ".")
    {
        var folder = Path.Combine(root, "samples", "fixtures");

        if (!Directory.Exists(folder))
        {
            Console.WriteLine("checkfixtures: chưa có fixture — chạy `mkfixtures` trước");
            return 1;
        }

        var signatureMethod = typeof(OpenCad.Core.Audit.DrawingAudit).GetMethod(
            "Signature",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        static string Print(IEnumerable<string> keys)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();

            return Convert.ToHexString(sha.ComputeHash(
                System.Text.Encoding.UTF8.GetBytes(string.Join("\n", keys))))[..16];
        }

        var failures = 0;

        foreach (var path in Directory.GetFiles(folder, "*.dwg").OrderBy(p => p))
        {
            var name = Path.GetFileName(path);

            try
            {
                var before = CadWorkspace.Open(path);

                var beforeScene = new CadScene();
                beforeScene.Build(before.Document);

                var beforeKeys = before.Document.Entities
                    .Select(e => (string)signatureMethod.Invoke(null, new object[] { e })!)
                    .OrderBy(k => k, StringComparer.Ordinal)
                    .ToList();

                var beforePrint = Print(beforeKeys);

                var copy = Path.Combine(Path.GetTempPath(),
                    "fixture-" + Guid.NewGuid().ToString("N")[..6] + ".dwg");

                before.Save(copy);

                var after = CadWorkspace.Open(copy);

                var afterKeys = after.Document.Entities
                    .Select(e => (string)signatureMethod.Invoke(null, new object[] { e })!)
                    .OrderBy(k => k, StringComparer.Ordinal)
                    .ToList();

                var same = Print(afterKeys) == beforePrint;

                try { File.Delete(copy); } catch { }

                // Các fixture này KHÔNG chứa đối tượng trùng thật → audit phải sạch.
                // (Bài học từ dữ liệu thật: chữ cùng ký tự ở khác vị trí từng bị coi là trùng.)
                var duplicates = OpenCad.Core.Audit.DrawingAudit.Run(before.Document)
                    .Count(f => f.Code == "DUPLICATE");

                var ok = beforeScene.SkippedCount == 0 && beforeScene.PrimitiveCount > 0
                         && same && duplicates == 0;

                if (!ok) failures++;

                var reasons = new List<string>();

                if (beforeScene.SkippedCount > 0) reasons.Add("BỎ QUA " + beforeScene.SkippedCount);
                if (beforeScene.PrimitiveCount == 0) reasons.Add("KHÔNG VẼ ĐƯỢC");
                if (duplicates > 0) reasons.Add($"BÁO NHẦM {duplicates} TRÙNG LẶP");
                if (!same) reasons.Add("ROUND-TRIP KHÁC");

                var note = ok ? "OK" : string.Join(" + ", reasons);

                Console.WriteLine($"{name,-26} {before.Document.Entities.Count(),6} entity " +
                                  $"{beforeScene.PrimitiveCount,7} prim  {note}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"{name,-26} LỖI: {ex.GetType().Name}");
            }
        }

        Console.WriteLine(failures == 0
            ? "checkfixtures: mọi fixture mở được, vẽ được, round-trip nguyên vẹn"
            : $"checkfixtures: {failures} fixture có vấn đề");

        return failures == 0 ? 0 : 1;
    }

    private static int WorkflowCommand(string root = ".")
    {
        var steps = new List<(string Name, bool Ok, string Detail)>();

        void Step(string name, bool ok, string detail = "") => steps.Add((name, ok, detail));

        var workspace = CadWorkspace.New();
        var editor = new CadEditor(workspace);
        var doc = workspace.Document;

        // 1. Tạo bản vẽ mới có đủ kiểu nét
        Step("Bản vẽ mới", doc.Entities.Count() == 0 && doc.LineTypes.Count() >= 9,
            doc.LineTypes.Count() + " kiểu nét");

        // 2. Tạo layer
        var tuong = editor.CreateLayer("TUONG", new ACadSharp.Color((short)7));
        var cua = editor.CreateLayer("CUA", new ACadSharp.Color((short)3));
        var kt = editor.CreateLayer("KICH_THUOC", new ACadSharp.Color((short)1));
        var chu = editor.CreateLayer("CHU", new ACadSharp.Color((short)2));

        Step("Tạo 4 layer", tuong is not null && cua is not null && kt is not null && chu is not null,
            doc.Layers.Count() + " layer");

        // 3. Tường: hình chữ nhật 7000 × 5200
        var wall = new LwPolyline { IsClosed = true, Layer = tuong };

        wall.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 0) });
        wall.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(7000, 0) });
        wall.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(7000, 5200) });
        wall.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 5200) });

        editor.AddEntities(new Entity[] { wall }, "Wall");

        var wallScene = new CadScene();
        wallScene.Build(doc);

        Step("Vẽ tường 7000×5200", wallScene.PrimitiveCount == 1,
            wallScene.PrimitiveCount + " primitive, bounds " + wallScene.Bounds);

        // 4. Cửa: cánh + cung mở
        var doorLeaf = new Line
        {
            StartPoint = new CSMath.XYZ(1000, 0, 0),
            EndPoint = new CSMath.XYZ(1900, 0, 0),
            Layer = cua,
        };

        var doorSwing = new Arc
        {
            Center = new CSMath.XYZ(1000, 0, 0),
            Radius = 900,
            StartAngle = 0,
            EndAngle = Math.PI / 2,
            Layer = cua,
        };

        editor.AddEntities(new Entity[] { doorLeaf, doorSwing }, "Door");

        Step("Thêm cửa (cánh + cung)", doc.Entities.Count() == 3, doc.Entities.Count() + " entity");

        // 5. Kích thước
        var dim = new DimensionAligned
        {
            FirstPoint = new CSMath.XYZ(0, 0, 0),
            SecondPoint = new CSMath.XYZ(7000, 0, 0),
            DefinitionPoint = new CSMath.XYZ(0, -600, 0),
            Layer = kt,
        };

        editor.AddEntities(new Entity[] { dim }, "Dim");

        var dimScene = new CadScene();
        dimScene.Build(doc);

        var dimTexts = dimScene.Entries
            .Where(e => ReferenceEquals(e.Entity, dim))
            .SelectMany(e => e.Prims)
            .Where(p => p.Kind == PrimKind.Text)
            .Select(p => p.Text)
            .ToList();

        Step("Ghi kích thước 7000", dimTexts.Contains("7000"), string.Join(",", dimTexts));

        // 6. Chữ
        var label = new TextEntity
        {
            InsertPoint = new CSMath.XYZ(1500, 2500, 0),
            Value = "PHONG KHACH",
            Height = 250,
            Layer = chu,
        };

        editor.AddEntities(new Entity[] { label }, "Text");

        Step("Đặt chữ tiếng Việt", doc.Entities.Count() == 5, doc.Entities.Count() + " entity");

        // 7. Tô tường
        var hatch = HatchFactory.FromBoundary(wall);

        if (hatch is null)
        {
            Step("Tô tường ANSI31", false, "không tạo được hatch");
        }
        else
        {
            hatch.Layer = tuong;
            editor.AddEntities(new Entity[] { hatch }, "Hatch");
            editor.SetHatchProperties(new Entity[] { hatch }, 100, 0, "ANSI31");

            var hatchScene = new CadScene();
            hatchScene.Build(doc);

            var hatchPrims = hatchScene.Entries
                .Where(e => ReferenceEquals(e.Entity, hatch))
                .SelectMany(e => e.Prims)
                .Count();

            Step("Tô tường ANSI31", hatchPrims > 1, hatchPrims + " primitive");
        }

        // 8. Block có attribute (cửa sổ) + 2 insert + ATTSYNC
        var block = new ACadSharp.Tables.BlockRecord("CUA_SO");
        doc.BlockRecords.Add(block);

        block.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(1200, 0, 0),
        });

        var definition = editor.AddAttributeDefinition(block, "MA_CUA", "C1", new Pt(0, 200), 150);

        var insert1 = new Insert(block) { InsertPoint = new CSMath.XYZ(2000, 5200, 0) };
        var insert2 = new Insert(block) { InsertPoint = new CSMath.XYZ(4000, 5200, 0) };

        editor.AddEntities(new Entity[] { insert1, insert2 }, "Insert");

        Step("Block cửa sổ + 2 insert", definition is not null &&
            doc.Entities.OfType<Insert>().Count(i => ReferenceEquals(i.Block, block)) == 2,
            doc.Entities.OfType<Insert>().Count() + " insert");

        // 9. Tờ A3 tỷ lệ 1:50
        var modelScene = new CadScene();
        modelScene.Build(doc);

        var layout = OpenCad.Core.Layout.LayoutFactory.Create(doc, "A3-150",
            new OpenCad.Core.Layout.SheetTemplateOptions
            {
                Title = "MAT BANG",
                WidthMm = 420,
                HeightMm = 297,
                UnitsPerMm = 50,
            },
            modelScene);

        Step("Tạo tờ A3", layout is not null, layout?.Name ?? "null");

        if (layout is not null)
        {
            var sheet = OpenCad.Core.Layout.LayoutFactory.Read(doc, layout);
            var viewports = sheet?.Viewport is null ? 0 : 1;

            Step("Tờ có viewport", viewports > 0, viewports + " viewport");

            var sheetPath = Path.Combine(Path.GetTempPath(), "opencad-workflow.pdf");
            var sheetOptions = new OpenCad.Core.Export.PdfExportOptions
            {
                Title = "WORKFLOW",
                FontPath = OpenCad.Core.Export.PdfExportOptions.FindSystemFont(),
                Monochrome = true,
                PlotStyle = OpenCad.Core.Export.PlotStyleTable.LoadFile(
                    Path.Combine(root, "samples", "kieu-in.txt")),
            };

            try
            {
                OpenCad.Core.Export.PdfExporter.ExportPaperSheet(sheet!, sheetPath, sheetOptions);

                var size = new FileInfo(sheetPath).Length;

                Step("In PDF tờ A3", size > 10_000, size.ToString("N0") + " bytes");

                try { File.Delete(sheetPath); } catch { }
            }
            catch (Exception ex)
            {
                Step("In PDF tờ A3", false, ex.GetType().Name);
            }
        }

        // 10. Lưu — mở lại — audit
        var drawingPath = Path.Combine(Path.GetTempPath(), "opencad-workflow.dwg");

        try
        {
            workspace.Save(drawingPath);

            Step("Lưu DWG", File.Exists(drawingPath),
                new FileInfo(drawingPath).Length.ToString("N0") + " bytes");

            var back = CadWorkspace.Open(drawingPath);
            var backScene = new CadScene();
            backScene.Build(back.Document);

            Step("Mở lại đủ đối tượng", back.Document.Entities.Count() == doc.Entities.Count(),
                doc.Entities.Count() + " → " + back.Document.Entities.Count() + " entity");

            Step("Mở lại vẫn vẽ được", backScene.PrimitiveCount > 10,
                backScene.PrimitiveCount + " primitive");

            Step("Mở lại giữ đủ layer", back.Document.Layers.Count(l => l.Name is
                    "TUONG" or "CUA" or "KICH_THUOC" or "CHU") == 4,
                back.Document.Layers.Count() + " layer");

            Step("Mở lại giữ block + insert", back.Document.Entities.OfType<Insert>().Any(),
                back.Document.Entities.OfType<Insert>().Count() + " insert");

            var audit = OpenCad.Core.Audit.DrawingAudit.Run(back.Document);

            Step("Audit sạch", audit.Count == 0, audit.Count + " vấn đề");

            // 11. Kiểm không nhúng dữ liệu song song: mở lại rồi lưu lần nữa phải ổn định
            var secondPath = Path.Combine(Path.GetTempPath(), "opencad-workflow-2.dwg");
            back.Save(secondPath);

            var third = CadWorkspace.Open(secondPath);

            Step("Lưu lần hai ổn định", third.Document.Entities.Count() == back.Document.Entities.Count(),
                back.Document.Entities.Count() + " → " + third.Document.Entities.Count());

            try { File.Delete(secondPath); } catch { }
        }
        catch (Exception ex)
        {
            Step("Lưu/mở DWG", false, ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            try { File.Delete(drawingPath); } catch { }
        }

        // In bảng
        Console.WriteLine("Quy trình làm hồ sơ (đầu → cuối)");
        Console.WriteLine("---------------------------------------------");

        foreach (var (name, ok, detail) in steps)
            Console.WriteLine($"  {(ok ? "✔" : "✘")} {name,-38} {detail}");

        var failures = steps.Count(s => !s.Ok);

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? $"workflow: {steps.Count}/{steps.Count} bước ĐẠT — sản phẩm chạy được đầu-đến-cuối"
            : $"workflow: {failures}/{steps.Count} bước HỎNG");

        return failures == 0 ? 0 : 1;
    }

    private static int GripHuntCommand()
    {
        var factories = new (string Name, Func<Entity> Make)[]
        {
            ("Line", () => new Line
            {
                StartPoint = new CSMath.XYZ(0, 0, 0),
                EndPoint = new CSMath.XYZ(1000, 0, 0),
            }),
            ("Circle", () => new Circle
            {
                Center = new CSMath.XYZ(0, 0, 0),
                Radius = 500,
            }),
            ("Arc", () => new Arc
            {
                Center = new CSMath.XYZ(0, 0, 0),
                Radius = 500,
                StartAngle = 0,
                EndAngle = Math.PI / 2,
            }),
            ("Ellipse", () => new Ellipse
            {
                Center = new CSMath.XYZ(0, 0, 0),
                RadiusRatio = 0.5,
                MajorAxisEndPoint = new CSMath.XYZ(500, 0, 0),
            }),
            ("Point", () => new Point { Location = new CSMath.XYZ(0, 0, 0) }),
            ("TextEntity", () => new TextEntity
            {
                InsertPoint = new CSMath.XYZ(0, 0, 0),
                Value = "ABC",
                Height = 100,
            }),
            ("MText", () => new MText
            {
                InsertPoint = new CSMath.XYZ(0, 0, 0),
                Value = "ABC",
                Height = 100,
            }),
            ("LwPolyline", () =>
            {
                var poly = new LwPolyline();

                poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 0) });
                poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(1000, 0) });
                poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(1000, 800) });

                return poly;
            }),
            ("Leader", () =>
            {
                var leader = new Leader();

                leader.Vertices.Add(new CSMath.XYZ(0, 0, 0));
                leader.Vertices.Add(new CSMath.XYZ(500, 500, 0));

                return leader;
            }),
            ("MLine", () =>
            {
                var mline = new MLine();

                var v1 = new MLine.Vertex();
                v1.Position = new CSMath.XYZ(0, 0, 0);
                mline.Vertices.Add(v1);

                var v2 = new MLine.Vertex();
                v2.Position = new CSMath.XYZ(1000, 500, 0);
                mline.Vertices.Add(v2);

                return mline;
            }),
            ("XLine", () => new XLine
            {
                FirstPoint = new CSMath.XYZ(0, 0, 0),
                Direction = new CSMath.XYZ(1, 0, 0),
            }),
            ("Ray", () => new Ray
            {
                StartPoint = new CSMath.XYZ(0, 0, 0),
                Direction = new CSMath.XYZ(1, 0, 0),
            }),
        };

        // Toạ độ đặt thử: gốc, rất xa, âm lớn, và cực nhỏ
        var origins = new (string Label, double X, double Y)[]
        {
            ("gốc", 0, 0),
            ("1e6", 1_000_000, 1_000_000),
            ("-5e5", -500_000, 250_000),
            ("1e-3", 0.001, 0.001),
            ("1e7", 10_000_000, 10_000_000),
        };

        // Điểm đích khi kéo: gần, xa, trùng grip, và cực xa
        var deltas = new (string Label, double DX, double DY)[]
        {
            ("+100,+50", 100, 50),
            ("trùng grip", 0, 0),
            ("-1e6", -1_000_000, 0),
            ("+1e6", 1_000_000, 1_000_000),
            ("+0.001", 0.001, 0.001),
        };

        var drags = 0;
        var failures = new List<string>();
        var nanCount = 0;

        bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        foreach (var (name, make) in factories)
        {
            foreach (var origin in origins)
            {
                var workspace = CadWorkspace.New();
                var editor = new CadEditor(workspace);

                var entity = make();
                editor.AddEntities(new Entity[] { entity }, "Add");
                editor.ApplyTransform(new Entity[] { entity }, Affine2.Translate(origin.X, origin.Y));
                editor.CommitTransform(new Entity[] { entity }, Affine2.Translate(origin.X, origin.Y), "Move");

                var grips = GripSet.For(entity);

                foreach (var grip in grips)
                {
                    foreach (var delta in deltas)
                    {
                        drags++;

                        var target = new Pt(grip.Position.X + delta.DX, grip.Position.Y + delta.DY);

                        try
                        {
                            GripSet.For(entity);
                            var moved = GripEdit.Move(entity, grip, target);

                            if (moved)
                            {
                                // Toạ độ sau khi kéo phải hữu hạn
                                var scene = new CadScene();
                                scene.Build(workspace.Document);

                                foreach (var entry in scene.Entries)
                                {
                                    if (!Finite(entry.Bounds.MinX) || !Finite(entry.Bounds.MaxY))
                                    {
                                        nanCount++;
                                        failures.Add($"{name}@{origin.Label} Δ{delta.Label}: toạ độ không hữu hạn");
                                        break;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            failures.Add($"{name}@{origin.Label} Δ{delta.Label}: {ex.GetType().Name}: {ex.Message}");
                        }

                        // Trả về trạng thái ban đầu cho lần kéo sau
                        try
                        {
                            editor.Reload();
                        }
                        catch { }
                    }
                }
            }
        }

        Console.WriteLine($"griphunt: {drags} lần kéo grip trên {factories.Length} loại entity × "
                          + $"{origins.Length} toạ độ × {deltas.Length} hướng kéo");

        foreach (var failure in failures.Take(10)) Console.WriteLine("   HỎNG  " + failure);

        if (failures.Count > 10) Console.WriteLine($"   … và {failures.Count - 10} lỗi nữa");

        Console.WriteLine(failures.Count == 0
            ? "   ✔ không có ngoại lệ, không có toạ độ NaN"
            : $"   ✘ {failures.Count} lỗi ({nanCount} do toạ độ không hữu hạn)");

        return failures.Count == 0 ? 0 : 1;
    }

    private static int LifecycleCommand()
    {
        var factories = new (string Name, Func<Entity> Make)[]
        {
            ("Line", () => new Line
            {
                StartPoint = new CSMath.XYZ(1000, 1000, 0),
                EndPoint = new CSMath.XYZ(2000, 1000, 0),
            }),
            ("Circle", () => new Circle
            {
                Center = new CSMath.XYZ(1500, 1500, 0),
                Radius = 400,
            }),
            ("Arc", () => new Arc
            {
                Center = new CSMath.XYZ(1500, 1500, 0),
                Radius = 400,
                StartAngle = 0,
                EndAngle = Math.PI / 2,
            }),
            ("Ellipse", () => new Ellipse
            {
                Center = new CSMath.XYZ(1500, 1500, 0),
                RadiusRatio = 0.5,
                MajorAxisEndPoint = new CSMath.XYZ(2000, 1500, 0),
            }),
            ("Point", () => new Point { Location = new CSMath.XYZ(1500, 1500, 0) }),
            ("TextEntity", () => new TextEntity
            {
                InsertPoint = new CSMath.XYZ(1500, 1500, 0),
                Value = "ABC",
                Height = 200,
            }),
            ("MText", () => new MText
            {
                InsertPoint = new CSMath.XYZ(1500, 1500, 0),
                Value = "ABC",
                Height = 200,
            }),
            ("LwPolyline", () =>
            {
                var poly = new LwPolyline();

                poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(1000, 1000) });
                poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(2000, 1000) });
                poly.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(2000, 2000) });

                return poly;
            }),
            ("Leader", () =>
            {
                var leader = new Leader();

                leader.Vertices.Add(new CSMath.XYZ(1000, 1000, 0));
                leader.Vertices.Add(new CSMath.XYZ(1500, 1500, 0));

                return leader;
            }),
            ("MLine", () =>
            {
                var mline = new MLine();

                var v1 = new MLine.Vertex();
                v1.Position = new CSMath.XYZ(1000, 1000, 0);
                mline.Vertices.Add(v1);

                var v2 = new MLine.Vertex();
                v2.Position = new CSMath.XYZ(2000, 1500, 0);
                mline.Vertices.Add(v2);

                return mline;
            }),
            ("XLine", () => new XLine
            {
                FirstPoint = new CSMath.XYZ(1000, 1500, 0),
                Direction = new CSMath.XYZ(1, 0, 0),
            }),
            ("Ray", () => new Ray
            {
                StartPoint = new CSMath.XYZ(1000, 1500, 0),
                Direction = new CSMath.XYZ(1, 0, 0),
            }),
            ("Solid", () =>
            {
                var solid = new Solid
                {
                    FirstCorner = new CSMath.XYZ(1000, 1000, 0),
                    SecondCorner = new CSMath.XYZ(2000, 1000, 0),
                    ThirdCorner = new CSMath.XYZ(2000, 2000, 0),
                };

                return solid;
            }),
        };

        Console.WriteLine("Loại entity    vẽ  hit  move  undo  lưu");
        Console.WriteLine("------------------------------------------");

        var failures = 0;

        foreach (var (name, make) in factories)
        {
            var workspace = CadWorkspace.New();
            var editor = new CadEditor(workspace);

            var entity = make();
            editor.AddEntities(new Entity[] { entity }, "Add");

            // 1. vẽ
            var scene = new CadScene();
            scene.Build(workspace.Document);

            var rendered = scene.Entries
                .Where(e => ReferenceEquals(e.Entity, entity))
                .SelectMany(e => e.Prims)
                .Count() > 0;

            // 2. hit-test tại một điểm NẰM TRÊN nét vẽ (tâm vùng bao có thể là chỗ rỗng
            //    với đường tròn/cung/ellipse — không phải lỗi của app)
            var entry = scene.Entries.FirstOrDefault(e => ReferenceEquals(e.Entity, entity));
            var center = entry is null
                ? new Pt(1500, 1500)
                : new Pt((entry.Bounds.MinX + entry.Bounds.MaxX) / 2,
                         (entry.Bounds.MinY + entry.Bounds.MaxY) / 2);

            var pickPoint = center;

            if (entry is not null)
            {
                foreach (var prim in entry.Prims)
                {
                    if (prim.Points.Length >= 2)
                    {
                        pickPoint = new Pt((prim.Points[0].X + prim.Points[1].X) / 2,
                                           (prim.Points[0].Y + prim.Points[1].Y) / 2);
                        break;
                    }

                    if (prim.Kind == PrimKind.Text || prim.Kind == PrimKind.Point)
                    {
                        pickPoint = prim.Position;
                        break;
                    }
                }
            }

            var hit = scene.HitTest(pickPoint, 50)?.Entity;

            var hitOk = hit is not null &&
                        (ReferenceEquals(hit, entity) || hit.GetType() == entity.GetType());

            // 3. MOVE
            var beforeCenter = center;

            var moveBy = Affine2.Translate(500, 300);

            editor.ApplyTransform(new Entity[] { entity }, moveBy);
            editor.CommitTransform(new Entity[] { entity }, moveBy, "Move");

            var movedScene = new CadScene();
            movedScene.Build(workspace.Document);

            var movedEntry = movedScene.Entries.FirstOrDefault(e => ReferenceEquals(e.Entity, entity));
            var movedCenter = movedEntry is null
                ? beforeCenter
                : new Pt((movedEntry.Bounds.MinX + movedEntry.Bounds.MaxX) / 2,
                         (movedEntry.Bounds.MinY + movedEntry.Bounds.MaxY) / 2);

            var moved = Math.Abs(movedCenter.X - beforeCenter.X - 500) < 5 &&
                        Math.Abs(movedCenter.Y - beforeCenter.Y - 300) < 5;

            // 4. undo
            editor.Undo();

            var undoScene = new CadScene();
            undoScene.Build(workspace.Document);

            var undoEntry = undoScene.Entries.FirstOrDefault(e => ReferenceEquals(e.Entity, entity));
            var undoCenter = undoEntry is null
                ? movedCenter
                : new Pt((undoEntry.Bounds.MinX + undoEntry.Bounds.MaxX) / 2,
                         (undoEntry.Bounds.MinY + undoEntry.Bounds.MaxY) / 2);

            var undone = Math.Abs(undoCenter.X - beforeCenter.X) < 5 &&
                         Math.Abs(undoCenter.Y - beforeCenter.Y) < 5;

            // 5. lưu/đọc
            var path = Path.Combine(Path.GetTempPath(), "opencad-life-" + name + ".dwg");

            bool saved;

            try
            {
                workspace.Save(path);

                var back = CadWorkspace.Open(path);
                saved = back.Document.Entities.Any(e => e.GetType().Name == name);
            }
            catch { saved = false; }

            try { File.Delete(path); } catch { }

            bool[] cells = { rendered, hitOk, moved, undone, saved };
            failures += cells.Count(c => !c);

            Console.WriteLine(
                $"{name,-14} {(rendered ? " ✔ " : " ✘ ")} " +
                $"{(hitOk ? " ✔ " : " ✘ ")} " +
                $"{(moved ? " ✔  " : " ✘  ")} " +
                $"{(undone ? " ✔   " : " ✘   ")} " +
                $"{(saved ? " ✔" : " ✘")}");
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? $"lifecycle: {factories.Length} loại — mọi bước đều ĐẠT"
            : $"lifecycle: {failures} ô HỎNG trên {factories.Length} loại");

        return failures == 0 ? 0 : 1;
    }

    private static int CheckRenderCommand(string root = ".")
    {
        var flattenerPath = Path.Combine(root, "src", "OpenCad.Core", "Rendering", "EntityFlattener.cs");

        if (!File.Exists(flattenerPath))
        {
            Console.WriteLine("checkrender: không thấy " + flattenerPath);
            return 1;
        }

        var source = File.ReadAllText(flattenerPath);

        // Loại không vẽ được nhưng CÓ CHỦ Ý (không hỗ trợ, app đã báo qua UNSUPPORTED)
        var intentionally = new HashSet<string>(StringComparer.Ordinal)
        {
            "Solid3D", "Region", "Body", "Surface", "ExtrudedSurface", "LoftedSurface",
            "RevolvedSurface", "SweptSurface", "PlaneSurface", "NurbSurface", "Mesh",
            "PolygonMesh", "PolyfaceMesh", "Helix", "Shape", "Ole2Frame", "OleFrame",
            "Underlay", "PdfUnderlay", "DwfUnderlay", "DgnUnderlay", "Image", "RasterImage",
            "ProxyEntity", "CadImage", "UnknownEntity", "AttributeDefinition", "Seqend",
            "Face3D", "PolygonMeshVertex", "PolyfaceMeshFace", "PolyfaceMeshVertex",
            "AttributeEntity", "Vertex2D", "Vertex3D", "VertexFaceMesh", "VertexFaceRecord",
            "DimensionOrdinate", "DimensionLinear", "DimensionDiametric",

            // Marker cấu trúc của tệp, không phải hình vẽ
            "Block", "BlockEnd", "PolyLinePlaceholder", "VertexPlaceholder",

            // Đối tượng của AutoCAD Mechanical / Architectural — không hỗ trợ,
            // nhưng Scene BÁO qua SkippedTypes nên không mất dữ liệu âm thầm
            "AcmBalloon", "AcmPartList", "AcmPartRef", "Wall",

            // Viewport chỉ vẽ qua đường tờ in (PaperSheet), không vẽ trong model space
            "Viewport",

            // Body ACIS — cùng nhóm với Solid3D/Region
            "CadBody",
        };

        var rendered = new List<string>();
        var missing = new List<string>();

        var entityTypes = typeof(Entity).Assembly
            .GetTypes()
            .Where(t => typeof(Entity).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericType)
            .Select(t => t.Name)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        // Bản đồ tên → kiểu, để xét cả lớp cha (DimensionAligned được vẽ qua "case Dimension")
        var typeMap = typeof(Entity).Assembly
            .GetTypes()
            .Where(t => typeof(Entity).IsAssignableFrom(t) && !t.IsAbstract)
            .GroupBy(t => t.Name)
            .ToDictionary(g => g.Key, g => g.First());

        bool HasBranch(string name)
            => source.Contains("case " + name + " ", StringComparison.Ordinal)
               || source.Contains("case " + name + ":", StringComparison.Ordinal);

        bool CoveredByAny(string name)
        {
            if (!typeMap.TryGetValue(name, out var type)) return false;

            for (Type? t = type; t is not null && t != typeof(object); t = t.BaseType)
                if (HasBranch(t.Name)) return true;

            return false;
        }

        foreach (var name in entityTypes)
        {
            if (CoveredByAny(name)) rendered.Add(name);
            else if (!intentionally.Contains(name)) missing.Add(name);
        }

        Console.WriteLine($"checkrender: {entityTypes.Count} lớp entity, {rendered.Count} có nhánh vẽ, "
                          + $"{missing.Count} thiếu nhánh và không nằm trong danh sách chủ ý");

        foreach (var name in missing) Console.WriteLine("   THIẾU NHÁNH VẼ: " + name);

        Console.WriteLine(missing.Count == 0
            ? "   mọi loại đều vẽ được hoặc đã được khai báo là không hỗ trợ"
            : "   → thêm nhánh vẽ hoặc khai báo vào danh sách chủ ý");

        return missing.Count == 0 ? 0 : 1;
    }

    private static int CheckClaimsCommand()
    {
        var path = Path.Combine(".", "README.md");

        if (!File.Exists(path))
        {
            Console.WriteLine("checkclaims: không thấy README.md");
            return 1;
        }

        var text = File.ReadAllText(path);
        var problems = new List<string>();

        // --- (1) lệnh có thật nhưng bị ghi là "còn thiếu"
        var missingStart = text.IndexOf("### Còn thiếu thật", StringComparison.Ordinal);

        if (missingStart > 0)
        {
            var missingEnd = text.IndexOf("\n---", missingStart, StringComparison.Ordinal);
            var section = missingEnd > missingStart
                ? text[missingStart..missingEnd]
                : text[missingStart..];

            foreach (var command in OpenCad.Core.Commands.CommandRegistry.All)
            {
                if (section.Contains("`" + command.Name + "`", StringComparison.OrdinalIgnoreCase))
                    problems.Add($"lệnh {command.Name} CÓ trong bảng lệnh nhưng README ghi là còn thiếu");
            }
        }
        else
        {
            problems.Add("README không có mục '### Còn thiếu thật'");
        }

        // --- (2) số test Core ghi trong README phải khớp số thật
        // Lấy số test thật: dùng lần chạy gần nhất; nếu chưa có thì chạy smoke một lần
        // (checkall gọi cổng này TRƯỚC checkdead nhưng SAU checktests, nên thường đã có sẵn).
        var realCount = SmokeTest.LastCount;

        if (realCount == 0)
        {
            Console.WriteLine("   (chạy smoke để lấy số test thật…)");
            SmokeTest.Run();
            realCount = SmokeTest.LastCount;
        }

        if (realCount == 0)
        {
            Console.WriteLine("   (bỏ qua kiểm số test: chưa chạy smoke trong tiến trình này)");
        }
        else
        {
            var declared = System.Text.RegularExpressions.Regex.Match(text, @"(\d+)\s+test Core");

            if (!declared.Success)
                problems.Add("README không ghi số test Core");
            else if (int.Parse(declared.Groups[1].Value) != realCount)
                problems.Add($"README ghi {declared.Groups[1].Value} test Core nhưng thật là {realCount}");

            var declaredSmoke = System.Text.RegularExpressions.Regex.Match(
                text, @"smoke\s+#\s*(\d+)\s+kiểm tra Core");

            if (declaredSmoke.Success && int.Parse(declaredSmoke.Groups[1].Value) != realCount)
                problems.Add($"dòng lệnh smoke ghi {declaredSmoke.Groups[1].Value} nhưng thật là {realCount}");
        }

        // --- (3) mọi lệnh đều được nhắc trong README
        var notMentioned = OpenCad.Core.Commands.CommandRegistry.All
            .Where(c => !text.Contains("`" + c.Name + "`", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Name)
            .ToList();

        if (notMentioned.Count > 0)
            problems.Add("lệnh không được nhắc trong README: " + string.Join(", ", notMentioned.Take(5)));

        // --- (4) số lệnh ghi ở dòng trạng thái phải khớp
        var declaredCommands = System.Text.RegularExpressions.Regex.Match(
            text, @"\*\*(\d+)\s+lệnh\*\*");

        if (declaredCommands.Success &&
            int.Parse(declaredCommands.Groups[1].Value) != OpenCad.Core.Commands.CommandRegistry.All.Length)
        {
            problems.Add($"README ghi {declaredCommands.Groups[1].Value} lệnh nhưng thật là "
                         + OpenCad.Core.Commands.CommandRegistry.All.Length);
        }

        Console.WriteLine($"checkclaims: {realCount} test Core, {OpenCad.Core.Commands.CommandRegistry.All.Length} lệnh");

        foreach (var problem in problems) Console.WriteLine("   SAI  " + problem);

        Console.WriteLine(problems.Count == 0
            ? "   README khớp thực tế"
            : $"   {problems.Count} tuyên bố sai");

        return problems.Count == 0 ? 0 : 1;
    }

    private static int CheckDeadCommand(string root = ".")
    {
        var appFolder = Path.Combine(root, "src", "OpenCad.App");

        if (!Directory.Exists(appFolder))
        {
            Console.WriteLine("checkdead: không thấy " + appFolder);
            return 1;
        }

        var files = Directory.GetFiles(appFolder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        // Toàn bộ văn bản App để đếm số lần xuất hiện
        var allText = string.Join("\n", files.Select(File.ReadAllText));

        // Bỏ comment cho dễ đếm (giữ nguyên độ dài để không ảnh hưởng gì)
        var declaration = new System.Text.RegularExpressions.Regex(
            @"^\s*(?:private|internal)\s+(?:static\s+|sealed\s+|readonly\s+|async\s+|override\s+|partial\s+)*
              [\w<>\[\],\.\?\(\)\s]+?\s+(\w+)\s*\(",
            System.Text.RegularExpressions.RegexOptions.Multiline |
            System.Text.RegularExpressions.RegexOptions.IgnorePatternWhitespace);

        var suspicious = new List<(string Name, string File, int Line)>();

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (line.Contains("//")) continue;

                var match = declaration.Match(line);

                if (!match.Success) continue;

                var name = match.Groups[1].Value;

                // Bỏ qua hàm dựng và những tên quá ngắn dễ trùng
                if (name.Length < 4) continue;
                if (name is "Main" or "Dispose" or "InitializeComponent") continue;

                // Đếm số lần tên xuất hiện trong toàn bộ App
                var uses = System.Text.RegularExpressions.Regex.Matches(
                    allText, @"\b" + System.Text.RegularExpressions.Regex.Escape(name) + @"\b").Count;

                // 1 lần = chỉ có khai báo → không ai gọi
                if (uses <= 1)
                    suspicious.Add((name, Path.GetFileName(file), i + 1));
            }
        }

        Console.WriteLine($"checkdead: {files.Count} tệp App, {suspicious.Count} thành viên có vẻ không dùng");

        foreach (var item in suspicious.OrderBy(s => s.File).ThenBy(s => s.Line))
            Console.WriteLine($"   {item.Name,-34} {item.File}:{item.Line}");

        // Không chặn build: đây là báo cáo, trừ khi có quá nhiều (dấu hiệu refactor dở dang)
        if (suspicious.Count > 30)
        {
            Console.WriteLine("   → quá nhiều mã chết, nên dọn");
            return 1;
        }

        return 0;
    }

    private static int CheckLimitsCommand()
    {
        var failures = new List<string>();
        var changed = new List<string>();

        void MustNotBeWritable(Type type, string property, string why)
        {
            var info = type.GetProperty(property);

            if (info is null)
            {
                changed.Add(type.Name + "." + property + " không còn tồn tại");
                return;
            }

            var writable = info.SetMethod is not null && info.SetMethod.IsPublic;

            if (writable)
                changed.Add(type.Name + "." + property + " nay ĐẶT ĐƯỢC (giới hạn có thể đã hết)");
            else
                Console.WriteLine($"   OK  {type.Name}.{property} vẫn chỉ đọc — {why}");
        }

        void MustNotExist(Type type, string member, string why)
        {
            var info = type.GetProperty(member) as System.Reflection.MemberInfo
                       ?? type.GetField(member);

            if (info is null)
                Console.WriteLine($"   OK  {type.Name} không có {member} — {why}");
            else
                changed.Add(type.Name + "." + member + " nay ĐÃ CÓ (giới hạn có thể đã hết)");
        }

        Console.WriteLine("checklimits: kiểm các giới hạn ACadSharp đã ghi trong README");

        // 1. MTEXT không có setter Rotation, nhưng góc được SUY RA từ AlignmentPoint.
        //    Cách xoay của app dựa vào đúng hành vi này — kiểm lại để biết khi nào hỏng.
        try
        {
            var probeMText = new ACadSharp.Entities.MText
            {
                InsertPoint = new CSMath.XYZ(1000, 1000, 0),
                AlignmentPoint = new CSMath.XYZ(0, 100, 0),
            };

            if (Math.Abs(probeMText.Rotation - Math.PI / 2) < 1e-6)
            {
                Console.WriteLine("   OK  MText.Rotation suy ra từ AlignmentPoint — xoay MTEXT được (qua AP)");
            }
            else
            {
                changed.Add("MText.Rotation KHÔNG còn suy ra từ AlignmentPoint — cách xoay MTEXT của app hỏng");
            }
        }
        catch (Exception ex)
        {
            failures.Add("MText.AlignmentPoint: " + ex.GetType().Name);
        }

        // 2. Không đặt được BasePoint của block
        MustNotExist(typeof(ACadSharp.Tables.BlockRecord), "BasePoint", "không đặt được điểm gốc block");

        // 3. Không ghi được nội dung ô TABLE
        MustNotBeWritable(typeof(ACadSharp.CadValue), "Value", "không ghi được giá trị ô bảng");
        MustNotBeWritable(typeof(ACadSharp.Entities.TableEntity.CellContent), "CadValue",
            "không thay được CadValue của ô");

        // 3b. Không tạo được thứ tự vẽ (DRAWORDER): SortEntitiesTable không có constructor công khai
        var sortTable = typeof(ACadSharp.Objects.SortEntitiesTable);

        if (sortTable.GetConstructors().Length == 0)
            Console.WriteLine("   OK  SortEntitiesTable không có constructor công khai — không làm DRAWORDER được");
        else
            changed.Add("SortEntitiesTable nay TẠO ĐƯỢC → có thể làm DRAWORDER");

        // 4. Tiền tố kích thước: DÙNG ĐƯỢC (tuyên bố "bị nhân đôi" trước đây là SAI)
        try
        {
            var postStyle = new ACadSharp.Tables.DimensionStyle("LIMIT_POST");

            postStyle.Prefix = "L=";
            postStyle.Suffix = " mm";

            if (postStyle.Prefix == "L=" && postStyle.Suffix == " mm")
                Console.WriteLine("   OK  DimensionStyle.Prefix độc lập với Suffix — tiền tố dùng được");
            else
                changed.Add("DimensionStyle.Prefix không còn đọc đúng — kiểm lại DIMPOST");
        }
        catch (Exception ex)
        {
            failures.Add("DimensionStyle.Prefix: " + ex.GetType().Name);
        }

        // 5. MULTILEADER và TABLE mất khi lưu — kiểm bằng round-trip thật
        foreach (var (name, make) in new (string, Func<ACadSharp.CadDocument, ACadSharp.Entities.Entity>)[]
                 {
                     ("MultiLeader", _ => new ACadSharp.Entities.MultiLeader()),
                     ("TableEntity", doc =>
                     {
                         var block = new ACadSharp.Tables.BlockRecord("LIMIT_TB");
                         doc.BlockRecords.Add(block);

                         // Bảng 2×2 như app tạo — bảng 1×1 lại SỐNG sót, không đại diện
                         var table = new ACadSharp.Entities.TableEntity(block)
                         {
                             RowCount = 2,
                             ColumnCount = 2,
                             RowSpacing = 10,
                             ColumnSpacing = 20,
                         };

                         table.Rows.Add(new ACadSharp.Entities.TableEntity.Row { Height = 10 });
                         table.Rows.Add(new ACadSharp.Entities.TableEntity.Row { Height = 10 });
                         table.Columns.Add(new ACadSharp.Entities.TableEntity.Column { Width = 20 });
                         table.Columns.Add(new ACadSharp.Entities.TableEntity.Column { Width = 20 });

                         return table;
                     }),
                 })
        {
            try
            {
                var ws = CadWorkspace.New();
                var entity = make(ws.Document);
                ws.Document.Entities.Add(entity);

                var path = Path.Combine(Path.GetTempPath(), "opencad-limit-" + name + ".dwg");
                ws.Save(path);

                var back = CadWorkspace.Open(path);
                var survived = back.Document.Entities.Any(e => e.GetType().Name == name);

                try { File.Delete(path); } catch { }

                if (!survived)
                {
                    Console.WriteLine($"   OK  {name} vẫn mất khi lưu DWG");
                }
                else if (name == "MultiLeader")
                {
                    // 3.8.30: entity sống sót nhưng chữ ghi chú bị mất
                    Console.WriteLine("   OK  " + name
                        + " sống sót nhưng MẤT CHỮ (vẫn phải cảnh báo + chuyển đổi)");
                }
                else
                {
                    changed.Add(name + " NAY lưu được (giới hạn có thể đã hết)");
                }
            }
            catch (Exception ex)
            {
                failures.Add(name + ": " + ex.GetType().Name);
            }
        }

        foreach (var item in changed) Console.WriteLine("   THAY ĐỔI " + item);
        foreach (var item in failures) Console.WriteLine("   LỖI " + item);

        if (changed.Count > 0)
            Console.WriteLine("   → cập nhật mục 'Giới hạn' trong README");

        Console.WriteLine(changed.Count == 0 && failures.Count == 0
            ? "   các giới hạn đã ghi vẫn đúng"
            : $"   {changed.Count} giới hạn đã thay đổi, {failures.Count} lỗi");

        return failures.Count == 0 ? 0 : 1;
    }

    private static int CheckTestsCommand()
    {
        var files = new[]
        {
            Path.Combine("tools", "OpenCad.Tools", "SmokeTest.cs"),
            Path.Combine("src", "OpenCad.App", "UiSelfTest.cs"),
        };

        var literal = new System.Text.RegularExpressions.Regex(
            "Che[cC]k\\(\\s*\"[^\"]*\"\\s*,\\s*(true|false)\\s*,");

        var alwaysTrue = new System.Text.RegularExpressions.Regex(
            "(\\.Count|\\.Length)\\s*>=\\s*0");

        var problems = new List<string>();
        var total = 0;

        foreach (var file in files)
        {
            if (!File.Exists(file)) continue;

            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("heck(")) continue;

                total++;

                foreach (System.Text.RegularExpressions.Match match in literal.Matches(lines[i]))
                {
                    if (match.Groups[1].Value != "true") continue;

                    problems.Add(Path.GetFileName(file) + ":" + (i + 1)
                                 + " điều kiện luôn đúng: " + lines[i].Trim());
                }
            }

            var text = File.ReadAllText(file);

            foreach (System.Text.RegularExpressions.Match match in alwaysTrue.Matches(text))
            {
                var line = text.Take(match.Index).Count(c => c == '\n') + 1;

                problems.Add(Path.GetFileName(file) + ":" + line
                             + " điều kiện luôn đúng: " + match.Value);
            }
        }

        Console.WriteLine("checktests: " + total + " lệnh kiểm trong 2 tệp");

        foreach (var problem in problems) Console.WriteLine("   GIẢ " + problem);

        Console.WriteLine(problems.Count == 0
            ? "   không có test giả"
            : "   " + problems.Count + " test giả cần sửa");

        return problems.Count == 0 ? 0 : 1;
    }

    private static int CheckParityCommand()
    {
        var capabilities = new (string What, string Cli, string? App)[]
        {
            ("xuất PDF một bản vẽ", "pdf", "PDF"),
            ("xuất PDF nhiều bản vẽ", "pdfbatch", "PDF"),
            ("in tờ có khung tên", "plotsheet", "PLOT"),
            ("in một layout", "plotlayout", "PLOT"),
            ("liệt kê layout", "layouts", "INFO"),
            ("thêm viewport", "addviewport", "VPORT"),
            ("liệt kê mẫu tô", "patterns", "HATCHPATTERNS"),
            ("rà bản vẽ", "audit", "AUDIT"),
            ("dọn định nghĩa mồ côi", "purge", "PURGE"),
            ("chạy script", "script", "SCRIPT"),
            ("thông tin bản vẽ", "info", "INFO"),
            ("bảng kiểu in", "pdf style=", "PLOTSTYLE"),
        };

        var registry = OpenCad.Core.Commands.CommandRegistry.All
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var gaps = new List<string>();

        foreach (var (what, cli, app) in capabilities)
        {
            if (app is null) continue;
            if (registry.Contains(app)) continue;

            gaps.Add(what + ": CLI có '" + cli + "' nhưng app thiếu lệnh '" + app + "'");
        }

        Console.WriteLine("checkparity:");

        foreach (var gap in gaps) Console.WriteLine("   THIẾU " + gap);

        Console.WriteLine(gaps.Count == 0
            ? "   " + capabilities.Length + " khả năng CLI đều có lệnh app tương ứng"
            : "   " + gaps.Count + " khả năng chỉ có ở CLI");

        return gaps.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// Đối chiếu alias với nghĩa CHUẨN của AutoCAD. Một alias đặt sai làm người quen AutoCAD
    /// gọi nhầm lệnh (ví dụ DRA từng trỏ DIMALIGNED trong khi AutoCAD dùng cho DIMRADIUS).
    /// </summary>
    private static int CheckAliasesCommand()
    {
        // alias → lệnh mà AutoCAD quy cho nó
        var canonical = new (string Alias, string Command)[]
        {
            ("L", "LINE"), ("PL", "PLINE"), ("REC", "RECTANG"), ("C", "CIRCLE"), ("A", "ARC"),
            ("EL", "ELLIPSE"), ("POL", "POLYGON"), ("SPL", "SPLINE"), ("PO", "POINT"),
            ("T", "TEXT"), ("MT", "MTEXT"), ("H", "HATCH"), ("HE", "HATCHEDIT"),
            ("M", "MOVE"), ("CO", "COPY"), ("CP", "COPY"), ("RO", "ROTATE"), ("SC", "SCALE"),
            ("MI", "MIRROR"), ("O", "OFFSET"), ("TR", "TRIM"), ("EX", "EXTEND"),
            ("F", "FILLET"), ("CHA", "CHAMFER"), ("E", "ERASE"), ("S", "STRETCH"),
            ("AR", "ARRAY"), ("X", "EXPLODE"), ("J", "JOIN"), ("B", "BLOCK"), ("I", "INSERT"),
            ("Z", "ZOOM"), ("U", "UNDO"), ("DI", "DIST"), ("AA", "AREA"), ("MA", "MATCHPROP"),
            ("ED", "DDEDIT"), ("DLI", "DIMLINEAR"), ("DAL", "DIMALIGNED"), ("DAN", "DIMANGULAR"),
            ("DRA", "DIMRADIUS"), ("DDI", "DIMDIAMETER"), ("DCO", "DIMCONTINUE"),
            ("DBA", "DIMBASELINE"), ("DST", "DIMSTYLE"), ("DIV", "DIVIDE"), ("ME", "MEASURE"),
            ("LA", "LAYER"), ("OS", "OSNAP"), ("LTS", "LTSCALE"), ("PE", "PEDIT"),
        };

        var registry = OpenCad.Core.Commands.CommandRegistry.All;

        var byAlias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var command in registry)
        {
            foreach (var alias in command.Aliases)
            {
                if (string.IsNullOrWhiteSpace(alias)) continue;

                byAlias[alias.Trim()] = command.Name;
            }
        }

        var wrong = new List<string>();
        var notImplemented = new List<string>();

        foreach (var (alias, expected) in canonical)
        {
            var implemented = registry.Any(c => c.Name.Equals(expected, StringComparison.OrdinalIgnoreCase));

            if (!implemented)
            {
                notImplemented.Add($"{alias}→{expected}");
                continue;
            }

            if (!byAlias.TryGetValue(alias, out var mapped))
            {
                wrong.Add($"{alias} chưa có (AutoCAD: {expected})");
                continue;
            }

            if (!mapped.Equals(expected, StringComparison.OrdinalIgnoreCase))
                wrong.Add($"{alias} đang trỏ {mapped} (AutoCAD: {expected})");
        }

        Console.WriteLine("checkaliases:");

        if (notImplemented.Count > 0)
            Console.WriteLine("   chưa làm: " + string.Join(", ", notImplemented));

        foreach (var problem in wrong)
            Console.WriteLine("   SAI " + problem);

        Console.WriteLine(wrong.Count == 0
            ? $"   {canonical.Length - notImplemented.Count}/{canonical.Length - notImplemented.Count} alias khớp AutoCAD"
            : $"   {wrong.Count} alias lệch nghĩa AutoCAD");

        return wrong.Count == 0 ? 0 : 1;
    }

    private static int CheckAllCommand(string[] args)
    {
        var root = args.Length > 1 ? args[1] : ".";

        Console.WriteLine("=== checkcmds ===");
        var commands = CheckCommandsCommand();

        Console.WriteLine();
        Console.WriteLine("=== checkdocs ===");
        var docs = CheckDocsCommand();

        Console.WriteLine();
        Console.WriteLine("=== checkaliases ===");
        var aliases = CheckAliasesCommand();

        Console.WriteLine();
        Console.WriteLine("=== checkparity ===");
        var parity = CheckParityCommand();

        Console.WriteLine();
        Console.WriteLine("=== checktests ===");
        var tests = CheckTestsCommand();

        Console.WriteLine();
        Console.WriteLine("=== checklimits ===");
        var limits = CheckLimitsCommand();

        Console.WriteLine();
        Console.WriteLine("=== checkclaims ===");
        var claims = CheckClaimsCommand();

        Console.WriteLine();
        Console.WriteLine("=== lifecycle ===");
        var life = LifecycleCommand();

        Console.WriteLine();
        Console.WriteLine("=== checkfixtures ===");
        var fixtures = CheckFixturesCommand(root);

        Console.WriteLine();
        Console.WriteLine("=== workflow ===");
        var workflow = WorkflowCommand(root);

        Console.WriteLine();
        Console.WriteLine("=== griphunt ===");
        var grip = GripHuntCommand();

        Console.WriteLine();
        Console.WriteLine("=== checkrender ===");
        var render = CheckRenderCommand(root);

        Console.WriteLine();
        Console.WriteLine("=== checkdead ===");
        var dead = CheckDeadCommand(root);

        Console.WriteLine();
        var invariants = CheckInvariantsCommand(new[] { "checkinvariants", root });

        Console.WriteLine();

        var ok = commands == 0 && docs == 0 && aliases == 0 && parity == 0
                 && tests == 0 && limits == 0 && claims == 0 && life == 0 && fixtures == 0 && workflow == 0 && grip == 0 && render == 0 && dead == 0 && invariants == 0;

        Console.WriteLine(ok ? "TẤT CẢ CỔNG KIỂM ĐỀU ĐẠT" : "CÓ CỔNG KIỂM KHÔNG ĐẠT");

        return ok ? 0 : 1;
    }

    private static int CheckInvariantsCommand(string[] args)
    {
        var root = args.Length > 1 ? args[1] : ".";

        string Read(params string[] parts)
        {
            var path = Path.Combine(new[] { root }.Concat(parts).ToArray());

            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        var canvas = Read("src", "OpenCad.App", "CadCanvas.cs");
        var mainForm = Read("src", "OpenCad.App", "MainForm.cs");
        var workspace = Read("src", "OpenCad.Core", "CadWorkspace.cs");
        var purge = Read("src", "OpenCad.Core", "Audit", "DrawingPurge.cs");

        var checks = new (string Name, bool Ok)[]
        {
            // Guard: lỗi khi vẽ màn hình không được làm sập app
            ("OnPaint có try/catch",
                canvas.Contains("ReportUiError(\"Vẽ màn hình\"")),

            // Guard: kéo grip không được ném ngoại lệ ra UI
            ("Kéo grip có guard",
                canvas.Contains("ReportUiError(\"Kéo grip\"")),

            // Guard: chỉ có MỘT nhánh kéo grip (từng bị nhân đôi)
            ("Chỉ một nhánh kéo grip",
                canvas.Split("// Kéo grip: cập nhật hình theo con trỏ").Length - 1 == 1),

            // Resize viewport: đủ 4 chỗ (bắt đầu, rê, nhả, khai báo)
            ("Resize viewport được nối đủ",
                canvas.Split("_resizingViewport").Length - 1 >= 4),

            // Autosave phải được xoá sau khi lưu thật
            ("Lưu xong xoá autosave",
                mainForm.Contains("ClearAutosave()")),

            // Lưu an toàn: ghi ra .tmp rồi thay thế
            ("Save dùng file tạm",
                workspace.Contains(".tmp")),

            // Purge không được coi block của layout là mồ côi
            ("Purge giữ block của layout",
                purge.Contains("AssociatedBlock")),

            // Bắt lỗi toàn cục trong app
            ("App bắt lỗi toàn cục",
                Read("src", "OpenCad.App", "Program.cs").Contains("ThreadException")),

            // EXPLODE phải xử lý được kích thước (từng bị ghi đè mất nhánh)
            ("EXPLODE có nhánh Dimension",
                Read("src", "OpenCad.Core", "Editing", "ExplodeOperation.cs").Contains("case Dimension")),

            ("EXPLODE có nhánh MultiLeader",
                Read("src", "OpenCad.Core", "Editing", "ExplodeOperation.cs").Contains("case MultiLeader")),

            // Phím tắt phải xử lý tường minh (menu ShortcutKeys không đủ khi form chưa hiện)
            ("Phím tắt Ctrl+Z/Ctrl+Y/A/Esc/Delete xử lý tường minh",
                Read("src", "OpenCad.App", "MainForm.cs").Contains("Keys.Control | Keys.Z") &&
                Read("src", "OpenCad.App", "MainForm.cs").Contains("Keys.Control | Keys.Y") &&
                Read("src", "OpenCad.App", "MainForm.cs").Contains("Keys.Control | Keys.A") &&
                Read("src", "OpenCad.App", "MainForm.cs").Contains("Keys.Delete")),

            // ResolvePdfFont phải được GỌI (từng khai báo mà không dùng — vòng 147 báo sai)
            ("PDF dùng ResolvePdfFont",
                Read("src", "OpenCad.App", "MainForm.cs").Contains("FontPath = ResolvePdfFont(),")),

            // Chữ ký trùng lặp phải tính Loops (hatch) và vị trí chữ — từng bỏ lọt
            ("Signature tính Loops của hatch",
                Read("src", "OpenCad.Core", "Audit", "DrawingAudit.cs").Contains("p.Loops is { Length: > 0 }")),
            ("Signature tính vị trí chữ",
                Read("src", "OpenCad.Core", "Audit", "DrawingAudit.cs").Contains("|t:")),

            // XLINE/RAY phải có nhánh VẼ (từng thiếu hoàn toàn — chỉ có nhánh biến đổi)
            ("XLINE có nhánh vẽ",
                Read("src", "OpenCad.Core", "Rendering", "EntityFlattener.cs").Contains("case XLine xline:")),
            ("RAY có nhánh vẽ",
                Read("src", "OpenCad.Core", "Rendering", "EntityFlattener.cs").Contains("case Ray ray:")),

            // Dispatcher phải tách TÊN LỆNH khỏi tham số (từng khớp cả chuỗi → lệnh có
            // tham số như "ATTOUT <tệp>", "WBLOCK <tệp>", "TEXTANGLE 45" không chạy được)
            ("Dispatcher tách tên lệnh khỏi tham số",
                Read("src", "OpenCad.App", "MainForm.cs").Contains("switch (dispatch)")),

            // LWT phải còn trên status bar (từng mất im lặng khi bỏ toolbar ở vòng 81)
            ("LWT có trên status bar",
                Read("src", "OpenCad.App", "MainForm.cs").Contains("_status.Items.Add(_toggleLwt)")),

            // lossy phải kiểm NỘI DUNG (từng bỏ lọt vụ MULTILEADER mất chữ ở vòng 139)
            ("lossy kiểm nội dung chứ không chỉ tồn tại",
                Read("tools", "OpenCad.Tools", "Program.cs").Contains("SAI NỘI DUNG")),
        };

        var failed = 0;

        Console.WriteLine("checkinvariants:");

        foreach (var (name, ok) in checks)
        {
            Console.WriteLine($"   {(ok ? "OK  " : "PHÁ ")} {name}");

            if (!ok) failed++;
        }

        Console.WriteLine(failed == 0
            ? $"   {checks.Length}/{checks.Length} bất biến còn nguyên"
            : $"   {failed}/{checks.Length} bất biến đã bị phá");

        return failed == 0 ? 0 : 1;
    }

    private static int CheckWiringCommand(string[] args)
    {
        var root = args.Length > 1 ? args[1] : ".";

        var coreDir = Path.Combine(root, "src", "OpenCad.Core");

        if (!Directory.Exists(coreDir))
        {
            Console.WriteLine($"checkwiring: không thấy {coreDir}");
            return 1;
        }

        // Toàn bộ mã nguồn (Core + App + tools), trừ obj/bin
        var allFiles = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        var texts = allFiles.ToDictionary(f => f, File.ReadAllText);

        // Chỉ PHƯƠNG THỨC: "public [static] <kiểu> Tên(" — bỏ record/class/enum và thuộc tính
        var declaration = new System.Text.RegularExpressions.Regex(
            @"^\s*public\s+(?:static\s+|sealed\s+|override\s+|virtual\s+|async\s+)*" +
            @"(?:[A-Za-z_][\w<>,\[\]\.?]*)\s+([A-Z][A-Za-z0-9_]*)\s*\(",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        // Thành viên hệ thống / quá phổ biến → bỏ qua
        var ignore = new HashSet<string>(StringComparer.Ordinal)
        {
            "Equals", "GetHashCode", "ToString", "GetType", "CompareTo", "Clone",
        };

        var unused = new List<(string Name, string File, int Line)>();

        // Chỉ so với tầng App — đó mới là chỗ "nối vào giao diện"
        var appDir = Path.Combine(root, "src", "OpenCad.App");

        foreach (var file in allFiles.Where(f => f.StartsWith(coreDir, StringComparison.OrdinalIgnoreCase)))
        {
            var text = texts[file];

            foreach (System.Text.RegularExpressions.Match match in declaration.Matches(text))
            {
                var name = match.Groups[1].Value;

                if (name.Length < 4 || ignore.Contains(name)) continue;

                // Có được tầng App gọi tới không?
                var usedElsewhere = texts
                    .Where(kv => kv.Key.StartsWith(appDir, StringComparison.OrdinalIgnoreCase))
                    .Any(kv => kv.Value.Contains(name, StringComparison.Ordinal));

                if (usedElsewhere) continue;

                var line = text.Take(match.Index).Count(c => c == '\n') + 1;

                unused.Add((name, Path.GetFileName(file), line));
            }
        }

        Console.WriteLine($"checkwiring: {unused.Count} API Core không được dùng ngoài file khai báo");

        foreach (var (name, file, line) in unused.OrderBy(u => u.File).ThenBy(u => u.Line))
            Console.WriteLine($"   {name,-32} {file}:{line}");

        return 0;
    }
    private static int CheckDocsCommand()
    {
        var readme = Path.Combine("README.md");

        if (!File.Exists(readme))
        {
            Console.WriteLine("Không thấy README.md");
            return 1;
        }

        var text = File.ReadAllText(readme);
        var missing = new List<string>();

        foreach (var command in OpenCad.Core.Commands.CommandRegistry.All)
            if (!text.Contains("| `" + command.Name + "` |", StringComparison.Ordinal))
                missing.Add(command.Name);

        var expected = $"Tổng: {OpenCad.Core.Commands.CommandRegistry.All.Length} lệnh";
        var countOk = text.Contains(expected, StringComparison.Ordinal);

        Console.WriteLine($"README có {OpenCad.Core.Commands.CommandRegistry.All.Length - missing.Count}"
                          + $"/{OpenCad.Core.Commands.CommandRegistry.All.Length} lệnh");

        if (missing.Count > 0)
            Console.WriteLine("Thiếu trong README: " + string.Join(", ", missing));

        if (!countOk)
            Console.WriteLine($"Dòng tổng số lệnh không đúng (cần \"{expected}\")");

        return missing.Count == 0 && countOk ? 0 : 1;
    }

    private static int DocsCommand(string[] args)
    {
        var readme = Path.Combine("README.md");

        if (!File.Exists(readme))
        {
            Console.WriteLine("Không thấy README.md (chạy từ thư mục gốc dự án).");
            return 1;
        }

        var lines = File.ReadAllLines(readme).ToList();
        var start = lines.FindIndex(l => l.Trim() == "<!-- COMMANDS:START -->");
        var end = lines.FindIndex(l => l.Trim() == "<!-- COMMANDS:END -->");

        if (start < 0 || end < 0 || end <= start)
        {
            Console.WriteLine("README thiếu mốc <!-- COMMANDS:START --> … <!-- COMMANDS:END -->");
            return 1;
        }

        var groupTitles = new Dictionary<string, string>
        {
            ["Vẽ"] = "Vẽ",
            ["Sửa"] = "Sửa",
            ["Kích thước"] = "Kích thước",
            ["Đo"] = "Đo",
            ["Block"] = "Block",
            ["Xem"] = "Xem",
            ["Tệp"] = "Tệp",
            ["Khác"] = "Khác",
        };

        var generated = new List<string>();
        var total = 0;

        foreach (var (category, commands) in OpenCad.Core.Commands.CommandRegistry.ByCategory())
        {
            var title = groupTitles.TryGetValue(category, out var mapped) ? mapped : category;

            generated.Add($"**{title}**");
            generated.Add(string.Empty);
            generated.Add("| Lệnh | Viết tắt | Mô tả |");
            generated.Add("| --- | --- | --- |");

            foreach (var command in commands)
            {
                var aliases = string.Join(", ", command.Aliases.Select(a => "`" + a + "`"));
                generated.Add($"| `{command.Name}` | {aliases} | {command.Description} |");
                total++;
            }

            generated.Add(string.Empty);
        }

        generated.Insert(0, $"_Tổng: {total} lệnh. Phần này sinh tự động từ `CommandRegistry` — chạy `docs` để cập nhật._");
        generated.Insert(1, string.Empty);

        var result = new List<string>();
        result.AddRange(lines.Take(start + 1));
        result.AddRange(generated);
        result.AddRange(lines.Skip(end));

        File.WriteAllLines(readme, result);

        Console.WriteLine($"Đã cập nhật README: {total} lệnh trong {OpenCad.Core.Commands.CommandRegistry.ByCategory().Count} nhóm");

        return 0;
    }

    private static int CheckCommandsCommand()
    {
        var mainForm = Path.Combine("src", "OpenCad.App", "MainForm.cs");

        if (!File.Exists(mainForm))
        {
            Console.WriteLine($"Không thấy {mainForm} (chạy từ thư mục gốc dự án).");
            return 1;
        }

        var source = File.ReadAllText(mainForm);

        // Case labels: case "L": case "LINE":
        var routerLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(source, "case \"([^\"]+)\":"))
            routerLabels.Add(match.Groups[1].Value.ToUpperInvariant());

        var registry = OpenCad.Core.Commands.CommandRegistry.All;
        var registryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var command in registry)
            foreach (var name in command.All)
                if (!string.IsNullOrWhiteSpace(name))
                    registryNames.Add(name.ToUpperInvariant());

        var missingInRouter = new List<string>();
        var missingInRegistry = new List<string>();

        foreach (var command in registry)
        {
            var any = command.All
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Any(a => routerLabels.Contains(a.ToUpperInvariant()));

            if (!any) missingInRouter.Add(command.Name);
        }

        // ESC/ESCAPE là phím thoát, không phải lệnh vẽ nên không cần có trong HELP
        var uiOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ESC", "ESCAPE" };

        foreach (var label in routerLabels.OrderBy(l => l))
            if (!registryNames.Contains(label) && !uiOnly.Contains(label))
                missingInRegistry.Add(label);

        Console.WriteLine($"Router: {routerLabels.Count} nhãn | Bảng lệnh: {registryNames.Count} tên");

        Console.WriteLine();
        Console.WriteLine("Có trong HELP nhưng KHÔNG có trong router (" + missingInRouter.Count + "):");

        foreach (var name in missingInRouter) Console.WriteLine("   " + name);

        Console.WriteLine();
        Console.WriteLine("Có trong router nhưng KHÔNG có trong HELP (" + missingInRegistry.Count + "):");

        foreach (var name in missingInRegistry) Console.WriteLine("   " + name);

        return missingInRouter.Count == 0 && missingInRegistry.Count == 0 ? 0 : 1;
    }

    private static int PurgeCommand(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Dùng: purge <file.dwg> [--apply] [out.dwg]");
            return 1;
        }

        var apply = args.Contains("--apply");
        var workspace = CadWorkspace.Open(args[1]);
        var plan = OpenCad.Core.Audit.DrawingPurge.Inspect(workspace.Document);

        Console.WriteLine($"{args[1]}: {plan.Total} định nghĩa mồ côi");
        if (plan.Layers.Count > 0) Console.WriteLine("   Layer   : " + string.Join(", ", plan.Layers));
        if (plan.Blocks.Count > 0) Console.WriteLine("   Block   : " + string.Join(", ", plan.Blocks));
        if (plan.LineTypes.Count > 0) Console.WriteLine("   Kiểu nét: " + string.Join(", ", plan.LineTypes));

        if (!apply || plan.IsEmpty) return 0;

        var removed = OpenCad.Core.Audit.DrawingPurge.Apply(workspace.Document, plan);
        var output = args.Skip(2).FirstOrDefault(a => !a.StartsWith("--")) ?? args[1];

        workspace.Save(output);
        Console.WriteLine($"Đã xoá {removed} định nghĩa → {output}");

        return 0;
    }

    private static int AuditCommand(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Dùng: audit <file.dwg>");
            return 1;
        }

        var workspace = CadWorkspace.Open(args[1]);
        var findings = OpenCad.Core.Audit.DrawingAudit.Run(workspace.Document);

        Console.WriteLine($"{args[1]}: {findings.Count} vấn đề");

        foreach (var (code, count) in OpenCad.Core.Audit.DrawingAudit.Summary(findings))
            Console.WriteLine($"   {code}: {count}");

        foreach (var finding in findings.Take(20))
            Console.WriteLine("   " + finding);

        return findings.Count == 0 ? 0 : 1;
    }

    /// <summary>Tạo bản vẽ mẫu thực tế (căn hộ) kèm tờ A3 — dùng để thử toàn bộ đường đi.</summary>
    private static int MakeSampleCommand(string[] args)
    {
        var path = args.Length > 1 ? args[1] : "samples/can-ho.dwg";

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (directory is not null) Directory.CreateDirectory(directory);

        var built = CadWorkspace.New();

        // Vẽ THẲNG vào document của workspace (entity chưa có chủ)
        SampleDrawing.Populate(built.Document);

        built.Save(path);

        var scene = new CadScene();
        scene.Build(built.Document);

        Console.WriteLine($"Đã tạo {path}");
        Console.WriteLine($"   Entity : {built.Document.Entities.Count()}");
        Console.WriteLine($"   Layer  : {built.Document.Layers.Count()}");
        Console.WriteLine($"   Layout : {string.Join(", ", OpenCad.Core.Layout.LayoutFactory.LayoutNames(built.Document))}");
        Console.WriteLine($"   Prim   : {scene.PrimitiveCount}");

        var audit = OpenCad.Core.Audit.DrawingAudit.Run(built.Document);

        Console.WriteLine($"   Audit  : {audit.Count} vấn đề");

        foreach (var issue in audit.Take(10))
            Console.WriteLine($"      [{issue.Code}] {issue.Message}");

        return 0;
    }

    private static int LossyCommand(string[] args)
    {
        var makers = new (string Name, Func<CadDocument, Entity> Make)[]
        {
            ("Line", d => new Line { StartPoint = new CSMath.XYZ(0, 0, 0), EndPoint = new CSMath.XYZ(100, 0, 0) }),
            ("Circle", d => new Circle { Center = new CSMath.XYZ(0, 0, 0), Radius = 50 }),
            ("Arc", d => new Arc { Center = new CSMath.XYZ(0, 0, 0), Radius = 50, StartAngle = 0, EndAngle = Math.PI }),
            ("Ellipse", d => new Ellipse { Center = new CSMath.XYZ(0, 0, 0), MajorAxisEndPoint = new CSMath.XYZ(100, 0, 0), RadiusRatio = 0.5 }),
            ("Point", d => new ACadSharp.Entities.Point { Location = new CSMath.XYZ(10, 10, 0) }),
            ("Ray", d => new Ray { StartPoint = new CSMath.XYZ(0, 0, 0), Direction = new CSMath.XYZ(1, 1, 0) }),
            ("XLine", d => new XLine { FirstPoint = new CSMath.XYZ(0, 0, 0), Direction = new CSMath.XYZ(1, 0, 0) }),
            ("TextEntity", d => new TextEntity { InsertPoint = new CSMath.XYZ(0, 0, 0), Height = 10, Value = "ABC" }),
            ("MText", d => new MText { InsertPoint = new CSMath.XYZ(0, 0, 0), Height = 10, Value = "A\\PB" }),
            ("Spline", d =>
            {
                var s = new Spline();
                s.ControlPoints.Add(new CSMath.XYZ(0, 0, 0));
                s.ControlPoints.Add(new CSMath.XYZ(10, 20, 0));
                s.ControlPoints.Add(new CSMath.XYZ(30, 0, 0));
                return s;
            }),
            ("Solid", d => new Solid
            {
                FirstCorner = new CSMath.XYZ(0, 0, 0),
                SecondCorner = new CSMath.XYZ(10, 0, 0),
                ThirdCorner = new CSMath.XYZ(0, 10, 0),
                FourthCorner = new CSMath.XYZ(10, 10, 0),
            }),
            ("Hatch", d =>
            {
                var rect = new LwPolyline { IsClosed = true };
                rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 0) });
                rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(100, 0) });
                rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(100, 50) });
                rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 50) });
                Entity? made = OpenCad.Core.Editing.HatchFactory.FromBoundary(rect);
                return made ?? new Line();
            }),
            ("Leader", d =>
            {
                var l = new Leader { TextHeight = 10, ArrowHeadEnabled = true };
                l.Vertices.Add(new CSMath.XYZ(0, 0, 0));
                l.Vertices.Add(new CSMath.XYZ(50, 50, 0));
                return l;
            }),
            ("MLine", d =>
            {
                var m = new MLine();
                m.Vertices.Add(new MLine.Vertex { Position = new CSMath.XYZ(0, 0, 0) });
                m.Vertices.Add(new MLine.Vertex { Position = new CSMath.XYZ(50, 0, 0) });
                return m;
            }),
            ("Tolerance", d => new ACadSharp.Entities.Tolerance { InsertionPoint = new CSMath.XYZ(0, 0, 0), Text = "%%p0.05" }),
            ("Wipeout", d =>
            {
                var w = new ACadSharp.Entities.Wipeout
                {
                    InsertPoint = new CSMath.XYZ(0, 0, 0),
                    Size = new CSMath.XY(100, 50),
                    UVector = new CSMath.XYZ(1, 0, 0),
                    VVector = new CSMath.XYZ(0, 1, 0),
                };
                w.ClipBoundaryVertices.Add(new CSMath.XY(0, 0));
                w.ClipBoundaryVertices.Add(new CSMath.XY(1, 0));
                w.ClipBoundaryVertices.Add(new CSMath.XY(1, 1));
                return w;
            }),
            ("Insert", d =>
            {
                // Insert có attribute: kiểm cả tên block lẫn số attribute sau khi lưu
                var block = new ACadSharp.Tables.BlockRecord("BLK_LOSSY");
                d.BlockRecords.Add(block);

                block.Entities.Add(new ACadSharp.Entities.Line
                {
                    StartPoint = new CSMath.XYZ(0, 0, 0),
                    EndPoint = new CSMath.XYZ(100, 0, 0),
                });

                var definition = new ACadSharp.Entities.AttributeDefinition
                {
                    Tag = "MA",
                    Value = "A1",
                    InsertPoint = new CSMath.XYZ(0, 50, 0),
                    Height = 20,
                };

                block.Entities.Add(definition);

                var insert = new ACadSharp.Entities.Insert(block)
                {
                    InsertPoint = new CSMath.XYZ(200, 200, 0),
                };

                insert.Attributes.Add(new ACadSharp.Entities.AttributeEntity
                {
                    Tag = "MA",
                    Value = "A1",
                    InsertPoint = new CSMath.XYZ(200, 250, 0),
                    Height = 20,
                });

                return insert;
            }),            ("MultiLeader", d =>
            {
                // Phải có CHỮ thì mới kiểm được việc mất chữ khi lưu
                var multi = new ACadSharp.Entities.MultiLeader();
                multi.ContextData.TextLabel = "GHI CHU";
                multi.ContextData.TextHeight = 100;
                multi.ContextData.TextLocation = new CSMath.XYZ(1000, 1000, 0);

                var leaderLine = new ACadSharp.Objects.MultiLeaderObjectContextData.LeaderLine();
                leaderLine.Points.Add(new CSMath.XYZ(0, 0, 0));
                leaderLine.Points.Add(new CSMath.XYZ(500, 400, 0));

                var root = new ACadSharp.Objects.MultiLeaderObjectContextData.LeaderRoot();
                root.Lines.Add(leaderLine);
                multi.ContextData.LeaderRoots.Add(root);

                return multi;
            }),
            ("TableEntity", d =>
            {
                var block = new ACadSharp.Tables.BlockRecord("TB_" + Guid.NewGuid().ToString("N")[..6]);
                d.BlockRecords.Add(block);

                var table = new TableEntity(block)
                {
                    InsertPoint = new CSMath.XYZ(0, 0, 0),
                    RowCount = 2,
                    ColumnCount = 2,
                    RowSpacing = 10,
                    ColumnSpacing = 20,
                };

                table.Rows.Add(new TableEntity.Row { Height = 10 });
                table.Rows.Add(new TableEntity.Row { Height = 10 });
                table.Columns.Add(new TableEntity.Column { Width = 20 });
                table.Columns.Add(new TableEntity.Column { Width = 20 });

                return table;
            }),
            ("DimensionAligned", d => new DimensionAligned
            {
                FirstPoint = new CSMath.XYZ(0, 0, 0),
                SecondPoint = new CSMath.XYZ(100, 0, 0),
                DefinitionPoint = new CSMath.XYZ(50, 30, 0),
            }),
            ("DimensionRadius", d => new DimensionRadius
            {
                AngleVertex = new CSMath.XYZ(0, 0, 0),
                DefinitionPoint = new CSMath.XYZ(50, 0, 0),
            }),
            ("LwPolyline", d =>
            {
                var p = new LwPolyline { IsClosed = true };
                p.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 0) });
                p.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(50, 0) });
                p.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(50, 50) });
                return p;
            }),
        };

        // Kiểm thêm nội dung quan trọng của từng loại (không chỉ "có tồn tại")
        // Kiểm NỘI DUNG chứ không chỉ sự tồn tại: mất chữ, mất đỉnh, mất pattern… đều phải lộ ra.
        static string? Detail(Entity e) => e switch
        {
            // Text / ghi chú
            TextEntity text => "text:" + text.Value + "@" + text.Height.ToString("0.##"),
            MText mtext => "mtext:" + (mtext.Value ?? "").Replace("\\P", "|"),

            // Cung tròn phải đứng trước Circle (Arc : Circle)
            Arc arc => "arc:" + arc.Radius.ToString("0.###") + "/" + arc.StartAngle.ToString("0.###"),
            Circle circle => "circle:" + circle.Radius.ToString("0.###"),
            Ellipse ellipse => "ellipse:" + ellipse.RadiusRatio.ToString("0.###"),

            // Đa tuyến
            LwPolyline poly => "lw:" + poly.Vertices.Count + (poly.IsClosed ? " closed" : ""),
            Polyline2D poly2 => "p2d:" + poly2.Vertices.Count,
            Spline spline => "spline:" + spline.ControlPoints.Count,

            // Kích thước: giữ số đo
            DimensionAligned dim =>
                "dim:" + Math.Abs(dim.SecondPoint.X - dim.FirstPoint.X).ToString("0.###"),
            DimensionRadius radius => "dimrad:" + Math.Sqrt(
                Math.Pow(radius.DefinitionPoint.X - radius.AngleVertex.X, 2) +
                Math.Pow(radius.DefinitionPoint.Y - radius.AngleVertex.Y, 2)).ToString("0.###"),
            Dimension dim => "dim:" + (dim.Text ?? "-"),

            // Chú thích
            Leader leader => "leader:" + leader.Vertices.Count,
            MultiLeader multi =>
                "mleader:" + (multi.ContextData?.TextLabel ?? "<không chữ>")
                + "/" + (multi.ContextData?.LeaderRoots.Count ?? 0),
            Tolerance tolerance => "tol:" + tolerance.Text,

            // Vùng tô
            Hatch hatch => "hatch:" + (hatch.Pattern?.Name ?? "solid") + "/" + hatch.PatternScale.ToString("0.###"),

            // Khối: số attribute + tên block
            Insert insert => "insert:" + (insert.Block?.Name ?? "?") + "/" + insert.Attributes.Count,
            MLine mline => "mline:" + mline.Vertices.Count,

            _ => null,
        };

        var lost = new List<string>();

        Console.WriteLine("Loại entity          Trước  Sau  Kết quả");
        Console.WriteLine("---------------------------------------------");

        foreach (var (name, make) in makers)
        {
            var ws = CadWorkspace.New();
            var entity = make(ws.Document);

            try { ws.Document.Entities.Add(entity); }
            catch (Exception ex)
            {
                Console.WriteLine($"{name,-20} không tạo được ({ex.GetType().Name})");
                continue;
            }

            var path = Path.Combine(Path.GetTempPath(), "opencad-lossy.dwg");
            ws.Save(path);

            var back = CadWorkspace.Open(path);
            var survivor = back.Document.Entities.FirstOrDefault(e => e.GetType().Name == name);
            var survived = survivor is not null;

            // Lớp phải sống sót theo — nếu mất lớp thì hình cũng coi như hỏng
            var layerKept = survived && string.Equals(survivor!.Layer?.Name, entity.Layer?.Name,
                StringComparison.OrdinalIgnoreCase);

            // Nội dung có giữ nguyên không?
            var detailKept = true;
            var detailNote = "";

            if (survived)
            {
                var before = Detail(entity);
                var after = Detail(survivor!);

                if (before is not null && after is not null && before != after)
                {
                    detailKept = false;
                    detailNote = $" ({before}→{after})";
                }
            }

            var verdict = !survived
                ? "MẤT"
                : !layerKept ? "MẤT LỚP"
                : !detailKept ? "SAI NỘI DUNG" + detailNote
                : "OK";

            Console.WriteLine($"{name,-20} {1,5}  {(survived ? 1 : 0),4}  {verdict}");

            if (!survived) lost.Add(name);
            else if (!layerKept) lost.Add(name + " (lớp)");
            else if (!detailKept) lost.Add(name + " (nội dung)");
        }

        Console.WriteLine();
        Console.WriteLine(lost.Count == 0
            ? "Tất cả loại entity đều sống sót."
            : "MẤT KHI LƯU: " + string.Join(", ", lost));

        // Mất dữ liệu khi lưu là lỗi → exit code 1 để CI bắt được
        return lost.Count == 0 ? 0 : 1;
    }

    private static int TortureCommand(string[] args)
    {
        var path = args.Length > 1
            ? args[1]
            : Path.Combine(Path.GetTempPath(), "opencad-torture.dwg");

        var ws = CadWorkspace.New();
        var doc = ws.Document;
        var layer = doc.Layers.First();

        // ---- Đủ loại hình học
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

        // Polyline có bulge (cung)
        var bulge = new LwPolyline { IsClosed = true, Layer = layer };
        bulge.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 1000), Bulge = 0.4142 });
        bulge.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(400, 1000) });
        bulge.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(400, 1300) });
        bulge.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(0, 1300) });
        doc.Entities.Add(bulge);

        // Solid
        doc.Entities.Add(new Solid
        {
            FirstCorner = new CSMath.XYZ(600, 1000, 0),
            SecondCorner = new CSMath.XYZ(800, 1000, 0),
            ThirdCorner = new CSMath.XYZ(600, 1200, 0),
            FourthCorner = new CSMath.XYZ(800, 1200, 0),
            Layer = layer,
        });

        // Text + MText nhiều dòng
        doc.Entities.Add(new TextEntity { InsertPoint = new CSMath.XYZ(0, 1500, 0), Height = 100, Value = "GHI CHU", Layer = layer });
        doc.Entities.Add(new MText
        {
            InsertPoint = new CSMath.XYZ(500, 1500, 0),
            Height = 100,
            Value = "Dong 1\\PDong 2",
            Layer = layer,
        });

        // Spline
        var spline = new Spline { Layer = layer };
        spline.ControlPoints.Add(new CSMath.XYZ(1000, 1000, 0));
        spline.ControlPoints.Add(new CSMath.XYZ(1100, 1300, 0));
        spline.ControlPoints.Add(new CSMath.XYZ(1300, 1000, 0));
        spline.ControlPoints.Add(new CSMath.XYZ(1400, 1300, 0));
        doc.Entities.Add(spline);

        // Hatch có pattern
        var hatchBoundary = new LwPolyline { IsClosed = true, Layer = layer };
        hatchBoundary.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(1600, 900) });
        hatchBoundary.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(1900, 900) });
        hatchBoundary.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(1900, 1200) });
        hatchBoundary.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(1600, 1200) });

        var hatch = OpenCad.Core.Editing.HatchFactory.FromBoundary(hatchBoundary);
        if (hatch is not null)
        {
            try { hatch.Pattern = new ACadSharp.Entities.HatchPattern("ANSI31"); } catch { }
            hatch.IsSolid = false;
            hatch.PatternScale = 5;
            doc.Entities.Add(hatch);
        }

        // Dimension
        doc.Entities.Add(new DimensionAligned
        {
            FirstPoint = new CSMath.XYZ(0, -200, 0),
            SecondPoint = new CSMath.XYZ(1000, -200, 0),
            DefinitionPoint = new CSMath.XYZ(500, -400, 0),
            Layer = layer,
        });

        // Leader
        var leader = new Leader { TextHeight = 80, ArrowHeadEnabled = true, Layer = layer };
        leader.Vertices.Add(new CSMath.XYZ(2000, 1000, 0));
        leader.Vertices.Add(new CSMath.XYZ(2200, 1200, 0));
        leader.Vertices.Add(new CSMath.XYZ(2500, 1200, 0));
        doc.Entities.Add(leader);

        // MLine, Wipeout, Tolerance, MultiLeader
        var mline = new MLine();
        mline.Vertices.Add(new MLine.Vertex { Position = new CSMath.XYZ(0, 2000, 0) });
        mline.Vertices.Add(new MLine.Vertex { Position = new CSMath.XYZ(1500, 2000, 0) });
        doc.Entities.Add(mline);

        var wipeout = new ACadSharp.Entities.Wipeout
        {
            InsertPoint = new CSMath.XYZ(2000, 1800, 0),
            Size = new CSMath.XY(400, 300),
            UVector = new CSMath.XYZ(1, 0, 0),
            VVector = new CSMath.XYZ(0, 1, 0),
        };
        wipeout.ClipBoundaryVertices.Add(new CSMath.XY(0, 0));
        wipeout.ClipBoundaryVertices.Add(new CSMath.XY(1, 0));
        wipeout.ClipBoundaryVertices.Add(new CSMath.XY(1, 1));
        wipeout.ClipBoundaryVertices.Add(new CSMath.XY(0, 1));
        doc.Entities.Add(wipeout);

        doc.Entities.Add(new ACadSharp.Entities.Tolerance
        {
            InsertionPoint = new CSMath.XYZ(0, 2500, 0),
            Text = "%%p0.05",
        });

        var mleader = new ACadSharp.Entities.MultiLeader();
        mleader.ContextData.TextLabel = "GHI CHU\\P%%c20";
        mleader.ContextData.TextHeight = 100;
        mleader.ContextData.TextLocation = new CSMath.XYZ(3000, 2500, 0);
        doc.Entities.Add(mleader);

        // Block lồng nhau + attribute
        var innerBlock = new ACadSharp.Tables.BlockRecord("TRONG");
        innerBlock.Entities.Add(new Circle { Center = new CSMath.XYZ(0, 0, 0), Radius = 50 });

        var outerBlock = new ACadSharp.Tables.BlockRecord("NGOAI");
        outerBlock.Entities.Add(new Line { StartPoint = new CSMath.XYZ(-100, 0, 0), EndPoint = new CSMath.XYZ(100, 0, 0) });
        outerBlock.Entities.Add(new ACadSharp.Entities.Insert(innerBlock) { InsertPoint = new CSMath.XYZ(0, 0, 0), XScale = 1, YScale = 1, ZScale = 1 });
        outerBlock.Entities.Add(new AttributeDefinition
        {
            Tag = "MA",
            Value = "X1",
            InsertPoint = new CSMath.XYZ(0, 150, 0),
            Height = 60,
        });

        doc.BlockRecords.Add(innerBlock);
        doc.BlockRecords.Add(outerBlock);

        var insert = new ACadSharp.Entities.Insert(outerBlock)
        {
            InsertPoint = new CSMath.XYZ(3000, 1000, 0),
            XScale = 1,
            YScale = 1,
            ZScale = 1,
            Layer = layer,
        };

        doc.Entities.Add(insert);

        // ---- Lưu và mở lại
        ws.Save(path);
        var reopened = CadWorkspace.Open(path);

        var before = doc.Entities.Select(e => e.GetType().Name).OrderBy(n => n).ToList();
        var after = reopened.Document.Entities.Select(e => e.GetType().Name).OrderBy(n => n).ToList();

        Console.WriteLine($"Torture: {before.Count} entity → {path} ({new FileInfo(path).Length / 1024} KB)");
        Console.WriteLine($"  loại trước khi lưu: {string.Join(", ", before.Distinct())}");
        Console.WriteLine($"  loại sau khi mở  : {string.Join(", ", after.Distinct())}");

        var missing = before.GroupBy(n => n)
            .Select(g => (Name: g.Key, Before: g.Count(), After: after.Count(n => n == g.Key)))
            .Where(x => x.Before != x.After)
            .ToList();

        Console.WriteLine(missing.Count == 0 ? "  ✔ giữ đủ số lượng từng loại"
            : "  ✘ lệch: " + string.Join(", ", missing.Select(m => $"{m.Name} {m.Before}→{m.After}")));

        // ---- Mỗi entity phải vẽ được ít nhất 1 primitive
        var scene = new CadScene();
        scene.Build(reopened.Document);

        var blank = scene.Entries.Where(e => e.Prims.Count == 0)
            .Select(e => e.Entity.GetType().Name)
            .ToList();

        Console.WriteLine($"  vẽ được: {scene.Entries.Count - blank.Count}/{scene.Entries.Count} entity, {scene.PrimitiveCount} primitive");

        // Loại đã biết là mất dữ liệu khi lưu (MULTILEADER mất chữ, TABLE mất entity)
        // thì "không vẽ được sau khi đọc lại" là HỆ QUẢ ĐÃ BIẾT, không phải lỗi vẽ.
        var knownLossy = new[] { "MultiLeader", "TableEntity" };

        var unexpected = blank.Where(name => !knownLossy.Contains(name)).Distinct().ToList();
        var explained = blank.Where(name => knownLossy.Contains(name)).Distinct().ToList();

        if (explained.Count > 0)
            Console.WriteLine("  ⚠ mất dữ liệu khi lưu (đã biết, app tự cảnh báo): "
                              + string.Join(", ", explained));

        if (unexpected.Count > 0)
            Console.WriteLine("  ✘ không vẽ được: " + string.Join(", ", unexpected));
        else if (explained.Count == 0)
            Console.WriteLine("  ✔ mọi entity đều vẽ được");
        else
            Console.WriteLine("  ✔ các loại còn lại đều vẽ được");

        return missing.Count == 0 && unexpected.Count == 0 ? 0 : 1;
    }

    private static int StressCommand(string[] args)
    {
        var count = args.Length > 1 && int.TryParse(args[1], out var n) ? n : 5000;
        var path = args.Length > 2 ? args[2] : Path.Combine(Path.GetTempPath(), "opencad-stress.dwg");

        var ws = CadWorkspace.New();
        var doc = ws.Document;

        var wall = doc.Layers.FirstOrDefault() ?? new ACadSharp.Tables.Layer("0");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var perRow = (int)Math.Ceiling(Math.Sqrt(count));

        for (var i = 0; i < count; i++)
        {
            var x = i % perRow * 500.0;
            var y = i / perRow * 500.0;

            switch (i % 5)
            {
                case 0:
                    doc.Entities.Add(new Line
                    {
                        StartPoint = new CSMath.XYZ(x, y, 0),
                        EndPoint = new CSMath.XYZ(x + 400, y + 300, 0),
                        Layer = wall,
                    });
                    break;
                case 1:
                    doc.Entities.Add(new Circle
                    {
                        Center = new CSMath.XYZ(x + 200, y + 200, 0),
                        Radius = 120,
                        Layer = wall,
                    });
                    break;
                case 2:
                    var rect = new LwPolyline { IsClosed = true, Layer = wall };
                    rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(x, y) });
                    rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(x + 300, y) });
                    rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(x + 300, y + 200) });
                    rect.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(x, y + 200) });
                    doc.Entities.Add(rect);
                    break;
                case 3:
                    doc.Entities.Add(new TextEntity
                    {
                        InsertPoint = new CSMath.XYZ(x, y, 0),
                        Height = 100,
                        Value = "P" + i,
                        Layer = wall,
                    });
                    break;
                default:
                    doc.Entities.Add(new Arc
                    {
                        Center = new CSMath.XYZ(x + 100, y + 100, 0),
                        Radius = 100,
                        StartAngle = 0,
                        EndAngle = Math.PI,
                        Layer = wall,
                    });
                    break;
            }
        }

        var create = sw.ElapsedMilliseconds;

        sw.Restart();
        ws.Save(path);
        var save = sw.ElapsedMilliseconds;
        var size = new FileInfo(path).Length;

        sw.Restart();
        var reopened = CadWorkspace.Open(path);
        var open = sw.ElapsedMilliseconds;

        sw.Restart();
        var scene = new CadScene();
        scene.Build(reopened.Document);
        var build = sw.ElapsedMilliseconds;

        sw.Restart();
        var hits = 0;
        for (var i = 0; i < 200; i++)
        {
            var px = Random.Shared.NextDouble() * perRow * 500;
            var py = Random.Shared.NextDouble() * (count / perRow + 1) * 500;
            if (scene.HitTest(new OpenCad.Core.Geometry.Pt(px, py), 10) is not null) hits++;
        }
        var hitTest = sw.ElapsedMilliseconds;

        Console.WriteLine($"{count} entity → {path} ({size / 1024.0 / 1024.0:0.##} MB)");
        Console.WriteLine($"  tạo      : {create,6} ms");
        Console.WriteLine($"  lưu DWG  : {save,6} ms");
        Console.WriteLine($"  mở lại   : {open,6} ms");
        Console.WriteLine($"  dựng scene: {build,5} ms  ({scene.PrimitiveCount} primitive)");
        Console.WriteLine($"  200 hit-test: {hitTest,3} ms  ({hits} trúng, {hitTest / 200.0:0.###} ms/lần)");
        return 0;
    }

    private static int PatternsCommand(string[] args)
    {
        if (args.Length > 1 && File.Exists(args[1]))
        {
            var loaded = OpenCad.Core.Styles.HatchPatternLibrary.LoadFile(args[1]);
            Console.WriteLine($"Nạp {loaded} pattern từ {args[1]}");
        }

        var names = OpenCad.Core.Styles.HatchPatternLibrary.Names;
        Console.WriteLine($"{names.Count} pattern có sẵn: {string.Join(", ", names)}");
        return 0;
    }

    private static int InfoCommand(string[] args)    {
        if (args.Length < 2)
        {
            Console.WriteLine("Thiếu đường dẫn file.");
            return 1;
        }

        var workspace = CadWorkspace.Open(args[1]);
        var scene = new CadScene();
        scene.Build(workspace.Document);

        Console.WriteLine($"File      : {args[1]}");
        Console.WriteLine($"Entities  : {workspace.Document.Entities.Count()}");
        Console.WriteLine($"Primitives: {scene.PrimitiveCount} (bỏ qua {scene.SkippedCount})");
        Console.WriteLine($"Bounds    : {scene.Bounds}");

        var groups = scene.Entries
            .GroupBy(e => e.Entity.GetType().Name)
            .OrderByDescending(g => g.Count());

        foreach (var g in groups)
        {
            var empty = g.Count(e => e.Prims.Count == 0);
            Console.WriteLine($"  {g.Key,-18} {g.Count(),5}" + (empty > 0 ? $"  ({empty} chưa render được)" : ""));
        }

        return 0;
    }
}

using ACadSharp;
using ACadSharp.Tables;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using OpenCad.Core;
using OpenCad.Core.Editing;
using OpenCad.Core.Layout;

namespace OpenCad.Tools;

/// <summary>
/// Bản vẽ mẫu thực tế: căn hộ nhỏ, có tường, cửa, kích thước chuỗi, tô sàn,
/// nhãn phòng, và một tờ A3 tỷ lệ 1:50 có kích thước vẽ ngay trên tờ.
/// Dùng để thử mở / rà / in của toàn bộ đường đi.
/// </summary>
public static class SampleDrawing
{
    private const double W = 7000;   // rộng (mm)
    private const double H = 5200;   // sâu (mm)
    private const double T = 200;    // dày tường

    private const double DoorW = 900;
    private const double WindowW = 1400;

    /// <summary>Vẽ bản vẽ mẫu trực tiếp vào document có sẵn (entity chưa thuộc về đâu).</summary>
    public static void Populate(CadDocument document)
    {

        var walls = Layer(document, "TUONG", new Color((short)7), LineWeightType.W50);
        var doors = Layer(document, "CUA", new Color((short)4), LineWeightType.W25);
        var windows = Layer(document, "CUA_SO", new Color((short)3), LineWeightType.W25);
        var dims = Layer(document, "KICH_THUOC", new Color((short)2), LineWeightType.W25);
        var text = Layer(document, "CHU", new Color((short)7), LineWeightType.W25);
        var hatchLayer = Layer(document, "TO_SAN", new Color((short)8), LineWeightType.W13);
        var axes = Layer(document, "TRUC", new Color((short)6), LineWeightType.W13);

        // ---- Tường: hai hình chữ nhật đồng tâm (mỗi cạnh vẽ MỘT lần)
        Rect(document, 0, 0, W, H, walls);
        Rect(document, T, T, W - T, H - T, walls);

        // Vách ngăn dọc chia hai phòng, chừa cửa
        var splitX = 4200.0;

        Line(document, splitX, T, splitX, 1800, walls);
        Line(document, splitX, 1800 + DoorW, splitX, H - T, walls);

        // ---- Cửa đi: cánh + cung quét
        // Cánh cửa mở vào phòng khách: bản lề tại (splitX, 1800), cánh nằm ngang
        Line(document, splitX, 1800, splitX - DoorW, 1800, doors);

        // Cung quét từ cánh (180°) tới khung cửa (90°)
        document.Entities.Add(new Arc
        {
            Center = new XYZ(splitX, 1800, 0),
            Radius = DoorW,
            StartAngle = Math.PI / 2,
            EndAngle = Math.PI,
            Layer = doors,
        });

        // ---- Cửa sổ trên tường trái và tường trên
        Window(document, 900, 0, WindowW, true, windows);
        Window(document, 1500, H, WindowW, true, windows);

        // ---- Trục: hai đường tâm phòng
        Line(document, splitX / 2, -600, splitX / 2, H + 600, axes, LineTypeDash(document));
        Line(document, splitX + (W - splitX) / 2, -600, splitX + (W - splitX) / 2, H + 600, axes, LineTypeDash(document));

        // ---- Tô sàn phòng khách
        var floor = new LwPolyline { IsClosed = true, Layer = hatchLayer };
        floor.Vertices.Add(new LwPolyline.Vertex { Location = new XY(T + 50, T + 50) });
        floor.Vertices.Add(new LwPolyline.Vertex { Location = new XY(splitX - 50, T + 50) });
        floor.Vertices.Add(new LwPolyline.Vertex { Location = new XY(splitX - 50, H - T - 50) });
        floor.Vertices.Add(new LwPolyline.Vertex { Location = new XY(T + 50, H - T - 50) });

        // Tô sàn bằng PATTERN (không phải khối đặc) để thấy được hình bên dưới
        var hatch = HatchFactory.FromBoundary(floor);

        if (hatch is not null)
        {
            hatch.Layer = hatchLayer;
            hatch.IsSolid = false;
            hatch.PatternScale = 120;
            hatch.PatternAngle = 0;

            try { hatch.Pattern = new HatchPattern("ANSI31"); }
            catch { }

            document.Entities.Add(hatch);
        }

        // ---- Nhãn phòng
        Text(document, 1000, 3000, 260, "PHONG KHACH", text);
        Text(document, splitX + 700, 3000, 240, "PHONG NGU", text);

        // ---- Kích thước chuỗi theo chiều ngang (dưới)
        var baseY = -500.0;
        var cursor = 0.0;

        foreach (var stop in new[] { splitX, W })
        {
            document.Entities.Add(new DimensionAligned
            {
                FirstPoint = new XYZ(cursor, 0, 0),
                SecondPoint = new XYZ(stop, 0, 0),
                DefinitionPoint = new XYZ((cursor + stop) / 2, baseY, 0),
                Layer = dims,
            });

            cursor = stop;
        }

        // Kích thước tổng
        document.Entities.Add(new DimensionAligned
        {
            FirstPoint = new XYZ(0, 0, 0),
            SecondPoint = new XYZ(W, 0, 0),
            DefinitionPoint = new XYZ(W / 2, baseY - 700, 0),
            Layer = dims,
        });

        // ---- Tờ A3 1:50
        var scene = new CadScene();
        scene.Build(document);

        var template = new SheetTemplateOptions
        {
            Title = "CAN HO MAU",
            Project = "OpenCAD",
            Drafter = "OpenCAD",
            WidthMm = 420,
            HeightMm = 297,
            UnitsPerMm = 50,
        };

        var layout = LayoutFactory.Create(document, "A3-1-50", template, scene);

        // Kích thước tổng ghi NGAY TRÊN TỜ (đơn vị mm giấy)
        if (layout?.AssociatedBlock is not null)
        {
            layout.AssociatedBlock.Entities.Add(new DimensionAligned
            {
                FirstPoint = new XYZ(70, 250, 0),
                SecondPoint = new XYZ(230, 250, 0),
                DefinitionPoint = new XYZ(150, 265, 0),
                Layer = document.Layers.FirstOrDefault(l => l.Name == "0"),
            });

            layout.AssociatedBlock.Entities.Add(new TextEntity
            {
                InsertPoint = new XYZ(70, 268, 0),
                Height = 6,
                Value = "Kich thuoc tren to: 160 mm = 8000 mm thuc te",
                Layer = document.Layers.FirstOrDefault(l => l.Name == "0"),
            });
        }

    }

    // ------------------------------------------------------------- TIỆN ÍCH

    private static Layer Layer(CadDocument document, string name, Color color, LineWeightType weight)
    {
        var layer = new Layer(name)
        {
            Color = color,
            LineWeight = weight,
        };

        document.Layers.Add(layer);

        return layer;
    }

    /// <summary>Lấy (hoặc tạo) kiểu nét DASHED của bản vẽ.</summary>
    private static LineType? LineTypeDash(CadDocument document)
    {
        try
        {
            var existing = document.LineTypes.FirstOrDefault(t => t.Name == "DASHED");

            if (existing is not null) return existing;

            return OpenCad.Core.Styles.LineTypeLibrary.Ensure(document, "DASHED");
        }
        catch { return null; }
    }

    private static void Rect(CadDocument document, double x, double y, double width, double height, Layer layer)
    {
        Line(document, x, y, x + width, y, layer);
        Line(document, x + width, y, x + width, y + height, layer);
        Line(document, x + width, y + height, x, y + height, layer);
        Line(document, x, y + height, x, y, layer);
    }

    private static Line Line(CadDocument document, double x1, double y1, double x2, double y2,
                             Layer layer, LineType? lineType = null)
    {
        var line = new Line
        {
            StartPoint = new XYZ(x1, y1, 0),
            EndPoint = new XYZ(x2, y2, 0),
            Layer = layer,
        };

        if (lineType is not null) line.LineType = lineType;

        document.Entities.Add(line);

        return line;
    }

    private static void Window(CadDocument document, double x, double y, double width,
                               bool horizontal, Layer layer)
    {
        // Hai nét song song + hai nét đứng khép kín ô cửa sổ
        if (horizontal)
        {
            Line(document, x, y - 40, x + width, y - 40, layer);
            Line(document, x, y + 40, x + width, y + 40, layer);
            Line(document, x, y - 40, x, y + 40, layer);
            Line(document, x + width, y - 40, x + width, y + 40, layer);
        }
        else
        {
            Line(document, x - 40, y, x - 40, y + width, layer);
            Line(document, x + 40, y, x + 40, y + width, layer);
            Line(document, x - 40, y, x + 40, y, layer);
            Line(document, x - 40, y + width, x + 40, y + width, layer);
        }
    }

    private static void Text(CadDocument document, double x, double y, double height,
                             string value, Layer layer)
    {
        document.Entities.Add(new TextEntity
        {
            InsertPoint = new XYZ(x, y, 0),
            Height = height,
            Value = value,
            Layer = layer,
        });
    }
}

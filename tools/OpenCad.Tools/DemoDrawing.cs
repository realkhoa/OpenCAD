using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;

namespace OpenCad.Tools;

/// <summary>Bản vẽ demo để kiểm tra render và round-trip.</summary>
public static class DemoDrawing
{
    public static CadDocument Build()
    {
        var doc = new CadDocument(ACadVersion.AC1032);

        // ---- Định nghĩa line type để kiểm chứng render nét đứt
        var dashed = new LineType("DASHED") { Description = "Dashed __ __ __" };
        dashed.AddSegment(new LineType.Segment { Length = 300 });
        dashed.AddSegment(new LineType.Segment { Length = -150 });
        doc.LineTypes.Add(dashed);

        var center = new LineType("CENTER") { Description = "Center ____ _ ____" };
        center.AddSegment(new LineType.Segment { Length = 900 });
        center.AddSegment(new LineType.Segment { Length = -120 });
        center.AddSegment(new LineType.Segment { Length = 120 });
        center.AddSegment(new LineType.Segment { Length = -120 });
        doc.LineTypes.Add(center);

        var walls = AddLayer(doc, "WALLS", new Color((short)7), LineWeightType.W50);
        var openings = AddLayer(doc, "OPENINGS", Color.Cyan, LineWeightType.W25);
        var notes = AddLayer(doc, "NOTES", Color.Yellow, LineWeightType.W18);
        var furniture = AddLayer(doc, "FURNITURE", Color.Green, LineWeightType.W25);
        var dims = AddLayer(doc, "DIM", Color.Red, LineWeightType.W13);
        var hidden = AddLayer(doc, "HIDDEN", Color.Blue, LineWeightType.W18);
        var axis = AddLayer(doc, "AXIS", new Color((short)6), LineWeightType.W13);

        hidden.LineType = dashed;
        axis.LineType = center;

        // ---- Tường ngoài 8000 x 6000, tường dày 200
        AddRect(doc, walls, 0, 0, 8000, 6000);
        AddRect(doc, walls, 200, 200, 7800, 5800);

        // Vách ngăn dọc chia 2 phòng
        AddLine(doc, walls, 5000, 200, 5000, 3200);
        AddLine(doc, walls, 5200, 200, 5200, 3200);

        // ---- Cửa đi (khoảng trống + cung quét cửa)
        AddLine(doc, walls, 5000, 3600, 5000, 5800);
        AddLine(doc, walls, 5200, 3600, 5200, 5800);

        doc.Entities.Add(new Arc
        {
            Center = new XYZ(5000, 3600, 0),
            Radius = 1000,
            StartAngle = 0,
            EndAngle = Math.PI / 2,
            Layer = openings,
        });

        // ---- Cửa sổ (polyline khép kín)
        var window = new LwPolyline { Layer = openings, IsClosed = true };
        window.Vertices.Add(new LwPolyline.Vertex { Location = new XY(1500, -80) });
        window.Vertices.Add(new LwPolyline.Vertex { Location = new XY(3000, -80) });
        window.Vertices.Add(new LwPolyline.Vertex { Location = new XY(3000, 80) });
        window.Vertices.Add(new LwPolyline.Vertex { Location = new XY(1500, 80) });
        doc.Entities.Add(window);

        // ---- Bàn tròn + ghế
        doc.Entities.Add(new Circle
        {
            Center = new XYZ(2600, 3600, 0),
            Radius = 800,
            Layer = furniture,
        });
        doc.Entities.Add(new Circle
        {
            Center = new XYZ(2600, 3600, 0),
            Radius = 650,
            Layer = furniture,
        });

        // ---- Polyline bo góc, có bulge (cung tròn) để test bulge rendering
        var counter = new LwPolyline { Layer = furniture, IsClosed = true };
        counter.Vertices.Add(new LwPolyline.Vertex { Location = new XY(6000, 4200), Bulge = 1.0 });
        counter.Vertices.Add(new LwPolyline.Vertex { Location = new XY(7200, 4200) });
        counter.Vertices.Add(new LwPolyline.Vertex { Location = new XY(7200, 5200), Bulge = 1.0 });
        counter.Vertices.Add(new LwPolyline.Vertex { Location = new XY(6000, 5200) });
        doc.Entities.Add(counter);

        // ---- Chữ
        AddText(doc, notes, "PHÒNG KHÁCH", 1200, 4800, 320);
        AddText(doc, notes, "PHÒNG NGỦ", 5600, 4800, 320);
        AddText(doc, notes, "OpenCAD — bản vẽ demo", 200, 6300, 300);
        AddText(doc, notes, "Tỷ lệ 1:100", 200, 5950, 200);

        // ---- Điểm và solid
        doc.Entities.Add(new Point { Location = new XYZ(0, 0, 0), Layer = dims });        doc.Entities.Add(new Solid
        {
            FirstCorner = new XYZ(7300, 300, 0),
            SecondCorner = new XYZ(7600, 300, 0),
            ThirdCorner = new XYZ(7300, 600, 0),
            FourthCorner = new XYZ(7600, 600, 0),
            Layer = furniture,
        });

        // ---- Spline
        var spline = new Spline { Layer = notes };
        spline.ControlPoints.Add(new XYZ(5400, 900, 0));
        spline.ControlPoints.Add(new XYZ(6000, 2200, 0));
        spline.ControlPoints.Add(new XYZ(6800, 900, 0));
        spline.ControlPoints.Add(new XYZ(7400, 2200, 0));
        spline.Knots.AddRange(new[] { 0.0, 0, 0, 0, 1, 1, 1, 1 });
        spline.Degree = 3;
        doc.Entities.Add(spline);

        // ---- Nét đứt: dầm chìm phía trên mặt bằng
        AddLine(doc, hidden, 400, 3000, 4700, 3000);
        AddRect(doc, hidden, 4300, 4600, 4700, 5400);

        // ---- Đường tâm của bàn tròn
        AddLine(doc, axis, 1100, 3600, 4100, 3600);
        AddLine(doc, axis, 2600, 2100, 2600, 5100);

        // ---- Hatch: vùng tô đặc và vùng tô có pattern
        AddHatch(doc, furniture, 6100, 400, 7600, 1500, solid: true, angle: 0, scale: 1);
        AddHatch(doc, hidden, 600, 600, 1600, 1600, solid: false, angle: 0, scale: 40, patternName: "CONCRETE");

        // ---- Style kích thước thứ hai để thử lệnh DIMSTYLE
        try
        {
            doc.DimensionStyles.Add(new DimensionStyle("IN-DAM")
            {
                TextHeight = 40,
                ArrowSize = 60,
            });

            // Style ghi theo mét: vẽ bằng mm nhưng hiện "8 m"
            var metricStyle = new DimensionStyle("MET")
            {
                TextHeight = 200,
                ArrowSize = 150,
                DecimalPlaces = 3,
                LinearScaleFactor = 0.001,
            };

            doc.DimensionStyles.Add(metricStyle);
            metricStyle.Suffix = " m";
        }
        catch { }

        // ---- Block: cửa đi và ghế, rồi đặt vài tham chiếu
        var doorBlock = new BlockRecord("CUA");
        doorBlock.Entities.Add(new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(0, 900, 0),
            Layer = openings,
        });
        doorBlock.Entities.Add(new Arc
        {
            Center = new XYZ(0, 0, 0),
            Radius = 900,
            StartAngle = 0,
            EndAngle = Math.PI / 2,
            Layer = openings,
        });
        doc.BlockRecords.Add(doorBlock);

        var chairBlock = new BlockRecord("GHE");
        var chair = new LwPolyline { IsClosed = true, Layer = furniture };
        chair.Vertices.Add(new LwPolyline.Vertex { Location = new XY(-200, -200) });
        chair.Vertices.Add(new LwPolyline.Vertex { Location = new XY(200, -200) });
        chair.Vertices.Add(new LwPolyline.Vertex { Location = new XY(200, 200) });
        chair.Vertices.Add(new LwPolyline.Vertex { Location = new XY(-200, 200) });
        chairBlock.Entities.Add(chair);
        doc.BlockRecords.Add(chairBlock);

        // Attribute: số hiệu cửa hiển thị kèm block
        doorBlock.Entities.Add(new AttributeDefinition
        {
            Tag = "SO_CUA",
            Value = "C01",
            InsertPoint = new XYZ(0, 1100, 0),
            Height = 250,
            Layer = openings,
        });

        var doorInsert = new Insert(doorBlock)
        {
            InsertPoint = new XYZ(5000, 3600, 0),
            XScale = 1,
            YScale = 1,
            ZScale = 1,
            Layer = openings,
        };

        // ACadSharp sinh sẵn attribute từ ATTDEF — chỉ cần đặt lại vị trí WCS
        if (doorInsert.Attributes is { Count: > 0 })
        {
            var attribute = doorInsert.Attributes.First();
            attribute.InsertPoint = new XYZ(5100, 4700, 0);
            attribute.Value = "C01";
            attribute.Height = 300;
        }
        else
        {
            doorInsert.Attributes.Add(new AttributeEntity
            {
                Tag = "SO_CUA",
                Value = "C01",
                InsertPoint = new XYZ(5100, 4700, 0),
                Height = 300,
                Layer = openings,
            });
        }

        doc.Entities.Add(doorInsert);

        doc.Entities.Add(new Insert(chairBlock)
        {
            InsertPoint = new XYZ(2600, 4300, 0),
            XScale = 1,
            YScale = 1,
            ZScale = 1,
            Layer = furniture,
        });

        // ---- Leader: mũi tên ghi chú chỉ vào bàn
        var leader = new Leader
        {
            TextHeight = 200,
            ArrowHeadEnabled = true,
            Layer = notes,
        };

        leader.Vertices.Add(new XYZ(2600, 3600, 0));
        leader.Vertices.Add(new XYZ(3400, 4300, 0));
        leader.Vertices.Add(new XYZ(4300, 4300, 0));

        doc.Entities.Add(leader);

        // ---- Kích thước chiều ngang nhà
        var dimension = new DimensionAligned
        {
            FirstPoint = new XYZ(0, 0, 0),
            SecondPoint = new XYZ(8000, 0, 0),
            DefinitionPoint = new XYZ(4000, -900, 0),
            Layer = dims,
        };

        try
        {
            var style = doc.DimensionStyles.FirstOrDefault();
            if (style is not null) dimension.Style = style;
        }
        catch { }

        doc.Entities.Add(dimension);

        return doc;
    }

    /// <summary>Bản vẽ chữ thập đơn giản để kiểm chứng TRIM/EXTEND trên UI.</summary>
    public static CadDocument BuildTrimDemo()
    {
        var doc = new CadDocument(ACadVersion.AC1032);

        doc.Entities.Add(new Line
        {
            StartPoint = new XYZ(-1000, 0, 0),
            EndPoint = new XYZ(1000, 0, 0),
        });

        doc.Entities.Add(new Line
        {
            StartPoint = new XYZ(0, -1000, 0),
            EndPoint = new XYZ(0, 1000, 0),
        });

        doc.Entities.Add(new Line
        {
            StartPoint = new XYZ(-500, -600, 0),
            EndPoint = new XYZ(-500, -200, 0),
        });

        return doc;
    }

    private static Layer AddLayer(CadDocument doc, string name, Color color,
                                  LineWeightType lineWeight = LineWeightType.Default)    {
        var layer = new Layer(name) { Color = color, LineWeight = lineWeight };
        doc.Layers.Add(layer);
        return layer;
    }

    private static void AddLine(CadDocument doc, Layer layer, double x1, double y1, double x2, double y2)
        => doc.Entities.Add(new Line
        {
            StartPoint = new XYZ(x1, y1, 0),
            EndPoint = new XYZ(x2, y2, 0),
            Layer = layer,
        });

    private static void AddRect(CadDocument doc, Layer layer, double x1, double y1, double x2, double y2, bool skip = false)
    {
        if (skip) return;
        AddLine(doc, layer, x1, y1, x2, y1);
        AddLine(doc, layer, x2, y1, x2, y2);
        AddLine(doc, layer, x2, y2, x1, y2);
        AddLine(doc, layer, x1, y2, x1, y1);
    }

    private static void AddHatch(CadDocument doc, Layer layer,
                                 double x1, double y1, double x2, double y2,
                                 bool solid, double angle, double scale, string? patternName = null)
    {
        var hatch = new Hatch
        {
            Layer = layer,
            IsSolid = solid,
            PatternAngle = angle,
            PatternScale = scale,
        };

        if (!string.IsNullOrEmpty(patternName))
        {
            try { hatch.Pattern = new ACadSharp.Entities.HatchPattern(patternName); }
            catch { }
        }

        var path = new Hatch.BoundaryPath();
        path.Edges.Add(new Hatch.BoundaryPath.Line { Start = new XY(x1, y1), End = new XY(x2, y1) });
        path.Edges.Add(new Hatch.BoundaryPath.Line { Start = new XY(x2, y1), End = new XY(x2, y2) });
        path.Edges.Add(new Hatch.BoundaryPath.Line { Start = new XY(x2, y2), End = new XY(x1, y2) });
        path.Edges.Add(new Hatch.BoundaryPath.Line { Start = new XY(x1, y2), End = new XY(x1, y1) });

        hatch.Paths.Add(path);
        doc.Entities.Add(hatch);
    }

    private static void AddText(CadDocument doc, Layer layer, string value, double x, double y, double height)        => doc.Entities.Add(new TextEntity
        {
            InsertPoint = new XYZ(x, y, 0),
            Height = height,
            Value = value,
            Layer = layer,
        });
}

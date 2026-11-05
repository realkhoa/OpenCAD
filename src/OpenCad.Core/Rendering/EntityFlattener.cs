using System.Globalization;
using ACadSharp;
using ACadSharp.Entities;
using OpenCad.Core.Colors;
using OpenCad.Core.Geometry;

namespace OpenCad.Core.Rendering;

/// <summary>
/// Chuyển entity CAD thành các <see cref="Prim"/> đã làm phẳng.
/// Đây là nơi duy nhất biết cách "vẽ" từng loại entity, nên renderer và hit-tester luôn khớp nhau.
/// </summary>
public static class EntityFlattener
{
    private const int CircleSegments = 72;
    private const int EllipseSegments = 96;
    private const int MaxNestDepth = 8;

    public static List<Prim> FlattenAll(IEnumerable<Entity> entities)
    {
        var list = new List<Prim>(512);
        foreach (var e in entities)
        {
            try { Flatten(e, list, Affine2.Identity, Rgb.White, 0); }
            catch { /* bỏ qua entity lỗi, không làm sập bản vẽ */ }
        }
        return list;
    }

    /// <summary>Độ dài "vô hạn" cho RAY/XLINE — đủ lớn so với mọi bản vẽ, canvas tự cắt.</summary>
    private const double InfiniteLength = 1_000_000;

    public static void Flatten(Entity entity, List<Prim> outList, Affine2 xf, Rgb inheritedColor, int depth)
    {
        if (entity is null || depth > MaxNestDepth) return;
        if (entity.IsInvisible) return;

        var color = ResolveColor(entity, inheritedColor);
        var startIndex = outList.Count;

        switch (entity)
        {
            case Line line:
                Emit(outList, xf, color, new[] { ToPt(line.StartPoint), ToPt(line.EndPoint) });
                break;

            // Vô hạn trong lý thuyết: vẽ thành đoạn rất dài, canvas tự cắt theo khung nhìn.
            // Ray: từ điểm gốc ra một phía. XLine: cả hai phía.
            case Ray ray:
            {
                var origin = ToPt(ray.StartPoint);
                var dir = new Pt(ray.Direction.X, ray.Direction.Y).Normalized();

                if (dir.LengthSquared < 1e-12) break;

                Emit(outList, xf, color, new[] { origin, origin + dir * InfiniteLength });
                break;
            }

            case XLine xline:
            {
                var origin = ToPt(xline.FirstPoint);
                var dir = new Pt(xline.Direction.X, xline.Direction.Y).Normalized();

                if (dir.LengthSquared < 1e-12) break;

                Emit(outList, xf, color, new[]
                {
                    origin - dir * InfiniteLength,
                    origin + dir * InfiniteLength,
                });
                break;
            }

            case Arc arc:
                EmitCurve(outList, xf, color, SampleArc(ToPt(arc.Center), arc.Radius, arc.StartAngle, arc.EndAngle),
                          ToPt(arc.Center), arc.Radius);
                break;

            case Circle circle:
                EmitCurve(outList, xf, color, SampleCircle(ToPt(circle.Center), circle.Radius),
                          ToPt(circle.Center), circle.Radius, closed: true);
                break;

            case Ellipse ellipse:
                EmitCurve(outList, xf, color, SampleEllipse(ellipse),
                          ToPt(ellipse.Center), ToPt(ellipse.MajorAxisEndPoint).Length);
                break;

            case Point point:
                outList.Add(Prim.PointAt(xf.Apply(ToPt(point.Location)), color));
                break;

            case LwPolyline lw:
                Emit(outList, xf, color, SampleLwPolyline(lw), closed: lw.IsClosed);
                break;

            case Polyline2D p2:
                Emit(outList, xf, color, SamplePolyline(p2.Vertices.Cast<Vertex>()), closed: p2.IsClosed);
                break;

            case Polyline3D p3:
                Emit(outList, xf, color, SamplePolyline(p3.Vertices.Cast<Vertex>()), closed: p3.IsClosed);
                break;

            case Solid solid:
                // Thứ tự đỉnh của SOLID trong DXF là 1‑2‑4‑3, không phải 1‑2‑3‑4.
                Emit(outList, xf, color, new[]
                {
                    ToPt(solid.FirstCorner), ToPt(solid.SecondCorner),
                    ToPt(solid.FourthCorner), ToPt(solid.ThirdCorner),
                }, closed: true, filled: true);
                break;

            case TextEntity text:
                outList.Add(Prim.TextAt(
                    DecodeTextCodes(text.Value),
                    xf.Apply(ToPt(GetTextAnchor(text))),
                    Math.Max(text.Height, 1e-6) * xf.ScaleFactor,
                    text.Rotation,
                    color));
                break;

            case MText mtext:
                FlattenMText(mtext, outList, xf, color);
                break;

            // TableEntity kế thừa Insert nên phải đứng TRƯỚC case Insert
            case TableEntity table:
                FlattenTable(table, outList, xf, color);
                break;

            case Insert insert:
                FlattenInsert(insert, outList, xf, color, depth);
                break;

            case Spline spline:
                EmitCurve(outList, xf, color, SampleSpline(spline), null, 0, closed: spline.IsClosed);
                break;

            case Dimension dim:
                FlattenDimension(dim, outList, xf, color, depth);
                break;

            case Hatch hatch:
                FlattenHatch(hatch, outList, xf, color);
                break;

            case Leader leader:
                FlattenLeader(leader, outList, xf, color);
                break;

            case Wipeout wipeout:
                FlattenWipeout(wipeout, outList, xf);
                break;

            case Tolerance tolerance:
                FlattenTolerance(tolerance, outList, xf, color);
                break;

            case MultiLeader multiLeader:
                FlattenMultiLeader(multiLeader, outList, xf, color);
                break;

            case MLine mline:
                FlattenMLine(mline, outList, xf, color);
                break;

            // Region, Solid3D, CadBody, Mesh...: chưa hỗ trợ ở phiên bản này.
            default:
                break;
        }

        ApplyDash(entity, outList, startIndex);
        ApplyLineWeight(entity, outList, startIndex);
    }

    /// <summary>Gán độ đậm nét và chỉ số màu ACI cho các primitive vừa sinh ra.</summary>
    private static void ApplyLineWeight(Entity entity, List<Prim> outList, int startIndex)
    {
        if (startIndex >= outList.Count) return;

        var weight = Styles.LineWeights.Resolve(entity);
        var aci = ResolveAci(entity);

        for (var i = startIndex; i < outList.Count; i++)
        {
            outList[i].LineWeightMm = weight;
            outList[i].AciIndex = aci;
        }
    }

    private static int ResolveAci(Entity entity)
    {
        try
        {
            var color = entity.Color;
            if (color.IsTrueColor) return 0;

            if (color.IsByLayer)
            {
                var layerColor = entity.Layer?.Color;
                if (layerColor is null || layerColor.Value.IsTrueColor) return 0;
                return layerColor.Value.Index;
            }

            return color.Index;
        }
        catch
        {
            return 0;
        }
    }

    // ----------------------------------------------------------------- HATCH

    private static void FlattenHatch(Hatch hatch, List<Prim> outList, Affine2 xf, Rgb color)
    {
        var loops = new List<Pt[]>(hatch.Paths.Count);

        foreach (var path in hatch.Paths)
        {
            List<Pt> points;
            try { points = FlattenBoundaryPath(path); }
            catch { continue; }

            if (points.Count < 3) continue;

            if (xf.IsIdentity)
            {
                loops.Add(points.ToArray());
            }
            else
            {
                var mapped = new Pt[points.Count];
                for (var i = 0; i < points.Count; i++) mapped[i] = xf.Apply(points[i]);
                loops.Add(mapped);
            }
        }

        if (loops.Count == 0) return;

        var scale = hatch.PatternScale > 0 ? hatch.PatternScale : 1;

        // Pattern có tên và có trong thư viện .pat → vẽ đúng pattern
        if (!hatch.IsSolid)
        {
            var pattern = Styles.HatchPatternLibrary.Find(hatch.Pattern?.Name);

            if (pattern is not null)
            {
                var segments = Styles.HatchPatternLibrary.Generate(
                    pattern, loops, scale, hatch.PatternAngle);

                if (segments.Count > 0)
                {
                    foreach (var segment in segments)
                        Emit(outList, xf, color, segment);

                    return;
                }
            }
        }

        outList.Add(Prim.HatchRegion(loops, color, hatch.IsSolid, hatch.PatternAngle, scale));
    }

    private static List<Pt> FlattenBoundaryPath(Hatch.BoundaryPath path)
    {
        var points = new List<Pt>(16);

        foreach (var edge in path.Edges)
        {
            switch (edge)
            {
                case Hatch.BoundaryPath.Line line:
                    AddPoint(points, new Pt(line.Start.X, line.Start.Y));
                    AddPoint(points, new Pt(line.End.X, line.End.Y));
                    break;

                case Hatch.BoundaryPath.Arc arc:
                {
                    var center = new Pt(arc.Center.X, arc.Center.Y);
                    var start = arc.StartAngle;
                    var sweep = arc.CounterClockWise
                        ? NormAngle(arc.EndAngle - start)
                        : -NormAngle(start - arc.EndAngle);

                    // Cung có start == end là biên TRÒN KHÉP KÍN → quét đủ 360°.
                    // (Gặp trong bản vẽ thật: 4 hatch tròn không vẽ được vì góc quét = 0.)
                    if (Math.Abs(sweep) < 1e-9)
                        sweep = arc.CounterClockWise ? Math.PI * 2 : -Math.PI * 2;

                    SampleEdgeArc(points, center, arc.Radius, start, sweep);
                    break;
                }

                case Hatch.BoundaryPath.Ellipse ellipse:
                {
                    var center = new Pt(ellipse.Center.X, ellipse.Center.Y);
                    var major = new Pt(ellipse.MajorAxisEndPoint.X, ellipse.MajorAxisEndPoint.Y);
                    var minor = major.Perp() * ellipse.RadiusRatio;

                    var start = ellipse.StartAngle;
                    var sweep = ellipse.CounterClockWise
                        ? NormAngle(ellipse.EndAngle - start)
                        : -NormAngle(start - ellipse.EndAngle);

                    var segments = Math.Max(8, (int)Math.Ceiling(Math.Abs(sweep) / (2 * Math.PI) * 64));
                    for (var i = 0; i <= segments; i++)
                    {
                        var t = start + sweep * i / segments;
                        var c = Math.Cos(t);
                        var s = Math.Sin(t);
                        AddPoint(points, new Pt(center.X + major.X * c + minor.X * s,
                                                center.Y + major.Y * c + minor.Y * s));
                    }
                    break;
                }

                case Hatch.BoundaryPath.Polyline polyline:
                {
                    var vertices = polyline.Vertices;
                    if (vertices.Count == 0) break;

                    var bulges = polyline.Bulges?.ToList() ?? new List<double>();

                    for (var i = 0; i < vertices.Count; i++)
                    {
                        var current = new Pt(vertices[i].X, vertices[i].Y);
                        AddPoint(points, current);

                        var isLast = i == vertices.Count - 1;
                        if (isLast && !polyline.IsClosed) break;

                        var nextIndex = (i + 1) % vertices.Count;
                        var next = new Pt(vertices[nextIndex].X, vertices[nextIndex].Y);
                        var bulge = i < bulges.Count ? bulges[i] : 0;

                        AppendBulge(points, current, next, bulge);
                    }
                    break;
                }
            }
        }

        // Bỏ điểm cuối trùng điểm đầu (vòng khép kín)
        if (points.Count > 1 && Pt.Distance(points[0], points[^1]) < 1e-9)
            points.RemoveAt(points.Count - 1);

        return points;
    }

    private static void SampleEdgeArc(List<Pt> points, Pt center, double radius, double start, double sweep)
    {
        if (radius <= 0) return;

        var segments = Math.Max(4, (int)Math.Ceiling(Math.Abs(sweep) / (2 * Math.PI) * 64));
        for (var i = 0; i <= segments; i++)
        {
            var a = start + sweep * i / segments;
            AddPoint(points, new Pt(center.X + radius * Math.Cos(a),
                                    center.Y + radius * Math.Sin(a)));
        }
    }

    private static void AddPoint(List<Pt> points, Pt point)
    {
        if (points.Count > 0 && Pt.Distance(points[^1], point) < 1e-9) return;
        points.Add(point);
    }

    private static double NormAngle(double angle)
    {
        var twoPi = 2 * Math.PI;
        angle %= twoPi;
        if (angle < 0) angle += twoPi;
        return angle;
    }

    /// <summary>Gán mẫu nét đứt của entity cho mọi primitive vừa sinh ra từ nó.</summary>
    private static void ApplyDash(Entity entity, List<Prim> outList, int startIndex)
    {
        if (startIndex >= outList.Count) return;

        var pattern = ResolveDashPattern(entity);
        if (pattern is null) return;

        for (var i = startIndex; i < outList.Count; i++)
            outList[i].Dash = pattern;
    }

    private static double[]? ResolveDashPattern(Entity entity)
    {
        try
        {
            var lineType = entity.LineType;

            if (lineType is null || IsSpecialLineType(lineType))
                lineType = entity.Layer?.LineType;

            if (lineType is null || IsSpecialLineType(lineType)) return null;

            var segments = lineType.Segments?.ToList();
            if (segments is null || segments.Count == 0) return null;

            var scale = entity.LineTypeScale;
            if (scale <= 1e-9) scale = 1;

            var pattern = new List<double>(segments.Count + 1);
            foreach (var segment in segments)
            {
                var length = Math.Abs(segment.Length);
                if (length < 1e-9) length = 0.5;          // điểm (dot)
                pattern.Add(segment.IsSpace ? -length * scale : length * scale);
            }

            if (pattern.Count == 0) return null;

            // Mẫu phải bắt đầu bằng nét
            if (pattern[0] < 0) pattern.Insert(0, 0.5);

            return pattern.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSpecialLineType(ACadSharp.Tables.LineType lineType)
    {
        var name = lineType.Name ?? string.Empty;
        return name.Equals("ByLayer", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ByBlock", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Continuous", StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- INSERT

    /// <summary>
    /// LEADER: đường dẫn + mũi tên. Chữ đi kèm thường là entity riêng trong model space
    /// nên không vẽ lại ở đây (tránh trùng).
    /// </summary>
    /// <summary>
    /// TABLE: kẻ lưới theo kích thước hàng/cột và ghi nội dung từng ô.
    /// Bảng thống kê (cửa, vật liệu) trước đây không hiện gì.
    /// </summary>
    private static void FlattenTable(TableEntity table, List<Prim> outList, Affine2 xf, Rgb color)
    {
        try
        {
            var rows = table.Rows;
            var cols = table.Columns;

            if (rows is null || cols is null || rows.Count == 0 || cols.Count == 0) return;

            var originX = table.InsertPoint.X;
            var originY = table.InsertPoint.Y;

            var totalWidth = cols.Sum(c => c.Width);
            var totalHeight = rows.Sum(r => r.Height);

            if (totalWidth < 1e-9 || totalHeight < 1e-9) return;

            // Bảng có thể bị xoay: dựng theo toạ độ cục bộ rồi xoay quanh điểm chèn
            var rotation = table.Rotation;
            var cos = Math.Cos(rotation);
            var sin = Math.Sin(rotation);

            Pt Map(double lx, double ly) => new(
                originX + lx * cos - ly * sin,
                originY + lx * sin + ly * cos);

            var x = 0.0;
            for (var c = 0; c <= cols.Count; c++)
            {
                Emit(outList, xf, color, new[] { Map(x, 0), Map(x, -totalHeight) });

                if (c < cols.Count) x += cols[c].Width;
            }

            var y = 0.0;
            for (var r = 0; r <= rows.Count; r++)
            {
                Emit(outList, xf, color, new[] { Map(0, y), Map(totalWidth, y) });

                if (r < rows.Count) y -= rows[r].Height;
            }

            var rowY = 0.0;

            foreach (var row in rows)
            {
                var colX = 0.0;

                for (var c = 0; c < cols.Count; c++)
                {
                    var width = cols[c].Width;
                    var height = row.Height;

                    if (c < row.Cells.Count)
                    {
                        var text = CellText(row.Cells[c]);

                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            var textHeight = Math.Min(height * 0.5, Math.Max(1.0, height * 0.4));
                            var pad = Math.Max(0.5, width * 0.04);

                            outList.Add(Prim.TextAt(text,
                                xf.Apply(Map(colX + pad, rowY - height * 0.72)),
                                textHeight * xf.ScaleFactor, rotation, color));
                        }
                    }

                    colX += width;
                }

                rowY -= row.Height;
            }
        }
        catch { }
    }

    private static string CellText(TableEntity.Cell cell)
    {
        try
        {
            var content = cell.Content;
            if (content is null) return string.Empty;

            var value = content.CadValue;
            if (value is null) return string.Empty;

            return value.ToString()?.Trim() ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    /// <summary>
    /// MTEXT nhiều dòng: AutoCAD dùng <c>\P</c> để xuống dòng. Trước đây vẽ cả chuỗi
    /// thành một dòng nên ký tự <c>\P</c> hiện ra như rác.
    /// </summary>
    private static void FlattenMText(MText mtext, List<Prim> outList, Affine2 xf, Rgb color)
    {
        string raw;

        try { raw = mtext.PlainText ?? mtext.Value ?? string.Empty; }
        catch { try { raw = mtext.Value ?? string.Empty; } catch { return; } }

        if (string.IsNullOrEmpty(raw)) return;

        var lines = raw
            .Replace("\\P", "\n")
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Replace("\\~", " ")
            .Split('\n');

        var height = Math.Max(mtext.Height, 1e-6);
        var origin = ToPt(mtext.InsertPoint);

        // Dòng dưới phải hạ theo hướng đã xoay, không phải luôn hạ theo trục Y
        double rotation;

        try { rotation = mtext.Rotation; }
        catch { rotation = 0; }

        var cos = Math.Cos(rotation);
        var sin = Math.Sin(rotation);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();

            if (line.Length == 0) continue;

            var drop = i * height * 1.4;

            var position = new Pt(
                origin.X + drop * sin,
                origin.Y - drop * cos);

            outList.Add(Prim.TextAt(line, xf.Apply(position),
                height * xf.ScaleFactor, rotation, color));
        }
    }

    /// <summary>
    /// WIPEOUT: đa giác che — vẽ bằng nền trắng đè lên nội dung phía trước.
    /// Đỉnh biên lưu theo toạ độ ảnh (0..1) nên phải ánh xạ qua InsertPoint/Size/UVector/VVector.
    /// </summary>
    private static void FlattenWipeout(Wipeout wipeout, List<Prim> outList, Affine2 xf)
    {
        try
        {
            var vertices = wipeout.ClipBoundaryVertices;

            if (vertices is null || vertices.Count < 3) return;

            var origin = ToPt(wipeout.InsertPoint);
            var size = wipeout.Size;

            var ux = wipeout.UVector.X == 0 && wipeout.UVector.Y == 0 ? 1 : wipeout.UVector.X;
            var uy = wipeout.UVector.X == 0 && wipeout.UVector.Y == 0 ? 0 : wipeout.UVector.Y;
            var vx = wipeout.VVector.X == 0 && wipeout.VVector.Y == 0 ? 0 : wipeout.VVector.X;
            var vy = wipeout.VVector.X == 0 && wipeout.VVector.Y == 0 ? 1 : wipeout.VVector.Y;

            var points = new Pt[vertices.Count];

            for (var i = 0; i < vertices.Count; i++)
            {
                var u = vertices[i].X * size.X;
                var v = vertices[i].Y * size.Y;

                points[i] = new Pt(
                    origin.X + u * ux + v * vx,
                    origin.Y + u * uy + v * vy);
            }

            // Trắng là màu "che": vẽ sau nên đè lên nội dung trước đó
            outList.Add(Prim.HatchRegion(
                new[] { points }, new Rgb(255, 255, 255), solid: true, patternAngle: 0, patternSpacing: 1));
        }
        catch { }
    }

    /// <summary>TOLERANCE: chữ dung sai kèm khung chữ nhật.</summary>
    /// <summary>
    /// Giải mã mã điều khiển của AutoCAD trong chuỗi chữ:
    /// %%d = °, %%c = Ø, %%p = ±, %%% = %, %%v = xuống dòng.
    /// </summary>
    private static string DecodeTextCodes(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        return raw
            .Replace("%%d", "\u00B0")
            .Replace("%%D", "\u00B0")
            .Replace("%%c", "\u00D8")
            .Replace("%%C", "\u00D8")
            .Replace("%%p", "\u00B1")
            .Replace("%%P", "\u00B1")
            .Replace("%%%", "%")
            .Replace("%%v", "\n")
            .Replace("%%V", "\n");
    }

    /// <summary>
    /// MULTILEADER: chữ ghi chú + các đường dẫn. Chữ lấy từ ContextData.TextLabel,
    /// đường dẫn lấy từ LeaderRoots → LeaderLine → Points.
    /// </summary>
    private static void FlattenMultiLeader(MultiLeader multiLeader, List<Prim> outList,
                                           Affine2 xf, Rgb color)
    {
        try
        {
            var context = multiLeader.ContextData;
            if (context is null) return;

            // Đường dẫn
            var roots = context.LeaderRoots;

            if (roots is not null)
            {
                foreach (var root in roots)
                {
                    if (root?.Lines is null) continue;

                    foreach (var line in root.Lines)
                    {
                        if (line?.Points is null || line.Points.Count < 2) continue;

                        var points = line.Points.Select(ToPt).ToArray();
                        Emit(outList, xf, color, points);

                        // Mũi tên ở đầu dây dẫn
                        var tip = points[0];
                        var direction = (points[1] - points[0]).Normalized();

                        if (direction.LengthSquared < 1e-12) continue;

                        var size = line.ArrowheadSize > 1e-6
                            ? line.ArrowheadSize
                            : Math.Max(1.0, context.TextHeight * 0.6);

                        EmitArrow(outList, xf, color, tip, direction * -1, size);
                    }
                }
            }

            // Chữ
            var label = DecodeTextCodes(context.TextLabel)
                .Replace("\\P", "\n")
                .Replace("\\~", " ");

            if (string.IsNullOrWhiteSpace(label)) return;

            var height = Math.Max(context.TextHeight, 1e-6);
            var origin = ToPt(context.TextLocation);
            var lines2 = label.Split('\n');

            for (var i = 0; i < lines2.Length; i++)
            {
                var line = lines2[i].TrimEnd();

                if (line.Length == 0) continue;

                outList.Add(Prim.TextAt(line,
                    xf.Apply(new Pt(origin.X, origin.Y - i * height * 1.4)),
                    height * xf.ScaleFactor, 0, color));
            }
        }
        catch { }
    }

    private static void FlattenTolerance(Tolerance tolerance, List<Prim> outList, Affine2 xf, Rgb color){
        try
        {
            var text = DecodeTextCodes(tolerance.Text).Replace("\n", " ").Trim();
            var origin = ToPt(tolerance.InsertionPoint);

            var height = 0.0;

            try { height = tolerance.Style?.TextHeight ?? 0; }
            catch { }

            if (height < 1e-6) height = 2.5;

            var width = Math.Max(height * 2, (text.Length + 2) * height * 0.62);

            // Khung
            var corners = new[]
            {
                new Pt(origin.X, origin.Y - height * 0.4),
                new Pt(origin.X + width, origin.Y - height * 0.4),
                new Pt(origin.X + width, origin.Y + height * 1.6),
                new Pt(origin.X, origin.Y + height * 1.6),
            };

            Emit(outList, xf, color, corners, closed: true);

            if (text.Length > 0)
            {
                outList.Add(Prim.TextAt(text,
                    xf.Apply(new Pt(origin.X + height * 0.4, origin.Y)),
                    height * xf.ScaleFactor, 0, color));
            }
        }
        catch { }
    }

    private static void FlattenLeader(Leader leader, List<Prim> outList, Affine2 xf, Rgb color)
    {
        try
        {
            if (leader.Vertices is null || leader.Vertices.Count < 2) return;

            var points = leader.Vertices.Select(ToPt).ToArray();
            Emit(outList, xf, color, points);

            if (!leader.ArrowHeadEnabled) return;

            // Mũi tên ở đỉnh đầu, hướng từ đỉnh thứ hai tới đỉnh đầu
            var tip = points[0];
            var direction = (points[1] - points[0]).Normalized();
            if (direction.LengthSquared < 1e-12) return;

            var size = leader.TextHeight > 1e-6
                ? leader.TextHeight * 0.8
                : Math.Max(1.0, Pt.Distance(points[0], points[1]) * 0.06);

            EmitArrow(outList, xf, color, tip, direction * -1, size);
        }
        catch { }
    }

    /// <summary>
    /// MLINE: vẽ đường tâm của tường nhiều nét. Chưa dựng đủ các nét song song theo
    /// MLineStyle nên chỉ thể hiện đường trục — vẫn hơn là để trống.
    /// </summary>
    private static void FlattenMLine(MLine mline, List<Prim> outList, Affine2 xf, Rgb color)
    {
        try
        {
            if (mline.Vertices is null || mline.Vertices.Count < 2) return;

            var points = mline.Vertices.Select(v => ToPt(v.Position)).ToArray();
            Emit(outList, xf, color, points);
        }
        catch { }
    }

    private static void FlattenInsert(Insert insert, List<Prim> outList, Affine2 xf, Rgb color, int depth)
    {
        var block = insert.Block;
        if (block is null || block.Entities.Count == 0) return;

        var rows = Math.Max(1, (int)insert.RowCount);
        var cols = Math.Max(1, (int)insert.ColumnCount);

        // Chặn INSERT tự tham chiếu / lồng quá sâu làm tràn stack
        if (rows * cols > 10_000) { rows = 1; cols = 1; }

        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                var offset = new Pt(c * insert.ColumnSpacing, r * insert.RowSpacing);
                var local = Affine2.ForInsert(
                    insert.XScale, insert.YScale, insert.Rotation,
                    new Pt(insert.InsertPoint.X + offset.X, insert.InsertPoint.Y + offset.Y));

                var combined = xf.After(local);

                foreach (var child in block.Entities)
                {
                    // ATTDEF chỉ là định nghĩa; khi chèn, AutoCAD hiển thị attribute của
                    // INSERT chứ không vẽ định nghĩa — tránh vẽ trùng hai lần.
                    if (child is AttributeDefinition) continue;

                    try { Flatten(child, outList, combined, color, depth + 1); }
                    catch { }
                }
            }
        }

        // Attribute của INSERT lưu toạ độ trong WCS nên chỉ áp dụng biến đổi ngoài.
        // Trước đây bỏ qua hoàn toàn nên nhãn block (số cửa, tên phòng) không hiện.
        if (insert.Attributes is null) return;

        foreach (var attribute in insert.Attributes)
        {
            if (attribute is null) continue;

            try { Flatten(attribute, outList, xf, color, depth + 1); }
            catch { }
        }
    }

    // ------------------------------------------------------------- DIMENSION

    private static void FlattenDimension(Dimension dim, List<Prim> outList, Affine2 xf, Rgb color, int depth)
    {
        // Hình học hiển thị của dimension nằm trong block ẩn do CAD sinh ra,
        // và đã ở toạ độ WCS nên không cần biến đổi thêm.
        var block = dim.Block;
        if (block is not null && block.Entities.Count > 0)
        {
            foreach (var child in block.Entities)
            {
                try { Flatten(child, outList, xf, color, depth + 1); }
                catch { }
            }
            return;
        }

        // Dimension mới tạo trong OpenCAD chưa có block → tự dựng hình học.
        if (dim is DimensionAligned aligned)
            RenderAlignedDimension(aligned, outList, xf, color);
        else if (dim is DimensionRadius radius)
            RenderRadiusDimension(radius, outList, xf, color);
        else if (dim is DimensionDiameter diameter)
            RenderDiameterDimension(diameter, outList, xf, color);
        else if (dim is DimensionAngular2Line angular)
            RenderAngularDimension(angular, outList, xf, color);
        else if (dim is DimensionOrdinate ordinate)
            RenderOrdinateDimension(ordinate, outList, xf, color);
    }

    /// <summary>Kích thước góc giữa hai đường: cung nối hai tia + đường gióng + số đo độ.</summary>
    private static void RenderAngularDimension(DimensionAngular2Line dim, List<Prim> outList,
                                               Affine2 xf, Rgb color)
    {
        var vertex = ToPt(dim.AngleVertex);
        var p1 = ToPt(dim.FirstPoint);
        var p2 = ToPt(dim.SecondPoint);
        var arcPoint = ToPt(dim.DimensionArc);

        var radius = Pt.Distance(vertex, arcPoint);
        if (radius < 1e-9)
            radius = Math.Max(Pt.Distance(vertex, p1), Pt.Distance(vertex, p2)) * 0.6;

        if (radius < 1e-9) return;

        var a1 = Math.Atan2(p1.Y - vertex.Y, p1.X - vertex.X);
        var a2 = Math.Atan2(p2.Y - vertex.Y, p2.X - vertex.X);

        // Lấy góc nhỏ hơn (≤ 180°) cho giống AutoCAD
        var sweep = a2 - a1;
        while (sweep > Math.PI) sweep -= 2 * Math.PI;
        while (sweep < -Math.PI) sweep += 2 * Math.PI;

        const int steps = 48;
        var arc = new Pt[steps + 1];

        for (var i = 0; i <= steps; i++)
        {
            var angle = a1 + sweep * i / steps;
            arc[i] = new Pt(vertex.X + radius * Math.Cos(angle), vertex.Y + radius * Math.Sin(angle));
        }

        Emit(outList, xf, color, arc);

        // Đường gióng từ hai điểm đo ra cung
        Emit(outList, xf, color, new[] { p1, arc[0] });
        Emit(outList, xf, color, new[] { p2, arc[steps] });

        var textHeight = SafeTextHeight(dim, radius);
        var degrees = Math.Abs(sweep) * 180 / Math.PI;

        var label = string.IsNullOrWhiteSpace(dim.Text)
            ? $"{FormatMeasurement(dim, degrees, angular: true)}\u00B0"
            : dim.Text;

        var mid = a1 + sweep / 2;
        var textPoint = new Pt(vertex.X + radius * 1.12 * Math.Cos(mid),
                               vertex.Y + radius * 1.12 * Math.Sin(mid));

        outList.Add(Prim.TextAt(label, xf.Apply(textPoint), textHeight * xf.ScaleFactor, 0, color));
    }

    /// <summary>Kích thước toạ độ: đo X hoặc Y của một điểm so với gốc, có đường dẫn chữ L.</summary>
    private static void RenderOrdinateDimension(DimensionOrdinate dim, List<Prim> outList,
                                                Affine2 xf, Rgb color)
    {
        var feature = ToPt(dim.FeatureLocation);
        var leader = ToPt(dim.LeaderEndpoint);

        var value = dim.IsOrdinateTypeX ? feature.X : feature.Y;

        // Khuỷu chữ L: đi theo trục đo trước rồi rẽ ngang tới chỗ đặt chữ
        var elbow = dim.IsOrdinateTypeX
            ? new Pt(leader.X, feature.Y)
            : new Pt(feature.X, leader.Y);

        Emit(outList, xf, color, new[] { feature, elbow });
        Emit(outList, xf, color, new[] { elbow, leader });

        var textHeight = SafeTextHeight(dim, Math.Abs(value) + 1);

        var label = string.IsNullOrWhiteSpace(dim.Text) ? FormatMeasurement(dim, value) : dim.Text;

        // Chữ nằm ngoài đầu đường dẫn, lệch theo hướng đo
        var dir = (leader - elbow).Normalized();
        var textPoint = leader + dir * (textHeight * (label.Length * 0.62 + 0.4));

        outList.Add(Prim.TextAt(label, xf.Apply(textPoint), textHeight * xf.ScaleFactor, 0, color));
    }

    private static void RenderRadiusDimension(DimensionRadius dim, List<Prim> outList,
                                              Affine2 xf, Rgb color)
    {
        var center = ToPt(dim.AngleVertex);
        var edge = ToPt(dim.DefinitionPoint);

        var radius = Pt.Distance(center, edge);
        if (radius < 1e-9) return;

        var direction = (edge - center).Normalized();
        var textHeight = SafeTextHeight(dim, radius);
        var arrow = ArrowSize(dim, textHeight);

        Emit(outList, xf, color, new[] { center, edge });
        EmitArrow(outList, xf, color, edge, direction, arrow);

        var label = string.IsNullOrWhiteSpace(dim.Text)
            ? $"R{FormatMeasurement(dim, radius)}"
            : dim.Text;

        var normal = direction.Perp();
        var textPoint = Pt.Lerp(center, edge, 0.55) + normal * (textHeight * 0.5);
        outList.Add(Prim.TextAt(label, xf.Apply(textPoint), textHeight * xf.ScaleFactor, 0, color));
    }

    private static void RenderDiameterDimension(DimensionDiameter dim, List<Prim> outList,
                                                Affine2 xf, Rgb color)
    {
        var a = ToPt(dim.AngleVertex);
        var b = ToPt(dim.DefinitionPoint);

        var diameter = Pt.Distance(a, b);
        if (diameter < 1e-9) return;

        var direction = (b - a).Normalized();
        var textHeight = SafeTextHeight(dim, diameter);
        var arrow = ArrowSize(dim, textHeight);

        Emit(outList, xf, color, new[] { a, b });
        EmitArrow(outList, xf, color, b, direction, arrow);
        EmitArrow(outList, xf, color, a, direction * -1, arrow);

        var label = string.IsNullOrWhiteSpace(dim.Text)
            ? $"\u00D8{FormatMeasurement(dim, diameter)}"
            : dim.Text;

        var normal = direction.Perp();
        var textPoint = Pt.Lerp(a, b, 0.5) + normal * (textHeight * 0.5);
        outList.Add(Prim.TextAt(label, xf.Apply(textPoint), textHeight * xf.ScaleFactor, 0, color));
    }

    private static void RenderAlignedDimension(DimensionAligned dim, List<Prim> outList,
                                               Affine2 xf, Rgb color)
    {
        var p1 = ToPt(dim.FirstPoint);
        var p2 = ToPt(dim.SecondPoint);
        var location = ToPt(dim.DefinitionPoint);

        var direction = (p2 - p1).Normalized();
        if (direction.LengthSquared < 1e-12) return;

        // Chiếu hai điểm đo xuống đường kích thước đi qua vị trí đặt
        var a = location + direction * Pt.Dot(p1 - location, direction);
        var b = location + direction * Pt.Dot(p2 - location, direction);

        var measurement = Pt.Distance(p1, p2);

        var textHeight = SafeTextHeight(dim, measurement);
        var arrowSize = ArrowSize(dim, textHeight);

        var normal = direction.Perp();
        var side = Pt.Dot(location - p1, normal) >= 0 ? 1.0 : -1.0;

        // Đường gióng (extension line): từ điểm đo tới quá đường kích thước một chút
        var overshoot = textHeight * 0.6;
        Emit(outList, xf, color, new[] { p1, a + normal * (side * overshoot) });
        Emit(outList, xf, color, new[] { p2, b + normal * (side * overshoot) });

        // Đường kích thước
        Emit(outList, xf, color, new[] { a, b });

        // Mũi tên hai đầu
        EmitArrow(outList, xf, color, a, direction, arrowSize);
        EmitArrow(outList, xf, color, b, direction * -1, arrowSize);

        // Chữ số đo
        var label = string.IsNullOrWhiteSpace(dim.Text)
            ? FormatMeasurement(dim, measurement)
            : dim.Text;

        var midpoint = Pt.Lerp(a, b, 0.5) + normal * (side * textHeight * 0.4);
        outList.Add(Prim.TextAt(label, xf.Apply(midpoint), textHeight * xf.ScaleFactor, 0, color));
    }

    private static void EmitArrow(List<Prim> outList, Affine2 xf, Rgb color,
                                  Pt tip, Pt direction, double size)
    {
        if (size <= 0) return;

        var perp = direction.Perp();
        var back = tip + direction * size;

        var triangle = new[]
        {
            tip,
            back + perp * (size * 0.28),
            back - perp * (size * 0.28),
        };

        Emit(outList, xf, color, triangle, closed: true, filled: true);
    }

    private static double SafeTextHeight(Dimension dim, double measurement)
    {
        var styleHeight = 0.0;

        try { styleHeight = dim.Style?.TextHeight ?? 0; }
        catch { }

        // Style chuẩn của AutoCAD là 2.5 đơn vị — với bản vẽ tính bằng mm thì
        // chữ sẽ nhỏ đến mức không nhìn thấy. Nếu chiều cao style quá nhỏ so với
        // kích thước đo được thì dùng cỡ theo tỷ lệ để chữ còn đọc được.
        var minimumSensible = measurement / 200;
        if (styleHeight >= minimumSensible && styleHeight > 1e-6) return styleHeight;

        return Math.Max(2.5, measurement / 25);
    }

    /// <summary>Chiều dài mũi tên theo style; style không có thì suy ra từ cỡ chữ.</summary>
    private static double ArrowSize(Dimension dim, double textHeight)
    {
        var styleArrow = 0.0;
        var dimScale = 1.0;

        try
        {
            styleArrow = dim.Style?.ArrowSize ?? 0;
            var scale = dim.Style?.ScaleFactor ?? 1;
            if (scale > 1e-9) dimScale = scale;
        }
        catch { }

        if (styleArrow > 1e-6) return styleArrow * dimScale;

        return textHeight * 0.8;
    }

    /// <summary>
    /// Định dạng số đo theo style: ghép tiền tố (DIMPOST), nhân hệ số đo (DIMLFAC),
    /// hiện tới N chữ số thập phân rồi bỏ số 0 thừa (DIMZIN), cuối cùng ghép hậu tố đơn vị.
    /// </summary>
    private static string FormatMeasurement(Dimension dim, double value, bool angular = false)
    {
        var decimals = angular ? 0 : 2;
        var scale = 1.0;
        var suffix = string.Empty;
        var prefix = string.Empty;

        try
        {
            if (dim.Style is { } style)
            {
                decimals = angular ? style.AngularDecimalPlaces : style.DecimalPlaces;
                if (style.LinearScaleFactor > 1e-9) scale = style.LinearScaleFactor;
                suffix = style.Suffix ?? string.Empty;
                prefix = style.Prefix ?? string.Empty;
            }
        }
        catch { }

        decimals = Math.Clamp(decimals, 0, 8);

        var rounded = Math.Round(value * scale, decimals, MidpointRounding.AwayFromZero);
        var text = rounded.ToString("F" + decimals, CultureInfo.InvariantCulture);

        if (text.Contains('.'))
            text = text.TrimEnd('0').TrimEnd('.');

        if (text is "-0" or "") text = "0";

        return prefix + text + suffix;
    }

    // ------------------------------------------------------------- GEOMETRY

    private static void Emit(List<Prim> outList, Affine2 xf, Rgb color, IReadOnlyList<Pt> pts,
                             bool closed = false, bool filled = false)
    {
        if (pts.Count == 0) return;

        if (xf.IsIdentity)
        {
            outList.Add(Prim.Polyline(pts, color, closed, filled));
            return;
        }

        var mapped = new Pt[pts.Count];
        for (var i = 0; i < pts.Count; i++) mapped[i] = xf.Apply(pts[i]);
        outList.Add(Prim.Polyline(mapped, color, closed, filled));
    }

    /// <summary>Phát ra một đường cong đã lấy mẫu, giữ kèm tâm/bán kính để phục vụ snap.</summary>
    private static void EmitCurve(List<Prim> outList, Affine2 xf, Rgb color, IReadOnlyList<Pt> pts,
                                  Pt? center, double radius,
                                  bool closed = false, bool filled = false)
    {
        if (pts.Count == 0) return;

        var mapped = new Pt[pts.Count];
        for (var i = 0; i < pts.Count; i++) mapped[i] = xf.Apply(pts[i]);

        outList.Add(Prim.Curve(mapped, color,
            center is { } c ? xf.Apply(c) : Pt.Zero,
            radius * xf.ScaleFactor,
            closed, filled));
    }

    private static Pt[] SampleCircle(Pt center, double radius)
    {
        if (radius <= 0) return Array.Empty<Pt>();
        var pts = new Pt[CircleSegments];
        for (var i = 0; i < CircleSegments; i++)
        {
            var a = i * 2 * Math.PI / CircleSegments;
            pts[i] = new Pt(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a));
        }
        return pts;
    }

    /// <summary>Góc tính bằng radian. Tự xử lý trường hợp arc vòng qua 0.</summary>
    private static Pt[] SampleArc(Pt center, double radius, double startRad, double endRad)
    {
        if (radius <= 0) return Array.Empty<Pt>();

        var sweep = endRad - startRad;
        while (sweep <= 0) sweep += 2 * Math.PI;
        while (sweep > 2 * Math.PI) sweep -= 2 * Math.PI;

        var segments = Math.Max(2, (int)Math.Ceiling(sweep / (2 * Math.PI) * CircleSegments));
        var pts = new Pt[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var a = startRad + sweep * i / segments;
            pts[i] = new Pt(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a));
        }
        return pts;
    }

    private static Pt[] SampleEllipse(Ellipse e)
    {
        var center = ToPt(e.Center);
        var major = ToPt(e.MajorAxisEndPoint);
        var minor = major.Perp() * e.RadiusRatio;

        var start = e.StartParameter;
        var end = e.EndParameter;
        var sweep = end - start;
        if (Math.Abs(sweep) < 1e-9)
        {
            start = 0;
            sweep = 2 * Math.PI;
        }

        var segments = Math.Max(4, (int)Math.Ceiling(Math.Abs(sweep) / (2 * Math.PI) * EllipseSegments));
        var pts = new Pt[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var t = start + sweep * i / segments;
            var c = Math.Cos(t);
            var s = Math.Sin(t);
            pts[i] = new Pt(center.X + major.X * c + minor.X * s,
                            center.Y + major.Y * c + minor.Y * s);
        }
        return pts;
    }

    private static IReadOnlyList<Pt> SampleLwPolyline(LwPolyline lw)
    {
        var verts = lw.Vertices;
        if (verts.Count == 0) return Array.Empty<Pt>();

        var pts = new List<Pt>(verts.Count * 2);
        for (var i = 0; i < verts.Count; i++)
        {
            var v = verts[i];
            var p = new Pt(v.Location.X, v.Location.Y);
            pts.Add(p);

            var isLast = i == verts.Count - 1;
            if (isLast && !lw.IsClosed) break;

            var next = verts[(i + 1) % verts.Count];
            var pNext = new Pt(next.Location.X, next.Location.Y);
            AppendBulge(pts, p, pNext, v.Bulge);
        }
        return pts;
    }

    private static IReadOnlyList<Pt> SamplePolyline(IEnumerable<Vertex> verts)
    {
        var list = verts.ToList();
        if (list.Count == 0) return Array.Empty<Pt>();

        var pts = new List<Pt>(list.Count * 2);
        for (var i = 0; i < list.Count; i++)
        {
            var v = list[i];
            pts.Add(ToPt(v.Location));
            if (i == list.Count - 1) break;
            AppendBulge(pts, ToPt(v.Location), ToPt(list[i + 1].Location), v.Bulge);
        }
        return pts;
    }

    /// <summary>
    /// Nối thêm điểm cuối của một đoạn cong "bulge".
    /// bulge = tan(theta/4), dấu âm nghĩa là cung chạy theo chiều kim đồng hồ.
    /// </summary>
    private static void AppendBulge(List<Pt> pts, Pt p1, Pt p2, double bulge)
    {
        if (Math.Abs(bulge) < 1e-9)
        {
            pts.Add(p2);
            return;
        }

        var theta = 4 * Math.Atan(bulge);
        var chord = Pt.Distance(p1, p2);
        if (chord < 1e-12 || Math.Abs(Math.Sin(theta / 2)) < 1e-12)
        {
            pts.Add(p2);
            return;
        }

        var radius = chord / (2 * Math.Sin(theta / 2));
        var mid = Pt.Lerp(p1, p2, 0.5);
        var dir = (p2 - p1) / chord;
        var center = mid + dir.Perp() * (radius * Math.Cos(theta / 2));

        var a0 = Math.Atan2(p1.Y - center.Y, p1.X - center.X);
        var segments = Math.Max(2, (int)Math.Ceiling(Math.Abs(theta) / (2 * Math.PI) * CircleSegments));

        for (var i = 1; i <= segments; i++)
        {
            var a = a0 + theta * i / segments;
            pts.Add(new Pt(center.X + Math.Abs(radius) * Math.Cos(a),
                           center.Y + Math.Abs(radius) * Math.Sin(a)));
        }

        // Đảm bảo khớp chính xác điểm cuối (tránh sai số tích luỹ)
        pts[^1] = p2;
    }

    private static Pt[] SampleSpline(Spline spline)
    {
        var cp = spline.ControlPoints?.Select(ToPt).ToArray() ?? Array.Empty<Pt>();
        if (cp.Length == 0)
        {
            var fit = spline.FitPoints?.Select(ToPt).ToArray() ?? Array.Empty<Pt>();
            return fit;
        }
        if (cp.Length < 3) return cp;

        var degree = Math.Clamp(spline.Degree, 1, cp.Length - 1);
        var knots = spline.Knots?.ToArray() ?? Array.Empty<double>();

        // Knot vector không hợp lệ (hay gặp ở dữ liệu thô): dùng Catmull-Rom
        // để vẫn có đường cong mượt thay vì đa giác điều khiển gấp khúc.
        if (knots.Length != cp.Length + degree + 1) return CatmullRom(cp);

        double u0 = knots[degree], u1 = knots[cp.Length];
        if (u1 - u0 < 1e-12) return CatmullRom(cp);

        var samples = Math.Clamp(cp.Length * 8, 16, 256);
        var pts = new Pt[samples + 1];
        for (var i = 0; i <= samples; i++)
        {
            var u = u0 + (u1 - u0) * i / samples;
            pts[i] = DeBoor(cp, degree, knots, u);
        }
        return pts;
    }

    private static Pt DeBoor(Pt[] control, int degree, double[] knots, double u)
    {
        var n = control.Length - 1;
        var k = FindSpan(n, degree, u, knots);

        var d = new Pt[degree + 1];
        for (var j = 0; j <= degree; j++) d[j] = control[k - degree + j];

        for (var r = 1; r <= degree; r++)
        {
            for (var j = degree; j >= r; j--)
            {
                var denom = knots[k + 1 + j - r] - knots[k + j - degree];
                var a = Math.Abs(denom) < 1e-12 ? 0 : (u - knots[k + j - degree]) / denom;
                d[j] = Pt.Lerp(d[j - 1], d[j], a);
            }
        }
        return d[degree];
    }

    /// <summary>
    /// Nội suy Catmull-Rom đi qua toàn bộ điểm điều khiển.
    /// Dùng làm phương án dự phòng khi spline thiếu/ hỏng knot vector.
    /// </summary>
    private static Pt[] CatmullRom(Pt[] pts)
    {
        if (pts.Length < 3) return pts;

        const int stepsPerSpan = 12;
        var result = new List<Pt>((pts.Length - 1) * stepsPerSpan + 1) { pts[0] };

        for (var i = 0; i < pts.Length - 1; i++)
        {
            var p0 = pts[Math.Max(i - 1, 0)];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = pts[Math.Min(i + 2, pts.Length - 1)];

            for (var s = 1; s <= stepsPerSpan; s++)
            {
                var t = (double)s / stepsPerSpan;
                var t2 = t * t;
                var t3 = t2 * t;

                var x = 0.5 * ((2 * p1.X) + (-p0.X + p2.X) * t
                        + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2
                        + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);
                var y = 0.5 * ((2 * p1.Y) + (-p0.Y + p2.Y) * t
                        + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2
                        + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);

                result.Add(new Pt(x, y));
            }
        }
        return result.ToArray();
    }

    private static int FindSpan(int n, int degree, double u, double[] knots)    {
        if (u >= knots[n + 1]) return n;
        if (u <= knots[degree]) return degree;

        int low = degree, high = n + 1, mid = (low + high) / 2;
        while (u < knots[mid] || u >= knots[mid + 1])
        {
            if (u < knots[mid]) high = mid;
            else low = mid;
            mid = (low + high) / 2;
        }
        return mid;
    }

    // ---------------------------------------------------------------- COLOR

    private static Rgb ResolveColor(Entity entity, Rgb inherited)
    {
        try
        {
            var c = entity.Color;
            if (c.IsByLayer) return LayerColor(entity);
            if (c.IsByBlock) return inherited;
            if (c.IsTrueColor) return new Rgb(c.R, c.G, c.B);
            return AciPalette.FromAci(c.Index);
        }
        catch
        {
            return inherited;
        }
    }

    private static Rgb LayerColor(Entity entity)
    {
        try
        {
            var layer = entity.Layer;
            if (layer is null) return Rgb.White;

            var c = layer.Color;
            if (c.IsTrueColor) return new Rgb(c.R, c.G, c.B);
            return AciPalette.FromAci(c.Index);
        }
        catch
        {
            return Rgb.White;
        }
    }

    // --------------------------------------------------------------- HELPERS

    private static Pt ToPt(CSMath.XYZ v) => new(v.X, v.Y);

    private static CSMath.XYZ GetTextAnchor(TextEntity text)
    {
        // Khi có canh lề, AutoCAD dùng AlignmentPoint; ngược lại dùng InsertPoint.
        var ap = text.AlignmentPoint;
        var ip = text.InsertPoint;
        var hasAlignment = ap.X != 0 || ap.Y != 0 || ap.Z != 0;
        return hasAlignment ? ap : ip;
    }
}

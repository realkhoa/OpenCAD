using ACadSharp.Entities;
using ACadSharp.Tables;
using OpenCad.Core.Geometry;

namespace OpenCad.Core.Editing;

/// <summary>
/// Điều phối mọi thay đổi trên bản vẽ: cập nhật tài liệu, dựng lại scene,
/// và ghi lịch sử undo. UI chỉ cần gọi các hàm ở đây.
/// </summary>
public sealed class CadEditor
{
    public CadWorkspace Workspace { get; }
    public CadScene Scene { get; } = new();
    public UndoStack History { get; } = new();

    /// <summary>Ảnh chụp lựa chọn gần nhất — để panel thuộc tính thao tác trên đó.</summary>
    public IReadOnlyList<Entity> LastSelectionSnapshot { get; set; } = Array.Empty<Entity>();

    /// <summary>Hình học đã thay đổi (cần vẽ lại).</summary>
    public event Action? Changed;

    /// <summary>Trạng thái undo/redo hoặc cờ "đã sửa" thay đổi.</summary>
    public event Action? StateChanged;

    public CadEditor(CadWorkspace workspace)
    {
        Workspace = workspace;
        History.Changed += () => StateChanged?.Invoke();
        Reload();
    }

    public void Reload()
    {
        Scene.Build(Workspace.Document, IsEntityVisible);
        Changed?.Invoke();
        StateChanged?.Invoke();
    }

    // ------------------------------------------------------------ ADD / DEL

    public void AddEntities(IReadOnlyList<Entity> entities, string name = "Add")
    {
        if (entities.Count == 0) return;

        var command = new DelegateCommand(name,
            () =>
            {
                foreach (var e in entities) Workspace.Add(e);
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                foreach (var e in entities) Workspace.Remove(e);
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();
    }

    public void RemoveEntities(IReadOnlyList<Entity> entities, string name = "Erase")
    {
        if (entities.Count == 0) return;

        var command = new DelegateCommand(name,
            () =>
            {
                foreach (var e in entities) Workspace.Remove(e);
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                foreach (var e in entities) Workspace.Add(e);
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();
    }

    // ------------------------------------------------------------- TRANSFORM

    /// <summary>Áp dụng ngay, không ghi lịch sử — dùng khi đang kéo chuột.</summary>
    public void ApplyTransform(IReadOnlyList<Entity> entities, Affine2 transform)
    {
        foreach (var e in entities)
        {
            if (EntityTransform.Apply(e, transform)) Scene.Rebuild(e);
        }
        Changed?.Invoke();
    }

    /// <summary>Ghi lại một phép biến đổi đã áp dụng xong (kết thúc kéo chuột).</summary>
    public void CommitTransform(IReadOnlyList<Entity> entities, Affine2 transform, string name = "Move")
    {
        if (transform.IsIdentity) return;

        var inverse = transform.Inverse();

        var command = new DelegateCommand(name,
            () => { foreach (var e in entities) EntityTransform.Apply(e, transform); Scene.Build(Workspace.Document, IsEntityVisible); Changed?.Invoke(); },
            () => { foreach (var e in entities) EntityTransform.Apply(e, inverse); Scene.Build(Workspace.Document, IsEntityVisible); Changed?.Invoke(); });

        History.Record(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();
    }

    /// <summary>Thực hiện một biến đổi kèm ghi lịch sử (dùng cho lệnh gõ số).</summary>
    public void TransformEntities(IReadOnlyList<Entity> entities, Affine2 transform, string name = "Move")
    {
        if (transform.IsIdentity || entities.Count == 0) return;

        var inverse = transform.Inverse();

        var command = new DelegateCommand(name,
            () => { foreach (var e in entities) EntityTransform.Apply(e, transform); Scene.Build(Workspace.Document, IsEntityVisible); Changed?.Invoke(); },
            () => { foreach (var e in entities) EntityTransform.Apply(e, inverse); Scene.Build(Workspace.Document, IsEntityVisible); Changed?.Invoke(); });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();
    }

    /// <summary>Sao chép entity (bản sao độc lập) rồi biến đổi bản sao.</summary>
    public void CopyTransform(IReadOnlyList<Entity> entities, Affine2 transform, Func<Entity, Entity?> clone, string name = "Copy")
    {
        var copies = new List<Entity>();
        foreach (var e in entities)
        {
            var copy = clone(e);
            if (copy is null) continue;
            EntityTransform.Apply(copy, transform);
            copies.Add(copy);
        }

        if (copies.Count == 0) return;
        AddEntities(copies, name);
    }

    /// <summary>
    /// Thay một entity bằng danh sách entity khác (TRIM/EXTEND) trong một bước undo duy nhất.
    /// </summary>
    public void ReplaceEntity(Entity original, IReadOnlyList<Entity> replacement, string name = "Replace")
        => ReplaceEntities(new[] { original }, replacement, name);

    /// <summary>Thay nhiều entity bằng nhiều entity khác trong một bước undo duy nhất.</summary>
    public void ReplaceEntities(IReadOnlyList<Entity> originals, IReadOnlyList<Entity> replacements,
                                string name = "Replace")
    {
        var command = new DelegateCommand(name,
            () =>
            {
                foreach (var e in originals) Workspace.Remove(e);
                foreach (var e in replacements) Workspace.Add(e);
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                foreach (var e in replacements) Workspace.Remove(e);
                foreach (var e in originals) Workspace.Add(e);
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Chạy một thao tác tuỳ biến dưới dạng lệnh có undo — dùng cho paper space,
    /// nơi entity nằm trong block record của layout chứ không ở model space.
    /// </summary>
    public void RunCustom(string name, Action apply, Action revert)
    {
        var command = new DelegateCommand(name,
            () =>
            {
                apply();
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                revert();
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Tạo dãy chữ nhật: <paramref name="rows"/> hàng × <paramref name="cols"/> cột,
    /// bước <paramref name="dx"/>, <paramref name="dy"/>. Bản gốc giữ nguyên; toàn bộ
    /// bản sao nằm trong một bước undo.
    /// </summary>
    public void ArrayCopy(IReadOnlyList<Entity> source, int rows, int cols,
                          double dx, double dy, Func<Entity, Entity?> clone, string name = "Array")
    {
        if (source.Count == 0) return;
        if (rows < 1 || cols < 1) return;
        if (rows == 1 && cols == 1) return;

        var created = new List<Entity>(source.Count * rows * cols);

        var command = new DelegateCommand(name,
            () =>
            {
                for (var row = 0; row < rows; row++)
                for (var col = 0; col < cols; col++)
                {
                    if (row == 0 && col == 0) continue;

                    var shift = Affine2.Translate(col * dx, row * dy);

                    foreach (var entity in source)
                    {
                        var copy = clone(entity);
                        if (copy is null) continue;

                        EntityTransform.Apply(copy, shift);
                        Workspace.Add(copy);
                        created.Add(copy);
                    }
                }

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                foreach (var entity in created) Workspace.Remove(entity);
                created.Clear();

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Dãy tròn: xoay bản sao quanh <paramref name="center"/>.
    /// Đủ 360° thì chia đều <paramref name="count"/> phần; góc hở thì chia <paramref name="count"/>-1.
    /// </summary>
    public void ArrayPolar(IReadOnlyList<Entity> source, int count, Pt center,
                           double totalDegrees, Func<Entity, Entity?> clone, string name = "Polar array")
    {
        if (source.Count == 0 || count < 2) return;

        var full = Math.Abs(Math.Abs(totalDegrees) - 360) < 1e-6;
        var stepDegrees = full ? totalDegrees / count : totalDegrees / (count - 1);

        if (Math.Abs(stepDegrees) < 1e-9) return;

        var created = new List<Entity>(source.Count * count);

        var command = new DelegateCommand(name,
            () =>
            {
                for (var i = 1; i < count; i++)
                {
                    var angle = stepDegrees * i * Math.PI / 180.0;
                    var rotation = RotationAbout(center, angle);

                    foreach (var entity in source)
                    {
                        var copy = clone(entity);
                        if (copy is null) continue;

                        EntityTransform.Apply(copy, rotation);
                        Workspace.Add(copy);
                        created.Add(copy);
                    }
                }

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                foreach (var entity in created) Workspace.Remove(entity);
                created.Clear();

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Entity có đang hiển thị không: layer phải đang bật và không bị đóng băng.
    /// Entity không hiển thị bị loại khỏi scene nên không chọn/sửa được.
    /// </summary>
    public static bool IsEntityVisible(Entity entity)
    {
        try
        {
            var layer = entity.Layer;
            if (layer is null) return true;

            return layer.IsOn && (layer.Flags & ACadSharp.Tables.LayerFlags.Frozen) == 0;
        }
        catch { return true; }
    }

    /// <summary>Layer của entity có bị khoá không (vẫn thấy nhưng không sửa được).</summary>
    public static bool IsEntityLocked(Entity entity)
    {
        try
        {
            return entity.Layer is { } layer
                   && (layer.Flags & ACadSharp.Tables.LayerFlags.Locked) != 0;
        }
        catch { return false; }
    }

    private static Affine2 RotationAbout(Pt center, double angleRadians)
    {
        var cos = Math.Cos(angleRadians);
        var sin = Math.Sin(angleRadians);

        // Xoay quanh tâm: T(−c) · R(θ) · T(c)
        return new Affine2(cos, -sin, sin, cos,
            center.X - center.X * cos + center.Y * sin,
            center.Y - center.X * sin - center.Y * cos);
    }

    /// <summary>Đặt các điểm chia dọc theo một đối tượng (DIVIDE / MEASURE), có undo.</summary>
    public int PlaceMarkers(Entity target, IReadOnlyList<Pt> points, string name = "Divide")
    {
        if (points.Count == 0) return 0;

        var created = new List<Entity>(points.Count);

        var command = new DelegateCommand(name,
            () =>
            {
                foreach (var point in points)
                {
                    var marker = new ACadSharp.Entities.Point
                    {
                        Location = new CSMath.XYZ(point.X, point.Y, 0),
                        Layer = target.Layer,
                    };

                    Workspace.Add(marker);
                    created.Add(marker);
                }

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                foreach (var marker in created) Workspace.Remove(marker);
                created.Clear();

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return points.Count;
    }

    /// <summary>
    /// Đổi thông số một kiểu kích thước (chữ, mũi tên, số thập phân, hệ số đo, hậu tố).
    /// Giá trị null = giữ nguyên. Có undo.
    /// </summary>
    public bool SetDimensionStyleProperties(
        ACadSharp.Tables.DimensionStyle style,
        double? textHeight = null,
        double? arrowSize = null,
        int? decimals = null,
        double? linearScale = null,
        string? suffix = null,
        string name = "Dimension style",
        string? prefix = null)
    {
        if (style is null) return false;

        var before = (style.TextHeight, style.ArrowSize, (int)style.DecimalPlaces,
                      style.LinearScaleFactor, style.Suffix, style.Prefix);

        void Apply(double text, double arrow, int dec, double scale,
                   string? suffixValue, string? prefixValue)
        {
            try
            {
                if (textHeight is not null) style.TextHeight = text;
                if (arrowSize is not null) style.ArrowSize = arrow;
                if (decimals is not null) style.DecimalPlaces = (short)dec;
                if (linearScale is not null && linearScale > 1e-9) style.LinearScaleFactor = scale;
                if (suffix is not null) style.Suffix = suffixValue ?? string.Empty;
                if (prefix is not null) style.Prefix = prefixValue ?? string.Empty;
            }
            catch { }

            Scene.Build(Workspace.Document, IsEntityVisible);
            Changed?.Invoke();
        }

        var command = new DelegateCommand(name,
            () => Apply(textHeight ?? before.TextHeight,
                        arrowSize ?? before.ArrowSize,
                        decimals ?? before.Item3,
                        linearScale ?? before.LinearScaleFactor,
                        suffix, prefix),
            () => Apply(before.TextHeight, before.ArrowSize, before.Item3,
                        before.LinearScaleFactor, before.Suffix, before.Prefix));

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return true;
    }

    /// <summary>
    /// JOIN: nối các đoạn thẳng rời thành một polyline (khi chúng nối đầu-cuối).
    /// Trả về true nếu nối được. Có undo — các line gốc bị thay bằng polyline.
    /// </summary>
    public bool JoinLines(IReadOnlyList<Entity> entities, string name = "Join")
    {
        var lines = entities.OfType<Line>().ToList();

        if (lines.Count < 2) return false;

        var joined = JoinOperation.JoinLines(lines);

        if (joined is null) return false;

        var command = new DelegateCommand(name,
            () =>
            {
                foreach (var line in lines) Workspace.Remove(line);

                Workspace.Add(joined);

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                Workspace.Remove(joined);

                foreach (var line in lines) Workspace.Add(line);

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return true;
    }

    /// <summary>
    /// EXPLODE: bung một hoặc nhiều đối tượng phức hợp thành đối tượng đơn.
    /// Trả về số đối tượng đã bung. Có undo.
    /// </summary>
    /// <summary>
    /// Đặt chữ cho một ô bảng: ACadSharp không cho ghi nội dung ô qua API công khai,
    /// nên OpenCAD tạo một MText đặt đúng vị trí ô (cách AutoCAD vẫn vẽ chữ trong bảng).
    /// Trả về entity chữ vừa tạo, hoặc null.
    /// </summary>
    public Entity? SetTableCellLabel(ACadSharp.Entities.TableEntity table, int row, int column,
                                     string text, double height, string name = "Table cell")
    {
        if (table is null || row < 0 || column < 0) return null;
        if (row >= table.RowCount || column >= table.ColumnCount) return null;
        if (string.IsNullOrWhiteSpace(text)) return null;

        var rowHeight = table.RowSpacing > 1e-6 ? table.RowSpacing : 300;
        var columnWidth = table.ColumnSpacing > 1e-6 ? table.ColumnSpacing : 1200;

        // Góc trên-trái bảng là InsertPoint; trục Y hướng xuống theo hàng
        var x = table.InsertPoint.X + column * columnWidth + columnWidth * 0.1;
        var y = table.InsertPoint.Y - row * rowHeight - rowHeight * 0.7;

        var label = new MText
        {
            InsertPoint = new CSMath.XYZ(x, y, table.InsertPoint.Z),
            Height = height > 0 ? height : rowHeight * 0.4,
            Value = text,
            Layer = table.Layer,
        };

        AddEntities(new Entity[] { label }, name);

        return label;
    }

    public int Explode(IReadOnlyList<Entity> entities, string name = "Explode")
    {
        if (entities.Count == 0) return 0;

        var removed = new List<Entity>();
        var added = new List<Entity>();

        foreach (var entity in entities)
        {
            var children = ExplodeOperation.Explode(entity);

            if (children.Count == 0) continue;

            removed.Add(entity);
            added.AddRange(children);
        }

        if (removed.Count == 0) return 0;

        var command = new DelegateCommand(name,
            () =>
            {
                foreach (var entity in removed) Workspace.Remove(entity);
                foreach (var entity in added) Workspace.Add(entity);

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                foreach (var entity in added) Workspace.Remove(entity);
                foreach (var entity in removed) Workspace.Add(entity);

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return removed.Count;
    }

    /// <summary>
    /// Đổi chiều dài và/hoặc góc của một Line, giữ nguyên điểm đầu (có undo).
    /// Tham số null = giữ nguyên. Trả về false nếu không đổi được.
    /// </summary>
    public bool SetLineGeometry(Entity entity, double? length = null,
                                double? angleDegrees = null, string name = "Change line")
    {
        if (entity is not Line line) return false;

        var start = line.StartPoint;
        var dx = line.EndPoint.X - start.X;
        var dy = line.EndPoint.Y - start.Y;

        var currentLength = Math.Sqrt(dx * dx + dy * dy);
        var currentAngle = Math.Atan2(dy, dx) * 180 / Math.PI;

        if (currentLength < 1e-9) return false;
        if (length is <= 1e-9) return false;

        var newLength = length ?? currentLength;
        var newAngle = angleDegrees ?? currentAngle;

        if (Math.Abs(newLength - currentLength) < 1e-9 &&
            Math.Abs(newAngle - currentAngle) < 1e-9) return false;

        var before = line.EndPoint;

        var radians = newAngle * Math.PI / 180;

        var after = new CSMath.XYZ(
            start.X + newLength * Math.Cos(radians),
            start.Y + newLength * Math.Sin(radians),
            start.Z);

        void Apply(CSMath.XYZ end)
        {
            line.EndPoint = end;
            Scene.Build(Workspace.Document, IsEntityVisible);
            Changed?.Invoke();
        }

        var command = new DelegateCommand(name, () => Apply(after), () => Apply(before));

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return true;
    }

    /// <summary>Đổi bán kính của đường tròn / cung (có undo). Trả về false nếu không đổi được.</summary>
    public bool SetRadius(Entity entity, double radius, string name = "Change radius")
    {
        if (radius <= 1e-9) return false;

        double Read()
        {
            return entity switch
            {
                Arc arc => arc.Radius,
                Circle circle => circle.Radius,
                _ => double.NaN,
            };
        }

        var before = Read();

        if (double.IsNaN(before)) return false;
        if (Math.Abs(before - radius) < 1e-9) return false;

        void Apply(double value)
        {
            switch (entity)
            {
                case Arc arc: arc.Radius = value; break;
                case Circle circle: circle.Radius = value; break;
            }

            Scene.Build(Workspace.Document, IsEntityVisible);
            Changed?.Invoke();
        }

        var command = new DelegateCommand(name, () => Apply(radius), () => Apply(before));

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return true;
    }

    /// <summary>Đổi chiều cao chữ của Text/MText (có undo). Trả về false nếu không đổi được.</summary>
    public bool SetTextHeight(Entity entity, double height, string name = "Change text height")
    {
        if (height <= 1e-9) return false;

        double Read() => entity switch
        {
            TextEntity text => text.Height,
            MText mtext => mtext.Height,
            _ => double.NaN,
        };

        var before = Read();

        if (double.IsNaN(before)) return false;
        if (Math.Abs(before - height) < 1e-9) return false;

        void Apply(double value)
        {
            switch (entity)
            {
                case TextEntity text: text.Height = value; break;
                case MText mtext: mtext.Height = value; break;
            }

            Scene.Build(Workspace.Document, IsEntityVisible);
            Changed?.Invoke();
        }

        var command = new DelegateCommand(name, () => Apply(height), () => Apply(before));

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return true;
    }

    /// <summary>
    /// PEDIT: đóng hoặc mở polyline đang chọn (có undo). Trả về số polyline đã đổi.
    /// </summary>
    public int SetPolylineClosed(IReadOnlyList<Entity> entities, bool closed, string name = "PEDIT")
    {
        var polylines = entities.OfType<LwPolyline>()
            .Where(p => p.IsClosed != closed)
            .ToList();

        if (polylines.Count == 0) return 0;

        var command = new DelegateCommand(name,
            () =>
            {
                foreach (var polyline in polylines)
                {
                    try { polyline.IsClosed = closed; }
                    catch { }
                }

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                foreach (var polyline in polylines)
                {
                    try { polyline.IsClosed = !closed; }
                    catch { }
                }

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return polylines.Count;
    }

    /// <summary>
    /// ATTSYNC: đồng bộ attribute của MỌI insert theo định nghĩa (ATTDEF) trong block.
    /// Insert thiếu attribute sẽ được thêm với giá trị mặc định.
    /// Trả về số attribute đã thêm. Có undo.
    /// </summary>
    public int SyncAttributes(ACadSharp.Tables.BlockRecord block, string name = "ATTSYNC")
    {
        if (block is null) return 0;

        var definitions = block.Entities.OfType<AttributeDefinition>().ToList();

        if (definitions.Count == 0) return 0;

        var inserts = Workspace.Document.Entities
            .OfType<Insert>()
            .Where(i => ReferenceEquals(i.Block, block))
            .ToList();

        if (inserts.Count == 0) return 0;

        var added = new List<(Insert Owner, AttributeEntity Attribute)>();

        foreach (var insert in inserts)
        {
            foreach (var definition in definitions)
            {
                var existing = insert.Attributes.FirstOrDefault(a =>
                    string.Equals(a.Tag, definition.Tag, StringComparison.OrdinalIgnoreCase));

                if (existing is not null) continue;

                var attribute = new AttributeEntity
                {
                    Tag = definition.Tag,
                    Value = string.IsNullOrEmpty(definition.Value) ? definition.Tag : definition.Value,
                    InsertPoint = new CSMath.XYZ(
                        insert.InsertPoint.X + definition.InsertPoint.X * insert.XScale,
                        insert.InsertPoint.Y + definition.InsertPoint.Y * insert.YScale,
                        0),
                    Height = definition.Height > 0 ? definition.Height : 250,
                    Layer = definition.Layer,
                };

                added.Add((insert, attribute));
            }
        }

        if (added.Count == 0) return 0;

        var command = new DelegateCommand(name,
            () =>
            {
                foreach (var (owner, attribute) in added)
                {
                    try { owner.Attributes.Add(attribute); }
                    catch { }
                }

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                foreach (var (owner, attribute) in added)
                {
                    try { owner.Attributes.Remove(attribute); }
                    catch { }
                }

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return added.Count;
    }

    /// <summary>
    /// ATTOUT: gom attribute của mọi block insert thành bảng (mỗi insert một dòng).
    /// Trả về (tiêu đề cột, các dòng) để ghi CSV.
    /// Cột = BLOCK, LAYER, X, Y + mọi tag attribute tìm thấy.
    /// </summary>
    public (List<string> Headers, List<Dictionary<string, string>> Rows) ExtractAttributes()
    {
        var headers = new List<string> { "BLOCK", "LAYER", "X", "Y" };
        var rows = new List<Dictionary<string, string>>();

        var inserts = Workspace.Document.Entities
            .OfType<Insert>()
            .Where(i => i.Attributes.Count > 0)
            .ToList();

        foreach (var insert in inserts)
        {
            foreach (var attribute in insert.Attributes)
            {
                var tag = attribute.Tag?.Trim() ?? string.Empty;

                if (tag.Length > 0 && !headers.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    headers.Add(tag);
            }
        }

        foreach (var insert in inserts)
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BLOCK"] = insert.Block?.Name ?? string.Empty,
                ["LAYER"] = insert.Layer?.Name ?? "0",
                ["X"] = insert.InsertPoint.X.ToString("0.###",
                    System.Globalization.CultureInfo.InvariantCulture),
                ["Y"] = insert.InsertPoint.Y.ToString("0.###",
                    System.Globalization.CultureInfo.InvariantCulture),
            };

            foreach (var attribute in insert.Attributes)
            {
                var tag = attribute.Tag?.Trim() ?? string.Empty;

                if (tag.Length == 0) continue;

                row[tag] = attribute.Value ?? string.Empty;
            }

            rows.Add(row);
        }

        return (headers, rows);
    }

    /// <summary>
    /// LIST: mô tả ngắn gọn các đối tượng — loại, lớp, màu, hình học chính.
    /// Trả về danh sách dòng để hiện ở cửa sổ lệnh.
    /// </summary>
    public IReadOnlyList<string> Describe(IEnumerable<Entity> entities)
    {
        var lines = new List<string>();
        var list = entities?.Where(e => e is not null).ToList() ?? new List<Entity>();

        if (list.Count == 0) return lines;

        lines.Add($"LIST: {list.Count} đối tượng");

        foreach (var entity in list)
        {
            var layer = entity.Layer?.Name ?? "0";
            var color = entity.Color.IsByLayer ? "ByLayer" : entity.Color.ToString();
            var kind = entity.GetType().Name;

            var geometry = entity switch
            {
                Line line =>
                    $"({Num(line.StartPoint.X)},{Num(line.StartPoint.Y)}) → " +
                    $"({Num(line.EndPoint.X)},{Num(line.EndPoint.Y)}) dài " +
                    Num(Math.Sqrt(
                        Math.Pow(line.EndPoint.X - line.StartPoint.X, 2) +
                        Math.Pow(line.EndPoint.Y - line.StartPoint.Y, 2))),

                // Arc : Circle → phải đứng TRƯỚC Circle
                Arc arc => $"tâm ({Num(arc.Center.X)},{Num(arc.Center.Y)}) R={Num(arc.Radius)}",

                Circle circle => $"tâm ({Num(circle.Center.X)},{Num(circle.Center.Y)}) R={Num(circle.Radius)}",

                TextEntity text =>
                    $"'{Shorten(text.Value)}' cao {Num(text.Height)} tại ({Num(text.InsertPoint.X)},{Num(text.InsertPoint.Y)})",

                MText mtext =>
                    $"'{Shorten(mtext.PlainText)}' cao {Num(mtext.Height)} tại ({Num(mtext.InsertPoint.X)},{Num(mtext.InsertPoint.Y)})",

                LwPolyline poly => $"{poly.Vertices.Count} đỉnh{(poly.IsClosed ? ", khép kín" : "")}",

                Insert insert => $"block '{insert.Block?.Name}' tại ({Num(insert.InsertPoint.X)},{Num(insert.InsertPoint.Y)})",

                Hatch hatch => $"{hatch.Pattern?.Name ?? "solid"} tỉ lệ {Num(hatch.PatternScale)}",

                _ => "—",
            };

            lines.Add($"   {kind}  lớp {layer}  màu {color}  {geometry}");
        }

        return lines;
    }

    private static string Num(double value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string Shorten(string? text)
    {
        var value = (text ?? string.Empty).Replace("\n", " ").Trim();

        return value.Length <= 40 ? value : value[..40] + "…";
    }

    /// <summary>
    /// Đặt góc xoay cho chữ (độ). Chỉ xoay chữ tại chỗ, không dời điểm chèn.
    /// Trả về số đối tượng đã đổi. Có undo.
    /// </summary>
    public int SetTextRotation(IEnumerable<Entity> entities, double degrees, string name = "TEXTANGLE")
    {
        var list = entities?.ToList() ?? new List<Entity>();

        var texts = list.OfType<TextEntity>().ToList();

        // MTEXT: Rotation chỉ đọc, nhưng nó được TÍNH từ góc của AlignmentPoint
        // (ACadSharp: atan2(AP.y, AP.x)). Vị trí chữ vẫn theo InsertPoint nên xoay được
        // mà không dời chữ — miễn là điểm neo là TopLeft (khi đó AutoCAD không dùng AP làm vị trí).
        var mtexts = list.OfType<MText>()
            .Where(m => m.AttachmentPoint == ACadSharp.Entities.AttachmentPointType.TopLeft)
            .ToList();

        if (texts.Count == 0 && mtexts.Count == 0) return 0;

        var radians = degrees * Math.PI / 180.0;

        var beforeText = texts.ToDictionary(t => t, t => t.Rotation);
        var beforeMText = mtexts.ToDictionary(m => m, m => m.AlignmentPoint);

        var changed = texts.Any(t => Math.Abs(t.Rotation - radians) > 1e-9) ||
                      mtexts.Any(m => Math.Abs(m.Rotation - radians) > 1e-9);

        if (!changed) return 0;

        // Hướng đơn vị; AP phải khác gốc toạ độ, nếu không góc không xác định
        var apX = Math.Cos(radians) * 100;
        var apY = Math.Sin(radians) * 100;

        var changedCount = 0;

        var command = new DelegateCommand(name,
            () =>
            {
                foreach (var target in texts) target.Rotation = radians;

                foreach (var target in mtexts)
                    target.AlignmentPoint = new CSMath.XYZ(apX, apY, 0);

                changedCount = texts.Count + mtexts.Count;

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                foreach (var target in texts) target.Rotation = beforeText[target];

                foreach (var target in mtexts) target.AlignmentPoint = beforeMText[target];

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return changedCount;
    }

    /// <summary>
    /// OVERKILL (phần 2): gộp các đường thẳng **chồng một phần** hoặc nối tiếp trên cùng
    /// một đường thẳng (cùng lớp, cùng hướng) thành một đoạn. Trả về số đối tượng đã bỏ.
    /// Có undo. Dùng cho bản vẽ nhập từ nơi khác bị chia nhỏ nét.
    /// </summary>
    public int MergeOverlappingLines(double tolerance = 0.5, string name = "OVERKILL")
    {
        var merged = new List<(Line Result, List<Line> Replaced)>();
        var consumed = new HashSet<Line>();

        var candidates = Workspace.Document.Entities
            .OfType<Line>()
            .Where(l => !consumed.Contains(l))
            .ToList();

        // Nhóm theo (lớp, hướng đường, khoảng cách tới gốc toạ độ)
        var groups = new Dictionary<string, List<Line>>(StringComparer.Ordinal);

        foreach (var line in candidates)
        {
            var key = LineKey(line, tolerance);

            if (key is null) continue;

            if (!groups.TryGetValue(key, out var bucket))
            {
                bucket = new List<Line>();
                groups[key] = bucket;
            }

            bucket.Add(line);
        }

        foreach (var bucket in groups.Values)
        {
            if (bucket.Count < 2) continue;

            // Chiếu mọi đoạn lên trục của đường, rồi gộp các khoảng chồng nhau
            var reference = bucket[0];
            var origin = new Pt(reference.StartPoint.X, reference.StartPoint.Y);
            var direction = new Pt(
                reference.EndPoint.X - reference.StartPoint.X,
                reference.EndPoint.Y - reference.StartPoint.Y);

            var length = direction.Length;

            if (length < 1e-9) continue;

            direction = new Pt(direction.X / length, direction.Y / length);

            var intervals = bucket
                .Select(l => (
                    Line: l,
                    From: Project(l.StartPoint, origin, direction),
                    To: Project(l.EndPoint, origin, direction)))
                .Select(x => (x.Line, From: Math.Min(x.From, x.To), To: Math.Max(x.From, x.To)))
                .OrderBy(x => x.From)
                .ToList();

            var runFrom = intervals[0].From;
            var runTo = intervals[0].To;
            var runLines = new List<Line> { intervals[0].Line };

            void FlushRun()
            {
                if (runLines.Count < 2) return;

                // Đã gộp được ít nhất 2 đoạn → thay bằng một đoạn
                var start = new CSMath.XYZ(
                    origin.X + direction.X * runFrom,
                    origin.Y + direction.Y * runFrom, 0);

                var end = new CSMath.XYZ(
                    origin.X + direction.X * runTo,
                    origin.Y + direction.Y * runTo, 0);

                var result = new Line
                {
                    StartPoint = start,
                    EndPoint = end,
                    Layer = runLines[0].Layer,
                    Color = runLines[0].Color,
                    LineWeight = runLines[0].LineWeight,
                };

                merged.Add((result, runLines.ToList()));
            }

            for (var i = 1; i < intervals.Count; i++)
            {
                var item = intervals[i];

                // Chồng nhau hoặc nối tiếp trong dung sai → cùng một nhóm
                if (item.From <= runTo + tolerance)
                {
                    runTo = Math.Max(runTo, item.To);
                    runLines.Add(item.Line);
                }
                else
                {
                    FlushRun();

                    runFrom = item.From;
                    runTo = item.To;
                    runLines = new List<Line> { item.Line };
                }
            }

            FlushRun();
        }

        if (merged.Count == 0) return 0;

        var command = new DelegateCommand(name,
            () =>
            {
                foreach (var (result, replaced) in merged)
                {
                    foreach (var old in replaced) Workspace.Document.Entities.Remove(old);

                    Workspace.Document.Entities.Add(result);
                }

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                foreach (var (result, replaced) in merged)
                {
                    Workspace.Document.Entities.Remove(result);

                    foreach (var old in replaced) Workspace.Document.Entities.Add(old);
                }

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return merged.Sum(m => m.Replaced.Count - 1);
    }

    /// <summary>Khoá nhóm: cùng lớp + cùng hướng (đã chuẩn hoá) + cùng khoảng cách tới gốc.</summary>
    private static string? LineKey(Line line, double tolerance)
    {
        var dx = line.EndPoint.X - line.StartPoint.X;
        var dy = line.EndPoint.Y - line.StartPoint.Y;

        var length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 1e-9) return null;

        // Hướng chuẩn hoá, đảo chiều cho trùng (đường vô hướng)
        var ux = dx / length;
        var uy = dy / length;

        if (ux < 0 || (Math.Abs(ux) < 1e-12 && uy < 0))
        {
            ux = -ux;
            uy = -uy;
        }

        // Khoảng cách có dấu từ gốc tới đường (pháp tuyến)
        var offset = -uy * line.StartPoint.X + ux * line.StartPoint.Y;

        var quant = Math.Max(tolerance, 1e-6);

        var layer = line.Layer?.Name ?? "0";

        return string.Join("|",
            layer,
            Math.Round(ux / 1e-3),
            Math.Round(uy / 1e-3),
            Math.Round(offset / quant));
    }

    private static double Project(CSMath.XYZ point, Pt origin, Pt direction)
        => (point.X - origin.X) * direction.X + (point.Y - origin.Y) * direction.Y;
    /// <summary>
    /// OVERKILL: xoá các đối tượng trùng hình học (cùng loại, cùng layer, cùng hình).
    /// Giữ lại bản đầu tiên. Trả về số đối tượng đã xoá. Có undo.
    /// </summary>
    public int RemoveDuplicates(string name = "OVERKILL")
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var victims = new List<Entity>();

        foreach (var entity in Workspace.Document.Entities.ToList())
        {
            string signature;

            try { signature = OpenCad.Core.Audit.DrawingAudit.Signature(entity); }
            catch { continue; }

            if (signature.Length == 0) continue;

            if (!seen.Add(signature)) victims.Add(entity);
        }

        if (victims.Count == 0) return 0;

        var command = new DelegateCommand(name,
            () =>
            {
                foreach (var entity in victims) Workspace.Remove(entity);

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                foreach (var entity in victims) Workspace.Add(entity);

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return victims.Count;
    }

    /// <summary>
    /// LTSCALE: đặt tỷ lệ kiểu nét cho toàn bộ đối tượng (AutoCAD dùng biến toàn cục;
    /// ở đây áp thẳng vào từng entity vì định dạng không có biến đó). Có undo.
    /// Trả về số entity đã đổi.
    /// </summary>
    public int SetLineTypeScaleAll(double scale, string name = "LTSCALE")
    {
        if (scale <= 1e-6) return 0;

        var entities = Workspace.Document.Entities.ToList();

        if (entities.Count == 0) return 0;

        var previous = entities.Select(e => e.LineTypeScale).ToArray();

        var command = new DelegateCommand(name,
            () =>
            {
                foreach (var entity in entities)
                {
                    try { entity.LineTypeScale = scale; }
                    catch { }
                }

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                for (var i = 0; i < entities.Count; i++)
                {
                    try { entities[i].LineTypeScale = previous[i]; }
                    catch { }
                }

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return entities.Count;
    }

    /// <summary>Danh sách entity bị xoá gần nhất (cho lệnh OOPS).</summary>
    private readonly List<Entity> _lastErased = new();

    /// <summary>Số entity có thể phục hồi bằng OOPS.</summary>
    public int LastErasedCount => _lastErased.Count;

    /// <summary>Ghi nhớ các entity vừa bị xoá (gọi từ ERASE).</summary>
    public void RememberErased(IReadOnlyList<Entity> entities)
    {
        _lastErased.Clear();
        _lastErased.AddRange(entities);
    }

    /// <summary>
    /// OOPS: phục hồi các entity vừa bị xoá, kể cả khi đã undo/redo việc khác.
    /// Trả về số entity đã phục hồi (có undo).
    /// </summary>
    public int RestoreLastErased(string name = "Oops")
    {
        var restored = _lastErased
            .Where(e => !Workspace.Document.Entities.Contains(e))
            .ToList();

        if (restored.Count == 0) return 0;

        var command = new DelegateCommand(name,
            () =>
            {
                foreach (var entity in restored) Workspace.Add(entity);
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                foreach (var entity in restored) Workspace.Remove(entity);
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return restored.Count;
    }

    /// <summary>Undo nhiều bước liên tiếp (U 5). Trả về số bước đã lùi.</summary>
    public int Undo(int steps)
    {
        var done = 0;

        for (var i = 0; i < steps && History.CanUndo; i++)
            if (Undo()) done++;

        return done;
    }

    /// <summary>Đổi nội dung chữ (có undo).</summary>
    public void SetText(Entity entity, string value, string name = "Edit text")
    {
        if (entity is not TextEntity text) return;
        if (string.IsNullOrEmpty(value)) return;

        var previous = text.Value;

        var command = new DelegateCommand(name,
            () =>
            {
                text.Value = value;
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                text.Value = previous;
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Tạo định nghĩa block từ các entity đang chọn: entity được sao chép vào block
    /// (dời về quanh gốc), bản gốc bị xoá và thay bằng một tham chiếu tại <paramref name="basePoint"/>.
    /// </summary>
    public bool CreateBlock(IReadOnlyList<Entity> entities, string name, Pt basePoint)
    {
        if (entities.Count == 0) return false;
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (BlockLibrary.Find(Workspace.Document, name) is not null) return false;

        Layer? blockLayer = null;

        try { blockLayer = Workspace.FindLayer("0"); }
        catch { }

        BlockRecord? block = null;
        Insert? insert = null;

        var command = new DelegateCommand("Create block",
            () =>
            {
                block = new BlockRecord(name);
                Workspace.Document.BlockRecords.Add(block);

                var shift = Affine2.Translate(-basePoint.X, -basePoint.Y);

                foreach (var entity in entities)
                {
                    var copy = entity.Clone() as Entity;
                    if (copy is null) continue;

                    EntityTransform.Apply(copy, shift);
                    if (blockLayer is not null) copy.Layer = blockLayer;

                    block.Entities.Add(copy);
                }

                foreach (var entity in entities) Workspace.Remove(entity);

                insert = BlockLibrary.Create(block, basePoint);
                if (insert is not null) Workspace.Add(insert);

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                if (insert is not null) Workspace.Remove(insert);
                foreach (var entity in entities) Workspace.Add(entity);

                if (block is not null)
                {
                    try { Workspace.Document.BlockRecords.Remove(block.Name); } catch { }
                }

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return insert is not null;
    }

    // -------------------------------------------------------------- PROPERTY

    /// <summary>Đổi màu cho các entity (có undo).</summary>
    public void SetColor(IReadOnlyList<Entity> entities, ACadSharp.Color color, string name = "Change color")
    {
        if (entities.Count == 0) return;

        var previous = entities.Select(e => e.Color).ToArray();

        var command = new DelegateCommand(name,
            () =>
            {
                for (var i = 0; i < entities.Count; i++) entities[i].Color = color;
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                for (var i = 0; i < entities.Count; i++) entities[i].Color = previous[i];
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Thêm định nghĩa attribute (ATTDEF) vào block và tạo attribute tương ứng cho các
    /// insert đang có, để nhãn hiện ra ngay. Có undo.
    /// </summary>
    public AttributeDefinition? AddAttributeDefinition(
        ACadSharp.Tables.BlockRecord block,
        string tag,
        string defaultValue,
        Pt position,
        double height = 250,
        string name = "ATTDEF")
    {
        if (block is null || string.IsNullOrWhiteSpace(tag)) return null;

        var definition = new AttributeDefinition
        {
            Tag = tag.Trim(),
            Value = defaultValue ?? string.Empty,
            InsertPoint = new CSMath.XYZ(position.X, position.Y, 0),
            Height = Math.Max(height, 1e-6),
            Layer = Workspace.Document.Layers.FirstOrDefault(),
        };

        var created = new List<AttributeEntity>();

        // Insert hiện có: thêm attribute ở toạ độ WCS tương ứng
        foreach (var insert in Workspace.Document.Entities.OfType<Insert>())
        {
            if (!ReferenceEquals(insert.Block, block)) continue;

            try
            {
                insert.Attributes.Add(new AttributeEntity
                {
                    Tag = definition.Tag,
                    Value = definition.Value,
                    InsertPoint = new CSMath.XYZ(
                        insert.InsertPoint.X + position.X * insert.XScale,
                        insert.InsertPoint.Y + position.Y * insert.YScale,
                        0),
                    Height = definition.Height,
                    Layer = definition.Layer,
                });

                created.Add(insert.Attributes.Last());
            }
            catch { }
        }

        var command = new DelegateCommand(name,
            () =>
            {
                block.Entities.Add(definition);

                foreach (var attribute in created)
                {
                    var owner = Workspace.Document.Entities
                        .OfType<Insert>()
                        .FirstOrDefault(i => ReferenceEquals(i.Block, block));

                    if (owner is null) continue;
                    if (owner.Attributes.Any(a => ReferenceEquals(a, attribute))) continue;

                    owner.Attributes.Add(attribute);
                }

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                foreach (var insert in Workspace.Document.Entities.OfType<Insert>())
                    foreach (var attribute in insert.Attributes.ToList())
                        if (string.Equals(attribute.Tag, definition.Tag, StringComparison.OrdinalIgnoreCase)
                            && created.Contains(attribute))
                            insert.Attributes.Remove(attribute);

                block.Entities.Remove(definition);

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return definition;
    }

    /// <summary>
    /// Đổi thuộc tính hatch cho các entity đã chọn (tỷ lệ, góc, tên pattern). Có undo.
    /// Trả về số hatch đã đổi.
    /// </summary>
    public int SetHatchProperties(IReadOnlyList<Entity> entities,
                                  double? scale = null,
                                  double? angle = null,
                                  string? pattern = null,
                                  string name = "Hatch edit")
    {
        var hatches = entities.OfType<Hatch>().ToList();

        if (hatches.Count == 0) return 0;

        // Tên mẫu phải có trong thư viện, nếu không thì từ chối (tránh đặt tên rỗng nghĩa)
        var solidRequested = pattern is not null &&
                             pattern.Trim().Equals("SOLID", StringComparison.OrdinalIgnoreCase);

        if (pattern is not null && !solidRequested &&
            OpenCad.Core.Styles.HatchPatternLibrary.Find(pattern) is null)
            return 0;

        var previous = hatches
            .Select(h => (Hatch: h, h.PatternScale, h.PatternAngle, Pattern: h.Pattern?.Name, h.IsSolid))
            .ToList();

        void Apply(double targetScale, double targetAngle, string? targetPattern)
        {
            foreach (var hatch in hatches)
            {
                try
                {
                    if (scale is not null) hatch.PatternScale = targetScale;
                    if (angle is not null) hatch.PatternAngle = targetAngle;

                    if (pattern is not null)
                    {
                        // Đặt mẫu phải BỎ chế độ đặc, nếu không mẫu bị bỏ qua khi vẽ
                        if (solidRequested)
                        {
                            hatch.IsSolid = true;
                        }
                        else if (!string.IsNullOrWhiteSpace(targetPattern))
                        {
                            hatch.Pattern = new HatchPattern(targetPattern);
                            hatch.IsSolid = false;
                        }
                    }
                }
                catch { }
            }

            Scene.Build(Workspace.Document, IsEntityVisible);
            Changed?.Invoke();
        }

        var command = new DelegateCommand(name,
            () => Apply(scale ?? previous[0].PatternScale,
                        angle ?? previous[0].PatternAngle,
                        pattern),
            () =>
            {
                foreach (var entry in previous)
                {
                    try
                    {
                        entry.Hatch.PatternScale = entry.PatternScale;
                        entry.Hatch.PatternAngle = entry.PatternAngle;
                        entry.Hatch.IsSolid = entry.IsSolid;

                        if (entry.Pattern is not null)
                            entry.Hatch.Pattern = new HatchPattern(entry.Pattern);
                    }
                    catch { }
                }

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return hatches.Count;
    }

    /// <summary>
    /// Đổi tỷ lệ một viewport trên tờ: <paramref name="unitsPerMm"/> là mẫu số tỷ lệ
    /// (50 = 1:50). Giữ nguyên tâm và kích thước khung, chỉ đổi vùng nhìn. Có undo.
    /// </summary>
    public bool SetViewportScale(Viewport viewport, double unitsPerMm, string name = "Viewport scale")
    {
        if (viewport is null || unitsPerMm <= 1e-9) return false;

        var previous = viewport.ViewHeight;
        // ViewHeight là số đơn vị bản vẽ trên chiều cao khung: 1:50 → ViewHeight = Height × 50
        var target = viewport.Height * unitsPerMm;

        if (target <= 1e-9) return false;

        var command = new DelegateCommand(name,
            () =>
            {
                viewport.ViewHeight = target;
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                viewport.ViewHeight = previous;
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return true;
    }

    /// <summary>
    /// Dời viewport trên tờ theo (dx, dy) tính bằng mm giấy. Có undo.
    /// </summary>
    public bool MoveViewport(Viewport viewport, double dx, double dy, string name = "Move viewport")
    {
        if (viewport is null) return false;

        var before = viewport.Center;

        var command = new DelegateCommand(name,
            () =>
            {
                viewport.Center = new CSMath.XYZ(
                    before.X + dx, before.Y + dy, before.Z);

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                viewport.Center = before;
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return true;
    }

    /// <summary>
    /// Đổi kích thước khung viewport (mm giấy), giữ nguyên tâm và tỷ lệ.
    /// Có undo.
    /// </summary>
    public bool ResizeViewport(Viewport viewport, double width, double height,
                               string name = "Resize viewport")
    {
        if (viewport is null || width <= 1e-6 || height <= 1e-6) return false;

        var beforeWidth = viewport.Width;
        var beforeHeight = viewport.Height;
        var beforeView = viewport.ViewHeight;

        // Giữ tỷ lệ: vùng nhìn co giãn theo chiều cao khung
        var scale = ViewportScale(viewport);
        var targetViewHeight = scale > 1e-9 ? height * scale : beforeView;

        var command = new DelegateCommand(name,
            () =>
            {
                viewport.Width = width;
                viewport.Height = height;
                viewport.ViewHeight = targetViewHeight;

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                viewport.Width = beforeWidth;
                viewport.Height = beforeHeight;
                viewport.ViewHeight = beforeView;

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return true;
    }

    /// <summary>Tỷ lệ hiện tại của viewport (mẫu số: 50 = 1:50).</summary>
    public static double ViewportScale(Viewport viewport)
    {
        if (viewport is null || viewport.ViewHeight <= 1e-9) return 0;

        return viewport.ViewHeight / viewport.Height;
    }

    /// <summary>
    /// Chép thuộc tính hiển thị từ <paramref name="source"/> sang các entity khác:
    /// layer, màu, độ đậm, kiểu nét (và với chữ: chiều cao, góc xoay). Có undo.
    /// Trả về số entity đã đổi.
    /// </summary>
    public int MatchProperties(Entity source, IReadOnlyList<Entity> targets, string name = "Match properties")
    {
        if (source is null || targets.Count == 0) return 0;

        var previous = targets.Select(t => (
            Entity: t,
            Layer: t.Layer,
            Color: t.Color,
            Weight: t.LineWeight,
            LineType: t.LineType,
            Height: (t as TextEntity)?.Height,
            Rotation: (t as TextEntity)?.Rotation)).ToList();

        var affected = 0;

        void Apply()
        {
            foreach (var target in targets)
            {
                if (ReferenceEquals(target, source)) continue;

                try
                {
                    if (source.Layer is not null) target.Layer = source.Layer;

                    target.Color = source.Color;
                    target.LineWeight = source.LineWeight;

                    if (source.LineType is not null) target.LineType = source.LineType;

                    if (target is TextEntity text)
                    {
                        if (source is TextEntity sourceText)
                        {
                            text.Height = sourceText.Height;
                            text.Rotation = sourceText.Rotation;
                        }
                    }

                    affected++;
                }
                catch { }
            }

            Scene.Build(Workspace.Document, IsEntityVisible);
            Changed?.Invoke();
        }

        var command = new DelegateCommand(name,
            Apply,
            () =>
            {
                foreach (var entry in previous)
                {
                    try
                    {
                        entry.Entity.Layer = entry.Layer;
                        entry.Entity.Color = entry.Color;
                        entry.Entity.LineWeight = entry.Weight;
                        entry.Entity.LineType = entry.LineType;

                        if (entry.Entity is TextEntity text)
                        {
                            if (entry.Height is not null) text.Height = entry.Height.Value;
                            if (entry.Rotation is not null) text.Rotation = entry.Rotation.Value;
                        }
                    }
                    catch { }
                }

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return affected;
    }

    /// <summary>Đổi kiểu nét cho các entity (có undo).</summary>
    public void SetLineType(IReadOnlyList<Entity> entities, ACadSharp.Tables.LineType? lineType,
                            string name = "Change linetype")
    {
        if (entities.Count == 0 || lineType is null) return;

        var previous = entities.Select(e => e.LineType).ToArray();

        var command = new DelegateCommand(name,
            () =>
            {
                for (var i = 0; i < entities.Count; i++)
                {
                    try { entities[i].LineType = lineType; }
                    catch { }
                }

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                for (var i = 0; i < entities.Count; i++)
                {
                    try { entities[i].LineType = previous[i]; }
                    catch { }
                }

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();
    }

    /// <summary>Đổi độ đậm nét cho các entity (có undo).</summary>
    public void SetLineWeight(IReadOnlyList<Entity> entities, ACadSharp.LineWeightType weight,
                              string name = "Change lineweight")
    {
        if (entities.Count == 0) return;

        var previous = entities.Select(e => e.LineWeight).ToArray();

        var command = new DelegateCommand(name,
            () =>
            {
                for (var i = 0; i < entities.Count; i++) entities[i].LineWeight = weight;
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                for (var i = 0; i < entities.Count; i++) entities[i].LineWeight = previous[i];
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();
    }

    /// <summary>Tạo layer mới (có undo). Trả về null nếu tên đã tồn tại.</summary>
    public Layer? CreateLayer(string name, ACadSharp.Color color)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (Workspace.FindLayer(name) is not null) return null;

        Layer? layer = null;

        var command = new DelegateCommand("New layer",
            () =>
            {
                layer = new Layer(name) { Color = color };
                try { Workspace.Document.Layers.Add(layer); } catch { }
                Changed?.Invoke();
            },
            () =>
            {
                if (layer is null) return;
                try { Workspace.Document.Layers.Remove(layer.Name); } catch { }
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return layer;
    }

    /// <summary>Đổi màu cả layer (có undo).</summary>
    /// <summary>
    /// Cô lập: tắt mọi layer trừ layer của các đối tượng cho trước (LAYISO).
    /// Trả về danh sách layer đã tắt. Có undo.
    /// </summary>
    public List<string> IsolateLayers(IReadOnlyList<Entity> entities, string name = "Isolate layers")
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities)
            if (entity.Layer is not null) keep.Add(entity.Layer.Name);

        if (keep.Count == 0) return new List<string>();

        var turnedOff = new List<Layer>();

        foreach (var layer in Workspace.Document.Layers)
        {
            if (!layer.IsOn) continue;
            if (keep.Contains(layer.Name)) continue;

            turnedOff.Add(layer);
        }

        if (turnedOff.Count == 0) return new List<string>();

        var command = new DelegateCommand(name,
            () =>
            {
                foreach (var layer in turnedOff) layer.IsOn = false;

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                foreach (var layer in turnedOff) layer.IsOn = true;

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return turnedOff.Select(l => l.Name).ToList();
    }

    /// <summary>Bật lại mọi layer (LAYUNISO). Trả về số layer đã bật. Có undo.</summary>
    public int ShowAllLayers(string name = "Show all layers")
    {
        var hidden = Workspace.Document.Layers.Where(l => !l.IsOn).ToList();

        if (hidden.Count == 0) return 0;

        var command = new DelegateCommand(name,
            () =>
            {
                foreach (var layer in hidden) layer.IsOn = true;

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                foreach (var layer in hidden) layer.IsOn = false;

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return hidden.Count;
    }

    /// <summary>
    /// Ghi đè chữ của kích thước (DIMTEDIT). Chuỗi rỗng/null = trả về số đo thật.
    /// Có undo. Trả về số kích thước đã đổi.
    /// </summary>
    public int SetDimensionText(IReadOnlyList<Entity> entities, string? text, string name = "Dimension text")
    {
        var dimensions = entities.OfType<Dimension>().ToList();

        if (dimensions.Count == 0) return 0;

        var previous = dimensions.Select(d => d.Text).ToArray();
        var value = string.IsNullOrWhiteSpace(text) ? null : text!.Trim();

        var command = new DelegateCommand(name,
            () =>
            {
                for (var i = 0; i < dimensions.Count; i++)
                {
                    try { dimensions[i].Text = value; }
                    catch { }
                }

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                for (var i = 0; i < dimensions.Count; i++)
                {
                    try { dimensions[i].Text = previous[i]; }
                    catch { }
                }

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return dimensions.Count;
    }

    /// <summary>Đổi tên layer (có undo). Trả về false nếu tên trống hoặc đã có layer khác dùng.</summary>
    public bool RenameLayer(Layer layer, string newName, string name = "Rename layer")
    {
        if (layer is null) return false;

        newName = (newName ?? string.Empty).Trim();

        if (newName.Length == 0) return false;

        if (string.Equals(layer.Name, newName, StringComparison.Ordinal)) return true;

        var taken = Workspace.Document.Layers.Any(l =>
            !ReferenceEquals(l, layer) &&
            string.Equals(l.Name, newName, StringComparison.OrdinalIgnoreCase));

        if (taken) return false;

        var previous = layer.Name;

        var command = new DelegateCommand(name,
            () => { layer.Name = newName; Changed?.Invoke(); },
            () => { layer.Name = previous; Changed?.Invoke(); });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return true;
    }

    /// <summary>
    /// Xoá layer (có undo). Từ chối nếu là layer "0", layer hiện hành hoặc còn entity dùng.
    /// Trả về thông báo lỗi, hoặc null nếu xoá thành công.
    /// </summary>
    public string? DeleteLayer(Layer layer, string? currentLayerName = null, string name = "Delete layer")
    {
        if (layer is null) return "Layer không tồn tại.";

        if (string.Equals(layer.Name, "0", StringComparison.Ordinal))
            return "Không xoá được layer 0.";

        if (currentLayerName is not null &&
            string.Equals(layer.Name, currentLayerName, StringComparison.OrdinalIgnoreCase))
            return "Không xoá được layer đang dùng.";

        var used = Workspace.Document.Entities.Any(e => ReferenceEquals(e.Layer, layer))
                   || Workspace.Document.BlockRecords
                       .SelectMany(b => b.Entities)
                       .Any(e => ReferenceEquals(e.Layer, layer));

        if (used) return "Layer còn đối tượng, không xoá được.";

        var command = new DelegateCommand(name,
            () => Workspace.Document.Layers.Remove(layer.Name),
            () => Workspace.Document.Layers.Add(layer));

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return null;
    }

    /// <summary>
    /// LAYDEL: xoá layer VÀ mọi đối tượng trên layer đó (giống AutoCAD).
    /// Khác DeleteLayer (chỉ xoá layer rỗng). Có undo. Trả về số đối tượng đã xoá.
    /// </summary>
    public int DeleteLayerAndContents(Layer layer, string? currentLayerName = null,
                                      string name = "Delete layer and contents")
    {
        if (layer is null) return -1;

        if (string.Equals(layer.Name, "0", StringComparison.Ordinal)) return -1;

        if (currentLayerName is not null &&
            string.Equals(layer.Name, currentLayerName, StringComparison.OrdinalIgnoreCase))
            return -1;

        var victims = Workspace.Document.Entities
            .Where(e => ReferenceEquals(e.Layer, layer))
            .ToList();

        var command = new DelegateCommand(name,
            () =>
            {
                foreach (var entity in victims) Workspace.Remove(entity);

                try { Workspace.Document.Layers.Remove(layer.Name); } catch { }

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                try { Workspace.Document.Layers.Add(layer); } catch { }

                foreach (var entity in victims) Workspace.Add(entity);

                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();

        return victims.Count;
    }

    public void SetLayerColor(Layer layer, ACadSharp.Color color, string name = "Layer color")
    {
        var previous = layer.Color;

        var command = new DelegateCommand(name,
            () =>
            {
                layer.Color = color;
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            },
            () =>
            {
                layer.Color = previous;
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();
    }

    public void SetLayer(IReadOnlyList<Entity> entities, Layer layer, string name = "Change layer")
    {
        if (entities.Count == 0) return;

        var previous = entities.Select(e => e.Layer).ToArray();

        var command = new DelegateCommand(name,
            () => { foreach (var e in entities) e.Layer = layer; Scene.Build(Workspace.Document, IsEntityVisible); Changed?.Invoke(); },
            () =>
            {
                for (var i = 0; i < entities.Count; i++) entities[i].Layer = previous[i];
                Scene.Build(Workspace.Document, IsEntityVisible);
                Changed?.Invoke();
            });

        History.Execute(command);
        Workspace.MarkModified();
        StateChanged?.Invoke();
    }

    // --------------------------------------------------------------- HISTORY

    public bool Undo()
    {
        var ok = History.Undo();
        if (ok) SyncModified();
        return ok;
    }

    public bool Redo()
    {
        var ok = History.Redo();
        if (ok) SyncModified();
        return ok;
    }

    /// <summary>Đánh dấu mốc "đã lưu" sau khi ghi file thành công.</summary>
    public void MarkSaved()
    {
        History.MarkSavePoint();
        Workspace.SetModified(false);
        StateChanged?.Invoke();
    }

    /// <summary>Cờ "đã sửa" bám theo vị trí con trỏ lịch sử so với mốc lưu.</summary>
    private void SyncModified()
    {
        Workspace.SetModified(!History.IsAtSavePoint);
        StateChanged?.Invoke();
    }
}

using ACadSharp.Entities;
using ACadSharp.Tables;
using OpenCad.Core;
using OpenCad.Core.Editing;
using OpenCad.Core.Geometry;
using OpenCad.Core.Snapping;

namespace OpenCad.App;

public enum EditMode
{
    Select,
    Line,
    XLine,
    Ray,
    Polyline,
    Rectangle,
    Circle,
    Arc,
    Text,
    Move,
    Copy,
    Rotate,
    Scale,
    Mirror,
    Offset,
    Trim,
    Extend,
    Hatch,
    Fillet,
    Chamfer,
    Dimension,
    DimensionRadius,
    DimensionDiameter,
    DimensionAngular,
    DimensionOrdinate,
    Ellipse,
    Polygon,
    Dist,
    Area,
    Insert,
    BlockCreate,
    EditTextPick,
    PaperText,
    PaperViewport,
    Array,
    ArrayPolar,
    Stretch,
    AttributeEdit,
    AttributeDefine,
    Divide,
    Measure,
    PaperLine,
    MatchSource,
    MatchTarget,
    ZoomWindow,
    Spline,
    PointDraw,
    MLine,
    DimContinue,
    DimBaseline,
    TableDraw,
    MLeader,
}

internal enum InputRequest
{
    None,
    Text,
    ScaleFactor,
    RotateAngle,
    OffsetDistance,
    FilletRadius,
    ChamferDistance,
    PolygonSides,
    BlockName,
    NewBlockName,
    EditText,
    PaperText,
    VportScale,
    ArrayRowsCols,
    ArraySpacing,
    ArrayCount,
    AttributeValue,
    AttributeNew,
    HatchProperty,
    TableSize,
    TableInsertPoint,
    MLeaderText,
    ViewportScaleNew,
    DimensionText,
    DivideCount,
    MeasureSpacing,
}

/// <summary>
/// Vùng vẽ kiểu AutoCAD: pan/zoom, object snap, ortho, chọn theo khung,
/// kéo di chuyển và bộ công cụ vẽ/sửa.
/// </summary>
public sealed class CadCanvas : Control
{
    private readonly CadRenderer _renderer = new();
    private readonly ViewTransform _view = new();
    private readonly HashSet<Entity> _selection = new();
    private readonly SnapEngine _snap = new();

    private CadEditor? _editor;

    // Trạng thái chuột
    private bool _panning;
    private PointF _lastMouse;
    private bool _draggingEntities;
    private Pt _dragAnchor;
    private Affine2 _dragAccum = Affine2.Identity;
    private bool _boxSelecting;
    private Pt _boxStart;

    // Trạng thái lệnh
    private EditMode _mode = EditMode.Select;
    private readonly List<Pt> _points = new();
    private Pt _cursorRaw;
    private Pt _cursor;
    private SnapResult? _activeSnap;
    private InputRequest _input = InputRequest.None;
    private string? _statusOverride;
    private double _offsetDistance;
    private int _polygonSides = 6;
    private ACadSharp.Tables.BlockRecord? _pendingBlock;
    private string? _pendingBlockName;
    private TextEntity? _editingText;
    private CadScene? _paperScene;
    private ACadSharp.Tables.BlockRecord? _paperBlock;
    private readonly HashSet<Entity> _paperSelection = new();
    private Pt _paperTextPoint;
    private bool _paperDragging;
    private PointF _paperDragFromScreen;
    private PointF _paperDragToScreen;
    private bool _editingPaperText;
    private PointF _lastMouseScreen;
    private double _vportScale = 100;
    private int _arrayRows = 1;
    private int _arrayCols = 1;
    private int _arrayCount = 6;
    private ACadSharp.Entities.Insert? _attributeTarget;
    private Pt _attributeDefinePoint;
    private Entity? _matchSource;

    // Trạng thái DIMCONTINUE
    private DimensionAligned? _lastDimension;
    private Pt _lastDimFirst;
    private Pt _lastDimSecond;
    private Pt _lastDimLocation;
    private int _baselineStep;

    // TABLE
    private int _tableRows;
    private int _tableColumns;
    private Viewport? _selectedViewport;

    // Đang kéo góc để đổi kích thước viewport
    private bool _resizingViewport;
    private Pt _resizeAnchorWorld;
    private int _resizeCornerX;
    private int _resizeCornerY;
    private Pt _viewportDragStart;
    private CSMath.XYZ _viewportDragOrigin;
    private Entity? _gripEntity;
    private Grip? _grip;
    private Entity? _gripOriginal;
    private bool _gripDragging;


    /// <summary>Style kích thước đang dùng cho lệnh DIM.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ACadSharp.Tables.DimensionStyle? CurrentDimensionStyle { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ShowGrid { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ShowCrosshair { get; set; } = true;

    public event EventHandler? SelectionChanged;
    public event Action<string>? PromptChanged;
    public event Action? StateChanged;
    public event Action<Pt>? CursorMoved;

    /// <summary>Thông báo kết quả (DIST/AREA…) — MainForm ghi vào log và status bar.</summary>
    public event Action<string>? Message;

    public Pt CursorWorld => _cursor;
    public SnapResult? ActiveSnap => _activeSnap;
    public bool AwaitingInput => _input != InputRequest.None;

    /// <summary>Tờ in đang xem. Null = đang ở model space.</summary>
    public OpenCad.Core.Layout.PaperSheet? Sheet { get; private set; }
    public bool IsPaperMode => Sheet is not null;

    /// <summary>Chuyển canvas sang xem một tờ in (paper space), hoặc về model space.</summary>
    public void ShowPaper(OpenCad.Core.Layout.PaperSheet? sheet)
        => ShowPaper(sheet, null, string.Empty);

    /// <summary>Xem/ sửa một tờ in: block record chứa các đối tượng paper space.</summary>
    public void ShowPaper(OpenCad.Core.Layout.PaperSheet? sheet,
                          ACadSharp.Tables.BlockRecord? block,
                          string layoutName = "")
    {
        Sheet = sheet;
        _paperBlock = block;
        _paperSelection.Clear();
        _paperScene = null;

        // Nhớ tờ đang mở để lệnh LAYOUTDEL biết cần xoá tờ nào
        if (sheet is null) ActiveLayoutName = string.Empty;
        else if (layoutName.Length > 0) ActiveLayoutName = layoutName;

        if (sheet is not null && block is not null)
        {
            _paperScene = new CadScene();

            try
            {
                _paperScene.Build(block.Entities.Where(e => e is not Viewport));
            }
            catch { }
        }

        _selection.Clear();
        _points.Clear();
        _mode = EditMode.Select;
        _input = InputRequest.None;
        _activeSnap = null;

        ZoomExtents();
        RaiseSelection();
        Raised();
        Invalidate();
    }

    private void Raised() => PromptChanged?.Invoke(Prompt);

    public void Undo()
    {
        _editor?.Undo();
        _selection.RemoveWhere(e => !(_editor?.Workspace.Document.Entities.Contains(e) ?? false));
        RaiseSelection();
        Invalidate();
    }

    public void Redo()
    {
        _editor?.Redo();
        _selection.RemoveWhere(e => !(_editor?.Workspace.Document.Entities.Contains(e) ?? false));
        RaiseSelection();
        Invalidate();
    }

    public CadCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint |
                 ControlStyles.StandardClick |
                 ControlStyles.StandardDoubleClick |
                 ControlStyles.ResizeRedraw, true);
        BackColor = _renderer.Background;
        TabStop = true;
    }

    public CadEditor? Editor => _editor;
    public IReadOnlyCollection<Entity> Selection => _selection;
    public EditMode Mode => _mode;
    public SnapEngine Snap => _snap;

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Layer? CurrentLayer { get; set; }

    public string Prompt
    {
        get
        {
            if (_statusOverride is not null) return _statusOverride;


            return (_input, _mode) switch
            {
                (InputRequest.Text, _) => "Nhập nội dung chữ rồi Enter:",
                (InputRequest.ScaleFactor, _) => "Nhập hệ số scale rồi Enter:",
                (InputRequest.RotateAngle, _) => "Nhập góc xoay (độ) rồi Enter:",
                (InputRequest.OffsetDistance, _) => "OFFSET: nhập khoảng cách rồi Enter:",
                (InputRequest.FilletRadius, _) => "FILLET: nhập bán kính bo góc rồi Enter:",
                (InputRequest.ChamferDistance, _) => "CHAMFER: nhập khoảng vát (hoặc d1,d2) rồi Enter:",
                (InputRequest.PolygonSides, _) => "POLYGON: nhập số cạnh (3–1024) rồi Enter:",
                (InputRequest.BlockName, _) => "INSERT: nhập tên block rồi Enter (gõ BLOCKS để xem danh sách):",
                (InputRequest.NewBlockName, _) => "BLOCK: nhập tên block mới rồi Enter:",
                (InputRequest.EditText, _) => $"Sửa chữ \"{_editingText?.Value}\" — nhập nội dung mới rồi Enter:",
                (InputRequest.PaperText, _) => "PTEXT: nhập nội dung ghi chú trên tờ rồi Enter:",
                (InputRequest.VportScale, _) => "VPORT: nhập tỷ lệ 1:N (ví dụ 20 = 1:20) rồi Enter:",
                (InputRequest.ArrayRowsCols, _) => "ARRAY: nhập số hàng, số cột (ví dụ 3,4) rồi Enter:",
                (InputRequest.ArraySpacing, _) => "ARRAY: nhập bước hàng, bước cột (ví dụ 1000,500) rồi Enter:",
                (InputRequest.ArrayCount, _) => "ARRAYPOLAR: nhập số bản (≥2) rồi Enter:",
                (InputRequest.DivideCount, _) => "DIVIDE: nhập số đoạn (≥2) rồi Enter:",
                (InputRequest.MeasureSpacing, _) => "MEASURE: nhập khoảng cách rồi Enter:",
                (_, EditMode.ZoomWindow) => _points.Count == 0
                    ? "ZOOM: click góc thứ nhất của vùng cần phóng"
                    : "ZOOM: click góc đối diện",
                (_, EditMode.MatchSource) => "MATCHPROP: click đối tượng NGUỒN (lấy thuộc tính từ đây)",
                (_, EditMode.MatchTarget) => _matchSource is null
                    ? "MATCHPROP: chưa có nguồn"
                    : "MATCHPROP: click từng đối tượng ĐÍCH — Esc để xong",
                (InputRequest.DimensionText, _) => "DIMTEDIT: nhập chữ mới cho kích thước (Enter trống = trả về số đo):",
                (InputRequest.ViewportScaleNew, _) => "VPSCALE: nhập mẫu số tỷ lệ (50 = 1:50) rồi Enter:",
                (InputRequest.HatchProperty, _) =>
                    "HATCHEDIT: nhập scale=…, angle=…, pattern=… (mẫu: "
                    + string.Join(", ", OpenCad.Core.Styles.HatchPatternLibrary.Names) + "):",
                (InputRequest.AttributeValue, _) => _attributeTarget is null
                    ? "ATTEDIT: nhập TAG=giá trị rồi Enter:"
                    : $"ATTEDIT [{DescribeAttributes(_attributeTarget)}]: nhập TAG=giá trị rồi Enter:",
                (_, EditMode.AttributeEdit) => "ATTEDIT: click vào block cần sửa attribute",
                (_, EditMode.ArrayPolar) => $"ARRAYPOLAR {_arrayCount} bản: click tâm xoay",
                (_, EditMode.Stretch) => _points.Count switch
                {
                    0 => "STRETCH: click góc thứ nhất của khung chọn (crossing)",
                    1 => "STRETCH: click góc đối diện",
                    2 => "STRETCH: click điểm gốc",
                    _ => "STRETCH: click điểm đích",
                },
                (_, EditMode.PaperViewport) => _points.Count == 0
                    ? $"VPORT 1:{_vportScale:0.##}: click góc thứ nhất của khung"
                    : $"VPORT 1:{_vportScale:0.##}: click góc đối diện",
                (_, EditMode.PaperText) => "PTEXT: click vị trí đặt ghi chú trên tờ",
                (InputRequest.None, EditMode.Select) when Sheet is not null => $"LAYOUT {Sheet.Name} — click chọn, PTEXT ghi chú, VPORT thêm khung",
                (_, EditMode.Line) => _points.Count == 0 ? "LINE: chọn điểm đầu" : "LINE: điểm tiếp theo — Enter để kết thúc",
            (_, EditMode.XLine) => _points.Count == 0 ? "XLINE: chọn điểm gốc" : "XLINE: chọn hướng",
            (_, EditMode.Ray) => _points.Count == 0 ? "RAY: chọn điểm gốc" : "RAY: chọn hướng",
                (_, EditMode.Polyline) => _points.Count == 0 ? "PLINE: điểm đầu" : $"PLINE: điểm tiếp theo ({_points.Count}) — Enter để đóng",
                (_, EditMode.PointDraw) => "POINT: click vị trí cần đặt điểm (Esc để xong)",
                (InputRequest.MLeaderText, _) => "MLEADER: nhập nội dung chú thích rồi Enter:",
                (_, EditMode.MLeader) => _points.Count == 0
                    ? "MLEADER: click đầu mũi tên"
                    : "MLEADER: click vị trí đặt chữ",
                (InputRequest.TableSize, _) => "TABLE: nhập số hàng,cột (ví dụ 3,2) rồi Enter:",
                (InputRequest.TableInsertPoint, _) => "TABLE: click điểm chèn (góc trên trái)",
                (_, EditMode.TableDraw) => "TABLE: nhập kích thước bảng trước",
                (_, EditMode.DimBaseline) => _lastDimension is null
                    ? "DIMBASELINE: chưa có kích thước gốc — tạo DIM trước"
                    : "DIMBASELINE: click điểm đo (từ gốc chung, Esc để xong)",
                (_, EditMode.DimContinue) => _lastDimension is null
                    ? "DIMCONTINUE: chưa có kích thước nào để nối — tạo DIM trước"
                    : "DIMCONTINUE: click điểm đo tiếp theo (Esc để xong)",
                (_, EditMode.MLine) => _points.Count == 0
                    ? "MLINE: click điểm đầu"
                    : $"MLINE: điểm tiếp theo ({_points.Count}) — Enter để kết thúc",
                (_, EditMode.Spline) => _points.Count == 0
                    ? "SPLINE: click điểm đầu"
                    : $"SPLINE: điểm tiếp theo ({_points.Count}) — Enter để kết thúc",
                (_, EditMode.Rectangle) => _points.Count == 0 ? "RECTANG: góc thứ nhất" : "RECTANG: góc đối diện",
                (_, EditMode.Circle) => _points.Count == 0 ? "CIRCLE: tâm" : "CIRCLE: bán kính",
                (_, EditMode.Arc) => _points.Count switch
                {
                    0 => "ARC: điểm đầu",
                    1 => "ARC: điểm giữa",
                    _ => "ARC: điểm cuối",
                },
                (_, EditMode.Text) => "TEXT: điểm chèn",
                (_, EditMode.Move) => _points.Count == 0 ? "MOVE: điểm gốc" : "MOVE: điểm đích",
                (_, EditMode.Copy) => _points.Count == 0 ? "COPY: điểm gốc" : "COPY: điểm đích",
                (_, EditMode.Rotate) => _points.Count == 0 ? "ROTATE: tâm xoay" : "ROTATE: góc (click hoặc nhập số)",
                (_, EditMode.Scale) => _points.Count == 0 ? "SCALE: điểm gốc" : "SCALE: nhập hệ số",
                (_, EditMode.Mirror) => _points.Count == 0 ? "MIRROR: điểm 1 của trục" : "MIRROR: điểm 2 của trục",
                (_, EditMode.Offset) => _offsetDistance > 0
                    ? "OFFSET: click phía cần offset"
                    : "OFFSET: nhập khoảng cách",
                (_, EditMode.Trim) => "TRIM: click vào đoạn cần cắt bỏ (biên = mọi đối tượng khác)",
                (_, EditMode.Extend) => "EXTEND: click gần đầu cần kéo dài",
                (_, EditMode.Hatch) => "HATCH: click vào trong vùng kín",
                (_, EditMode.Fillet) => "FILLET: nhập bán kính bo góc",
                (_, EditMode.Chamfer) => "CHAMFER: nhập khoảng vát",
                (_, EditMode.Dimension) => _points.Count switch
                {
                    0 => "DIM: điểm đo thứ nhất",
                    1 => "DIM: điểm đo thứ hai",
                    _ => "DIM: vị trí đường kích thước",
                },
                (_, EditMode.DimensionRadius) => _points.Count == 0
                    ? "DIMRAD: click vào đường tròn / cung"
                    : "DIMRAD: click vị trí đặt chữ",
                (_, EditMode.DimensionDiameter) => _points.Count == 0
                    ? "DIMDIA: click vào đường tròn / cung"
                    : "DIMDIA: click vị trí đặt chữ",
                (_, EditMode.DimensionAngular) => _points.Count switch
                {
                    0 => "DIMANG: click đường thẳng thứ nhất",
                    1 => "DIMANG: click đường thẳng thứ hai",
                    _ => "DIMANG: click vị trí cung góc",
                },
                (_, EditMode.DimensionOrdinate) => _points.Count == 0
                    ? "DIMORD: click điểm cần đo"
                    : "DIMORD: click đầu đường dẫn",
                (_, EditMode.Ellipse) => _points.Count switch
                {
                    0 => "ELLIPSE: tâm",
                    1 => "ELLIPSE: đầu trục lớn",
                    _ => "ELLIPSE: bán trục nhỏ",
                },
                (_, EditMode.Polygon) => _points.Count == 0
                    ? $"POLYGON {_polygonSides} cạnh: tâm"
                    : $"POLYGON {_polygonSides} cạnh: điểm trên đường tròn ngoại tiếp",
                (_, EditMode.Dist) => _points.Count == 0
                    ? "DIST: điểm thứ nhất"
                    : "DIST: điểm thứ hai",
                (_, EditMode.Area) => "AREA: click vào vùng khép kín",
                (_, EditMode.Insert) => _pendingBlock is null
                    ? "INSERT: nhập tên block"
                    : $"INSERT '{_pendingBlock.Name}': click điểm chèn (Esc để kết thúc)",
                (_, EditMode.BlockCreate) => _pendingBlockName is null
                    ? "BLOCK: nhập tên block mới"
                    : $"BLOCK '{_pendingBlockName}': click điểm gốc",
                (_, EditMode.EditTextPick) => "ED: click vào chữ cần sửa",
                _ => "Sẵn sàng — gõ lệnh hoặc chọn đối tượng",
            };
        }
    }

    // ------------------------------------------------------------------ API

    /// <summary>Áp font đã lưu trong bản vẽ vào bộ vẽ (gọi sau khi mở tệp).</summary>
    public void ApplyDocumentFont()
    {
        if (_editor is null) return;

        var saved = OpenCad.Core.Styles.FontLibrary.GetDocumentFont(_editor.Workspace.Document);

        if (saved.Length > 0) _renderer.SetFont(saved);
        else _renderer.SetFont(OpenCad.Core.Styles.FontLibrary.DefaultFont);
    }

    public void Attach(CadEditor editor)
    {
        if (_editor is not null)
        {
            _editor.Changed -= OnEditorChanged;
            _editor.StateChanged -= OnStateChanged;
        }

        _editor = editor;
        _editor.Changed += OnEditorChanged;
        _editor.StateChanged += OnStateChanged;

        _selection.Clear();
        _points.Clear();
        _mode = EditMode.Select;
        _input = InputRequest.None;
        CurrentLayer ??= SafeLayer("0");

        ZoomExtents();
        RaiseSelection();
        RaisePrompt();
        Invalidate();
    }

    private void OnEditorChanged()
    {
        _activeSnap = null;
        _selection.RemoveWhere(e => !_editor!.Workspace.Document.Entities.Contains(e));
        RunOnUi(Invalidate);
        RunOnUi(RaiseSelection);
    }

    private void OnStateChanged()
    {
        RunOnUi(() => StateChanged?.Invoke());
    }

    private void RunOnUi(Action action)
    {
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }

    public void SetMode(EditMode mode)
    {
        _points.Clear();
        _input = InputRequest.None;
        _statusOverride = null;

        if (IsModify(mode) && _selection.Count == 0)
        {
            _mode = EditMode.Select;
            _statusOverride = "Hãy chọn đối tượng trước rồi chạy lại lệnh.";
            RaisePrompt();
            Invalidate();
            return;
        }

        _mode = mode;
        _points.Clear();
        _offsetDistance = 0;

        if (mode == EditMode.Offset)
        {
            if (_selection.Count != 1)
            {
                _mode = EditMode.Select;
                _statusOverride = "OFFSET cần chọn đúng 1 đối tượng.";
                RaisePrompt();
                Invalidate();
                return;
            }
            _input = InputRequest.OffsetDistance;
        }

        if (mode is EditMode.Fillet or EditMode.Chamfer)        {
            var lines = _selection.OfType<Line>().ToList();
            var polylines = _selection.OfType<LwPolyline>().ToList();

            var twoLines = _selection.Count == 2 && lines.Count == 2;
            var onePolyline = mode == EditMode.Fillet
                              && _selection.Count == 1 && polylines.Count == 1;

            if (!twoLines && !onePolyline)
            {
                _mode = EditMode.Select;
                _statusOverride = mode == EditMode.Fillet
                    ? "FILLET cần 2 đường thẳng, hoặc 1 polyline (bo tất cả góc)."
                    : "CHAMFER cần chọn đúng 2 đường thẳng.";
                RaisePrompt();
                Invalidate();
                return;
            }

            _input = mode == EditMode.Fillet
                ? InputRequest.FilletRadius
                : InputRequest.ChamferDistance;
        }

        if (mode == EditMode.Polygon)
            _input = InputRequest.PolygonSides;

        if (mode == EditMode.Insert)
        {
            _pendingBlock = null;
            _input = InputRequest.BlockName;
        }

        if (mode == EditMode.ArrayPolar)
        {
            if (_selection.Count == 0)
            {
                _mode = EditMode.Select;
                _statusOverride = "ARRAYPOLAR: hãy chọn đối tượng trước.";
                RaisePrompt();
                Invalidate();
                return;
            }

            _input = InputRequest.ArrayCount;
        }

        if (mode is EditMode.Divide or EditMode.Measure)
        {
            if (_selection.Count != 1)
            {
                _mode = EditMode.Select;
                _statusOverride = $"{mode.ToString().ToUpperInvariant()} cần chọn đúng 1 đối tượng.";
                RaisePrompt();
                Invalidate();
                return;
            }

            _input = mode == EditMode.Divide ? InputRequest.DivideCount : InputRequest.MeasureSpacing;
        }

        if (mode == EditMode.AttributeEdit)
        {
            _attributeTarget = null;
        }

        if (mode == EditMode.Stretch)
        {
            _points.Clear();
        }

        if (mode == EditMode.Array)
        {
            if (_selection.Count == 0)
            {
                _mode = EditMode.Select;
                _statusOverride = "ARRAY: hãy chọn đối tượng trước.";
                RaisePrompt();
                Invalidate();
                return;
            }

            _input = InputRequest.ArrayRowsCols;
        }

        if (mode == EditMode.PaperViewport)
        {
            if (Sheet is null)
            {
                _mode = EditMode.Select;
                _statusOverride = "VPORT: chỉ dùng được khi đang xem một tờ in.";
                RaisePrompt();
                Invalidate();
                return;
            }

            _points.Clear();
            _input = InputRequest.VportScale;
        }

        if (mode == EditMode.BlockCreate)
        {
            if (_selection.Count == 0)
            {
                _mode = EditMode.Select;
                _statusOverride = "BLOCK: hãy chọn đối tượng trước.";
                RaisePrompt();
                Invalidate();
                return;
            }

            _pendingBlockName = null;
            _input = InputRequest.NewBlockName;
        }

        Cursor = mode == EditMode.Select ? Cursors.Default : Cursors.Cross;
        RaisePrompt();
        Invalidate();
    }

    private static bool IsModify(EditMode m)
        => m is EditMode.Move or EditMode.Copy or EditMode.Rotate
            or EditMode.Scale or EditMode.Mirror or EditMode.Offset;

    public void Cancel()
    {
        _points.Clear();
        _input = InputRequest.None;
        _boxSelecting = false;
        _draggingEntities = false;
        _mode = EditMode.Select;
        Cursor = Cursors.Default;

        // Không xoá _statusOverride ở đây: kết quả vừa tính (DIST/AREA/DIM…)
        // phải còn hiện cho tới khi người dùng di chuột hoặc chạy lệnh mới.
        RaisePrompt();
        Invalidate();
    }

    // ------------------------------------------------------- LỊCH SỬ ZOOM
    private readonly List<(double Zoom, Pt Center)> _zoomHistory = new();

    /// <summary>Lưu trạng thái nhìn hiện tại để ZOOM PREVIOUS quay lại.</summary>
    private void PushZoomHistory()
    {
        _zoomHistory.Add((_view.Zoom, _view.Center));

        if (_zoomHistory.Count > 50) _zoomHistory.RemoveAt(0);
    }

    /// <summary>Quay lại khung nhìn trước đó (ZOOM PREVIOUS).</summary>
    public bool ZoomPrevious()
    {
        if (_zoomHistory.Count == 0)
        {
            _statusOverride = "ZOOM: chưa có khung nhìn trước.";
            return false;
        }

        var last = _zoomHistory[^1];
        _zoomHistory.RemoveAt(_zoomHistory.Count - 1);

        _view.Zoom = last.Zoom;
        _view.Center = last.Center;

        _statusOverride = "ZOOM: đã quay lại khung nhìn trước.";
        Invalidate();
        return true;
    }

    /// <summary>Chọn mọi đối tượng trên một lớp. Trả về số đã chọn.</summary>
    public int SelectByLayer(string layerName)
    {
        if (_editor is null) return 0;

        _selection.Clear();

        foreach (var entity in _editor.Workspace.Document.Entities)
        {
            if (string.Equals(entity.Layer?.Name, layerName, StringComparison.OrdinalIgnoreCase))
                _selection.Add(entity);
        }

        RaiseSelection();
        Invalidate();

        return _selection.Count;
    }

    /// <summary>Chọn mọi đối tượng thuộc một loại (theo tên kiểu, không phân biệt hoa thường).</summary>
    public int SelectByType(string typeName)
    {
        if (_editor is null) return 0;

        _selection.Clear();

        foreach (var entity in _editor.Workspace.Document.Entities)
        {
            var name = entity.GetType().Name;

            if (name.Contains(typeName, StringComparison.OrdinalIgnoreCase))
                _selection.Add(entity);
        }

        RaiseSelection();
        Invalidate();

        return _selection.Count;
    }

    /// <summary>Đảo lựa chọn: chọn những gì chưa chọn. Trả về số đang chọn.</summary>
    public int InvertSelection()
    {
        if (_editor is null) return 0;

        var current = new HashSet<Entity>(_selection);

        _selection.Clear();

        foreach (var entity in _editor.Workspace.Document.Entities)
            if (!current.Contains(entity)) _selection.Add(entity);

        RaiseSelection();
        Invalidate();

        return _selection.Count;
    }

    /// <summary>Phóng tới vùng bao của các đối tượng đang chọn (ZOOM OBJECT).</summary>
    public bool ZoomToSelection()
    {
        if (_editor is null || _selection.Count == 0) return false;

        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        var found = false;

        var selected = new HashSet<Entity>(_selection);

        foreach (var entry in _editor.Scene.Entries)
        {
            if (!selected.Contains(entry.Entity)) continue;

            foreach (var prim in entry.Prims)
            {
                foreach (var point in prim.Points)
                {
                    minX = Math.Min(minX, point.X);
                    minY = Math.Min(minY, point.Y);
                    maxX = Math.Max(maxX, point.X);
                    maxY = Math.Max(maxY, point.Y);
                    found = true;
                }
            }
        }

        if (!found || maxX - minX < 1e-9 || maxY - minY < 1e-9) return false;

        PushZoomHistory();
        _view.Fit(new RectD(minX, minY, maxX - minX, maxY - minY));

        _statusOverride = $"ZOOMOBJ: {_selection.Count} đối tượng";
        Invalidate();

        return true;
    }

    /// <summary>
    /// Chọn TẤT CẢ đối tượng nằm dưới một điểm (khi chúng chồng lên nhau).
    /// Trả về số đối tượng đã chọn.
    /// </summary>
    public int SelectUnderCursor()
    {
        if (_editor is null) return 0;

        var tolerance = _view.PixelsToWorld(7);

        var entries = _editor.Scene.HitTestAll(_cursor, tolerance).ToList();

        if (entries.Count == 0) return 0;

        _selection.Clear();

        foreach (var entry in entries) _selection.Add(entry.Entity);

        RaiseSelection();
        Invalidate();

        _statusOverride = $"SELECTALLD: chọn {_selection.Count} đối tượng chồng nhau";
        Message?.Invoke(_statusOverride);

        return _selection.Count;
    }

    /// <summary>
    /// Nhập chữ cho một ô bảng đang chọn: "hàng,cột,nội dung". Trả về true nếu đặt được.
    /// </summary>
    public bool SetTableCellLabel(string argument)
    {
        if (_editor is null) return false;

        var table = _selection.OfType<ACadSharp.Entities.TableEntity>().FirstOrDefault();

        if (table is null)
        {
            _statusOverride = "TABLECELL: chọn một bảng trước.";
            Message?.Invoke(_statusOverride);
            return false;
        }

        var parts = argument.Split(new[] { ',', ';' }, 3);

        if (parts.Length < 3 ||
            !int.TryParse(parts[0].Trim(), out var row) ||
            !int.TryParse(parts[1].Trim(), out var column))
        {
            _statusOverride = "TABLECELL: cần dạng hàng,cột,nội dung — ví dụ TABLECELL 1,1,DIEN TICH";
            Message?.Invoke(_statusOverride);
            return false;
        }

        var label = _editor.SetTableCellLabel(table, row, column, parts[2].Trim(), 0);

        _statusOverride = label is null
            ? $"TABLECELL: không đặt được ô ({row},{column})."
            : $"TABLECELL: đã đặt chữ vào ô ({row},{column}).";

        Message?.Invoke(_statusOverride);
        Invalidate();

        return label is not null;
    }

    /// <summary>JOIN: nối các line đang chọn thành một polyline. Trả về true nếu nối được.</summary>
    public bool JoinSelection()
    {
        if (_editor is null || _selection.Count < 2)
        {
            _statusOverride = "JOIN: cần chọn ít nhất 2 đoạn thẳng.";
            Message?.Invoke(_statusOverride);
            return false;
        }

        var joined = _editor.JoinLines(_selection.ToList());

        _statusOverride = joined
            ? $"JOIN: đã nối {_selection.Count} đoạn thành polyline."
            : "JOIN: các đoạn không nối đầu-cuối nên không nối được.";

        Message?.Invoke(_statusOverride);

        if (joined) ClearSelection();

        Invalidate();

        return joined;
    }

    /// <summary>EXPLODE: bung các đối tượng đang chọn. Trả về số đối tượng đã bung.</summary>
    public int ExplodeSelection()
    {
        if (_editor is null || _selection.Count == 0)
        {
            _statusOverride = "EXPLODE: cần chọn đối tượng trước.";
            Message?.Invoke(_statusOverride);
            return 0;
        }

        var exploded = _editor.Explode(_selection.ToList());

        _statusOverride = exploded == 0
            ? "EXPLODE: loại đối tượng này không bung được."
            : $"EXPLODE: đã bung {exploded} đối tượng.";

        Message?.Invoke(_statusOverride);

        // Đối tượng gốc đã bị thay → bỏ chọn
        ClearSelection();
        Invalidate();

        return exploded;
    }

    /// <summary>OOPS: phục hồi đối tượng vừa xoá. Trả về số đối tượng.</summary>
    public int RestoreLastErased()
    {
        var count = _editor?.RestoreLastErased() ?? 0;

        _statusOverride = count == 0
            ? "OOPS: không có gì để phục hồi."
            : $"OOPS: đã phục hồi {count} đối tượng.";

        Message?.Invoke(_statusOverride);
        Invalidate();

        return count;
    }

    /// <summary>Có viewport đang được chọn không (dùng cho kiểm thử).</summary>
    internal bool HasSelectedViewportForTest => _selectedViewport is not null;

    /// <summary>Có đang kéo góc viewport không (dùng cho kiểm thử).</summary>
    internal bool IsResizingViewportForTest => _resizingViewport;

    /// <summary>Số điểm đã click của lệnh đa điểm (dùng cho kiểm thử).</summary>
    internal int PointCountForTest => _points.Count;

    /// <summary>Chế độ hiện tại dạng chuỗi (dùng cho kiểm thử).</summary>
    internal string ModeForTest => _mode.ToString();

    /// <summary>Đặt chế độ vẽ — dùng cho kiểm thử.</summary>
    internal void SetModeForTest(EditMode mode) => SetMode(mode);

    /// <summary>Tên layout đang mở (rỗng khi ở model space).</summary>
    public string ActiveLayoutName { get; private set; } = string.Empty;

    /// <summary>
    /// Đổi font chữ hiển thị (giống chọn Text Style trong AutoCAD).
    /// Trả về true nếu font tồn tại. PDF vẫn nhúng font hệ thống riêng.
    /// </summary>
    public bool SetTextFont(string name)
    {
        var changed = _renderer.SetFont(name);

        if (!changed)
        {
            _statusOverride = $"Không có font '{name}'.";
            Message?.Invoke(_statusOverride);
            return false;
        }

        // Lưu vào bản vẽ để mở lại vẫn giữ font
        if (_editor is not null)
        {
            OpenCad.Core.Styles.FontLibrary.SetDocumentFont(
                _editor.Workspace.Document, _renderer.FontName);
        }

        _statusOverride = "Font chữ: " + _renderer.FontName;
        Message?.Invoke(_statusOverride);
        Invalidate();

        return true;
    }

    /// <summary>Tên font đang hiển thị (dùng cho kiểm thử).</summary>
    public string TextFontName => _renderer.FontName;

    /// <summary>Hệ số phóng hiện tại (dùng cho kiểm thử).</summary>
    public double ZoomFactor => _view.Zoom;

    /// <summary>Tâm khung nhìn hiện tại (dùng cho kiểm thử).</summary>
    public Pt ViewCenter => _view.Center;

    /// <summary>Số khung nhìn đang lưu trong lịch sử zoom (dùng cho kiểm thử).</summary>
    internal int ZoomHistoryCount => _zoomHistory.Count;

    /// <summary>Phóng tới một vùng (ZOOM WINDOW), tính theo toạ độ thế giới.</summary>
    public void ZoomWindow(Pt corner1, Pt corner2)
    {
        var minX = Math.Min(corner1.X, corner2.X);
        var maxX = Math.Max(corner1.X, corner2.X);
        var minY = Math.Min(corner1.Y, corner2.Y);
        var maxY = Math.Max(corner1.Y, corner2.Y);

        if (maxX - minX < 1e-6 || maxY - minY < 1e-6)
        {
            _statusOverride = "ZOOM: vùng chọn quá nhỏ.";
            return;
        }

        PushZoomHistory();

        _view.Fit(new RectD(minX, minY, maxX - minX, maxY - minY));

        _statusOverride = $"ZOOM: khung {maxX - minX:0.#} × {maxY - minY:0.#}";
        Invalidate();
    }

    public void ZoomExtents()
    {
        PushZoomHistory();
        UpdateViewSize();

        var bounds = Sheet?.Bounds ?? _editor?.Scene.Bounds ?? RectD.Empty;
        _view.Fit(bounds);

        Invalidate();
    }

    public void ZoomBy(double factor)
    {
        PushZoomHistory();
        UpdateViewSize();
        _view.ZoomAt(new PointF(Width / 2f, Height / 2f), factor);
        Invalidate();
    }

    public void SelectAll()
    {
        if (_editor is null) return;

        _selection.Clear();
        foreach (var e in _editor.Workspace.Document.Entities) _selection.Add(e);
        RaiseSelection();
        Invalidate();
    }

    public void ClearSelection()
    {
        _selection.Clear();
        RaiseSelection();
        Invalidate();
    }

    /// <summary>Số đối tượng đang chọn nằm trên layer bị khoá.</summary>
    public int LockedSelectionCount => _selection.Count(CadEditor.IsEntityLocked);

    public void DeleteSelection()
    {
        if (_editor is null || _selection.Count == 0) return;

        _editor.RemoveEntities(_selection.ToList());
        _selection.Clear();
        RaiseSelection();
        Invalidate();
    }

    public void ToggleOrtho()
    {
        _snap.OrthoEnabled = !_snap.OrthoEnabled;
        _statusOverride = _snap.OrthoEnabled ? "ORTHO bật" : "ORTHO tắt";
        RaisePrompt();
        Invalidate();
    }

    public void ToggleSnap()
    {
        _snap.Enabled = !_snap.Enabled;
        _statusOverride = _snap.Enabled ? "OSNAP bật" : "OSNAP tắt";
        RaisePrompt();
        Invalidate();
    }

    public void ToggleGrid()
    {
        ShowGrid = !ShowGrid;
        _statusOverride = ShowGrid ? "GRID bật" : "GRID tắt";
        Invalidate();
    }

    public void TogglePolar()
    {
        _snap.PolarEnabled = !_snap.PolarEnabled;
        _statusOverride = _snap.PolarEnabled
            ? $"POLAR {_snap.PolarIncrementDegrees:0}° bật"
            : "POLAR tắt";
        RaisePrompt();
        Invalidate();
    }

    /// <summary>Bật/tắt hiển thị độ đậm nét (LWT) như AutoCAD.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ShowLineWeights
    {
        get => _renderer.ShowLineWeights;
        set
        {
            _renderer.ShowLineWeights = value;
            Invalidate();
        }
    }

    public void ToggleLineWeights()
    {
        ShowLineWeights = !ShowLineWeights;
        _statusOverride = ShowLineWeights ? "LWT bật" : "LWT tắt";
        RaisePrompt();
    }

    /// <summary>Nhận dữ liệu gõ từ command line. Chuỗi rỗng = Enter.</summary>
    public bool ProvideInput(string text)
    {
        text = text.Trim();

        if (_input == InputRequest.Text)
        {
            var value = text.Length > 0 ? text : _statusOverride ?? "Text";
            CommitText(value);
            return true;
        }

        if (_input == InputRequest.ScaleFactor)
        {
            if (!double.TryParse(text, out var factor) || Math.Abs(factor) < 1e-9)
            {
                _statusOverride = "Hệ số không hợp lệ.";
                RaisePrompt();
                return true;
            }
            CommitScale(factor);
            return true;
        }

        if (_input == InputRequest.RotateAngle)
        {
            if (!double.TryParse(text, out var deg))
            {
                _statusOverride = "Góc không hợp lệ.";
                RaisePrompt();
                return true;
            }
            CommitRotate(deg * Math.PI / 180.0);
            return true;
        }

        if (_input == InputRequest.OffsetDistance)
        {
            if (!double.TryParse(text, out var distance) || distance <= 0)
            {
                _statusOverride = "Khoảng cách không hợp lệ.";
                RaisePrompt();
                return true;
            }

            _offsetDistance = distance;
            _input = InputRequest.None;
            _statusOverride = "OFFSET: click phía cần offset";
            RaisePrompt();
            Invalidate();
            return true;
        }

        if (_input == InputRequest.FilletRadius)
        {
            if (!double.TryParse(text, out var radius) || radius <= 0)
            {
                _statusOverride = "Bán kính không hợp lệ.";
                RaisePrompt();
                return true;
            }
            ApplyFillet(radius);
            return true;
        }

        if (_input == InputRequest.ChamferDistance)
        {
            var parts = text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0 || !double.TryParse(parts[0], out var d1) || d1 <= 0)
            {
                _statusOverride = "Khoảng vát không hợp lệ.";
                RaisePrompt();
                return true;
            }

            var d2 = d1;
            if (parts.Length > 1 && double.TryParse(parts[1], out var parsed)) d2 = parsed;

            ApplyChamfer(d1, d2);
            return true;
        }

        if (_input == InputRequest.PolygonSides)
        {
            if (!int.TryParse(text, out var sides) || sides < 3 || sides > 1024)
            {
                _statusOverride = "Số cạnh phải từ 3 đến 1024.";
                RaisePrompt();
                return true;
            }

            _polygonSides = sides;
            _input = InputRequest.None;
            _statusOverride = null;
            RaisePrompt();
            return true;
        }

        if (_input == InputRequest.BlockName)
        {
            var block = _editor is null
                ? null
                : BlockLibrary.Find(_editor.Workspace.Document, text);

            if (block is null)
            {
                _statusOverride = $"Không có block tên '{text}'. Gõ BLOCKS để xem danh sách.";
                RaisePrompt();
                return true;
            }

            if (BlockLibrary.EntityCount(block) == 0)
            {
                _statusOverride = $"Block '{block.Name}' rỗng.";
                RaisePrompt();
                return true;
            }

            _pendingBlock = block;
            _input = InputRequest.None;
            _statusOverride = null;
            RaisePrompt();
            return true;
        }

        if (_input == InputRequest.NewBlockName)
        {
            var name = text.Trim();

            if (name.Length == 0 || name.StartsWith("*"))
            {
                _statusOverride = "Tên block không hợp lệ.";
                RaisePrompt();
                return true;
            }

            if (_editor is not null && BlockLibrary.Find(_editor.Workspace.Document, name) is not null)
            {
                _statusOverride = $"Block '{name}' đã tồn tại.";
                RaisePrompt();
                return true;
            }

            _pendingBlockName = name;
            _input = InputRequest.None;
            _statusOverride = null;
            RaisePrompt();
            return true;
        }

        if (_input == InputRequest.DivideCount)
        {
            if (!int.TryParse(text, out var count) || count < 2 || count > 10_000)
            {
                _statusOverride = "DIVIDE: số đoạn phải từ 2 đến 10000.";
                RaisePrompt();
                return true;
            }

            ApplyDivideMeasure(count, 0);
            return true;
        }

        if (_input == InputRequest.MeasureSpacing)
        {
            if (!double.TryParse(text, out var spacing) || spacing <= 1e-9)
            {
                _statusOverride = "MEASURE: khoảng cách phải là số dương.";
                RaisePrompt();
                return true;
            }

            ApplyDivideMeasure(0, spacing);
            return true;
        }

        if (_input == InputRequest.MLeaderText)
        {
            _input = InputRequest.None;

            if (_points.Count >= 2 && text.Length > 0)
                CreateMultiLeader(_points[0], _points[1], text);

            Cancel();
            return true;
        }

        if (_input == InputRequest.TableSize)
        {
            var parts = text.Split(new[] { ',', ';', 'x', 'X' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2 ||
                !int.TryParse(parts[0].Trim(), out var rows) ||
                !int.TryParse(parts[1].Trim(), out var cols) ||
                rows < 1 || cols < 1 || rows > 200 || cols > 50)
            {
                _statusOverride = "TABLE: cần dạng hàng,cột (ví dụ 3,2).";
                RaisePrompt();
                return true;
            }

            _tableRows = rows;
            _tableColumns = cols;
            _input = InputRequest.None;
            _mode = EditMode.TableDraw;

            _statusOverride = $"TABLE {rows}×{cols}: click điểm chèn";
            RaisePrompt();
            return true;
        }

        if (_input == InputRequest.DimensionText)
        {
            _input = InputRequest.None;

            var changed = _editor?.SetDimensionText(_selection.ToList(), text) ?? 0;

            _statusOverride = changed == 0
                ? "DIMTEDIT: không có kích thước nào trong lựa chọn."
                : string.IsNullOrWhiteSpace(text)
                    ? $"DIMTEDIT: {changed} kích thước trở về số đo thật"
                    : $"DIMTEDIT: đã đặt chữ cho {changed} kích thước";

            Message?.Invoke(_statusOverride);
            RaisePrompt();
            Invalidate();
            return true;
        }

        if (_input == InputRequest.ViewportScaleNew)
        {
            _input = InputRequest.None;

            if (double.TryParse(text, out var unitsPerMm) && unitsPerMm > 1e-6)
                ApplyViewportScale(unitsPerMm);
            else
            {
                _statusOverride = "VPSCALE: tỷ lệ phải là số dương (ví dụ 50 cho 1:50).";
                Message?.Invoke(_statusOverride);
            }

            RaisePrompt();
            Invalidate();
            return true;
        }

        if (_input == InputRequest.HatchProperty)
        {
            _input = InputRequest.None;
            ApplyHatchEdit(text);
            return true;
        }

        if (_input == InputRequest.AttributeNew)
        {
            if (_attributeTarget is null)
            {
                _input = InputRequest.None;
                RaisePrompt();
                return true;
            }

            var eq2 = text.IndexOf('=');

            if (eq2 <= 0)
            {
                _statusOverride = "ATTDEF: cần dạng TAG=giá trị (ví dụ SO_CUA=C01).";
                RaisePrompt();
                return true;
            }

            var newTag = text[..eq2].Trim();
            var newValue = text[(eq2 + 1)..].Trim();

            var block = _attributeTarget.Block;

            if (block is not null)
            {
                _editor?.AddAttributeDefinition(block, newTag, newValue, _attributeDefinePoint, 250);

                _statusOverride = $"ATTDEF: đã thêm '{newTag}' vào block {block.Name}.";
                Message?.Invoke(_statusOverride);
            }

            _attributeTarget = null;
            _input = InputRequest.None;
            _mode = EditMode.Select;
            RaisePrompt();
            Invalidate();
            return true;
        }

        if (_input == InputRequest.AttributeValue)
        {
            if (_attributeTarget is null)
            {
                _input = InputRequest.None;
                RaisePrompt();
                return true;
            }

            var eq = text.IndexOf('=');

            if (eq <= 0)
            {
                _statusOverride = "ATTEDIT: cần dạng TAG=giá trị (ví dụ SO_CUA=C02).";
                RaisePrompt();
                return true;
            }

            var tag = text[..eq].Trim();
            var value = text[(eq + 1)..].Trim();

            var attribute = _attributeTarget.Attributes?
                .FirstOrDefault(a => string.Equals(a.Tag, tag, StringComparison.OrdinalIgnoreCase));

            if (attribute is null)
            {
                _statusOverride = $"ATTEDIT: block không có attribute '{tag}'.";
                RaisePrompt();
                return true;
            }

            var previous = attribute.Value;
            var target = attribute;

            _editor?.RunCustom("Edit attribute",
                () => target.Value = value,
                () => target.Value = previous);

            _statusOverride = $"ATTEDIT: {tag} = {value}";
            Message?.Invoke(_statusOverride);

            _attributeTarget = null;
            _input = InputRequest.None;
            _mode = EditMode.Select;
            RaisePrompt();
            Invalidate();
            return true;
        }

        if (_input == InputRequest.ArrayCount)
        {
            if (!int.TryParse(text, out var count) || count < 2 || count > 500)
            {
                _statusOverride = "ARRAYPOLAR: số bản phải từ 2 đến 500.";
                RaisePrompt();
                return true;
            }

            _arrayCount = count;
            _input = InputRequest.None;
            _statusOverride = null;
            RaisePrompt();
            return true;
        }

        if (_input == InputRequest.ArrayRowsCols)
        {
            var parts = text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2 ||
                !int.TryParse(parts[0], out var rows) || !int.TryParse(parts[1], out var cols) ||
                rows < 1 || cols < 1 || rows > 500 || cols > 500 || (rows == 1 && cols == 1))
            {
                _statusOverride = "ARRAY: cần 2 số nguyên dương (hàng, cột), không cùng bằng 1.";
                RaisePrompt();
                return true;
            }

            _arrayRows = rows;
            _arrayCols = cols;
            _input = InputRequest.ArraySpacing;
            _statusOverride = null;
            RaisePrompt();
            return true;
        }

        if (_input == InputRequest.ArraySpacing)
        {
            var parts = text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2 ||
                !double.TryParse(parts[0], out var dy) || !double.TryParse(parts[1], out var dx) ||
                (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9))
            {
                _statusOverride = "ARRAY: cần 2 số (bước hàng, bước cột), không cùng bằng 0.";
                RaisePrompt();
                return true;
            }

            ApplyArray(dy, dx);
            return true;
        }

        if (_input == InputRequest.VportScale)
        {
            if (!double.TryParse(text, out var denominator) || denominator < 0.001 || denominator > 100000)
            {
                _statusOverride = "Tỷ lệ phải là số dương (ví dụ 20 cho 1:20).";
                RaisePrompt();
                return true;
            }

            _vportScale = denominator;
            _input = InputRequest.None;
            _statusOverride = null;
            RaisePrompt();
            return true;
        }

        if (_input == InputRequest.PaperText)
        {
            _input = InputRequest.None;

            if (_paperBlock is not null && !string.IsNullOrWhiteSpace(text))
            {
                var label = new TextEntity
                {
                    InsertPoint = new CSMath.XYZ(_paperTextPoint.X, _paperTextPoint.Y, 0),
                    Height = 3.5,
                    Value = text,
                };

                var block = _paperBlock;
                _editor?.RunCustom("Text (paper)",
                    () => block.Entities.Add(label),
                    () => block.Entities.Remove(label));

                Message?.Invoke($"PTEXT: \"{text}\"");
                RefreshPaper();
            }

            _mode = EditMode.Select;
            RaisePrompt();
            return true;
        }

        if (_input == InputRequest.EditText)
        {
            var target = _editingText;
            _editingText = null;
            _input = InputRequest.None;

            if (target is not null && !string.IsNullOrEmpty(text))
            {
                if (_editingPaperText)
                {
                    var previous = target.Value;

                    _editor?.RunCustom("Edit text (paper)",
                        () => target.Value = text,
                        () => target.Value = previous);

                    RefreshPaper();
                }
                else
                {
                    _editor?.SetText(target, text);
                }

                _editingPaperText = false;

                _statusOverride = $"Đã sửa chữ: \"{text}\"";
                Message?.Invoke(_statusOverride);
            }

            RaisePrompt();
            return true;
        }

        // Có nội dung gõ vào mà không phải đang chờ dữ liệu → để command processor xử lý
        if (text.Length > 0) return false;

        // Enter: kết thúc công cụ đa điểm
        if (_mode == EditMode.Polyline) { CommitPolyline(); return true; }
        if (_mode == EditMode.Spline) { CommitSpline(); return true; }
        if (_mode == EditMode.MLine) { CommitMLine(); return true; }
        if (_mode == EditMode.Line) { Cancel(); return true; }
        if (_mode != EditMode.Select) { Cancel(); return true; }

        return false;
    }

    // ---------------------------------------------------------------- PAINT

    protected override void OnPaint(PaintEventArgs e)
    {
        try
        {
            PaintCore(e.Graphics);
        }
        catch (Exception ex)
        {
            // Một lỗi khi vẽ không được phép làm sập app — chỉ ghi nhận và thử vẽ lại sau
            ReportUiError("Vẽ màn hình", ex);

            try
            {
                e.Graphics.Clear(Color.FromArgb(30, 30, 30));
            }
            catch { }
        }
    }

    private void PaintCore(Graphics g)
    {
        UpdateViewSize();

        if (Sheet is not null)
        {
            DrawPaperSheet(g);
            DrawCrosshair(g);
            return;
        }

        DrawGrid(g);
        _renderer.Draw(g, _editor?.Scene, _view, _selection, IsLayerVisible);
        DrawGrips(g);
        DrawViewportHighlight(g);
        DrawPreview(g);
        DrawUcsIcon(g);
        DrawViewportControls(g);
        DrawNavBar(g);
        DrawCrosshair(g);
        DrawSnapMarker(g);
    }

    /// <summary>Vẽ vào một bitmap — dùng cho kiểm thử đường vẽ (không cần cửa sổ).</summary>
    internal Bitmap RenderForTest(int width = 800, int height = 600)
    {
        var bitmap = new Bitmap(Math.Max(16, width), Math.Max(16, height));

        using var g = Graphics.FromImage(bitmap);
        using var args = new PaintEventArgs(g, new Rectangle(0, 0, bitmap.Width, bitmap.Height));

        OnPaint(args);

        return bitmap;
    }

    /// <summary>
    /// Thêm đối tượng vừa vẽ: ở model space thì vào bản vẽ, ở chế độ tờ in thì vào block của tờ.
    /// Nhờ vậy LINE/CIRCLE/ARC… dùng được ngay trên tờ.
    /// </summary>
    private void AddDrawn(Entity entity, string name)
    {
        if (_editor is null) return;

        if (Sheet is not null && _paperBlock is not null)
        {
            var block = _paperBlock;

            _editor.RunCustom(name + " (paper)",
                () => block.Entities.Add(entity),
                () => block.Entities.Remove(entity));

            RefreshPaper();
            _statusOverride = $"{name}: đã vẽ trên tờ.";
            Message?.Invoke(_statusOverride);
            return;
        }

        _editor.AddEntities(new[] { entity }, name);
    }

    // ---------------------------------------------------- HOOK KIỂM THỬ
    // Dùng cho --uitest: gọi thẳng các sự kiện chuột mà không cần cửa sổ thật.

    internal void SimulateMouseDown(int x, int y, bool shift = false)
    {
        if (shift) ModifierKeysForTest = Keys.Shift;

        OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
        ModifierKeysForTest = Keys.None;
    }

    internal void SimulateMouseMove(int x, int y) =>
        OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, x, y, 0));

    internal void SimulateMouseUp(int x, int y)
    {
        OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
        ModifierKeysForTest = Keys.None;
    }

    /// <summary>Phím Shift có đang được giữ không (ưu tiên giá trị do kiểm thử đặt).</summary>
    private bool ShiftHeld =>
        ModifierKeysForTest != Keys.None
            ? ModifierKeysForTest.HasFlag(Keys.Shift)
            : (ModifierKeys & Keys.Shift) != 0;

    /// <summary>Đặt ORTHO cho kiểm thử (không đổi ngược lại).</summary>
    internal void SetOrthoForTest(bool on) => _snap.OrthoEnabled = on;

    // ------------------------------------------------------- CLIPBOARD
    // Clipboard nội bộ: giữ bản sao các entity đã copy, dán lại với undo.

    private static readonly List<ACadSharp.Entities.Entity> Clipboard = new();

    /// <summary>Số đối tượng đang có trong clipboard.</summary>
    public static int ClipboardCount => Clipboard.Count;

    /// <summary>Copy lựa chọn hiện tại vào clipboard. Trả về số đối tượng.</summary>
    public int CopySelectionToClipboard()
    {
        if (_editor is null || _selection.Count == 0) return 0;

        Clipboard.Clear();

        foreach (var entity in _selection)
        {
            if (entity.Clone() is ACadSharp.Entities.Entity copy) Clipboard.Add(copy);
        }

        _statusOverride = $"Đã copy {Clipboard.Count} đối tượng.";
        Message?.Invoke(_statusOverride);

        return Clipboard.Count;
    }

    /// <summary>
    /// Dán clipboard vào bản vẽ tại điểm cho trước (nếu có) hoặc lệch 1 khoảng nhỏ
    /// so với vị trí gốc. Có undo.
    /// </summary>
    public int PasteFromClipboard(Pt? target = null)
    {
        if (_editor is null || Clipboard.Count == 0) return 0;

        var copies = new List<ACadSharp.Entities.Entity>(Clipboard.Count);

        // Điểm mốc: góc dưới-trái của cụm đã copy
        var anchor = new Pt(0, 0);

        if (target is not null)
        {
            var first = Clipboard.FirstOrDefault();
            var bounds = new List<OpenCad.Core.Rendering.Prim>(4);

            if (first is not null)
            {
                try
                {
                    OpenCad.Core.Rendering.EntityFlattener.Flatten(first, bounds,
                        Affine2.Identity, OpenCad.Core.Colors.Rgb.White, 0);

                    if (bounds.Count > 0)
                    {
                        var points = bounds.SelectMany(p => p.Points).ToList();

                        if (points.Count > 0)
                            anchor = new Pt(points.Min(p => p.X), points.Min(p => p.Y));
                    }
                }
                catch { }
            }
        }

        var delta = target is null
            ? Affine2.Translate(200, 200)
            : Affine2.Translate(target.Value.X - anchor.X, target.Value.Y - anchor.Y);

        foreach (var source in Clipboard)
        {
            if (source.Clone() is not ACadSharp.Entities.Entity copy) continue;

            try
            {
                EntityTransform.Apply(copy, delta);
            }
            catch { }

            copies.Add(copy);
        }

        if (copies.Count == 0) return 0;

        _editor.AddEntities(copies, "Paste");

        _statusOverride = $"Đã dán {copies.Count} đối tượng.";
        Message?.Invoke(_statusOverride);

        Invalidate();

        return copies.Count;
    }

    /// <summary>Số đối tượng paper đang chọn — chỉ dùng cho kiểm thử.</summary>
    internal int PaperSelectionCount => _paperSelection.Count;

    /// <summary>Tâm viewport đang chọn trên tờ — chỉ dùng cho kiểm thử.</summary>
    internal Pt? SelectedViewportCenter => _selectedViewport is null
        ? null
        : new Pt(_selectedViewport.Center.X, _selectedViewport.Center.Y);

    /// <summary>Trạng thái kéo hiện tại — chỉ dùng cho kiểm thử.</summary>
    internal string DragState =>
        $"dragging={_draggingEntities} grip={_gripDragging} gripEntity={_gripEntity?.GetType().Name ?? "null"} " +
        $"accum={!_dragAccum.IsIdentity} sel={_selection.Count}";

    /// <summary>Modifier cho kiểm thử (Control.ModifierKeys không có khi chạy headless).</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Keys ModifierKeysForTest { get; set; }

    /// <summary>Đổi toạ độ thế giới sang toạ độ màn hình để mô phỏng chuột.</summary>
    /// <summary>Xoá lỗi UI đã ghi — dùng cho kiểm thử.</summary>
    internal static void ClearLastUiError() => LastUiError = null;

    /// <summary>Zoom vừa khung — dùng cho kiểm thử.</summary>
    internal void ZoomExtentsForTest() => ZoomExtents();

    internal System.Drawing.Point WorldToScreenPoint(Pt world)
    {
        var screen = _view.ToScreen(world);

        return new System.Drawing.Point(
            (int)Math.Round(screen.X), (int)Math.Round(screen.Y));
    }

    /// <summary>Vẽ khung sáng quanh viewport đang chọn trên tờ.</summary>
    private void DrawViewportHighlight(Graphics g)
    {
        if (Sheet is null || _selectedViewport is null) return;

        var centerX = _selectedViewport.Center.X;
        var centerY = _selectedViewport.Center.Y;
        var halfWidth = _selectedViewport.Width / 2;
        var halfHeight = _selectedViewport.Height / 2;

        var topLeft = _view.ToScreen(new Pt(centerX - halfWidth, centerY + halfHeight));
        var bottomRight = _view.ToScreen(new Pt(centerX + halfWidth, centerY - halfHeight));

        using var pen = new Pen(Color.FromArgb(230, 120, 220, 255), 2f)
        {
            DashStyle = System.Drawing.Drawing2D.DashStyle.Dash,
        };

        g.DrawRectangle(pen, topLeft.X, topLeft.Y,
            Math.Max(1, bottomRight.X - topLeft.X), Math.Max(1, bottomRight.Y - topLeft.Y));
    }

    /// <summary>Vẽ các điểm điều khiển (grip) của đối tượng đang chọn.</summary>
    private void DrawGrips(Graphics g)
    {
        if (_selection.Count == 0 || Sheet is not null) return;

        var size = 7f;

        using var fill = new SolidBrush(Color.FromArgb(90, 0, 170, 255));
        using var border = new Pen(Color.FromArgb(210, 120, 220, 255), 1f);

        foreach (var entity in _selection)
        {
            foreach (var grip in GripSet.For(entity))
            {
                var screen = _view.ToScreen(grip.Position);
                var rect = new RectangleF(screen.X - size / 2, screen.Y - size / 2, size, size);

                g.FillRectangle(fill, rect);
                g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
            }
        }
    }

    /// <summary>Click trên tờ: chọn đối tượng paper space hoặc đặt chữ mới.</summary>
    private void HandlePaperClick(Pt paperPoint, bool addToSelection)
    {
        if (_paperScene is null || _editor is null) return;

        if (_mode == EditMode.PaperText)
        {
            _paperTextPoint = paperPoint;
            _input = InputRequest.PaperText;
            RaisePrompt();
            return;
        }

        // Viewport không nằm trong scene giấy nên phải dò khung riêng.
        // Click vào khung → chọn viewport và bắt đầu kéo dời.
        var viewportHit = FindViewportAt(paperPoint);

        // Gần góc của viewport ĐANG CHỌN → đổi kích thước thay vì dời khung
        if (_selectedViewport is not null &&
            ViewportCornerNear(_selectedViewport, paperPoint, out var cornerX, out var cornerY))
        {
            _resizingViewport = true;
            _paperDragging = true;
            _resizeAnchorWorld = paperPoint;
            _resizeCornerX = cornerX;
            _resizeCornerY = cornerY;
            _resizeOriginalWidth = viewportHit!.Width;
            _resizeOriginalHeight = viewportHit.Height;

            _statusOverride = "Đổi kích thước viewport: kéo góc";
            RaisePrompt();
            Invalidate();
            return;
        }

        if (viewportHit is not null)
        {
            _selectedViewport = viewportHit;
            _paperSelection.Clear();
            _paperDragging = true;
            _paperDragFromScreen = _lastMouseScreen;
            _paperDragToScreen = _lastMouseScreen;
            _viewportDragStart = paperPoint;
            _viewportDragOrigin = viewportHit.Center;

            _statusOverride = $"Chọn viewport 1:{CadEditor.ViewportScale(viewportHit):0.##}";
            RaiseSelection();
            RaisePrompt();
            Invalidate();
            return;
        }

        _selectedViewport = null;

        var tolerance = _view.PixelsToWorld(10);
        var hit = _paperScene.HitTest(paperPoint, tolerance);

        // Click vào đối tượng đang chọn → bắt đầu kéo
        if (!addToSelection && hit is not null && _paperSelection.Contains(hit.Entity))
        {
            _paperDragging = true;
            _paperDragFromScreen = _lastMouseScreen;
            _paperDragToScreen = _lastMouseScreen;
            return;
        }

        if (!addToSelection) _paperSelection.Clear();

        if (hit is not null) _paperSelection.Add(hit.Entity);

        RaiseSelection();
        Invalidate();
    }

    /// <summary>
    /// Điểm có nằm gần một trong 4 góc của viewport không (để bắt đầu đổi kích thước).
    /// Trả về hướng góc: (±1, ±1).
    /// </summary>
    private bool ViewportCornerNear(Viewport viewport, Pt point, out int cornerX, out int cornerY)
    {
        cornerX = 0;
        cornerY = 0;

        var halfWidth = viewport.Width / 2;
        var halfHeight = viewport.Height / 2;

        var centerX = viewport.Center.X;
        var centerY = viewport.Center.Y;

        var tolerance = _view.PixelsToWorld(10);

        var corners = new (double X, double Y, int SignX, int SignY)[]
        {
            (centerX - halfWidth, centerY - halfHeight, -1, -1),
            (centerX + halfWidth, centerY - halfHeight, 1, -1),
            (centerX + halfWidth, centerY + halfHeight, 1, 1),
            (centerX - halfWidth, centerY + halfHeight, -1, 1),
        };

        foreach (var corner in corners)
        {
            var distance = Math.Sqrt(
                Math.Pow(point.X - corner.X, 2) + Math.Pow(point.Y - corner.Y, 2));

            if (distance > tolerance) continue;

            cornerX = corner.SignX;
            cornerY = corner.SignY;

            return true;
        }

        return false;
    }

    /// <summary>Kết thúc đổi kích thước viewport: ghi undo bằng một lệnh của editor.</summary>
    private void CommitViewportResize()
    {
        if (_editor is null || _selectedViewport is null) return;

        var viewport = _selectedViewport;

        // editor không có API "đặt kích thước", dùng RunCustom để có undo
        var width = viewport.Width;
        var height = viewport.Height;

        _editor.RunCustom("Resize viewport",
            () =>
            {
                viewport.Width = width;
                viewport.Height = height;
            },
            () =>
            {
                viewport.Width = _resizeOriginalWidth;
                viewport.Height = _resizeOriginalHeight;
            });

        _statusOverride = $"Viewport {width:0.#}×{height:0.#} mm";
        RefreshPaper();
    }

    private double _resizeOriginalWidth;
    private double _resizeOriginalHeight;

    /// <summary>Viewport (khung nhìn mô hình) chứa điểm cho trước, nếu có.</summary>
    private Viewport? FindViewportAt(Pt paperPoint)
    {
        if (_paperBlock is null) return null;

        foreach (var viewport in _paperBlock.Entities.OfType<Viewport>())
        {
            if (viewport.ViewHeight <= 1e-9) continue;   // bỏ qua khung tờ

            var halfWidth = viewport.Width / 2;
            var halfHeight = viewport.Height / 2;

            if (Math.Abs(paperPoint.X - viewport.Center.X) <= halfWidth &&
                Math.Abs(paperPoint.Y - viewport.Center.Y) <= halfHeight)
                return viewport;
        }

        return null;
    }

    /// <summary>Kết thúc kéo: dịch các đối tượng paper đã chọn theo độ dời chuột.</summary>
    private void FinishPaperDrag()
    {
        if (!_paperDragging) return;

        _paperDragging = false;

        // Kéo viewport: ghi undo rồi vẽ lại tờ
        if (_selectedViewport is not null && _editor is not null)
        {
            var viewport = _selectedViewport;
            var origin = _viewportDragOrigin;
            var final = viewport.Center;

            viewport.Center = origin;

            _editor.RunCustom("Move viewport",
                () => { viewport.Center = final; },
                () => { viewport.Center = origin; });

            RefreshPaper();
            Invalidate();
            return;
        }

        if (_editor is null || _paperBlock is null || _paperSelection.Count == 0)
        {
            Invalidate();
            return;
        }

        var from = _view.ToWorld(System.Drawing.Point.Round(_paperDragFromScreen));
        var to = _view.ToWorld(System.Drawing.Point.Round(_paperDragToScreen));

        var dx = to.X - from.X;
        var dy = to.Y - from.Y;

        if (Math.Abs(dx) < 1e-6 && Math.Abs(dy) < 1e-6)
        {
            Invalidate();
            return;
        }

        var victims = _paperSelection.ToList();
        var delta = Affine2.Translate(dx, dy);

        _editor.RunCustom("Move (paper)",
            () => { foreach (var e in victims) EntityTransform.Apply(e, delta); },
            () => { foreach (var e in victims) EntityTransform.Apply(e, Affine2.Translate(-dx, -dy)); });

        Message?.Invoke($"MOVE (paper): ({dx:0.##}, {dy:0.##}) mm");
        RefreshPaper();
    }

    /// <summary>Xoá các đối tượng paper space đang chọn (có undo).</summary>
    public bool DeletePaperSelection()
    {
        if (_paperBlock is null || _editor is null || _paperSelection.Count == 0) return false;

        var victims = _paperSelection.ToList();
        var block = _paperBlock;

        _editor.RunCustom("Erase (paper)",
            () => { foreach (var e in victims) block.Entities.Remove(e); },
            () => { foreach (var e in victims) block.Entities.Add(e); });

        _paperSelection.Clear();
        RefreshPaper();
        return true;
    }

    /// <summary>Dựng lại tờ in sau khi sửa đối tượng paper space.</summary>
    public void RefreshPaper()
    {
        if (_editor is null || _paperBlock is null) return;

        var layoutObject = OpenCad.Core.Layout.LayoutFactory.Find(
            _editor.Workspace.Document, Sheet?.Name ?? string.Empty);

        if (layoutObject is not null)
        {
            var rebuilt = OpenCad.Core.Layout.LayoutFactory.Read(
                _editor.Workspace.Document, layoutObject);

            if (rebuilt is not null)
            {
                var block = _paperBlock;
                ShowPaper(rebuilt, block);
                return;
            }
        }

        _paperScene = new CadScene();

        try { _paperScene.Build(_paperBlock.Entities.Where(e => e is not Viewport)); }
        catch { }

        Invalidate();
    }

    /// <summary>Vẽ tờ in: nền giấy trắng, từng mảng hình cắt theo khung viewport.</summary>
    private void DrawPaperSheet(Graphics g)
    {
        var sheet = Sheet!;

        g.Clear(Color.FromArgb(45, 48, 54));

        var topLeft = _view.ToScreen(new Pt(0, sheet.HeightMm));
        var bottomRight = _view.ToScreen(new Pt(sheet.WidthMm, 0));

        var rect = new RectangleF(
            (float)Math.Min(topLeft.X, bottomRight.X),
            (float)Math.Min(topLeft.Y, bottomRight.Y),
            (float)Math.Abs(bottomRight.X - topLeft.X),
            (float)Math.Abs(bottomRight.Y - topLeft.Y));

        using (var page = new SolidBrush(Color.White))
            g.FillRectangle(page, rect);

        using (var border = new Pen(Color.FromArgb(90, 200, 200, 200), 1f))
            g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);

        foreach (var item in sheet.Items)
        {
            var state = g.Save();

            if (item.Clip is { } clip)
            {
                var c1 = _view.ToScreen(new Pt(clip.MinX, clip.MaxY));
                var c2 = _view.ToScreen(new Pt(clip.MaxX, clip.MinY));

                g.SetClip(new RectangleF(
                    (float)Math.Min(c1.X, c2.X),
                    (float)Math.Min(c1.Y, c2.Y),
                    (float)Math.Abs(c2.X - c1.X),
                    (float)Math.Abs(c2.Y - c1.Y)),
                    System.Drawing.Drawing2D.CombineMode.Intersect);
            }

            _renderer.PaperMode = true;
            _renderer.DrawPrims(g, item.Prims, _view);
            _renderer.PaperMode = false;

            g.Restore(state);
        }

        // Đánh dấu các đối tượng paper space đang chọn (kèm xem trước khi kéo)
        if (_paperSelection.Count > 0 && _paperScene is not null)
        {
            var state = g.Save();

            if (_paperDragging)
            {
                g.TranslateTransform(_paperDragToScreen.X - _paperDragFromScreen.X,
                                     _paperDragToScreen.Y - _paperDragFromScreen.Y);
            }

            _renderer.PaperMode = true;

            foreach (var entry in _paperScene.Entries)
                if (_paperSelection.Contains(entry.Entity))
                    _renderer.DrawPrims(g, entry.Prims, _view, selected: true);

            _renderer.PaperMode = false;
            g.Restore(state);
        }
    }

    /// <summary>Lưới điểm kiểu AutoCAD, mật độ tự đổi theo mức zoom.</summary>
    /// <summary>UCS icon tại gốc toạ độ, kiểu AutoCAD.</summary>
    /// <summary>
    /// @brief Draws standard AutoCAD viewport mode tags in the upper-left of the canvas.
    /// @param g Graphics device context.
    /// </summary>
    private void DrawViewportControls(Graphics g)
    {
        if (Sheet is not null || Width < 200 || Height < 150) return;

        using var font = new Font("Segoe UI", 8f, FontStyle.Regular);
        using var textBrush = new SolidBrush(Color.FromArgb(160, 170, 185));
        using var bgBrush = new SolidBrush(Color.FromArgb(140, 25, 30, 38));

        var text = I18n.T("Canvas.Viewport");
        var size = g.MeasureString(text, font);
        g.FillRectangle(bgBrush, 8, 8, size.Width + 8, size.Height + 4);
        g.DrawString(text, font, textBrush, 12, 10);
    }

    /// <summary>
    /// @brief Draws the AutoCAD floating navigation bar in the upper-right corner of the canvas.
    /// @param g Graphics device context.
    /// </summary>
    private void DrawNavBar(Graphics g)
    {
        if (Width < 300 || Height < 200) return;

        var barWidth = 32;
        var barHeight = 120;
        var x = Width - barWidth - 12;
        var y = 12;

        using var bgBrush = new SolidBrush(Color.FromArgb(180, 34, 38, 46));
        using var borderPen = new Pen(Color.FromArgb(60, 68, 80), 1f);

        g.FillRectangle(bgBrush, x, y, barWidth, barHeight);
        g.DrawRectangle(borderPen, x, y, barWidth, barHeight);

        var panBmp = Icons.Get(Glyph.Pan, 18);
        var zoomBmp = Icons.Get(Glyph.ZoomExtents, 18);
        g.DrawImage(panBmp, x + 7, y + 8);
        g.DrawImage(zoomBmp, x + 7, y + 36);

        using var pen = new Pen(Color.FromArgb(200, 210, 220), 1.5f);
        // Zoom In [+]
        g.DrawLine(pen, x + 16, y + 70, x + 16, y + 82);
        g.DrawLine(pen, x + 10, y + 76, x + 22, y + 76);
        // Zoom Out [-]
        g.DrawLine(pen, x + 10, y + 104, x + 22, y + 104);
    }

    private void DrawUcsIcon(Graphics g)
    {
        var origin = _view.ToScreen(new Pt(0, 0));

        if (origin.X < -200 || origin.X > Width + 200 ||
            origin.Y < -200 || origin.Y > Height + 200) return;

        using var xPen = new Pen(Color.FromArgb(220, 220, 80, 80), 1.6f);
        using var yPen = new Pen(Color.FromArgb(220, 90, 200, 90), 1.6f);

        const float len = 30f;

        g.DrawLine(xPen, origin.X, origin.Y, origin.X + len, origin.Y);
        g.DrawLine(yPen, origin.X, origin.Y, origin.X, origin.Y - len);

        g.DrawLine(xPen, origin.X + len, origin.Y, origin.X + len - 6, origin.Y - 4);
        g.DrawLine(xPen, origin.X + len, origin.Y, origin.X + len - 6, origin.Y + 4);
        g.DrawLine(yPen, origin.X, origin.Y - len, origin.X - 4, origin.Y - len + 6);
        g.DrawLine(yPen, origin.X, origin.Y - len, origin.X + 4, origin.Y - len + 6);

        using var font = new Font("Segoe UI", 8f, FontStyle.Bold);
        g.DrawString("X", font, Brushes.IndianRed, origin.X + len + 3, origin.Y - 7);
        g.DrawString("Y", font, Brushes.LightGreen, origin.X - 4, origin.Y - len - 17);
    }

    private void DrawGrid(Graphics g)
    {
        if (!ShowGrid || _view.Zoom <= 0) return;

        var spacing = ChooseGridSpacing(_view.Zoom);
        if (spacing <= 0) return;

        var topLeft = _view.ToWorld(new PointF(0, 0));
        var bottomRight = _view.ToWorld(new PointF(Width, Height));

        var minX = Math.Min(topLeft.X, bottomRight.X);
        var maxX = Math.Max(topLeft.X, bottomRight.X);
        var minY = Math.Min(topLeft.Y, bottomRight.Y);
        var maxY = Math.Max(topLeft.Y, bottomRight.Y);

        var startX = Math.Floor(minX / spacing) * spacing;
        var startY = Math.Floor(minY / spacing) * spacing;

        var cols = (int)Math.Ceiling((maxX - startX) / spacing) + 1;
        var rows = (int)Math.Ceiling((maxY - startY) / spacing) + 1;

        if (cols <= 0 || rows <= 0 || (long)cols * rows > 60_000) return;

        using var brush = new SolidBrush(Color.FromArgb(70, 160, 170, 185));

        for (var i = 0; i < cols; i++)
        {
            var x = startX + i * spacing;
            for (var j = 0; j < rows; j++)
            {
                var y = startY + j * spacing;
                var s = _view.ToScreen(new Pt(x, y));
                g.FillRectangle(brush, s.X, s.Y, 1.4f, 1.4f);
            }
        }
    }

    private static double ChooseGridSpacing(double zoom)
    {
        // Mong muốn khoảng 45 px giữa hai điểm lưới
        var target = 45.0 / zoom;
        if (target <= 0 || double.IsNaN(target) || double.IsInfinity(target)) return 0;

        var exponent = Math.Floor(Math.Log10(target));
        var baseValue = Math.Pow(10, exponent);
        var mantissa = target / baseValue;

        var multiplier = mantissa <= 1 ? 1 : mantissa <= 2 ? 2 : mantissa <= 5 ? 5 : 10;
        return baseValue * multiplier;
    }

    private void DrawCrosshair(Graphics g)
    {
        if (!ShowCrosshair || !ClientRectangle.Contains(PointToClient(MousePosition))) return;

        var p = _view.ToScreen(_cursor);

        using var pen = new Pen(Color.FromArgb(110, 190, 195, 205), 1f);
        g.DrawLine(pen, 0, p.Y, Width, p.Y);
        g.DrawLine(pen, p.X, 0, p.X, Height);

        // Pickbox nhỏ ở tâm
        using var fill = new SolidBrush(_renderer.Background);
        g.FillRectangle(fill, p.X - 5, p.Y - 5, 10, 10);
        g.DrawRectangle(pen, p.X - 5, p.Y - 5, 10, 10);
    }

    private bool IsLayerVisible(Entity entity)
    {
        var layer = entity.Layer;
        return layer is null || layer.IsOn;
    }

    private void DrawPreview(Graphics g)
    {
        using var pen = new Pen(Color.FromArgb(255, 210, 80), 1.3f)
        {
            DashStyle = System.Drawing.Drawing2D.DashStyle.Dash,
        };

        if (_boxSelecting)
        {
            var a = _view.ToScreen(_boxStart);
            var b = _view.ToScreen(_cursor);
            var rect = new RectangleF(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                                      Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            using var fill = new SolidBrush(Color.FromArgb(30, 0, 200, 255));
            g.FillRectangle(fill, rect);
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
        }

        if (_points.Count == 0) return;

        var last = _view.ToScreen(_points[^1]);
        var cur = _view.ToScreen(_cursor);

        switch (_mode)
        {
            case EditMode.Line:
            case EditMode.XLine:
            case EditMode.Ray:
            case EditMode.Polyline:
            case EditMode.Move:
            case EditMode.Copy:
            case EditMode.Mirror:
                g.DrawLine(pen, last, cur);
                break;

            case EditMode.Rectangle:
                g.DrawRectangle(pen, Math.Min(last.X, cur.X), Math.Min(last.Y, cur.Y),
                                Math.Abs(cur.X - last.X), Math.Abs(cur.Y - last.Y));
                break;

            case EditMode.Circle:
            {
                var r = (float)(Pt.Distance(_points[0], _cursor) * _view.Zoom);
                g.DrawEllipse(pen, last.X - r, last.Y - r, r * 2, r * 2);
                break;
            }

            case EditMode.Arc:
                g.DrawLine(pen, last, cur);
                if (_points.Count >= 2)
                    g.DrawLine(pen, _view.ToScreen(_points[0]), cur);
                break;

            case EditMode.Rotate:
                g.DrawLine(pen, last, cur);
                g.DrawEllipse(pen, last.X - 6, last.Y - 6, 12, 12);
                break;
        }

        using var marker = new SolidBrush(Color.FromArgb(255, 210, 80));
        foreach (var p in _points)
        {
            var s = _view.ToScreen(p);
            g.FillRectangle(marker, s.X - 2.5f, s.Y - 2.5f, 5, 5);
        }
    }

    private void DrawSnapMarker(Graphics g)
    {
        if (_activeSnap is null) return;

        var s = _view.ToScreen(_activeSnap.Point);
        using var pen = new Pen(Color.FromArgb(0, 255, 150), 1.6f);
        using var brush = new SolidBrush(Color.FromArgb(0, 255, 150));
        const float r = 6f;

        switch (_activeSnap.Kind)
        {
            case SnapKind.Endpoint:
                g.DrawRectangle(pen, s.X - r, s.Y - r, r * 2, r * 2);
                break;

            case SnapKind.Midpoint:
                g.DrawPolygon(pen, new[]
                {
                    new PointF(s.X, s.Y - r),
                    new PointF(s.X + r, s.Y + r),
                    new PointF(s.X - r, s.Y + r),
                });
                break;

            case SnapKind.Center:
                g.DrawEllipse(pen, s.X - r, s.Y - r, r * 2, r * 2);
                break;

            case SnapKind.Intersection:
                g.DrawLine(pen, s.X - r, s.Y - r, s.X + r, s.Y + r);
                g.DrawLine(pen, s.X - r, s.Y + r, s.X + r, s.Y - r);
                break;

            case SnapKind.Quadrant:
                g.DrawPolygon(pen, new[]
                {
                    new PointF(s.X, s.Y - r),
                    new PointF(s.X + r, s.Y),
                    new PointF(s.X, s.Y + r),
                    new PointF(s.X - r, s.Y),
                });
                break;

            default:
                g.FillRectangle(brush, s.X - 3, s.Y - 3, 6, 6);
                break;
        }

        using var font = new Font("Segoe UI", 8f);
        g.DrawString(_activeSnap.Label, font, brush, s.X + 10, s.Y + 6);
    }

    private void UpdateViewSize()
    {
        _view.Width = Width;
        _view.Height = Height;
    }

    // ---------------------------------------------------------------- MOUSE

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);

        if (e.Button != MouseButtons.Left) return;
        if (_editor is null || Sheet is not null || _mode != EditMode.Select) return;

        var hit = _editor.Scene.HitTest(_view.ToWorld(e.Location), _view.PixelsToWorld(6));

        if (hit?.Entity is not TextEntity text) return;

        _editingText = text;
        _input = InputRequest.EditText;
        _statusOverride = null;
        RaisePrompt();
        Focus();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (_editor is null) return;

        if (e.Button == MouseButtons.Middle)
        {
            _panning = true;
            _lastMouse = e.Location;
            Cursor = Cursors.SizeAll;
            return;
        }

        _lastMouseScreen = e.Location;

        if (e.Button == MouseButtons.Right)
        {
            // Đang chạy lệnh thì chuột phải = huỷ; đang chọn bình thường thì để menu ngữ cảnh hiện
            if (_points.Count > 0 || _input != InputRequest.None || _mode != EditMode.Select)
            {
                Cancel();
            }
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        // Ở chế độ tờ in: đang chạy công cụ cần điểm thì đưa điểm cho công cụ,
        // còn chế độ chọn thì xử lý chọn đối tượng trên tờ.
        if (Sheet is not null)
        {
            var paperPoint = _view.ToWorld(e.Location);

            if (_mode != EditMode.Select)
            {
                _cursorRaw = paperPoint;
                OnPoint(paperPoint);
                return;
            }

            HandlePaperClick(paperPoint, ShiftHeld);
            return;
        }

        UpdateCursor(_view.ToWorld(e.Location));

        if (_mode == EditMode.Select)
        {
            StartSelection(e.Location);
            return;
        }

        OnPoint(_cursor);
    }

    private void StartSelection(PointF screen)
    {
        // Ưu tiên grip của đối tượng đang chọn: kéo trực tiếp điểm điều khiển
        if (_selection.Count > 0)
        {
            var gripTolerance = _view.PixelsToWorld(8);

            foreach (var selected in _selection)
            {
                var grip = GripSet.Nearest(selected, _cursor, gripTolerance);
                if (grip is null) continue;

                // Clone có thể ném ngoại lệ với vài loại entity — không để app chết
                Entity? original = null;

                try { original = selected.Clone() as Entity; }
                catch (Exception ex) { ReportUiError("Chuẩn bị kéo grip", ex); }

                if (original is null) continue;

                _gripEntity = selected;
                _grip = grip;
                _gripOriginal = original;
                _gripDragging = true;
                Invalidate();
                return;
            }
        }

        var tolerance = _view.PixelsToWorld(7);
        var hit = _editor!.Scene.HitTest(_cursor, tolerance);

        if (hit is null)
        {
            if (!ShiftHeld) _selection.Clear();
            _boxSelecting = true;
            _boxStart = _cursor;
            RaiseSelection();
            Invalidate();
            return;
        }

        if (ShiftHeld)
        {
            if (!_selection.Add(hit.Entity)) _selection.Remove(hit.Entity);
            RaiseSelection();
            Invalidate();
            return;
        }

        if (!_selection.Contains(hit.Entity))
        {
            _selection.Clear();
            _selection.Add(hit.Entity);
        }

        _draggingEntities = true;
        _dragAnchor = _cursor;
        _dragAccum = Affine2.Identity;
        RaiseSelection();
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_editor is null) return;

        if (_panning)
        {
            _view.PanByPixels(e.X - _lastMouse.X, e.Y - _lastMouse.Y);
            _lastMouse = e.Location;
            Invalidate();
            return;
        }

        UpdateCursor(_view.ToWorld(e.Location));

        // Kéo grip: cập nhật hình theo con trỏ (undo ghi khi nhả chuột)
        if (_gripDragging && _gripEntity is not null && _grip is not null)
        {
            try
            {
                GripEdit.Move(_gripEntity, _grip, _cursor);
                _editor.Scene.Build(_editor.Workspace.Document, CadEditor.IsEntityVisible);
            }
            catch (Exception ex)
            {
                // Hình học không hợp lệ (bán kính 0, chiều cao 0…) — bỏ qua nhịp này
                ReportUiError("Kéo grip", ex);
            }

            Invalidate();
            return;
        }

        // Đang kéo GÓC viewport: đổi kích thước (phải kiểm TRƯỚC nhánh dời khung)
        if (_resizingViewport && _selectedViewport is not null)
        {
            var here = _view.ToWorld(e.Location);

            var dx = here.X - _resizeAnchorWorld.X;
            var dy = here.Y - _resizeAnchorWorld.Y;

            // Góc đang kéo quyết định dấu: kéo ra xa tâm thì khung to lên
            var width = _selectedViewport.Width + _resizeCornerX * dx * 2;
            var height = _selectedViewport.Height + _resizeCornerY * dy * 2;

            if (width > 5 && height > 5)
            {
                _selectedViewport.Width = width;
                _selectedViewport.Height = height;
            }

            RefreshPaper();
            Invalidate();
            return;
        }

        if (_draggingEntities && _selection.Count > 0)
        {
            var delta = Affine2.Translate(_cursor.X - _dragAnchor.X, _cursor.Y - _dragAnchor.Y);
            _editor.ApplyTransform(_selection.ToList(), delta);
            _dragAccum = delta.After(_dragAccum);
            _dragAnchor = _cursor;
            Invalidate();
            return;
        }

        // Đang kéo viewport trên tờ: dời khung theo con trỏ
        if (_paperDragging && _selectedViewport is not null)
        {
            var here = _view.ToWorld(e.Location);

            _selectedViewport.Center = new CSMath.XYZ(
                _viewportDragOrigin.X + (here.X - _viewportDragStart.X),
                _viewportDragOrigin.Y + (here.Y - _viewportDragStart.Y),
                _viewportDragOrigin.Z);

            RefreshPaper();
            Invalidate();
            return;
        }

        // Kéo đối tượng trên tờ: phải ghi lại vị trí con trỏ, nếu không độ dời luôn bằng 0
        if (_paperDragging)
        {
            _paperDragToScreen = e.Location;
            Invalidate();
            return;
        }

        if (_boxSelecting || _points.Count > 0) Invalidate();

        RaisePrompt();
    }

    /// <summary>Kết thúc kéo grip: ghi undo với hình cũ và hình mới.</summary>
    /// <summary>Lỗi giao diện gần nhất (dùng cho kiểm thử và chẩn đoán).</summary>
    internal static string? LastUiError { get; private set; }

    /// <summary>
    /// Hiện lỗi giao diện ở status bar và ghi log, KHÔNG ném tiếp.
    /// Dùng cho các thao tác chuột — một lỗi nhỏ không được phép làm chết app.
    /// </summary>
    internal static void ReportUiError(string context, Exception ex)
    {
        LastUiError = $"{context}: {ex.GetType().Name} — {ex.Message}";
    }

    private void CommitGripDrag()
    {
        var entity = _gripEntity;
        var original = _gripOriginal;

        _gripDragging = false;
        _gripEntity = null;
        _grip = null;
        _gripOriginal = null;

        if (_editor is null || entity is null || original is null) return;

        if (entity.Clone() is not Entity final) return;

        // Trả hình về trạng thái ban đầu rồi để lệnh áp lại hình mới (có undo/redo)
        GripEdit.CopyGeometry(original, entity);

        void Rebuild() => _editor.Scene.Build(_editor.Workspace.Document, CadEditor.IsEntityVisible);

        _editor.RunCustom("Grip edit",
            () => { GripEdit.CopyGeometry(final, entity); Rebuild(); },
            () => { GripEdit.CopyGeometry(original, entity); Rebuild(); });

        _statusOverride = "Đã kéo điểm điều khiển.";
        RaiseSelection();
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (_resizingViewport)
        {
            _resizingViewport = false;
            _paperDragging = false;
            CommitViewportResize();
            Invalidate();
            return;
        }

        if (_gripDragging)
        {
            CommitGripDrag();
            return;
        }

        if (_panning && e.Button == MouseButtons.Middle)
        {
            _panning = false;
            Cursor = _mode == EditMode.Select ? Cursors.Default : Cursors.Cross;
        }

        if (e.Button != MouseButtons.Left) return;

        // Kéo đối tượng trên tờ in: ghi undo rồi vẽ lại tờ
        if (_paperDragging)
        {
            FinishPaperDrag();
            return;
        }


        if (_draggingEntities)
        {
            _draggingEntities = false;

            if (!_dragAccum.IsIdentity)
                _editor?.CommitTransform(_selection.ToList(), _dragAccum, "Move");

            _dragAccum = Affine2.Identity;
            Invalidate();
            return;
        }

        if (_boxSelecting)
        {
            _boxSelecting = false;
            FinishBoxSelection();
            Invalidate();
        }
    }

    private void FinishBoxSelection()
    {
        if (_editor is null) return;

        var rect = new RectD(
            Math.Min(_boxStart.X, _cursor.X), Math.Min(_boxStart.Y, _cursor.Y),
            Math.Max(_boxStart.X, _cursor.X), Math.Max(_boxStart.Y, _cursor.Y));

        if (rect.Width < 1e-9 && rect.Height < 1e-9) return;

        var crossing = _cursor.X < _boxStart.X;   // quét phải → trái = crossing
        var entities = _editor.Scene.SelectInRect(rect, crossing);

        if (!ShiftHeld) _selection.Clear();
        foreach (var e in entities) _selection.Add(e);

        RaiseSelection();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        UpdateViewSize();
        _view.ZoomAt(e.Location, e.Delta > 0 ? 1.2 : 1 / 1.2);
        Invalidate();
        RaisePrompt();
    }

    private void UpdateCursor(Pt raw)
    {
        _cursorRaw = raw;

        if (_editor is not null && _snap.Enabled && !_panning && Sheet is null)
            _activeSnap = _snap.Find(_editor.Scene, raw, _view.Zoom);
        else
            _activeSnap = null;

        if (_activeSnap is not null)
        {
            _cursor = _activeSnap.Point;
            _statusOverride = null;
            CursorMoved?.Invoke(_cursor);
            return;
        }

        var reference = _points.Count > 0 ? _points[^1] : (Pt?)null;

        if (_snap.PolarEnabled && reference is { } polarRef && _mode != EditMode.Select)
            _cursor = SnapEngine.ApplyPolar(polarRef, raw, _snap.PolarIncrementDegrees);
        else if (_snap.OrthoEnabled && reference is { } r && _mode != EditMode.Select)
            _cursor = SnapEngine.ApplyOrtho(r, raw);
        else
            _cursor = raw;

        _statusOverride = null;
        CursorMoved?.Invoke(_cursor);
    }

    // ------------------------------------------------------------- COMMANDS

    private void OnPoint(Pt p)
    {
        if (_editor is null) return;

        switch (_mode)
        {
            // XLINE / RAY: hai điểm — gốc + hướng, rồi kết thúc ngay
            case EditMode.XLine:
            case EditMode.Ray:
                if (_points.Count == 0)
                {
                    _points.Add(p);
                    break;
                }

                if (Pt.Distance(_points[0], p) > 1e-9)
                {
                    var origin = _points[0];
                    var direction = new CSMath.XYZ(p.X - origin.X, p.Y - origin.Y, 0);

                    AddDrawn(
                        _mode == EditMode.XLine
                            ? new XLine
                            {
                                FirstPoint = new CSMath.XYZ(origin.X, origin.Y, 0),
                                Direction = direction,
                                Layer = CurrentLayer,
                            }
                            : new Ray
                            {
                                StartPoint = new CSMath.XYZ(origin.X, origin.Y, 0),
                                Direction = direction,
                                Layer = CurrentLayer,
                            },
                        _mode == EditMode.XLine ? "XLINE" : "RAY");
                }

                Cancel();
                break;

            case EditMode.Line:
                if (_points.Count == 0)
                {
                    _points.Add(p);
                }
                else
                {
                    var start = _points[^1];
                    if (Pt.Distance(start, p) > 1e-9)
                    {
                        AddDrawn(MakeLine(start, p), "LINE");
                        _points.Clear();
                        _points.Add(p);
                    }
                }
                break;

            case EditMode.DimBaseline:
            {
                if (_lastDimension is null || _editor is null)
                {
                    _statusOverride = "DIMBASELINE: chưa có kích thước để làm gốc.";
                    Cancel();
                    break;
                }

                // Từ gốc chung; mỗi kích thước mới lùi ra xa thêm một bậc
                var origin = _lastDimFirst;
                var step = _baselineStep++;

                var baseOffset = new Pt(
                    _lastDimLocation.X - _lastDimFirst.X,
                    _lastDimLocation.Y - _lastDimFirst.Y);

                var spread = 400.0 * (step + 1);

                var location = new Pt(
                    p.X + baseOffset.X + spread,
                    p.Y + baseOffset.Y + spread);

                CreateDimension(origin, p, location);
                break;
            }

            case EditMode.DimContinue:
            {
                if (_lastDimension is null || _editor is null)
                {
                    _statusOverride = "DIMCONTINUE: chưa có kích thước để nối.";
                    Cancel();
                    break;
                }

                // Kích thước mới nối từ điểm thứ hai của cái trước
                var start = _lastDimSecond;
                var sameOffset = new Pt(
                    _lastDimLocation.X - _lastDimSecond.X,
                    _lastDimLocation.Y - _lastDimSecond.Y);

                CreateDimension(start, p, new Pt(p.X + sameOffset.X, p.Y + sameOffset.Y));
                break;
            }

            case EditMode.MLeader:
            {
                _points.Add(p);

                if (_points.Count == 2)
                {
                    _input = InputRequest.MLeaderText;
                    RaisePrompt();
                }

                break;
            }

            case EditMode.TableDraw:
            {
                if (_tableRows < 1 || _tableColumns < 1)
                {
                    _statusOverride = "TABLE: chưa có kích thước bảng.";
                    Cancel();
                    break;
                }

                var table = MakeTable(p, _tableRows, _tableColumns);

                ApplyCurrentLayer(table);
                AddDrawn(table, "TABLE");

                _statusOverride = $"TABLE: {_tableRows}×{_tableColumns}";
                _tableRows = 0;
                _tableColumns = 0;
                Cancel();
                break;
            }

            case EditMode.PointDraw:
            {
                var placed = new ACadSharp.Entities.Point
                {
                    Location = new CSMath.XYZ(p.X, p.Y, 0),
                };

                ApplyCurrentLayer(placed);
                AddDrawn(placed, "POINT");
                break;
            }

            case EditMode.Polyline:
            case EditMode.MLine:
            case EditMode.Spline:
                _points.Add(p);
                break;

            case EditMode.Rectangle:
                if (_points.Count == 0) _points.Add(p);
                else
                {
                    AddDrawn(MakeRectangle(_points[0], p), "RECTANG");
                    Cancel();
                }
                break;

            case EditMode.Circle:
                if (_points.Count == 0) _points.Add(p);
                else
                {
                    var r = Pt.Distance(_points[0], p);
                    if (r > 1e-9)
                        AddDrawn(MakeCircle(_points[0], r), "CIRCLE");
                    Cancel();
                }
                break;

            case EditMode.Arc:
                _points.Add(p);
                if (_points.Count == 3)
                {
                    var arc = MakeArc(_points[0], _points[1], _points[2]);
                    if (arc is not null) AddDrawn(arc, "ARC");
                    Cancel();
                }
                break;

            case EditMode.Text:
                _points.Add(p);
                _input = InputRequest.Text;
                break;

            case EditMode.Move:
            case EditMode.Copy:
                _points.Add(p);
                if (_points.Count == 2)
                {
                    var basePoint = _points[0];
                    var delta = Affine2.Translate(_points[1].X - basePoint.X, _points[1].Y - basePoint.Y);
                    var targets = _selection.ToList();

                    if (_mode == EditMode.Move)
                    {
                        _editor.TransformEntities(targets, delta, "Move");
                        Cancel();
                    }
                    else
                    {
                        // COPY lặp như AutoCAD: giữ điểm gốc, click tiếp để sao chép thêm
                        _editor.CopyTransform(targets, delta, CloneEntity, "Copy");
                        _points.Clear();
                        _points.Add(basePoint);
                        _statusOverride = "COPY: click điểm đích tiếp theo (Esc để kết thúc)";
                        RaisePrompt();
                        Invalidate();
                    }
                }
                break;

            case EditMode.Rotate:
                _points.Add(p);
                if (_points.Count == 1) _input = InputRequest.RotateAngle;
                else
                {
                    var angle = Math.Atan2(p.Y - _points[0].Y, p.X - _points[0].X);
                    CommitRotate(angle);
                }
                break;

            case EditMode.Scale:
                _points.Add(p);
                if (_points.Count == 1) _input = InputRequest.ScaleFactor;
                break;

            case EditMode.Mirror:
                _points.Add(p);
                if (_points.Count == 2)
                {
                    var m = Affine2.Mirror(_points[0], _points[1]);
                    _editor.CopyTransform(_selection.ToList(), m, CloneEntity, "Mirror");
                    Cancel();
                }
                break;

            case EditMode.Offset:
            {
                if (_offsetDistance <= 0) break;

                var source = _selection.FirstOrDefault();
                if (source is null) { Cancel(); break; }

                var created = OffsetOperation.Create(source, _offsetDistance, p);
                if (created is not null)
                    _editor.AddEntities(new[] { created }, "Offset");

                Cancel();
                break;
            }

            case EditMode.Trim:
                ApplyTrim(p);
                break;

            case EditMode.Extend:
                ApplyExtend(p);
                break;

            case EditMode.Hatch:
                ApplyHatch(p);
                break;

            case EditMode.Dimension:
                _points.Add(p);
                if (_points.Count == 3)
                {
                    CreateDimension(_points[0], _points[1], _points[2]);
                    Cancel();
                }
                break;

            case EditMode.DimensionRadius:
            case EditMode.DimensionDiameter:
                _points.Add(p);
                if (_points.Count == 2)
                {
                    CreateRadialDimension(_mode == EditMode.DimensionDiameter, _points[0], _points[1]);
                    Cancel();
                }
                break;

            case EditMode.DimensionAngular:
                _points.Add(p);
                if (_points.Count == 3)
                {
                    CreateAngularDimension(_points[0], _points[1], _points[2]);
                    Cancel();
                }
                break;

            case EditMode.DimensionOrdinate:
                _points.Add(p);
                if (_points.Count == 2)
                {
                    CreateOrdinateDimension(_points[0], _points[1]);
                    Cancel();
                }
                break;

            case EditMode.Ellipse:
                _points.Add(p);
                if (_points.Count == 3)
                {
                    CreateEllipse(_points[0], _points[1], _points[2]);
                    Cancel();
                }
                break;

            case EditMode.Polygon:
                _points.Add(p);
                if (_points.Count == 2)
                {
                    CreatePolygon(_points[0], _points[1]);
                    Cancel();
                }
                break;

            case EditMode.Dist:
                _points.Add(p);
                if (_points.Count == 2)
                {
                    ReportDistance(_points[0], _points[1]);
                    Cancel();
                }
                break;

            case EditMode.Area:
                ReportArea(p);
                Cancel();
                break;

            case EditMode.Insert:
                PlaceBlock(p);
                break;

            case EditMode.BlockCreate:
                ApplyCreateBlock(p);
                break;

            case EditMode.ArrayPolar:
                ApplyPolarArray(p);
                Cancel();
                break;

            case EditMode.AttributeEdit:
                PickAttributeTarget(p, define: false);
                break;

            case EditMode.AttributeDefine:
                PickAttributeTarget(p, define: true);
                break;

            case EditMode.ZoomWindow:
            {
                _points.Add(p);

                if (_points.Count == 2)
                {
                    ZoomWindow(_points[0], _points[1]);
                    _mode = EditMode.Select;
                    _points.Clear();
                    RaisePrompt();
                }

                break;
            }

            case EditMode.MatchSource:
            {
                var hitSource = _editor?.Scene.HitTest(p, _view.PixelsToWorld(8));

                if (hitSource is null)
                {
                    _statusOverride = "MATCHPROP: không có đối tượng ở điểm này.";
                    RaisePrompt();
                    break;
                }

                _matchSource = hitSource.Entity;
                _mode = EditMode.MatchTarget;
                _statusOverride = $"MATCHPROP nguồn: {hitSource.Entity.GetType().Name}";
                Message?.Invoke(_statusOverride);
                RaisePrompt();
                Invalidate();
                break;
            }

            case EditMode.MatchTarget:
            {
                var hitTarget = _editor?.Scene.HitTest(p, _view.PixelsToWorld(8));

                if (hitTarget is null || _matchSource is null) break;

                if (!ReferenceEquals(hitTarget.Entity, _matchSource))
                {
                    _editor?.MatchProperties(_matchSource, new[] { hitTarget.Entity });
                    _statusOverride = $"MATCHPROP: đã áp cho {hitTarget.Entity.GetType().Name}";
                    Message?.Invoke(_statusOverride);
                }

                Invalidate();
                break;
            }

            case EditMode.PaperLine:
                _points.Add(p);
                if (_points.Count == 2) { CreatePaperLine(_points[0], _points[1]); Cancel(); }
                break;

            case EditMode.Stretch:
                _points.Add(p);
                if (_points.Count == 4) { ApplyStretch(_points[0], _points[1], _points[2], _points[3]); Cancel(); }
                break;

            case EditMode.PaperViewport:
                _points.Add(p);
                if (_points.Count == 2)
                {
                    CreatePaperViewport(_points[0], _points[1]);
                    Cancel();
                }
                break;

            case EditMode.EditTextPick:
                PickTextForEdit(p);
                break;
        }

        RaisePrompt();
        Invalidate();
    }

    private void ApplyTrim(Pt pick)
    {
        if (_editor is null) return;

        var hit = _editor.Scene.HitTest(pick, _view.PixelsToWorld(7));
        if (hit is null)
        {
            _statusOverride = "TRIM: không tìm thấy đối tượng.";
            RaisePrompt();
            return;
        }

        var target = hit.Entity;
        var boundaries = _editor.Workspace.Document.Entities
            .Where(e => !ReferenceEquals(e, target))
            .ToList();

        var pieces = TrimExtendOperations.Trim(target, pick, boundaries);

        if (pieces is null)
        {
            _statusOverride = "TRIM: không có giao điểm nào để cắt.";
            RaisePrompt();
            return;
        }

        _editor.ReplaceEntity(target, pieces, "Trim");
        _selection.Remove(target);
        _statusOverride = pieces.Count == 0
            ? "TRIM: đã xoá đối tượng."
            : $"TRIM: còn {pieces.Count} mảnh.";
        RaiseSelection();
        Invalidate();
    }

    private void ApplyExtend(Pt pick)
    {
        if (_editor is null) return;

        var hit = _editor.Scene.HitTest(pick, _view.PixelsToWorld(7));
        if (hit is null)
        {
            _statusOverride = "EXTEND: không tìm thấy đối tượng.";
            RaisePrompt();
            return;
        }

        var target = hit.Entity;
        var boundaries = _editor.Workspace.Document.Entities
            .Where(e => !ReferenceEquals(e, target))
            .ToList();

        var extended = TrimExtendOperations.Extend(target, pick, boundaries);

        if (extended is null)
        {
            _statusOverride = "EXTEND: không tìm thấy biên theo hướng kéo dài.";
            RaisePrompt();
            return;
        }

        _editor.ReplaceEntity(target, new[] { extended }, "Extend");
        _selection.Clear();
        _selection.Add(extended);
        _statusOverride = "EXTEND: đã kéo dài tới biên.";
        RaiseSelection();
        Invalidate();
    }

    private void ApplyHatch(Pt pick)
    {
        if (_editor is null) return;

        // Tìm biên khép kín nhỏ nhất chứa điểm click
        CadScene.Entry? best = null;

        foreach (var entry in _editor.Scene.Entries)
        {
            if (!HatchFactory.CanHatch(entry.Entity)) continue;

            var contains = entry.Prims.Any(p => p.ContainsPoint(pick));
            if (!contains) continue;

            if (best is null || entry.Bounds.Width < best.Bounds.Width)
                best = entry;
        }

        if (best is null)
        {
            _statusOverride = "HATCH: không tìm thấy vùng khép kín quanh điểm này.";
            RaisePrompt();
            return;
        }

        var hatch = HatchFactory.FromBoundary(best.Entity);
        if (hatch is null)
        {
            _statusOverride = "HATCH: biên này chưa hỗ trợ.";
            RaisePrompt();
            return;
        }

        _editor.AddEntities(new[] { hatch }, "Hatch");
        _statusOverride = $"HATCH: đã tô vùng {(int)best.Bounds.Width}×{(int)best.Bounds.Height}.";
        RaisePrompt();
        Invalidate();
    }

    private void ApplyFillet(double radius)
    {
        if (_editor is null) { Cancel(); return; }

        // Một polyline: bo tất cả góc
        if (_selection.Count == 1 && _selection.First() is LwPolyline polyline)
        {
            var rounded = FilletChamfer.FilletAllCorners(polyline, radius);
            if (rounded is null)
            {
                _statusOverride = "FILLET: không bo được polyline này.";
                RaisePrompt();
                return;
            }

            _editor.ReplaceEntity(polyline, new Entity[] { rounded }, "Fillet");
            _selection.Clear();
            _statusOverride = $"FILLET: đã bo góc polyline (r = {radius}).";
            RaiseSelection();
            Cancel();
            return;
        }

        var lines = _selection.OfType<Line>().ToList();
        if (lines.Count != 2) { Cancel(); return; }

        var result = FilletChamfer.Fillet(lines[0], lines[1], radius);
        if (result is null)
        {
            _statusOverride = "FILLET: không bo được (bán kính quá lớn hoặc hai đường song song).";
            RaisePrompt();
            return;
        }

        _editor.ReplaceEntities(
            new Entity[] { lines[0], lines[1] },
            new Entity[] { result.First, result.Second, result.Connector },
            "Fillet");

        _selection.Clear();
        _statusOverride = "FILLET: đã bo góc.";
        RaiseSelection();
        Cancel();
    }

    private void ApplyChamfer(double distanceA, double distanceB)
    {
        if (_editor is null) { Cancel(); return; }

        var lines = _selection.OfType<Line>().ToList();
        if (lines.Count != 2) { Cancel(); return; }

        var result = FilletChamfer.Chamfer(lines[0], lines[1], distanceA, distanceB);
        if (result is null)
        {
            _statusOverride = "CHAMFER: không vát được (khoảng vát quá lớn hoặc hai đường song song).";
            RaisePrompt();
            return;
        }

        _editor.ReplaceEntities(
            new Entity[] { lines[0], lines[1] },
            new Entity[] { result.First, result.Second, result.Connector },
            "Chamfer");

        _selection.Clear();
        _statusOverride = "CHAMFER: đã vát góc.";
        RaiseSelection();
        Cancel();
    }

    private void CreateDimension(Pt first, Pt second, Pt location)
    {
        if (_editor is null) return;

        var dim = new DimensionAligned
        {
            FirstPoint = new CSMath.XYZ(first.X, first.Y, 0),
            SecondPoint = new CSMath.XYZ(second.X, second.Y, 0),
            DefinitionPoint = new CSMath.XYZ(location.X, location.Y, 0),
        };

        ApplyCurrentLayer(dim);

        try
        {
            var style = ResolveDimensionStyle();
            if (style is not null) dim.Style = style;
        }
        catch { }

        AddDrawn(dim, "DIM");

        // Ghi nhớ để DIMCONTINUE nối tiếp
        _lastDimension = dim;
        _lastDimFirst = first;
        _lastDimSecond = second;
        _lastDimLocation = location;

        _statusOverride = $"DIM: {Pt.Distance(first, second):0.###}";
        RaisePrompt();
        Invalidate();
    }

    /// <summary>Style kích thước đang dùng; chưa chọn thì lấy style đầu tiên của bản vẽ.</summary>
    private ACadSharp.Tables.DimensionStyle? ResolveDimensionStyle()
    {
        if (CurrentDimensionStyle is not null) return CurrentDimensionStyle;

        try { return _editor?.Workspace.Document.DimensionStyles.FirstOrDefault(); }
        catch { return null; }
    }

    /// <summary>Giao của hai đường thẳng coi như dài vô hạn (đỉnh góc có thể nằm ngoài đoạn).</summary>
    private static Pt? InfiniteLineIntersection(Line a, Line b)
    {
        var p1 = new Pt(a.StartPoint.X, a.StartPoint.Y);
        var p2 = new Pt(a.EndPoint.X, a.EndPoint.Y);
        var p3 = new Pt(b.StartPoint.X, b.StartPoint.Y);
        var p4 = new Pt(b.EndPoint.X, b.EndPoint.Y);

        var d1 = p2 - p1;
        var d2 = p4 - p3;

        var denom = d1.X * d2.Y - d1.Y * d2.X;
        if (Math.Abs(denom) < 1e-12) return null;

        var t = ((p3.X - p1.X) * d2.Y - (p3.Y - p1.Y) * d2.X) / denom;
        return p1 + d1 * t;
    }

    /// <summary>Tạo kích thước góc giữa hai đường thẳng đã click.</summary>
    private void CreateAngularDimension(Pt pick1, Pt pick2, Pt arcPoint)
    {
        if (_editor is null) return;

        var tolerance = _view.PixelsToWorld(10);

        var first = _editor.Scene.HitTest(pick1, tolerance)?.Entity as Line;
        var second = _editor.Scene.HitTest(pick2, tolerance)?.Entity as Line;

        if (first is null || second is null)
        {
            _statusOverride = "DIMANG: phải click vào hai đường thẳng.";
            RaisePrompt();
            return;
        }

        var vertex = InfiniteLineIntersection(first, second);
        if (vertex is null)
        {
            _statusOverride = "DIMANG: hai đường thẳng song song, không có góc.";
            RaisePrompt();
            return;
        }

        var dim = new DimensionAngular2Line
        {
            AngleVertex = new CSMath.XYZ(vertex.Value.X, vertex.Value.Y, 0),
            FirstPoint = new CSMath.XYZ(pick1.X, pick1.Y, 0),
            SecondPoint = new CSMath.XYZ(pick2.X, pick2.Y, 0),
            DimensionArc = new CSMath.XYZ(arcPoint.X, arcPoint.Y, 0),
        };

        ApplyCurrentLayer(dim);

        try
        {
            var style = ResolveDimensionStyle();
            if (style is not null) dim.Style = style;
        }
        catch { }

        AddDrawn(dim, "DIMANGULAR");
        _statusOverride = "DIMANG: đã tạo kích thước góc";
        Message?.Invoke(_statusOverride);
        RaisePrompt();
        Invalidate();
    }

    /// <summary>Tạo kích thước toạ độ (X hoặc Y) cho một điểm.</summary>
    private void CreateOrdinateDimension(Pt feature, Pt leaderEnd)
    {
        if (_editor is null) return;

        var dx = Math.Abs(leaderEnd.X - feature.X);
        var dy = Math.Abs(leaderEnd.Y - feature.Y);

        // Đường dẫn thẳng đứng → đo X; nằm ngang → đo Y (giống AutoCAD)
        var measuresX = dy >= dx;

        var dim = new DimensionOrdinate
        {
            FeatureLocation = new CSMath.XYZ(feature.X, feature.Y, 0),
            LeaderEndpoint = new CSMath.XYZ(leaderEnd.X, leaderEnd.Y, 0),
            IsOrdinateTypeX = measuresX,
        };

        ApplyCurrentLayer(dim);

        try
        {
            var style = ResolveDimensionStyle();
            if (style is not null) dim.Style = style;
        }
        catch { }

        AddDrawn(dim, "DIMORDINATE");

        var value = measuresX ? feature.X : feature.Y;
        _statusOverride = $"DIMORD: {(measuresX ? "X" : "Y")} = {value:0.###}";
        Message?.Invoke(_statusOverride);
        RaisePrompt();
        Invalidate();
    }

    /// <summary>Tạo kích thước bán kính / đường kính từ một đường tròn hoặc cung.</summary>
    private void CreateRadialDimension(bool diameter, Pt pickOnCircle, Pt textPoint)
    {
        if (_editor is null) return;

        var hit = _editor.Scene.HitTest(pickOnCircle, _view.PixelsToWorld(10));
        var target = hit?.Entity;

        Pt center;
        double radius;

        switch (target)
        {
            case Arc arc:
                center = new Pt(arc.Center.X, arc.Center.Y);
                radius = arc.Radius;
                break;

            case Circle circle:
                center = new Pt(circle.Center.X, circle.Center.Y);
                radius = circle.Radius;
                break;

            default:
                _statusOverride = "Cần click lên đường tròn hoặc cung.";
                RaisePrompt();
                return;
        }

        if (radius <= 1e-9) return;

        var direction = (textPoint - center).Normalized();
        if (direction.LengthSquared < 1e-12) return;

        var edge = center + direction * radius;

        Dimension dim;

        if (diameter)
        {
            var opposite = center - direction * radius;

            dim = new DimensionDiameter
            {
                AngleVertex = new CSMath.XYZ(opposite.X, opposite.Y, 0),
                DefinitionPoint = new CSMath.XYZ(edge.X, edge.Y, 0),
            };
        }
        else
        {
            dim = new DimensionRadius
            {
                AngleVertex = new CSMath.XYZ(center.X, center.Y, 0),
                DefinitionPoint = new CSMath.XYZ(edge.X, edge.Y, 0),
            };
        }

        ApplyCurrentLayer(dim);

        try
        {
            var style = ResolveDimensionStyle();
            if (style is not null) dim.Style = style;
        }
        catch { }

        AddDrawn(dim, diameter ? "DIMDIAMETER" : "DIMRADIUS");
        _statusOverride = diameter
            ? $"DIMDIA: \u00D8{radius * 2:0.###}"
            : $"DIMRAD: R{radius:0.###}";
        RaisePrompt();
        Invalidate();
    }

    private void CreateEllipse(Pt center, Pt majorEnd, Pt minorPoint)
    {
        if (_editor is null) return;

        var majorLength = Pt.Distance(center, majorEnd);
        if (majorLength < 1e-9) return;

        // Bán trục nhỏ = khoảng cách vuông góc từ điểm thứ ba tới trục lớn
        var axis = (majorEnd - center).Normalized();
        var offset = minorPoint - center;
        var perpendicular = Math.Abs(Pt.Cross(axis, offset));

        var ellipse = ShapeFactory.CreateEllipse(center, majorEnd, perpendicular);
        if (ellipse is null) return;

        ApplyCurrentLayer(ellipse);
        AddDrawn(ellipse, "ELLIPSE");
        _statusOverride = $"ELLIPSE: {majorLength * 2:0.##} × {perpendicular * 2:0.##}";
        RaisePrompt();
        Invalidate();
    }

    private void CreatePolygon(Pt center, Pt vertex)
    {
        if (_editor is null) return;

        var polygon = ShapeFactory.CreatePolygon(center, vertex, _polygonSides);
        if (polygon is null) return;

        ApplyCurrentLayer(polygon);
        AddDrawn(polygon, "POLYGON");
        _statusOverride = $"POLYGON: {_polygonSides} cạnh, R{Pt.Distance(center, vertex):0.##}";
        RaisePrompt();
        Invalidate();
    }

    private void ReportDistance(Pt a, Pt b)
    {
        var result = Measure.Distance(a, b);

        var text =
            $"DIST: {result.Distance:0.###}  |  góc {result.AngleDegrees:0.##}°  |  " +
            $"ΔX {result.DeltaX:0.###}  ΔY {result.DeltaY:0.###}";

        _statusOverride = text;
        Message?.Invoke(text);
        RaisePrompt();
    }

    private void ReportArea(Pt pick)
    {
        if (_editor is null) return;

        var hit = _editor.Scene.HitTest(pick, _view.PixelsToWorld(10));
        if (hit is null)
        {
            _statusOverride = "AREA: không tìm thấy đối tượng.";
            Message?.Invoke(_statusOverride);
            RaisePrompt();
            return;
        }

        var area = Measure.Area(hit.Entity);
        var text = area > 1e-9
            ? $"AREA: {area:0.##}  ({hit.Entity.GetType().Name})"
            : "AREA: đối tượng không khép kín.";

        _statusOverride = text;
        Message?.Invoke(text);
        RaisePrompt();
    }

    /// <summary>Đặt một tham chiếu block tại điểm click. Giữ chế độ để đặt tiếp nhiều bản.</summary>
    private void PlaceBlock(Pt point)
    {
        if (_editor is null) return;

        if (_pendingBlock is null)
        {
            _input = InputRequest.BlockName;
            RaisePrompt();
            return;
        }

        var insert = BlockLibrary.Create(_pendingBlock, point);
        if (insert is null) return;

        ApplyCurrentLayer(insert);
        _editor.AddEntities(new Entity[] { insert }, "Insert");

        _statusOverride = $"INSERT '{_pendingBlock.Name}' @ {point}";
        Message?.Invoke(_statusOverride);
        RaisePrompt();
        Invalidate();
    }

    /// <summary>Tạo định nghĩa block từ lựa chọn hiện tại, gốc đặt tại điểm click.</summary>
    private void ApplyCreateBlock(Pt basePoint)
    {
        if (_editor is null) return;

        if (_pendingBlockName is null)
        {
            _input = InputRequest.NewBlockName;
            RaisePrompt();
            return;
        }

        var selected = _selection.ToList();
        if (selected.Count == 0)
        {
            _statusOverride = "BLOCK: không còn đối tượng nào được chọn.";
            RaisePrompt();
            Cancel();
            return;
        }

        var ok = _editor.CreateBlock(selected, _pendingBlockName, basePoint);

        _statusOverride = ok
            ? $"BLOCK '{_pendingBlockName}': đã tạo từ {selected.Count} đối tượng."
            : "BLOCK: không tạo được (tên trùng hoặc đối tượng không hợp lệ).";

        if (ok) Message?.Invoke(_statusOverride);

        _selection.Clear();
        RaiseSelection();
        Cancel();
    }

    /// <summary>Đặt điểm chia đều (DIVIDE) hoặc theo khoảng cách (MEASURE).</summary>
    private void ApplyDivideMeasure(int count, double spacing)
    {
        if (_editor is null || _selection.Count != 1) { Cancel(); return; }

        var target = _selection.First();

        var points = count > 0
            ? MeasureOperation.Divide(target, count)
            : MeasureOperation.Measure(target, spacing);

        if (points.Count == 0)
        {
            _statusOverride = count > 0
                ? "DIVIDE: không chia được đối tượng này."
                : "MEASURE: khoảng cách lớn hơn chiều dài đối tượng.";

            RaisePrompt();
            Cancel();
            return;
        }

        _editor.PlaceMarkers(target, points, count > 0 ? "Divide" : "Measure");

        _statusOverride = count > 0
            ? $"DIVIDE: {count} đoạn, {points.Count} điểm"
            : $"MEASURE: {points.Count} điểm cách {spacing:0.###}";

        Message?.Invoke(_statusOverride);
        Cancel();
    }

    /// <summary>Bắt đầu đổi tỷ lệ viewport trên tờ hiện tại.</summary>
    public void BeginViewportScale()
    {
        if (_editor is null) return;

        if (Sheet is null || _paperBlock is null)
        {
            _statusOverride = "VPSCALE: chỉ dùng được khi đang ở tab layout.";
            Message?.Invoke(_statusOverride);
            RaisePrompt();
            return;
        }

        _input = InputRequest.ViewportScaleNew;
        RaisePrompt();
    }

    /// <summary>Đặt tỷ lệ cho mọi viewport mô hình trên tờ.</summary>
    private void ApplyViewportScale(double unitsPerMm)
    {
        if (_editor is null || _paperBlock is null) return;

        var viewports = _paperBlock.Entities
            .OfType<Viewport>()
            .Where(v => v.ViewHeight > 1e-9)
            .ToList();

        if (viewports.Count == 0)
        {
            _statusOverride = "VPSCALE: tờ này chưa có viewport nào.";
            Message?.Invoke(_statusOverride);
            return;
        }

        foreach (var viewport in viewports)
            _editor.SetViewportScale(viewport, unitsPerMm);

        _statusOverride = $"VPSCALE: đã đặt {viewports.Count} viewport về 1:{unitsPerMm:0.##}";
        Message?.Invoke(_statusOverride);

        RefreshPaper();
    }

    /// <summary>Tạo bảng rows×cols bằng entity TABLE thật, góc trên-trái tại <paramref name="origin"/>.</summary>
    private ACadSharp.Entities.TableEntity MakeTable(Pt origin, int rows, int cols)
    {
        const double rowHeight = 300;
        const double columnWidth = 1200;

        // Bảng cần BlockRecord riêng: thiếu nó, ACadSharp ném lỗi khi xoá/undo
        var block = new ACadSharp.Tables.BlockRecord("TABLE_" + Guid.NewGuid().ToString("N")[..8]);

        var document = _editor?.Workspace.Document;

        if (document is not null)
        {
            try { document.BlockRecords.Add(block); }
            catch { }
        }

        var table = new ACadSharp.Entities.TableEntity(block)
        {
            InsertPoint = new CSMath.XYZ(origin.X, origin.Y, 0),
            RowCount = (ushort)rows,
            ColumnCount = (ushort)cols,
            RowSpacing = rowHeight,
            ColumnSpacing = columnWidth,
        };

        for (var r = 0; r < rows; r++)
            table.Rows.Add(new ACadSharp.Entities.TableEntity.Row { Height = rowHeight });

        for (var c = 0; c < cols; c++)
            table.Columns.Add(new ACadSharp.Entities.TableEntity.Column { Width = columnWidth });

        return table;
    }
    // ------------------------------------------------------------- MLEADER

    /// <summary>Tạo chú thích MULTILEADER: mũi tên tại điểm đầu, chữ tại điểm thứ hai.</summary>
    private void CreateMultiLeader(Pt arrow, Pt text, string label)
    {
        if (_editor is null || string.IsNullOrWhiteSpace(label)) return;

        var multiLeader = new MultiLeader();

        var context = multiLeader.ContextData;

        if (context is null)
        {
            _statusOverride = "MLEADER: không tạo được dữ liệu chú thích.";
            Message?.Invoke(_statusOverride);
            return;
        }

        context.TextLabel = label;
        context.TextHeight = Math.Max(150, _editor.Scene.Bounds.Height / 40);
        context.TextLocation = new CSMath.XYZ(text.X, text.Y, 0);

        var leaderLine = new ACadSharp.Objects.MultiLeaderObjectContextData.LeaderLine();

        leaderLine.Points.Add(new CSMath.XYZ(arrow.X, arrow.Y, 0));
        leaderLine.Points.Add(new CSMath.XYZ(
            (arrow.X + text.X) / 2, (arrow.Y + text.Y) / 2, 0));
        leaderLine.Points.Add(new CSMath.XYZ(text.X, text.Y, 0));

        var root = new ACadSharp.Objects.MultiLeaderObjectContextData.LeaderRoot();
        root.Lines.Add(leaderLine);

        context.LeaderRoots.Add(root);

        ApplyCurrentLayer(multiLeader);
        AddDrawn(multiLeader, "MLEADER");

        _statusOverride = "MLEADER: " + label;
        Message?.Invoke(_statusOverride + "  (lưu ý: DWG làm mất chữ của loại này — app sẽ chuyển thành Leader + chữ khi lưu)");
    }

    /// <summary>Bắt đầu tạo bảng: hỏi số hàng, cột trước.</summary>
    public void BeginTable()
    {
        if (_editor is null) return;

        _input = InputRequest.TableSize;
        _tableRows = 0;
        _tableColumns = 0;
        _mode = EditMode.TableDraw;

        _statusOverride = "TABLE: nhập số hàng,cột (ví dụ 3,2) rồi Enter:";
        RaisePrompt();
    }

    /// <summary>Bắt đầu sửa chữ kích thước (DIMTEDIT).</summary>
    public void BeginDimensionTextEdit()
    {
        if (_editor is null) return;

        if (!_selection.OfType<Dimension>().Any())
        {
            _statusOverride = "DIMTEDIT: cần chọn ít nhất một kích thước.";
            Message?.Invoke(_statusOverride);
            RaisePrompt();
            return;
        }

        _input = InputRequest.DimensionText;
        RaisePrompt();
    }

    /// <summary>Bắt đầu sửa hatch cho lựa chọn hiện tại.</summary>
    public void BeginHatchEdit()
    {
        if (_editor is null) return;

        if (!_selection.OfType<Hatch>().Any())
        {
            _statusOverride = "HATCHEDIT: cần chọn ít nhất một hatch.";
            Message?.Invoke(_statusOverride);
            RaisePrompt();
            return;
        }

        _input = InputRequest.HatchProperty;
        RaisePrompt();
    }

    /// <summary>Áp thuộc tính hatch từ chuỗi dạng "scale=2, angle=45, pattern=ANSI31".</summary>
    private void ApplyHatchEdit(string text)
    {
        if (_editor is null || _selection.Count == 0) return;

        double? scale = null;
        double? angle = null;
        string? pattern = null;

        foreach (var token in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = token.IndexOf('=');

            if (eq <= 0) continue;

            var key = token[..eq].Trim().ToLowerInvariant();
            var value = token[(eq + 1)..].Trim();

            switch (key)
            {
                case "scale" or "s" or "tỷ lệ" or "tyle":
                    if (double.TryParse(value, out var parsedScale) && parsedScale > 1e-6)
                        scale = parsedScale;
                    break;

                case "angle" or "a" or "góc" or "goc":
                    if (double.TryParse(value, out var parsedAngle))
                        angle = parsedAngle * Math.PI / 180.0;
                    break;

                case "pattern" or "p" or "mẫu" or "mau":
                    pattern = value;
                    break;
            }
        }

        if (scale is null && angle is null && pattern is null)
        {
            _statusOverride = "HATCHEDIT: cần ít nhất một trong scale=, angle=, pattern=.";
            Message?.Invoke(_statusOverride);
            RaisePrompt();
            return;
        }

        var changed = _editor.SetHatchProperties(_selection.ToList(), scale, angle, pattern);

        _statusOverride = changed > 0
            ? $"HATCHEDIT: đã đổi {changed} hatch"
              + (scale is null ? "" : $", scale={scale:0.###}")
              + (angle is null ? "" : $", angle={angle * 180 / Math.PI:0.#}°")
              + (pattern is null ? "" : $", pattern={pattern}")
            : "HATCHEDIT: không có hatch nào trong lựa chọn.";

        Message?.Invoke(_statusOverride);
        _mode = EditMode.Select;
        RaisePrompt();
        Invalidate();
    }

    /// <summary>Chọn block có attribute để sửa.</summary>
    private void PickAttributeTarget(Pt point, bool define = false)
    {
        if (_editor is null) return;

        var tolerance = _view.PixelsToWorld(10);
        var hit = _editor.Scene.HitTest(point, tolerance);

        if (hit?.Entity is not ACadSharp.Entities.Insert insert
            || (!define && !insert.HasAttributes))
        {
            _statusOverride = define
                ? "ATTDEF: không có block ở điểm này."
                : "ATTEDIT: không có block kèm attribute ở điểm này.";

            RaisePrompt();
            return;
        }

        _attributeTarget = insert;

        if (define)
        {
            // Đổi điểm click sang toạ độ block (bỏ qua xoay — đủ dùng cho block thường)
            var scale = Math.Abs(insert.XScale) < 1e-9 ? 1 : insert.XScale;

            _attributeDefinePoint = new Pt(
                (point.X - insert.InsertPoint.X) / scale,
                (point.Y - insert.InsertPoint.Y) / scale);

            _input = InputRequest.AttributeNew;
        }
        else
        {
            _input = InputRequest.AttributeValue;
        }

        _statusOverride = null;
        _mode = EditMode.Select;
        RaisePrompt();
    }

    private static string DescribeAttributes(ACadSharp.Entities.Insert insert)
    {
        try
        {
            return string.Join(", ", insert.Attributes.Select(a => a.Tag + "=" + a.Value));
        }
        catch { return string.Empty; }
    }

    /// <summary>Dãy tròn quanh tâm vừa click, chia đều 360°.</summary>
    private void ApplyPolarArray(Pt center)
    {
        if (_editor is null || _selection.Count == 0) return;

        var targets = _selection.ToList();
        var count = _arrayCount;

        _editor.ArrayPolar(targets, count, center, 360, e => e.Clone() as Entity, "Polar array");

        _statusOverride = $"ARRAYPOLAR: {count} bản quanh ({center.X:0.##}, {center.Y:0.##})";
        Message?.Invoke(_statusOverride);
        Invalidate();
    }

    /// <summary>STRETCH: dịch những đỉnh nằm trong khung chọn.</summary>
    private void ApplyStretch(Pt corner1, Pt corner2, Pt basePoint, Pt target)
    {
        if (_editor is null) return;

        var window = new RectD(Math.Min(corner1.X, corner2.X), Math.Min(corner1.Y, corner2.Y),
                               Math.Max(corner1.X, corner2.X), Math.Max(corner1.Y, corner2.Y));

        var delta = new Pt(target.X - basePoint.X, target.Y - basePoint.Y);

        if (Math.Abs(delta.X) < 1e-9 && Math.Abs(delta.Y) < 1e-9) return;

        var affected = _editor.Workspace.Document.Entities
            .Where(e => StretchOperation.Apply(e, window, new Pt(0, 0)))
            .ToList();

        // Tìm entity có ít nhất một đỉnh nằm trong khung
        affected = _editor.Workspace.Document.Entities
            .Where(e => TouchesWindow(e, window))
            .ToList();

        if (affected.Count == 0)
        {
            _statusOverride = "STRETCH: không có đỉnh nào trong khung.";
            RaisePrompt();
            return;
        }

        _editor.RunCustom("Stretch",
            () => { foreach (var e in affected) StretchOperation.Apply(e, window, delta); },
            () => { foreach (var e in affected) StretchOperation.Apply(e, window, new Pt(-delta.X, -delta.Y)); });

        _statusOverride = $"STRETCH: {affected.Count} đối tượng, dời ({delta.X:0.##}, {delta.Y:0.##})";
        Message?.Invoke(_statusOverride);
        Invalidate();
    }

    private static bool TouchesWindow(Entity entity, RectD window)
    {
        var prims = new List<OpenCad.Core.Rendering.Prim>(4);

        try
        {
            OpenCad.Core.Rendering.EntityFlattener.Flatten(entity, prims, Affine2.Identity, OpenCad.Core.Colors.Rgb.White, 0);
        }
        catch { return false; }

        foreach (var prim in prims)
            foreach (var p in prim.Points)
                if (p.X >= window.MinX && p.X <= window.MaxX && p.Y >= window.MinY && p.Y <= window.MaxY)
                    return true;

        return false;
    }

    /// <summary>Thực hiện dãy chữ nhật trên lựa chọn hiện tại.</summary>
    private void ApplyArray(double dy, double dx)
    {
        if (_editor is null || _selection.Count == 0) { Cancel(); return; }

        var targets = _selection.ToList();
        var rows = _arrayRows;
        var cols = _arrayCols;

        _editor.ArrayCopy(targets, rows, cols, dx, dy, e => e.Clone() as Entity, "Array");

        _statusOverride = $"ARRAY: {rows}×{cols}, bước ({dy:0.##}, {dx:0.##})";
        Message?.Invoke(_statusOverride);

        Cancel();
    }

    /// <summary>Vẽ một đường thẳng trên tờ (toạ độ mm giấy).</summary>
    private void CreatePaperLine(Pt a, Pt b)
    {
        if (_editor is null || _paperBlock is null) return;

        if (Pt.Distance(a, b) < 1e-9) return;

        var line = new Line
        {
            StartPoint = new CSMath.XYZ(a.X, a.Y, 0),
            EndPoint = new CSMath.XYZ(b.X, b.Y, 0),
        };

        var block = _paperBlock;

        _editor.RunCustom("Line (paper)",
            () => block.Entities.Add(line),
            () => block.Entities.Remove(line));

        _statusOverride = $"PLINE: ({a.X:0.#}, {a.Y:0.#}) → ({b.X:0.#}, {b.Y:0.#}) mm";
        Message?.Invoke(_statusOverride);

        RefreshPaper();
    }

    /// <summary>Tạo viewport mới trên tờ từ hai góc click (toạ độ mm giấy).</summary>
    private void CreatePaperViewport(Pt a, Pt b)
    {
        if (_editor is null || Sheet is null) return;

        var rect = new RectD(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                             Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

        if (rect.Width < 5 || rect.Height < 5)
        {
            _statusOverride = "VPORT: khung quá nhỏ.";
            RaisePrompt();
            return;
        }

        var layout = OpenCad.Core.Layout.LayoutFactory.Find(
            _editor.Workspace.Document, Sheet.Name);

        if (layout is null)
        {
            _statusOverride = "VPORT: không tìm thấy layout.";
            RaisePrompt();
            return;
        }

        var scene = _editor.Scene;
        var scale = _vportScale;
        ACadSharp.Entities.Viewport? created = null;

        _editor.RunCustom("Viewport",
            () => created = OpenCad.Core.Layout.LayoutFactory.AddViewport(layout, scene, rect, scale),
            () =>
            {
                if (created is not null) layout.AssociatedBlock?.Entities.Remove(created);
            });

        _statusOverride = created is null
            ? "VPORT: không tạo được viewport."
            : $"VPORT 1:{scale:0.##}: {rect.Width:0}×{rect.Height:0} mm";

        if (created is not null) Message?.Invoke(_statusOverride);

        RefreshPaper();
        RaisePrompt();
    }

    /// <summary>Chọn chữ để sửa (lệnh ED), sau đó nhập nội dung mới ở dòng lệnh.</summary>
    private void PickTextForEdit(Pt point)
    {
        if (_editor is null) return;

        // Trên tờ in: tìm chữ trong paper space
        if (Sheet is not null && _paperScene is not null)
        {
            var paperHit = _paperScene.HitTest(_cursorRaw, _view.PixelsToWorld(20));

            if (paperHit?.Entity is TextEntity paperText)
            {
                _editingText = paperText;
                _editingPaperText = true;
                _mode = EditMode.Select;
                _input = InputRequest.EditText;
                RaisePrompt();
                return;
            }

            _statusOverride = "ED: không có chữ trên tờ ở điểm này.";
            RaisePrompt();
            return;
        }

        // Dùng điểm thô: OSNAP có thể kéo con trỏ ra khỏi chữ
        var pick = _cursorRaw;
        var tolerance = _view.PixelsToWorld(20);

        var hit = _editor.Scene.HitTest(pick, tolerance);

        TextEntity? text = hit?.Entity as TextEntity;

        if (text is null)
            hit = _editor.Scene.HitTest(point, tolerance);

        text = hit?.Entity as TextEntity;

        // Vẫn không trúng: lấy chữ gần nhất trong bán kính rộng cho dễ dùng
        if (text is null)
        {
            var best = _editor.Scene.Entries
                .Where(e => e.Entity is TextEntity)
                .Select(e => (Entry: e, Distance: Pt.Distance(e.Bounds.Center, pick)))
                .Where(x => x.Distance <= _view.PixelsToWorld(45))
                .OrderBy(x => x.Distance)
                .FirstOrDefault();

            text = best.Entry?.Entity as TextEntity;
        }

        if (text is null)
        {
            _statusOverride = "ED: không có chữ ở điểm này.";
            RaisePrompt();
            return;
        }

        _editingText = text;
        _mode = EditMode.Select;
        _input = InputRequest.EditText;
        RaisePrompt();
    }

    private void CommitText(string value)
    {
        if (_editor is null || _points.Count == 0) { Cancel(); return; }

        var height = Math.Max(150, _editor.Scene.Bounds.Height / 40);
        var text = new TextEntity
        {
            InsertPoint = new CSMath.XYZ(_points[0].X, _points[0].Y, 0),
            Height = height,
            Value = value,
        };
        ApplyCurrentLayer(text);

        _editor.AddEntities(new[] { text }, "Text");
        Cancel();
    }

    private void CommitRotate(double angleRad)
    {
        if (_editor is null || _points.Count == 0) { Cancel(); return; }

        var m = Affine2.Rotate(angleRad, _points[0]);
        _editor.TransformEntities(_selection.ToList(), m, "Rotate");
        Cancel();
    }

    private void CommitScale(double factor)
    {
        if (_editor is null || _points.Count == 0) { Cancel(); return; }

        var m = Affine2.Scale(factor, _points[0]);
        _editor.TransformEntities(_selection.ToList(), m, "Scale");
        Cancel();
    }

    /// <summary>Kết thúc lệnh SPLINE: tạo spline qua các điểm đã click.</summary>
    /// <summary>Kết thúc lệnh MLINE: tạo tường nhiều nét qua các điểm đã click.</summary>
    private void CommitMLine()
    {
        if (_editor is null) { Cancel(); return; }

        if (_points.Count >= 2)
        {
            var mline = new MLine();

            foreach (var point in _points)
                mline.Vertices.Add(new MLine.Vertex
                {
                    Position = new CSMath.XYZ(point.X, point.Y, 0),
                });

            ApplyCurrentLayer(mline);
            AddDrawn(mline, "MLINE");
        }

        Cancel();
    }

    private void CommitSpline()
    {
        if (_editor is null) { Cancel(); return; }

        if (_points.Count >= 2)
        {
            var spline = new Spline { IsClosed = false };

            foreach (var point in _points)
                spline.ControlPoints.Add(new CSMath.XYZ(point.X, point.Y, 0));

            // Degree 3 cho mượt; spline 2 điểm thì hạ bậc
            spline.Degree = Math.Min(3, Math.Max(1, _points.Count - 1));

            ApplyCurrentLayer(spline);
            AddDrawn(spline, "SPLINE");
        }

        Cancel();
    }

    private void CommitPolyline()
    {
        if (_editor is null) { Cancel(); return; }

        if (_points.Count >= 2)
        {
            var pl = new LwPolyline { IsClosed = false };
            foreach (var p in _points)
                pl.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(p.X, p.Y) });

            ApplyCurrentLayer(pl);
            _editor.AddEntities(new[] { pl }, "Polyline");
        }

        Cancel();
    }

    private void ApplyCurrentLayer(Entity entity)
    {
        var layer = CurrentLayer ?? SafeLayer("0");
        if (layer is not null) entity.Layer = layer;
    }

    private Layer? SafeLayer(string name)
    {
        try { return _editor?.Workspace.FindLayer(name); }
        catch { return null; }
    }

    private Line MakeLine(Pt a, Pt b)
    {
        var line = new Line
        {
            StartPoint = new CSMath.XYZ(a.X, a.Y, 0),
            EndPoint = new CSMath.XYZ(b.X, b.Y, 0),
        };
        ApplyCurrentLayer(line);
        return line;
    }

    private Circle MakeCircle(Pt center, double radius)
    {
        var circle = new Circle
        {
            Center = new CSMath.XYZ(center.X, center.Y, 0),
            Radius = radius,
        };
        ApplyCurrentLayer(circle);
        return circle;
    }

    private LwPolyline MakeRectangle(Pt a, Pt b)
    {
        var pl = new LwPolyline { IsClosed = true };
        pl.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(a.X, a.Y) });
        pl.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(b.X, a.Y) });
        pl.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(b.X, b.Y) });
        pl.Vertices.Add(new LwPolyline.Vertex { Location = new CSMath.XY(a.X, b.Y) });
        ApplyCurrentLayer(pl);
        return pl;
    }

    /// <summary>Cung tròn đi qua 3 điểm. Trả về null nếu 3 điểm thẳng hàng.</summary>
    private Arc? MakeArc(Pt p1, Pt p2, Pt p3)
    {
        var d = 2 * (p1.X * (p2.Y - p3.Y) + p2.X * (p3.Y - p1.Y) + p3.X * (p1.Y - p2.Y));
        if (Math.Abs(d) < 1e-12) return null;

        var s1 = p1.LengthSquared;
        var s2 = p2.LengthSquared;
        var s3 = p3.LengthSquared;

        var cx = (s1 * (p2.Y - p3.Y) + s2 * (p3.Y - p1.Y) + s3 * (p1.Y - p2.Y)) / d;
        var cy = (s1 * (p3.X - p2.X) + s2 * (p1.X - p3.X) + s3 * (p2.X - p1.X)) / d;

        var center = new Pt(cx, cy);
        var radius = Pt.Distance(center, p1);

        double Norm(double a) => a < 0 ? a + 2 * Math.PI : a;

        var a1 = Math.Atan2(p1.Y - cy, p1.X - cx);
        var a2 = Math.Atan2(p2.Y - cy, p2.X - cx);
        var a3 = Math.Atan2(p3.Y - cy, p3.X - cx);

        var sweep13 = Norm(a3 - a1);
        var mid13 = Norm(a2 - a1);

        double start, end;
        if (mid13 <= sweep13) { start = a1; end = a3; }
        else { start = a3; end = a1; }

        var arc = new Arc
        {
            Center = new CSMath.XYZ(cx, cy, 0),
            Radius = radius,
            StartAngle = start,
            EndAngle = end,
        };
        ApplyCurrentLayer(arc);
        return arc;
    }

    private static Entity? CloneEntity(Entity entity)
    {
        try { return entity.Clone() as Entity; }
        catch { return null; }
    }

    // -------------------------------------------------------------- KEYBOARD

    protected override bool IsInputKey(Keys keyData) => keyData switch
    {
        Keys.Delete or Keys.Escape or Keys.Enter => true,
        _ => base.IsInputKey(keyData),
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.KeyCode)
        {
            case Keys.Escape:
                Cancel();
                e.Handled = true;
                break;

            case Keys.Delete:
                DeleteSelection();
                e.Handled = true;
                break;

            case Keys.Enter:
                ProvideInput(string.Empty);
                e.Handled = true;
                break;
        }
    }

    // ----------------------------------------------------------------- UTIL

    private void RaiseSelection()
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        RaisePrompt();
    }

    private void RaisePrompt()
    {
        PromptChanged?.Invoke(Prompt);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderer.Dispose();
            if (_editor is not null)
            {
                _editor.Changed -= OnEditorChanged;
                _editor.StateChanged -= OnStateChanged;
            }
        }
        base.Dispose(disposing);
    }
}

namespace OpenCad.App;

/// <summary>
/// @brief Static dictionaries providing translation mappings for OpenCad i18n support.
/// </summary>
public static partial class I18n
{
    private static readonly Dictionary<string, string> EnglishStrings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Application & QAT
        ["App.Title"] = "OpenCAD",
        ["App.Workspace"] = "2D Drafting & Annotation",
        ["App.Language"] = "Language",
        ["App.LangEn"] = "English",
        ["App.LangVi"] = "Tiếng Việt",
        ["App.Menu"] = "Application Menu",

        // Ribbon Tabs
        ["Ribbon.Tab.Home"] = "Home",
        ["Ribbon.Tab.Insert"] = "Insert",
        ["Ribbon.Tab.Annotate"] = "Annotate",
        ["Ribbon.Tab.View"] = "View",
        ["Ribbon.Tab.Output"] = "Output",

        // Ribbon Groups
        ["Ribbon.Group.Draw"] = "Draw",
        ["Ribbon.Group.Modify"] = "Modify",
        ["Ribbon.Group.Layers"] = "Layers",
        ["Ribbon.Group.Block"] = "Block",
        ["Ribbon.Group.Hatch"] = "Hatch",
        ["Ribbon.Group.Text"] = "Text",
        ["Ribbon.Group.Dimensions"] = "Dimensions",
        ["Ribbon.Group.Inquiry"] = "Inquiry",
        ["Ribbon.Group.Navigate"] = "Navigate",
        ["Ribbon.Group.Drafting"] = "Drafting",
        ["Ribbon.Group.Manage"] = "Manage",
        ["Ribbon.Group.Publish"] = "Publish",
        ["Ribbon.Group.Layout"] = "Layout",
        ["Ribbon.Group.File"] = "File",

        // Menu Bar
        ["Menu.File"] = "&File",
        ["Menu.Edit"] = "&Edit",
        ["Menu.Draw"] = "&Draw",
        ["Menu.Modify"] = "&Modify",
        ["Menu.View"] = "&View",
        ["Menu.Layout"] = "&Layout",
        ["Menu.Tools"] = "&Tools",
        ["Menu.Help"] = "&Help",

        ["Menu.New"] = "&New",
        ["Menu.Open"] = "&Open…",
        ["Menu.Recent"] = "Open &Recent",
        ["Menu.Save"] = "&Save",
        ["Menu.SaveAs"] = "Save &As…",
        ["Menu.ExportPdf"] = "&Export PDF",
        ["Menu.Exit"] = "E&xit",

        ["Menu.Undo"] = "&Undo",
        ["Menu.Redo"] = "&Redo",
        ["Menu.Erase"] = "&Erase",
        ["Menu.SelectAll"] = "Select &All",

        ["Menu.ZoomExtents"] = "Zoom &Extents",
        ["Menu.ZoomIn"] = "Zoom &In",
        ["Menu.ZoomOut"] = "Zoom &Out",
        ["Menu.ToggleOrtho"] = "Toggle &Ortho (F8)",
        ["Menu.TogglePolar"] = "Toggle &Polar (F10)",
        ["Menu.ToggleOsnap"] = "Toggle &Osnap (F3)",
        ["Menu.ToggleGrid"] = "Toggle &Grid (F7)",
        ["Menu.ToggleLwt"] = "Toggle &Lineweight (LWT)",
        ["Menu.CommandHistory"] = "&Command History (F2)",

        // Status Bar
        ["Status.Ready"] = "Ready",
        ["Status.Model"] = "MODEL",
        ["Status.Paper"] = "PAPER",
        ["Status.Grid"] = "GRID",
        ["Status.Ortho"] = "ORTHO",
        ["Status.Polar"] = "POLAR",
        ["Status.Osnap"] = "OSNAP",
        ["Status.Lwt"] = "LWT",

        ["Status.Tip.Model"] = "Switch Model / Paper Space",
        ["Status.Tip.Grid"] = "Grid Display (F7)",
        ["Status.Tip.Ortho"] = "Ortho Mode (F8)",
        ["Status.Tip.Polar"] = "Polar Tracking 15° (F10)",
        ["Status.Tip.Osnap"] = "2D Object Snap (F3)",
        ["Status.Tip.Lwt"] = "Show/Hide Lineweight",

        // Command Line
        ["CommandLine.Prompt"] = "COMMAND:",
        ["CommandLine.Watermark"] = "Type a command...",
        ["CommandLine.Ready"] = "Ready — enter a command or select objects",
        ["CommandLine.TabHint"] = "  (Tab)",

        // Layout Bar
        ["Layout.Model"] = "Model",
        ["Layout.New"] = "+",
        ["Layout.Tip.New"] = "Create new layout",

        // Layer Panel
        ["LayerPanel.Title"] = "LAYER PROPERTIES MANAGER",
        ["LayerPanel.ColName"] = "Name",
        ["LayerPanel.ColColor"] = "Color",
        ["LayerPanel.ColStatus"] = "Status",
        ["LayerPanel.Add"] = "+ Layer",
        ["LayerPanel.Color"] = "Layer Color",
        ["LayerPanel.Lock"] = "Lock / Unlock",
        ["LayerPanel.Freeze"] = "Freeze / Thaw",
        ["LayerPanel.SetCurrent"] = "Set Current",
        ["LayerPanel.Rename"] = "Rename Layer…",
        ["LayerPanel.Delete"] = "Delete Layer",

        // Properties Panel
        ["Properties.Title"] = "PROPERTIES",
        ["Properties.General"] = "General",
        ["Properties.Geometry"] = "Geometry",
        ["Properties.Layer"] = "Layer",
        ["Properties.Color"] = "Color",
        ["Properties.Linetype"] = "Linetype",
        ["Properties.Lineweight"] = "Lineweight",
        ["Properties.Radius"] = "Radius",
        ["Properties.Length"] = "Length",
        ["Properties.Angle"] = "Angle",
        ["Properties.TextHeight"] = "Text Height",
        ["Properties.NoSelection"] = "No selection",

        // Canvas Viewport Controls
        ["Canvas.Viewport"] = "[-] [Top] [2D Wireframe]",
        ["Canvas.Wcs"] = "WCS",
    };

    private static readonly Dictionary<string, string> VietnameseStrings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Application & QAT
        ["App.Title"] = "OpenCAD",
        ["App.Workspace"] = "Soạn thảo 2D & Chú thích",
        ["App.Language"] = "Ngôn ngữ",
        ["App.LangEn"] = "English",
        ["App.LangVi"] = "Tiếng Việt",
        ["App.Menu"] = "Menu ứng dụng",

        // Ribbon Tabs
        ["Ribbon.Tab.Home"] = "Trang chủ",
        ["Ribbon.Tab.Insert"] = "Chèn",
        ["Ribbon.Tab.Annotate"] = "Chú thích",
        ["Ribbon.Tab.View"] = "Xem",
        ["Ribbon.Tab.Output"] = "In",

        // Ribbon Groups
        ["Ribbon.Group.Draw"] = "Vẽ",
        ["Ribbon.Group.Modify"] = "Sửa",
        ["Ribbon.Group.Layers"] = "Lớp",
        ["Ribbon.Group.Block"] = "Block",
        ["Ribbon.Group.Hatch"] = "Tô",
        ["Ribbon.Group.Text"] = "Chữ",
        ["Ribbon.Group.Dimensions"] = "Kích thước",
        ["Ribbon.Group.Inquiry"] = "Đo",
        ["Ribbon.Group.Navigate"] = "Zoom",
        ["Ribbon.Group.Drafting"] = "Bật tắt",
        ["Ribbon.Group.Manage"] = "Kiểm tra",
        ["Ribbon.Group.Publish"] = "Xuất bản",
        ["Ribbon.Group.Layout"] = "Bố cục",
        ["Ribbon.Group.File"] = "Tệp",

        // Menu Bar
        ["Menu.File"] = "&Tệp",
        ["Menu.Edit"] = "&Sửa",
        ["Menu.Draw"] = "&Vẽ",
        ["Menu.Modify"] = "&Biến đổi",
        ["Menu.View"] = "&Xem",
        ["Menu.Layout"] = "&Layout",
        ["Menu.Tools"] = "&Công cụ",
        ["Menu.Help"] = "&Trợ giúp",

        ["Menu.New"] = "&Tạo mới",
        ["Menu.Open"] = "&Mở…",
        ["Menu.Recent"] = "Mở &gần đây",
        ["Menu.Save"] = "&Lưu",
        ["Menu.SaveAs"] = "Lưu &mới…",
        ["Menu.ExportPdf"] = "Xuất &PDF",
        ["Menu.Exit"] = "Th&oát",

        ["Menu.Undo"] = "&Hoàn tác",
        ["Menu.Redo"] = "&Làm lại",
        ["Menu.Erase"] = "&Xoá",
        ["Menu.SelectAll"] = "Chọn &tất cả",

        ["Menu.ZoomExtents"] = "Zoom &toàn bộ",
        ["Menu.ZoomIn"] = "Phóng &to",
        ["Menu.ZoomOut"] = "Thu &nhỏ",
        ["Menu.ToggleOrtho"] = "Bật/tắt &Ortho (F8)",
        ["Menu.TogglePolar"] = "Bật/tắt &Polar (F10)",
        ["Menu.ToggleOsnap"] = "Bật/tắt &Bắt điểm (F3)",
        ["Menu.ToggleGrid"] = "Bật/tắt &Lưới (F7)",
        ["Menu.ToggleLwt"] = "Hiện/ẩn &Độ đậm nét (LWT)",
        ["Menu.CommandHistory"] = "&Lịch sử dòng lệnh (F2)",

        // Status Bar
        ["Status.Ready"] = "Sẵn sàng",
        ["Status.Model"] = "MODEL",
        ["Status.Paper"] = "PAPER",
        ["Status.Grid"] = "GRID",
        ["Status.Ortho"] = "ORTHO",
        ["Status.Polar"] = "POLAR",
        ["Status.Osnap"] = "OSNAP",
        ["Status.Lwt"] = "LWT",

        ["Status.Tip.Model"] = "Chuyển đổi Model / Paper Space",
        ["Status.Tip.Grid"] = "Bật/tắt lưới (F7)",
        ["Status.Tip.Ortho"] = "Trực giao (F8)",
        ["Status.Tip.Polar"] = "Rãnh căn góc 15° (F10)",
        ["Status.Tip.Osnap"] = "Bắt điểm 2D (F3)",
        ["Status.Tip.Lwt"] = "Hiện độ đậm nét (LWT)",

        // Command Line
        ["CommandLine.Prompt"] = "LỆNH:",
        ["CommandLine.Watermark"] = "Nhập lệnh...",
        ["CommandLine.Ready"] = "Sẵn sàng — gõ lệnh hoặc chọn đối tượng",
        ["CommandLine.TabHint"] = "  (Tab)",

        // Layout Bar
        ["Layout.Model"] = "Model",
        ["Layout.New"] = "+",
        ["Layout.Tip.New"] = "Tạo layout mới",

        // Layer Panel
        ["LayerPanel.Title"] = "QUẢN LÝ LỚP (LAYER)",
        ["LayerPanel.ColName"] = "Tên",
        ["LayerPanel.ColColor"] = "Màu",
        ["LayerPanel.ColStatus"] = "Trạng thái",
        ["LayerPanel.Add"] = "+ Layer",
        ["LayerPanel.Color"] = "Màu layer",
        ["LayerPanel.Lock"] = "Khoá / Bỏ khoá",
        ["LayerPanel.Freeze"] = "Đóng băng / Bỏ đóng băng",
        ["LayerPanel.SetCurrent"] = "Đặt làm layer hiện hành",
        ["LayerPanel.Rename"] = "Đổi tên layer…",
        ["LayerPanel.Delete"] = "Xoá layer",

        // Properties Panel
        ["Properties.Title"] = "THUỘC TÍNH (PROPERTIES)",
        ["Properties.General"] = "Chung",
        ["Properties.Geometry"] = "Hình học",
        ["Properties.Layer"] = "Layer",
        ["Properties.Color"] = "Màu",
        ["Properties.Linetype"] = "Kiểu nét",
        ["Properties.Lineweight"] = "Độ đậm nét",
        ["Properties.Radius"] = "Bán kính",
        ["Properties.Length"] = "Độ dài",
        ["Properties.Angle"] = "Góc",
        ["Properties.TextHeight"] = "Chiều cao chữ",
        ["Properties.NoSelection"] = "Không chọn đối tượng",

        // Canvas Viewport Controls
        ["Canvas.Viewport"] = "[-] [Top] [2D Wireframe]",
        ["Canvas.Wcs"] = "WCS",
    };
}

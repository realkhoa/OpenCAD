using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using AcadLayout = ACadSharp.Objects.Layout;
using CSMath;
using OpenCad.Core.Colors;
using OpenCad.Core.Geometry;
using OpenCad.Core.Rendering;

namespace OpenCad.Core.Layout;

/// <summary>
/// Đọc/ghi layout **thật** trong file DWG: layout có block record riêng chứa
/// các đối tượng paper space (khung, khung tên) và một Viewport trỏ vào model space.
/// </summary>
public static class LayoutFactory
{
    private static readonly Rgb Fallback = new(20, 20, 20);

    /// <summary>Tên các layout có trong bản vẽ (bỏ qua "Model").</summary>
    public static IReadOnlyList<string> LayoutNames(CadDocument document)
    {
        try
        {
            return document.Layouts
                .Where(l => !string.Equals(l.Name, "Model", StringComparison.OrdinalIgnoreCase))
                .Select(l => l.Name)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static AcadLayout? Find(CadDocument document, string name)
    {
        try
        {
            return document.Layouts.FirstOrDefault(
                l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    // --------------------------------------------------------------- CREATE

    /// <summary>Tạo layout mới trong bản vẽ, kèm khung, khung tên và viewport đúng tỷ lệ.</summary>
    public static AcadLayout? Create(CadDocument document, string name,
                                 SheetTemplateOptions template, CadScene modelScene)
    {
        try
        {
            if (Find(document, name) is not null) return null;

            var sheet = PaperSheetBuilder.Build(modelScene, template);
            sheet.Name = name;

            var layout = new AcadLayout(name)
            {
                PaperWidth = sheet.WidthMm,
                PaperHeight = sheet.HeightMm,
                TabOrder = (short)(document.Layouts.Count() + 1),
            };

            document.Layouts.Add(layout);

            var block = layout.AssociatedBlock;
            if (block is null) return layout;

            // Chỉ chuyển phần khung/khung tên; hình vẽ để viewport lo
            foreach (var item in sheet.Items.Where(i => i.Clip is null))
            {
                foreach (var prim in item.Prims)
                    AddPrimAsEntities(block, prim);
            }

            if (sheet.Viewport is { } viewport)
            {
                block.Entities.Add(new Viewport
                {
                    Center = new XYZ(viewport.Rect.Center.X, viewport.Rect.Center.Y, 0),
                    Width = viewport.Rect.Width,
                    Height = viewport.Rect.Height,
                    ViewCenter = new XY(viewport.ViewCenter.X, viewport.ViewCenter.Y),
                    ViewHeight = viewport.ViewHeight,
                    ViewTarget = new XYZ(viewport.ViewCenter.X, viewport.ViewCenter.Y, 0),
                });
            }

            return layout;
        }
        catch
        {
            return null;
        }
    }

    private static void AddPrimAsEntities(ACadSharp.Tables.BlockRecord block, Prim prim)
    {
        switch (prim.Kind)
        {
            case PrimKind.Text:
            {
                var text = new TextEntity
                {
                    InsertPoint = new XYZ(prim.Position.X, prim.Position.Y, 0),
                    Height = Math.Max(prim.Height, 0.5),
                    Value = prim.Text,
                    Color = new Color((short)7),
                };
                block.Entities.Add(text);
                break;
            }

            case PrimKind.Polyline when prim.Points.Length >= 2:
            {
                var polyline = new LwPolyline { IsClosed = prim.Closed };

                foreach (var pt in prim.Points)
                    polyline.Vertices.Add(new LwPolyline.Vertex { Location = new XY(pt.X, pt.Y) });

                block.Entities.Add(polyline);
                break;
            }
        }
    }

    /// <summary>
    /// Thêm một viewport nữa vào layout: khung trên giấy (mm) + tỷ lệ 1:N.
    /// Nhờ vậy một tờ có thể trình bày mặt bằng 1:100 và chi tiết 1:20 cạnh nhau.
    /// </summary>
    /// <summary>
    /// Xoá một layout (không xoá layout cuối cùng — bản vẽ phải còn ít nhất một tờ).
    /// Trả về thông báo lỗi, hoặc null nếu xoá được.
    /// </summary>
    public static string? Delete(CadDocument document, string name)
    {
        var layout = Find(document, name);

        if (layout is null) return $"Không có layout '{name}'.";

        var remaining = LayoutNames(document).Count;

        if (remaining <= 1) return "Không xoá được layout cuối cùng.";

        try
        {
            document.Layouts.Remove(name);

            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>Đổi tên layout. Trả về thông báo lỗi, hoặc null nếu đổi được.</summary>
    public static string? Rename(CadDocument document, string name, string newName)
    {
        var layout = Find(document, name);

        if (layout is null) return $"Không có layout '{name}'.";

        newName = (newName ?? string.Empty).Trim();

        if (newName.Length == 0) return "Tên layout không được rỗng.";

        if (Find(document, newName) is not null) return $"Đã có layout '{newName}'.";

        try
        {
            layout.Name = newName;

            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public static Viewport? AddViewport(AcadLayout layout, CadScene modelScene,
                                        RectD rect, double unitsPerMm)
    {
        try
        {
            if (layout.AssociatedBlock is null) return null;
            if (rect.Width < 1 || rect.Height < 1 || unitsPerMm < 1e-6) return null;

            var bounds = modelScene.Bounds;
            var center = bounds.IsEmpty ? new Pt(0, 0) : bounds.Center;

            var viewport = new Viewport
            {
                Center = new XYZ(rect.Center.X, rect.Center.Y, 0),
                Width = rect.Width,
                Height = rect.Height,
                ViewCenter = new XY(center.X, center.Y),
                ViewHeight = rect.Height * unitsPerMm,
                ViewTarget = new XYZ(center.X, center.Y, 0),
            };

            layout.AssociatedBlock.Entities.Add(viewport);
            return viewport;
        }
        catch
        {
            return null;
        }
    }

    // ----------------------------------------------------------------- READ

    /// <summary>Dựng tờ in từ một layout có sẵn trong bản vẽ.</summary>
    public static PaperSheet? Read(CadDocument document, AcadLayout layout)
    {
        try
        {
            var sheet = new PaperSheet
            {
                Name = layout.Name,
                WidthMm = layout.PaperWidth > 1 ? layout.PaperWidth : 420,
                HeightMm = layout.PaperHeight > 1 ? layout.PaperHeight : 297,
            };

            var block = layout.AssociatedBlock;
            if (block is null) return sheet;

            // Đối tượng paper space (khung, khung tên, ghi chú)
            var paperPrims = new List<Prim>(64);
            var viewports = new List<Viewport>();

            foreach (var entity in block.Entities)
            {
                if (entity is Viewport viewport)
                {
                    if (!viewport.RepresentsPaper) viewports.Add(viewport);
                    continue;
                }

                FlattenInto(entity, Affine2.Identity, paperPrims);
            }

            sheet.Add(paperPrims);

            // Nội dung model space qua từng viewport
            foreach (var viewport in viewports)
            {
                var viewHeight = viewport.ViewHeight;
                if (viewHeight <= 1e-9 || viewport.Height <= 1e-9) continue;

                var scale = viewport.Height / viewHeight;
                var viewCenter = new Pt(viewport.ViewCenter.X, viewport.ViewCenter.Y);
                var center = new Pt(viewport.Center.X, viewport.Center.Y);

                var xf = new Affine2(scale, 0, 0, scale,
                    center.X - viewCenter.X * scale,
                    center.Y - viewCenter.Y * scale);

                var prims = new List<Prim>(512);
                foreach (var entity in document.Entities)
                {
                    if (entity.Layer is { IsOn: false }) continue;
                    FlattenInto(entity, xf, prims);
                }

                var clip = new RectD(
                    center.X - viewport.Width / 2,
                    center.Y - viewport.Height / 2,
                    center.X + viewport.Width / 2,
                    center.Y + viewport.Height / 2);

                sheet.Add(prims, clip);
                sheet.Viewport ??= new PaperViewport
                {
                    Rect = clip,
                    ViewCenter = viewCenter,
                    ViewHeight = viewHeight,
                    UnitsPerMm = 1 / scale,
                };
            }

            return sheet;
        }
        catch
        {
            return null;
        }
    }

    private static void FlattenInto(Entity entity, Affine2 transform, List<Prim> output)
    {
        try
        {
            EntityFlattener.Flatten(entity, output, transform, Fallback, 0);
        }
        catch
        {
            // bỏ qua entity lỗi
        }
    }
}

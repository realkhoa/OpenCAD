using ACadSharp.Entities;
using CSMath;
using OpenCad.Core.Geometry;

namespace OpenCad.Core.Editing;

/// <summary>
/// Tạo Hatch đặc từ một biên khép kín có sẵn (polyline khép kín hoặc đường tròn).
/// </summary>
public static class HatchFactory
{
    public static Hatch? FromBoundary(Entity boundary)
    {
        var hatch = new Hatch
        {
            IsSolid = true,
            Layer = boundary.Layer,
            Color = boundary.Color,
        };

        switch (boundary)
        {
            case LwPolyline polyline when polyline.IsClosed && polyline.Vertices.Count >= 3:
            {
                var path = new Hatch.BoundaryPath();
                var vertices = polyline.Vertices;

                for (var i = 0; i < vertices.Count; i++)
                {
                    var current = vertices[i];
                    var next = vertices[(i + 1) % vertices.Count];

                    var p1 = new Pt(current.Location.X, current.Location.Y);
                    var p2 = new Pt(next.Location.X, next.Location.Y);

                    // Cạnh cong (bulge) phải thành cung thật, nếu ép thành đoạn thẳng
                    // thì vùng tô sẽ tràn ra ngoài hình bo góc.
                    if (Math.Abs(current.Bulge) < 1e-9 || !TryMakeArc(p1, p2, current.Bulge, out var arc))
                    {
                        path.Edges.Add(new Hatch.BoundaryPath.Line
                        {
                            Start = new XY(p1.X, p1.Y),
                            End = new XY(p2.X, p2.Y),
                        });
                    }
                    else
                    {
                        path.Edges.Add(arc);
                    }
                }

                hatch.Paths.Add(path);
                return hatch;
            }

            case Circle circle:
            {
                var path = new Hatch.BoundaryPath();
                var center = new XY(circle.Center.X, circle.Center.Y);

                // Đường tròn kín = hai cung nửa vòng
                path.Edges.Add(new Hatch.BoundaryPath.Arc
                {
                    Center = center,
                    Radius = circle.Radius,
                    StartAngle = 0,
                    EndAngle = Math.PI,
                    CounterClockWise = true,
                });

                path.Edges.Add(new Hatch.BoundaryPath.Arc
                {
                    Center = center,
                    Radius = circle.Radius,
                    StartAngle = Math.PI,
                    EndAngle = 2 * Math.PI,
                    CounterClockWise = true,
                });

                hatch.Paths.Add(path);
                return hatch;
            }

            default:
                return null;
        }
    }

    /// <summary>Entity có phải biên khép kín dùng để tô được không.</summary>
    public static bool CanHatch(Entity entity) => entity switch
    {
        LwPolyline polyline => polyline.IsClosed && polyline.Vertices.Count >= 3,
        Circle => true,
        _ => false,
    };

    /// <summary>
    /// Đổi một đoạn có bulge thành cung tròn. bulge = tan(theta/4), âm nghĩa là chiều kim đồng hồ.
    /// </summary>
    private static bool TryMakeArc(Pt p1, Pt p2, double bulge, out Hatch.BoundaryPath.Arc arc)
    {
        arc = null!;

        var theta = 4 * Math.Atan(bulge);
        var chord = Pt.Distance(p1, p2);
        if (chord < 1e-9 || Math.Abs(Math.Sin(theta / 2)) < 1e-9) return false;

        var radius = chord / (2 * Math.Sin(theta / 2));
        var mid = Pt.Lerp(p1, p2, 0.5);
        var dir = (p2 - p1) / chord;
        var center = mid + dir.Perp() * (radius * Math.Cos(theta / 2));

        var start = Math.Atan2(p1.Y - center.Y, p1.X - center.X);
        var end = Math.Atan2(p2.Y - center.Y, p2.X - center.X);

        arc = new Hatch.BoundaryPath.Arc
        {
            Center = new XY(center.X, center.Y),
            Radius = Math.Abs(radius),
            StartAngle = start,
            EndAngle = end,
            CounterClockWise = bulge > 0,
        };

        return true;
    }
}

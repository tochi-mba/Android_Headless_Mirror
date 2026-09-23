namespace Rex.Core;

/// <summary>Pure selection math for the visual pattern trace. It never contains the unlock pattern.</summary>
public static class PatternPath
{
    public static int? HitTest(IReadOnlyList<PointD> points, PointD pointer, double radius)
    {
        var best = radius * radius;
        int? match = null;
        for (var i = 0; i < points.Count; i++)
        {
            var dx = points[i].X - pointer.X;
            var dy = points[i].Y - pointer.Y;
            var distance = dx * dx + dy * dy;
            if (distance <= best)
            {
                best = distance;
                match = i;
            }
        }

        return match;
    }

    /// <summary>
    /// Adds a cell once and follows Android's skipped-cell rule: crossing the centre of a row,
    /// column or diagonal selects that centre cell first when it has not already been visited.
    /// </summary>
    public static IReadOnlyList<int> AddNode(IReadOnlyList<int> selected, int next)
    {
        if (next is < 0 or > 8 || selected.Contains(next))
        {
            return selected.ToArray();
        }

        var result = selected.ToList();
        if (result.Count > 0)
        {
            var previous = result[^1];
            var previousRow = previous / 3;
            var previousColumn = previous % 3;
            var nextRow = next / 3;
            var nextColumn = next % 3;
            var rowDelta = nextRow - previousRow;
            var columnDelta = nextColumn - previousColumn;

            if (rowDelta % 2 == 0 && columnDelta % 2 == 0 &&
                (Math.Abs(rowDelta) == 2 || Math.Abs(columnDelta) == 2))
            {
                var middle = (previousRow + rowDelta / 2) * 3 + previousColumn + columnDelta / 2;
                if (!result.Contains(middle))
                {
                    result.Add(middle);
                }
            }
        }

        result.Add(next);
        return result;
    }
}

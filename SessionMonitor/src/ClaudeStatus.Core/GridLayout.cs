namespace ClaudeStatus.Core;

public readonly record struct Cell(double X, double Y, double Width, double Height);

/// <summary>
/// Splits a rectangle into n cells. The column count is chosen so that each cell is as large as possible
/// for a preferred aspect ratio, which automatically gives a single row for a wide strip and a single
/// column for a tall strip. Items in an incomplete last row are stretched to fill the full width.
/// </summary>
public static class GridLayout
{
    public static (int Columns, int Rows) ChooseGrid(int count, double width, double height, double preferredAspect)
    {
        if (count <= 0 || width <= 0 || height <= 0) return (Math.Max(count, 1), 1);
        int bestCols = 1;
        double bestScore = double.MinValue;
        for (int cols = 1; cols <= count; cols++)
        {
            int rows = (count + cols - 1) / cols;
            double cw = width / cols, ch = height / rows;
            // Width of the largest box with the preferred aspect that fits into a cell.
            double score = Math.Min(cw, ch * preferredAspect);
            int empty = cols * rows - count;
            score -= empty * 0.01; // tie-breaker: fewer holes
            if (score > bestScore + 1e-9)
            {
                bestScore = score;
                bestCols = cols;
            }
        }
        return (bestCols, (count + bestCols - 1) / bestCols);
    }

    public static IReadOnlyList<Cell> Arrange(int count, double width, double height, double preferredAspect, double gap)
    {
        var cells = new List<Cell>(count);
        if (count <= 0) return cells;
        var (cols, rows) = ChooseGrid(count, width, height, preferredAspect);
        double rowH = (height - gap * (rows - 1)) / rows;
        for (int r = 0; r < rows; r++)
        {
            int first = r * cols;
            int inRow = Math.Min(cols, count - first);
            double colW = (width - gap * (inRow - 1)) / inRow;
            for (int c = 0; c < inRow; c++)
                cells.Add(new Cell(c * (colW + gap), r * (rowH + gap), Math.Max(0, colW), Math.Max(0, rowH)));
        }
        return cells;
    }
}

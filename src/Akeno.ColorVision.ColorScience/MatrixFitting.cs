namespace Akeno.ColorVision.ColorScience;

/// <summary>
/// Least-squares fitting of a <see cref="ColorMatrix"/> to an arbitrary (possibly non-linear)
/// color function. Used to find the best <i>gamma-encoded-domain</i> matrix approximation of a
/// correction that is defined in linear light, because the system-wide filter (Magnification API)
/// can only apply one affine matrix to the encoded desktop values.
/// </summary>
public static class MatrixFitting
{
    /// <summary>
    /// Fits a gray-preserving linear matrix (rows sum to 1, zero offset) so that grays stay
    /// exactly neutral, minimising the squared error over a uniform grid of the RGB cube.
    /// </summary>
    /// <param name="target">Function to approximate. It should itself map grays to grays.</param>
    /// <param name="gridSteps">Samples per axis (total samples = gridSteps³).</param>
    public static ColorMatrix FitGrayPreserving(Func<Rgb, Rgb> target, int gridSteps = 9)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (gridSteps < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(gridSteps));
        }

        // For each output channel j: y_j - b = a0 (r - b) + a1 (g - b), with a2 = 1 - a0 - a1.
        Span<double> sxx = stackalloc double[3]; // [s00, s01, s11]
        Span<double> sxy = stackalloc double[6]; // per channel [s0y, s1y]
        sxx.Clear();
        sxy.Clear();

        for (var ri = 0; ri < gridSteps; ri++)
        {
            for (var gi = 0; gi < gridSteps; gi++)
            {
                for (var bi = 0; bi < gridSteps; bi++)
                {
                    var c = new Rgb(ri / (gridSteps - 1.0), gi / (gridSteps - 1.0), bi / (gridSteps - 1.0));
                    var y = target(c);
                    var x0 = c.R - c.B;
                    var x1 = c.G - c.B;
                    sxx[0] += x0 * x0;
                    sxx[1] += x0 * x1;
                    sxx[2] += x1 * x1;
                    for (var j = 0; j < 3; j++)
                    {
                        var yj = y[j] - c.B;
                        sxy[(j * 2) + 0] += x0 * yj;
                        sxy[(j * 2) + 1] += x1 * yj;
                    }
                }
            }
        }

        var det = (sxx[0] * sxx[2]) - (sxx[1] * sxx[1]);
        Span<double> m = stackalloc double[9];
        for (var j = 0; j < 3; j++)
        {
            var a0 = ((sxx[2] * sxy[j * 2]) - (sxx[1] * sxy[(j * 2) + 1])) / det;
            var a1 = ((sxx[0] * sxy[(j * 2) + 1]) - (sxx[1] * sxy[j * 2])) / det;
            m[(j * 3) + 0] = a0;
            m[(j * 3) + 1] = a1;
            m[(j * 3) + 2] = 1 - a0 - a1;
        }

        return ColorMatrix.FromRowMajor(m);
    }

    /// <summary>Root-mean-square error of <paramref name="matrix"/> (clamped) against <paramref name="target"/> on a grid.</summary>
    public static double RmsError(ColorMatrix matrix, Func<Rgb, Rgb> target, int gridSteps = 9)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(target);
        double sum = 0;
        var n = 0;
        for (var ri = 0; ri < gridSteps; ri++)
        {
            for (var gi = 0; gi < gridSteps; gi++)
            {
                for (var bi = 0; bi < gridSteps; bi++)
                {
                    var c = new Rgb(ri / (gridSteps - 1.0), gi / (gridSteps - 1.0), bi / (gridSteps - 1.0));
                    var a = matrix.TransformClamped(c);
                    var b = target(c).Clamp01();
                    sum += ((a.R - b.R) * (a.R - b.R)) + ((a.G - b.G) * (a.G - b.G)) + ((a.B - b.B) * (a.B - b.B));
                    n += 3;
                }
            }
        }

        return Math.Sqrt(sum / n);
    }
}

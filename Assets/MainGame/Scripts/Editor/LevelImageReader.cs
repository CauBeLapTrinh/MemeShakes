using System;
using UnityEngine;

namespace Catshakes.EditorTools
{
    /// <summary>Result of reading a level picture. All pixel values use image coordinates (origin top-left, y down).</summary>
    public sealed class LevelImageAnalysis
    {
        /// <summary>True when pixels of the floor colour were found.</summary>
        public bool HasFloor;

        /// <summary>True when the cell size could be measured (or was supplied by the user).</summary>
        public bool HasCellSize;

        public string Message = string.Empty;
        public int ImageWidth;
        public int ImageHeight;

        /// <summary>Bounding box of the floor-coloured pixels.</summary>
        public RectInt FloorBounds;

        public float CellSize;
        public int Columns;
        public int Rows;

        /// <summary>Top-left corner of the cell grid that covers the floor.</summary>
        public float OriginX;
        public float OriginY;

        /// <summary>[column, row] with row 0 at the top of the picture. True when the cell shows floor.</summary>
        public bool[,] Floor;

        /// <summary>How far the floor size is from a whole number of cells (0 = perfect, 0.5 = worst).</summary>
        public float FractionError;

        public int FloorCellCount
        {
            get
            {
                int count = 0;
                if (Floor != null)
                {
                    for (int c = 0; c < Floor.GetLength(0); c++)
                    {
                        for (int r = 0; r < Floor.GetLength(1); r++)
                        {
                            if (Floor[c, r])
                            {
                                count++;
                            }
                        }
                    }
                }

                return count;
            }
        }
    }

    /// <summary>
    /// Measures a level picture: finds the floor tiles by colour, measures the cell size from the periodic tile
    /// borders and counts how many columns and rows the floor covers. Pure maths on a pixel array, no Unity objects.
    /// </summary>
    public static class LevelImageReader
    {
        private const int MinFloorPixelsPerLine = 8;
        private const float EdgeClamp = 40f;

        /// <param name="pixelsBottomUp">Texture2D.GetPixels32() output (row 0 is the bottom row).</param>
        /// <param name="tolerance">Maximum RGB distance (0-441) from <paramref name="floorColor"/> that still counts as floor.</param>
        /// <param name="cellSizeOverride">Cell size in pixels, or 0 to measure it.</param>
        public static LevelImageAnalysis Analyze(Color32[] pixelsBottomUp, int width, int height, Color32 floorColor, float tolerance, float cellSizeOverride)
        {
            var result = new LevelImageAnalysis { ImageWidth = width, ImageHeight = height };
            if (pixelsBottomUp == null || width < 8 || height < 8 || pixelsBottomUp.Length < width * height)
            {
                result.Message = "The image is empty or too small.";
                return result;
            }

            var gray = new float[width * height];
            var mask = new bool[width * height];
            float tolSquared = tolerance * tolerance;

            for (int y = 0; y < height; y++)
            {
                int source = (height - 1 - y) * width;
                int target = y * width;
                for (int x = 0; x < width; x++)
                {
                    Color32 c = pixelsBottomUp[source + x];
                    float alpha = c.a / 255f;
                    float r = c.r * alpha + 255f * (1f - alpha);
                    float g = c.g * alpha + 255f * (1f - alpha);
                    float b = c.b * alpha + 255f * (1f - alpha);
                    gray[target + x] = 0.299f * r + 0.587f * g + 0.114f * b;

                    float dr = r - floorColor.r;
                    float dg = g - floorColor.g;
                    float db = b - floorColor.b;
                    mask[target + x] = alpha > 0.5f && dr * dr + dg * dg + db * db <= tolSquared;
                }
            }

            // Bounding box of the floor (ignore stray pixels).
            var columnCount = new int[width];
            var rowCount = new int[height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (mask[y * width + x])
                    {
                        columnCount[x]++;
                        rowCount[y]++;
                    }
                }
            }

            int x0 = FirstAtLeast(columnCount, MinFloorPixelsPerLine);
            int x1 = LastAtLeast(columnCount, MinFloorPixelsPerLine) + 1;
            int y0 = FirstAtLeast(rowCount, MinFloorPixelsPerLine);
            int y1 = LastAtLeast(rowCount, MinFloorPixelsPerLine) + 1;
            if (x0 < 0 || y0 < 0 || x1 <= x0 || y1 <= y0)
            {
                result.Message = "No floor-coloured pixels found. Click a floor tile in the preview to pick its colour, or raise the tolerance.";
                return result;
            }

            result.HasFloor = true;
            result.FloorBounds = new RectInt(x0, y0, x1 - x0, y1 - y0);
            int boundsWidth = x1 - x0;
            int boundsHeight = y1 - y0;

            float cellSize = cellSizeOverride;
            if (cellSize <= 1f)
            {
                float[] columnEdges = BuildEdgeProfile(gray, width, x0, x1, y0, y1, true);
                float[] rowEdges = BuildEdgeProfile(gray, width, x0, x1, y0, y1, false);
                int minLag = Math.Max(12, (int)(0.02f * Math.Min(width, height)));
                if (TryEstimateCellSize(columnEdges, rowEdges, minLag, out cellSize))
                {
                    result.HasCellSize = true;
                }
                else
                {
                    cellSize = Math.Min(boundsWidth, boundsHeight);
                    result.Message = "Could not measure the cell size automatically. Type it in manually.";
                }
            }
            else
            {
                result.HasCellSize = true;
            }

            result.CellSize = cellSize;
            result.Columns = Math.Max(1, (int)Math.Round(boundsWidth / cellSize));
            result.Rows = Math.Max(1, (int)Math.Round(boundsHeight / cellSize));
            result.FractionError = Math.Max(
                Math.Abs(boundsWidth / cellSize - result.Columns),
                Math.Abs(boundsHeight / cellSize - result.Rows));
            result.OriginX = x0 - (result.Columns * cellSize - boundsWidth) * 0.5f;
            result.OriginY = y0 - (result.Rows * cellSize - boundsHeight) * 0.5f;

            result.Floor = new bool[result.Columns, result.Rows];
            for (int c = 0; c < result.Columns; c++)
            {
                for (int r = 0; r < result.Rows; r++)
                {
                    int sx0 = Mathf.Clamp(Mathf.RoundToInt(result.OriginX + (c + 0.3f) * cellSize), 0, width - 1);
                    int sx1 = Mathf.Clamp(Mathf.RoundToInt(result.OriginX + (c + 0.7f) * cellSize), sx0 + 1, width);
                    int sy0 = Mathf.Clamp(Mathf.RoundToInt(result.OriginY + (r + 0.3f) * cellSize), 0, height - 1);
                    int sy1 = Mathf.Clamp(Mathf.RoundToInt(result.OriginY + (r + 0.7f) * cellSize), sy0 + 1, height);

                    int hits = 0;
                    int total = 0;
                    for (int y = sy0; y < sy1; y++)
                    {
                        for (int x = sx0; x < sx1; x++)
                        {
                            total++;
                            if (mask[y * width + x])
                            {
                                hits++;
                            }
                        }
                    }

                    result.Floor[c, r] = total > 0 && hits * 2 > total;
                }
            }

            if (result.FractionError > 0.25f && string.IsNullOrEmpty(result.Message))
            {
                result.Message = "The floor is not a whole number of cells wide or tall at this cell size. Check the overlay and adjust the cell size.";
            }

            return result;
        }

        private static int FirstAtLeast(int[] values, int minimum)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] >= minimum)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int LastAtLeast(int[] values, int minimum)
        {
            for (int i = values.Length - 1; i >= 0; i--)
            {
                if (values[i] >= minimum)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Edge strength per column (vertical edges, <paramref name="byColumn"/> = true) or per row, measured inside the
        /// floor bounding box. Cell borders show up as regularly spaced peaks.
        /// </summary>
        private static float[] BuildEdgeProfile(float[] gray, int width, int x0, int x1, int y0, int y1, bool byColumn)
        {
            int length = (byColumn ? x1 - x0 : y1 - y0) - 1;
            var profile = new float[Math.Max(0, length)];

            if (byColumn)
            {
                for (int y = y0; y < y1; y++)
                {
                    int row = y * width;
                    for (int x = x0; x < x1 - 1; x++)
                    {
                        float d = Math.Abs(gray[row + x + 1] - gray[row + x]);
                        profile[x - x0] += Math.Min(d, EdgeClamp) / EdgeClamp;
                    }
                }
            }
            else
            {
                for (int y = y0; y < y1 - 1; y++)
                {
                    int row = y * width;
                    for (int x = x0; x < x1; x++)
                    {
                        float d = Math.Abs(gray[row + width + x] - gray[row + x]);
                        profile[y - y0] += Math.Min(d, EdgeClamp) / EdgeClamp;
                    }
                }
            }

            return Smooth(profile);
        }

        private static float[] Smooth(float[] v)
        {
            var result = new float[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                float left = v[Math.Max(0, i - 1)];
                float right = v[Math.Min(v.Length - 1, i + 1)];
                result[i] = 0.25f * left + 0.5f * v[i] + 0.25f * right;
            }

            return result;
        }

        private static bool TryEstimateCellSize(float[] columnEdges, float[] rowEdges, int minLag, out float cellSize)
        {
            cellSize = 0f;
            int maxLag = Math.Max(columnEdges.Length, rowEdges.Length) - 9;
            if (maxLag <= minLag + 2)
            {
                return false;
            }

            float[] rx = AutoCorrelation(columnEdges, minLag, maxLag);
            float[] ry = AutoCorrelation(rowEdges, minLag, maxLag);

            var r = new float[maxLag + 2];
            float best = 0f;
            for (int lag = minLag; lag <= maxLag; lag++)
            {
                float a = rx[lag];
                float b = ry[lag];
                bool hasA = !float.IsNaN(a);
                bool hasB = !float.IsNaN(b);
                float value = hasA && hasB ? 0.5f * (a + b) : hasA ? a : hasB ? b : 0f;
                r[lag] = value;
                if (value > best)
                {
                    best = value;
                }
            }

            if (best < 0.15f)
            {
                return false;
            }

            int first = -1;
            for (int lag = minLag + 1; lag < maxLag; lag++)
            {
                if (r[lag] >= r[lag - 1] && r[lag] >= r[lag + 1] && r[lag] >= 0.7f * best)
                {
                    first = lag;
                    break;
                }
            }

            if (first < 0)
            {
                return false;
            }

            // The peak is broad: take its highest lag as the starting point for the fine search.
            int from = Math.Max(minLag, (int)(first * 0.93f));
            int to = Math.Min(maxLag, (int)(first * 1.07f) + 1);
            int peak = first;
            for (int lag = from; lag <= to; lag++)
            {
                if (r[lag] > r[peak])
                {
                    peak = lag;
                }
            }

            float span = Math.Max(2f, peak * 0.03f);
            float bestScore = float.MinValue;
            float bestPeriod = peak;
            for (float p = peak - span; p <= peak + span; p += 0.05f)
            {
                float score = FoldVariance(columnEdges, p) + FoldVariance(rowEdges, p);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPeriod = p;
                }
            }

            cellSize = bestPeriod;
            return true;
        }

        /// <summary>Normalised autocorrelation of the detrended profile. Lags without enough samples are NaN.</summary>
        private static float[] AutoCorrelation(float[] profile, int minLag, int maxLag)
        {
            var result = new float[maxLag + 2];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = float.NaN;
            }

            int n = profile.Length;
            if (n < minLag + 10)
            {
                return result;
            }

            // Remove the slow trend with a moving average (prefix sums).
            int window = Math.Max(31, n / 6) | 1;
            int half = window / 2;
            var prefix = new double[n + 1];
            for (int i = 0; i < n; i++)
            {
                prefix[i + 1] = prefix[i] + profile[i];
            }

            var d = new float[n];
            double variance = 0.0;
            for (int i = 0; i < n; i++)
            {
                int lo = Math.Max(0, i - half);
                int hi = Math.Min(n, i + half + 1);
                float mean = (float)((prefix[hi] - prefix[lo]) / (hi - lo));
                d[i] = profile[i] - mean;
                variance += d[i] * d[i];
            }

            variance = variance / n + 1e-9;

            int last = Math.Min(maxLag, n - 9);
            for (int lag = minLag; lag <= last; lag++)
            {
                double sum = 0.0;
                int count = n - lag;
                for (int i = 0; i < count; i++)
                {
                    sum += d[i] * d[i + lag];
                }

                result[lag] = (float)(sum / count / variance);
            }

            return result;
        }

        /// <summary>Variance of the profile folded modulo <paramref name="period"/>: highest when the period matches the grid.</summary>
        private static float FoldVariance(float[] profile, float period)
        {
            if (profile.Length == 0)
            {
                return 0f;
            }

            int bins = Math.Max(8, (int)Math.Round(period));
            var sums = new double[bins];
            var counts = new int[bins];
            for (int i = 0; i < profile.Length; i++)
            {
                float phase = i / period;
                phase -= (float)Math.Floor(phase);
                int bin = Math.Min(bins - 1, (int)(phase * bins));
                sums[bin] += profile[i];
                counts[bin]++;
            }

            double mean = 0.0;
            int used = 0;
            var means = new double[bins];
            for (int b = 0; b < bins; b++)
            {
                if (counts[b] > 0)
                {
                    means[b] = sums[b] / counts[b];
                    mean += means[b];
                    used++;
                }
            }

            if (used == 0)
            {
                return 0f;
            }

            mean /= used;
            double variance = 0.0;
            for (int b = 0; b < bins; b++)
            {
                if (counts[b] > 0)
                {
                    double diff = means[b] - mean;
                    variance += diff * diff;
                }
            }

            return (float)(variance / used);
        }
    }
}

using System.Globalization;
using UnityEngine;

namespace SplatXR
{
    // USplat CSV: a header line, then one splat per line as
    // x,y,z,opacity,f_dc_0,f_dc_1,f_dc_2,cov0,cov1,cov2,cov3,cov4,cov5
    public static class SplatCsvLoader
    {
        private const int ColumnCount = 13;

        public const int RecommendedMaxSplats = 200000;

        public static bool Load(TextAsset csvFile, out Vector3[] positions, out Vector3[] colors, out float[] opacities,
            out Vector3[] covariances0_3, out Vector3[] covariances3_6)
        {
            positions = colors = covariances0_3 = covariances3_6 = null;
            opacities = null;

            if (csvFile == null) return false;

            string[] lines = csvFile.text.Split('\n');
            int capacity = Mathf.Max(0, lines.Length - 1);

            positions = new Vector3[capacity];
            colors = new Vector3[capacity];
            opacities = new float[capacity];
            covariances0_3 = new Vector3[capacity];
            covariances3_6 = new Vector3[capacity];

            float[] values = new float[ColumnCount];
            int count = 0;
            int skipped = 0;
            int firstSkippedLine = 0;

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (!TryParseValues(line, values))
                {
                    if (skipped == 0) firstSkippedLine = i + 1;
                    skipped++;
                    continue;
                }

                positions[count] = new Vector3(values[0], values[1], values[2]);
                opacities[count] = values[3];
                colors[count] = new Vector3(values[4], values[5], values[6]);
                covariances0_3[count] = new Vector3(values[7], values[8], values[9]);
                covariances3_6[count] = new Vector3(values[10], values[11], values[12]);
                count++;
            }

            if (count < capacity)
            {
                System.Array.Resize(ref positions, count);
                System.Array.Resize(ref colors, count);
                System.Array.Resize(ref opacities, count);
                System.Array.Resize(ref covariances0_3, count);
                System.Array.Resize(ref covariances3_6, count);
            }

            if (skipped > 0)
            {
                Debug.LogWarning($"[SplatXR] Skipped {skipped} malformed line(s) in {csvFile.name} (first at line {firstSkippedLine}). " +
                                 $"Each line needs {ColumnCount} comma-separated numbers.", csvFile);
            }

            if (count > RecommendedMaxSplats)
            {
                Debug.LogWarning($"[SplatXR] {csvFile.name} has {count:N0} splats, which is more than the recommended maximum of " +
                                 $"{RecommendedMaxSplats:N0}. Rendering may be slow; consider cropping, decimating the splat, or using the LoD Renderer.", csvFile);
            }

            return true;
        }

        private static bool TryParseValues(string line, float[] values)
        {
            string[] parts = line.Split(',');
            if (parts.Length < ColumnCount) return false;

            for (int i = 0; i < ColumnCount; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                {
                    return false;
                }
            }
            return true;
        }
    }
}

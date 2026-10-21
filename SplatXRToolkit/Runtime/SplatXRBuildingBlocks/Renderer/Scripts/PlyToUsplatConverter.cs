using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;

namespace SplatXR
{
    public static class PlyToUsplatConverter
    {
        public static void Convert(
            string plyPath,
            string csvPath,
            float sceneScale = 1.0f,
            Vector4? sceneWXYZ = null,
            Vector3? sceneTranslation = null,
            bool convertConventionPosition = false,
            bool convertConventionRotation = false,
            bool flipY = false)
        {
            ConvertInternal(plyPath, csvPath, sceneScale, sceneWXYZ, sceneTranslation,
                            convertConventionPosition, convertConventionRotation, flipY, null);
        }

        public static async Task ConvertAsync(
            string plyPath,
            string csvPath,
            IProgress<float> progress = null,
            float sceneScale = 1.0f,
            Vector4? sceneWXYZ = null,
            Vector3? sceneTranslation = null,
            bool convertConventionPosition = false,
            bool convertConventionRotation = false,
            bool flipY = false)
        {
            await Task.Run(() =>
            {
                ConvertInternal(plyPath, csvPath, sceneScale, sceneWXYZ, sceneTranslation,
                                convertConventionPosition, convertConventionRotation, flipY, progress);
            });
        }

        private static void ConvertInternal(
            string plyPath,
            string csvPath,
            float sceneScale,
            Vector4? sceneWXYZ,
            Vector3? sceneTranslation,
            bool convertConventionPosition,
            bool convertConventionRotation,
            bool flipY,
            IProgress<float> progress)
        {
            const float SH_C0 = 0.28209479177387814f;

            if (!File.Exists(plyPath))
                throw new FileNotFoundException($"PLY file not found: {plyPath}");

            using (FileStream fs = new FileStream(plyPath, FileMode.Open, FileAccess.Read))
            using (BinaryReader br = new BinaryReader(fs))
            using (StreamWriter sw = new StreamWriter(csvPath, false, Encoding.UTF8))
            {
                int vertexCount = 0;
                List<string> properties = new List<string>();
                string line;
                while ((line = ReadAsciiLine(br)) != null)
                {
                    if (line.StartsWith("element vertex"))
                    {
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3) vertexCount = int.Parse(parts[2], CultureInfo.InvariantCulture);
                    }
                    else if (line.StartsWith("property"))
                    {
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3) properties.Add(parts[2]);
                    }
                    else if (line.StartsWith("end_header"))
                    {
                        break;
                    }
                }

                if (vertexCount <= 0)
                    throw new Exception("PLY header missing vertex count.");

                int floatsPerVertex = properties.Count;

                sw.WriteLine("x,y,z,opacity,f_dc_0,f_dc_1,f_dc_2,cov6_0,cov6_1,cov6_2,cov6_3,cov6_4,cov6_5");

                Vector4 qvec = sceneWXYZ ?? new Vector4(1f, 0f, 0f, 0f);
                Quaternion sceneRot = new Quaternion(qvec.y, qvec.z, qvec.w, qvec.x).normalized;
                Vector3 translation = sceneTranslation ?? Vector3.zero;

                for (int vi = 0; vi < vertexCount; vi++)
                {
                    float[] raw = new float[floatsPerVertex];
                    for (int j = 0; j < floatsPerVertex; j++)
                        raw[j] = br.ReadSingle();

                    float x = 0f, y = 0f, z = 0f;
                    float fdc0 = 0f, fdc1 = 0f, fdc2 = 0f;
                    float rawOpacity = 0f;
                    float scale0 = 0f, scale1 = 0f, scale2 = 0f;
                    float rot0 = 1f, rot1 = 0f, rot2 = 0f, rot3 = 0f;

                    for (int j = 0; j < properties.Count && j < raw.Length; j++)
                    {
                        string p = properties[j];
                        float v = raw[j];
                        switch (p)
                        {
                            case "x": x = v; break;
                            case "y": y = v; break;
                            case "z": z = v; break;
                            case "f_dc_0": fdc0 = v; break;
                            case "f_dc_1": fdc1 = v; break;
                            case "f_dc_2": fdc2 = v; break;
                            case "opacity": rawOpacity = v; break;
                            case "scale_0": scale0 = v; break;
                            case "scale_1": scale1 = v; break;
                            case "scale_2": scale2 = v; break;
                            case "rot_0": rot0 = v; break;
                            case "rot_1": rot1 = v; break;
                            case "rot_2": rot2 = v; break;
                            case "rot_3": rot3 = v; break;
                            default: break;
                        }
                    }

                    float s0 = Mathf.Exp(scale0);
                    float s1 = Mathf.Exp(scale1);
                    float s2 = Mathf.Exp(scale2);

                    fdc0 = 0.5f + SH_C0 * fdc0;
                    fdc1 = 0.5f + SH_C0 * fdc1;
                    fdc2 = 0.5f + SH_C0 * fdc2;

                    float op = 1.0f / (1.0f + Mathf.Exp(-rawOpacity));
                    op = Mathf.Clamp(Mathf.Clamp(op * 255f, 0f, 255f) / 255f, 0f, 1f);

                    Quaternion q = new Quaternion(rot1, rot2, rot3, rot0).normalized;

                    Vector3 pos = new Vector3(x, y, z) * sceneScale;
                    pos = sceneRot * pos;
                    pos += translation;

                    if (flipY) pos.y = -pos.y;
                    if (convertConventionPosition) pos.z = -pos.z;

                    s0 *= sceneScale; s1 *= sceneScale; s2 *= sceneScale;

                    Quaternion qAfterScene = (sceneRot * q).normalized;

                    if (convertConventionRotation)
                    {
                        Matrix4x4 Rmat = Matrix4x4.Rotate(qAfterScene);
                        Matrix4x4 M = Matrix4x4.identity; M.m22 = -1f;
                        Matrix4x4 Rconv = M * Rmat * M.transpose;
                        qAfterScene = QuaternionFromMatrix(Rconv);
                    }

                    float[] cov6 = ComputeCov6(new Vector3(s0, s1, s2), qAfterScene);

                    sw.WriteLine(FormattableString.Invariant(
                        $"{pos.x},{pos.y},{pos.z},{op},{fdc0},{fdc1},{fdc2},{cov6[0]},{cov6[1]},{cov6[2]},{cov6[3]},{cov6[4]},{cov6[5]}"));

                    if (progress != null && vi % 1000 == 0)
                        progress.Report((float)vi / vertexCount);
                }

                progress?.Report(1f);
            }
        }

        private static float[] ComputeCov6(Vector3 scale, Quaternion rotation)
        {
            Matrix4x4 R = Matrix4x4.Rotate(rotation);
            Matrix4x4 S = Matrix4x4.zero;
            S.m00 = scale.x * scale.x;
            S.m11 = scale.y * scale.y;
            S.m22 = scale.z * scale.z;
            S.m33 = 1f;

            Matrix4x4 cov = R * S * R.transpose;

            return new float[]
            {
                cov.m00, cov.m01, cov.m02,
                cov.m11, cov.m12, cov.m22
            };
        }

        private static Quaternion QuaternionFromMatrix(Matrix4x4 m)
        {
            Quaternion q = new Quaternion();
            float trace = m.m00 + m.m11 + m.m22;
            if (trace > 0f)
            {
                float s = Mathf.Sqrt(trace + 1f) * 2f;
                q.w = 0.25f * s;
                q.x = (m.m21 - m.m12) / s;
                q.y = (m.m02 - m.m20) / s;
                q.z = (m.m10 - m.m01) / s;
            }
            else if ((m.m00 > m.m11) && (m.m00 > m.m22))
            {
                float s = Mathf.Sqrt(1f + m.m00 - m.m11 - m.m22) * 2f;
                q.w = (m.m21 - m.m12) / s;
                q.x = 0.25f * s;
                q.y = (m.m01 + m.m10) / s;
                q.z = (m.m02 + m.m20) / s;
            }
            else if (m.m11 > m.m22)
            {
                float s = Mathf.Sqrt(1f + m.m11 - m.m00 - m.m22) * 2f;
                q.w = (m.m02 - m.m20) / s;
                q.x = (m.m01 + m.m10) / s;
                q.y = 0.25f * s;
                q.z = (m.m12 + m.m21) / s;
            }
            else
            {
                float s = Mathf.Sqrt(1f + m.m22 - m.m00 - m.m11) * 2f;
                q.w = (m.m10 - m.m01) / s;
                q.x = (m.m02 + m.m20) / s;
                q.y = (m.m12 + m.m21) / s;
                q.z = 0.25f * s;
            }
            return q.normalized;
        }

        private static string ReadAsciiLine(BinaryReader br)
        {
            List<byte> bytes = new List<byte>();
            while (br.BaseStream.Position < br.BaseStream.Length)
            {
                byte b = br.ReadByte();
                if (b == 10) break;
                bytes.Add(b);
            }
            if (bytes.Count == 0 && br.BaseStream.Position >= br.BaseStream.Length) return null;
            return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
        }
    }
}

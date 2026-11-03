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
            using (StreamWriter sw = new StreamWriter(csvPath, false, Encoding.UTF8, 1 << 16))
            {
                PlyVertexLayout layout = ReadPlyHeader(br, plyPath);
                int vertexCount = layout.count;

                sw.WriteLine("x,y,z,opacity,f_dc_0,f_dc_1,f_dc_2,cov6_0,cov6_1,cov6_2,cov6_3,cov6_4,cov6_5");

                Vector4 qvec = sceneWXYZ ?? new Vector4(1f, 0f, 0f, 0f);
                Quaternion sceneRot = new Quaternion(qvec.y, qvec.z, qvec.w, qvec.x).normalized;
                Vector3 translation = sceneTranslation ?? Vector3.zero;

                const int chunkVertices = 8192;
                byte[] chunk = null;
                int chunkStart = 0, chunkLength = 0;
                float[] row = new float[13];

                for (int vi = 0; vi < vertexCount; vi++)
                {
                    if (vi == chunkStart + chunkLength)
                    {
                        chunkStart = vi;
                        chunkLength = Math.Min(chunkVertices, vertexCount - vi);
                        chunk = br.ReadBytes(chunkLength * layout.stride);
                        if (chunk.Length != chunkLength * layout.stride)
                            throw new EndOfStreamException($"{plyPath} ended before all {vertexCount} vertices were read.");
                        layout.ConvertToHostByteOrder(chunk, chunkLength);
                    }
                    int vertexOffset = (vi - chunkStart) * layout.stride;

                    float x = layout.Read(chunk, vertexOffset, PlyVertexLayout.X, 0f);
                    float y = layout.Read(chunk, vertexOffset, PlyVertexLayout.Y, 0f);
                    float z = layout.Read(chunk, vertexOffset, PlyVertexLayout.Z, 0f);
                    float fdc0 = layout.Read(chunk, vertexOffset, PlyVertexLayout.FDc0, 0f);
                    float fdc1 = layout.Read(chunk, vertexOffset, PlyVertexLayout.FDc1, 0f);
                    float fdc2 = layout.Read(chunk, vertexOffset, PlyVertexLayout.FDc2, 0f);
                    float rawOpacity = layout.Read(chunk, vertexOffset, PlyVertexLayout.Opacity, 0f);
                    float scale0 = layout.Read(chunk, vertexOffset, PlyVertexLayout.Scale0, 0f);
                    float scale1 = layout.Read(chunk, vertexOffset, PlyVertexLayout.Scale1, 0f);
                    float scale2 = layout.Read(chunk, vertexOffset, PlyVertexLayout.Scale2, 0f);
                    float rot0 = layout.Read(chunk, vertexOffset, PlyVertexLayout.Rot0, 1f);
                    float rot1 = layout.Read(chunk, vertexOffset, PlyVertexLayout.Rot1, 0f);
                    float rot2 = layout.Read(chunk, vertexOffset, PlyVertexLayout.Rot2, 0f);
                    float rot3 = layout.Read(chunk, vertexOffset, PlyVertexLayout.Rot3, 0f);

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

                    row[0] = pos.x; row[1] = pos.y; row[2] = pos.z;
                    row[3] = op;
                    row[4] = fdc0; row[5] = fdc1; row[6] = fdc2;
                    ComputeCov6(new Vector3(s0, s1, s2), qAfterScene, row, 7);
                    WriteCsvRow(sw, row);

                    if (progress != null && vi % 1000 == 0)
                        progress.Report((float)vi / vertexCount);
                }

                progress?.Report(1f);
            }
        }

        private static void WriteCsvRow(TextWriter writer, float[] row)
        {
            for (int i = 0; i < row.Length; i++)
            {
                if (i > 0) writer.Write(',');
                writer.Write(row[i].ToString(CultureInfo.InvariantCulture));
            }
            writer.WriteLine();
        }

        private static void ComputeCov6(Vector3 scale, Quaternion rotation, float[] output, int start)
        {
            Matrix4x4 R = Matrix4x4.Rotate(rotation);
            Matrix4x4 S = Matrix4x4.zero;
            S.m00 = scale.x * scale.x;
            S.m11 = scale.y * scale.y;
            S.m22 = scale.z * scale.z;
            S.m33 = 1f;

            Matrix4x4 cov = R * S * R.transpose;

            output[start + 0] = cov.m00;
            output[start + 1] = cov.m01;
            output[start + 2] = cov.m02;
            output[start + 3] = cov.m11;
            output[start + 4] = cov.m12;
            output[start + 5] = cov.m22;
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

        private enum PlyScalar { Int8, UInt8, Int16, UInt16, Int32, UInt32, Float32, Float64 }

        private static PlyScalar ParsePlyScalar(string type)
        {
            switch (type)
            {
                case "char": case "int8": return PlyScalar.Int8;
                case "uchar": case "uint8": return PlyScalar.UInt8;
                case "short": case "int16": return PlyScalar.Int16;
                case "ushort": case "uint16": return PlyScalar.UInt16;
                case "int": case "int32": return PlyScalar.Int32;
                case "uint": case "uint32": return PlyScalar.UInt32;
                case "float": case "float32": return PlyScalar.Float32;
                case "double": case "float64": return PlyScalar.Float64;
                default: throw new NotSupportedException($"Unknown PLY property type '{type}'.");
            }
        }

        private static int PlyScalarSize(PlyScalar type)
        {
            switch (type)
            {
                case PlyScalar.Int8: case PlyScalar.UInt8: return 1;
                case PlyScalar.Int16: case PlyScalar.UInt16: return 2;
                case PlyScalar.Float64: return 8;
                default: return 4;
            }
        }

        private sealed class PlyElement
        {
            public string name;
            public int count;
            public bool hasListProperty;
            public readonly List<(string name, PlyScalar type)> properties = new List<(string name, PlyScalar type)>();

            public int Stride
            {
                get
                {
                    int stride = 0;
                    foreach (var property in properties) stride += PlyScalarSize(property.type);
                    return stride;
                }
            }
        }

        // Byte offsets and types of the splat properties inside one record of the PLY "vertex" element.
        private sealed class PlyVertexLayout
        {
            public const int X = 0, Y = 1, Z = 2, FDc0 = 3, FDc1 = 4, FDc2 = 5, Opacity = 6,
                             Scale0 = 7, Scale1 = 8, Scale2 = 9, Rot0 = 10, Rot1 = 11, Rot2 = 12, Rot3 = 13;

            private static readonly string[] Names =
            {
                "x", "y", "z", "f_dc_0", "f_dc_1", "f_dc_2", "opacity",
                "scale_0", "scale_1", "scale_2", "rot_0", "rot_1", "rot_2", "rot_3"
            };

            public readonly int count;
            public readonly int stride;
            private readonly int[] offsets = new int[Names.Length];
            private readonly PlyScalar[] types = new PlyScalar[Names.Length];
            private readonly bool swapBytes;

            public PlyVertexLayout(PlyElement vertex, bool bigEndian, string plyPath)
            {
                count = vertex.count;
                if (count <= 0)
                    throw new Exception("PLY header missing vertex count.");

                for (int i = 0; i < offsets.Length; i++) offsets[i] = -1;

                foreach (var property in vertex.properties)
                {
                    int index = Array.IndexOf(Names, property.name);
                    if (index >= 0)
                    {
                        offsets[index] = stride;
                        types[index] = property.type;
                    }
                    stride += PlyScalarSize(property.type);
                }

                if (offsets[X] < 0 || offsets[Y] < 0 || offsets[Z] < 0)
                    throw new InvalidDataException($"{plyPath} has no x/y/z vertex properties.");

                swapBytes = bigEndian == BitConverter.IsLittleEndian;
            }

            public void ConvertToHostByteOrder(byte[] data, int vertices)
            {
                if (!swapBytes) return;

                for (int property = 0; property < offsets.Length; property++)
                {
                    int size = PlyScalarSize(types[property]);
                    if (offsets[property] < 0 || size == 1) continue;

                    for (int v = 0; v < vertices; v++)
                    {
                        Array.Reverse(data, v * stride + offsets[property], size);
                    }
                }
            }

            public float Read(byte[] data, int vertexOffset, int property, float missingValue)
            {
                int offset = offsets[property];
                if (offset < 0) return missingValue;
                offset += vertexOffset;

                switch (types[property])
                {
                    case PlyScalar.Int8: return (sbyte)data[offset];
                    case PlyScalar.UInt8: return data[offset];
                    case PlyScalar.Int16: return BitConverter.ToInt16(data, offset);
                    case PlyScalar.UInt16: return BitConverter.ToUInt16(data, offset);
                    case PlyScalar.Int32: return BitConverter.ToInt32(data, offset);
                    case PlyScalar.UInt32: return BitConverter.ToUInt32(data, offset);
                    case PlyScalar.Float64: return (float)BitConverter.ToDouble(data, offset);
                    default: return BitConverter.ToSingle(data, offset);
                }
            }
        }

        // Parses the header and leaves the stream at the first vertex record. Elements before the vertices
        // (e.g. the camera metadata some exporters write) are skipped; elements after them are ignored.
        private static PlyVertexLayout ReadPlyHeader(BinaryReader br, string plyPath)
        {
            if (ReadAsciiLine(br) != "ply")
                throw new InvalidDataException($"{plyPath} is not a PLY file.");

            bool bigEndian = false;
            var elements = new List<PlyElement>();
            string line;
            while (true)
            {
                line = ReadAsciiLine(br);
                if (line == null)
                    throw new InvalidDataException($"Unexpected end of file in the PLY header of {plyPath}.");

                string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                if (parts[0] == "end_header") break;

                if (parts[0] == "format" && parts.Length >= 2)
                {
                    if (parts[1] == "binary_big_endian") bigEndian = true;
                    else if (parts[1] != "binary_little_endian")
                        throw new NotSupportedException($"Only binary PLY files are supported, got '{parts[1]}'.");
                }
                else if (parts[0] == "element" && parts.Length >= 3)
                {
                    elements.Add(new PlyElement { name = parts[1], count = int.Parse(parts[2], CultureInfo.InvariantCulture) });
                }
                else if (parts[0] == "property" && parts.Length >= 3 && elements.Count > 0)
                {
                    PlyElement element = elements[elements.Count - 1];
                    if (parts[1] == "list") element.hasListProperty = true;
                    else element.properties.Add((parts[2], ParsePlyScalar(parts[1])));
                }
            }

            long bytesBeforeVertices = 0;
            foreach (PlyElement element in elements)
            {
                if (element.name == "vertex")
                {
                    if (element.hasListProperty)
                        throw new NotSupportedException($"List properties in the vertex element of {plyPath} are not supported.");
                    br.BaseStream.Seek(bytesBeforeVertices, SeekOrigin.Current);
                    return new PlyVertexLayout(element, bigEndian, plyPath);
                }

                if (element.hasListProperty)
                    throw new NotSupportedException($"Can't skip the '{element.name}' element of {plyPath}: it has list properties and comes before the vertices.");
                bytesBeforeVertices += (long)element.Stride * element.count;
            }

            throw new Exception("PLY header missing vertex count.");
        }

        private static string ReadAsciiLine(BinaryReader br)
        {
            List<byte> bytes = new List<byte>();
            int b;
            while ((b = br.BaseStream.ReadByte()) >= 0)
            {
                if (b == 10) break;
                bytes.Add((byte)b);
            }
            if (bytes.Count == 0 && b < 0) return null;
            return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
        }
    }
}

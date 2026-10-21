using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;

namespace SplatXR
{
    public static class UsplatToPlyConverter
    {
        public static void Convert(
            string csvPath,
            string plyPath,
            float sceneScale = 1.0f,
            Vector4? sceneWXYZ = null,
            Vector3? sceneTranslation = null,
            bool convertConventionPosition = false,
            bool convertConventionRotation = false,
            bool flipY = false)
        {
            ConvertInternal(csvPath, plyPath, sceneScale, sceneWXYZ, sceneTranslation,
                            convertConventionPosition, convertConventionRotation, flipY, null);
        }

        public static async Task ConvertAsync(
            string csvPath,
            string plyPath,
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
                ConvertInternal(csvPath, plyPath, sceneScale, sceneWXYZ, sceneTranslation,
                                convertConventionPosition, convertConventionRotation, flipY, progress);
            });
        }

        private static void ConvertInternal(
            string csvPath,
            string plyPath,
            float sceneScale,
            Vector4? sceneWXYZ,
            Vector3? sceneTranslation,
            bool convertConventionPosition,
            bool convertConventionRotation,
            bool flipY,
            IProgress<float> progress)
        {
            const float SH_C0 = 0.28209479177387814f;

            if (!File.Exists(csvPath))
                throw new FileNotFoundException($"CSV file not found: {csvPath}");

            List<string> csvLines = new List<string>();
        
            using (StreamReader sr = new StreamReader(csvPath, Encoding.UTF8))
            {
                string line;
                bool isHeader = true;
                while ((line = sr.ReadLine()) != null)
                {
                    if (isHeader)
                    {
                        isHeader = false;
                        continue;
                    }
                    csvLines.Add(line);
                }
            }

            int vertexCount = csvLines.Count;
            if (vertexCount <= 0)
                throw new Exception("CSV file contains no data rows.");


            Vector4 qvec = sceneWXYZ ?? new Vector4(1f, 0f, 0f, 0f);
            Quaternion sceneRot = new Quaternion(qvec.y, qvec.z, qvec.w, qvec.x).normalized;
            Quaternion inverseSceneRot = Quaternion.Inverse(sceneRot);
            Vector3 translation = sceneTranslation ?? Vector3.zero;
            float inverseSceneScale = sceneScale != 0f ? 1f / sceneScale : 1f;

            using (FileStream fs = new FileStream(plyPath, FileMode.Create, FileAccess.Write))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                WritePlyHeader(bw, vertexCount);

                bool loggedFirst = false;

                for (int vi = 0; vi < vertexCount; vi++)
                {
                    string line = csvLines[vi];
                    string[] parts = line.Split(',');
                
                    if (parts.Length < 13)
                        throw new Exception($"CSV line {vi + 2} has insufficient columns. Expected 13, got {parts.Length}");

                    float x = float.Parse(parts[0], CultureInfo.InvariantCulture);
                    float y = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    float z = float.Parse(parts[2], CultureInfo.InvariantCulture);
                    float opacity = float.Parse(parts[3], CultureInfo.InvariantCulture);
                    float fdc0 = float.Parse(parts[4], CultureInfo.InvariantCulture);
                    float fdc1 = float.Parse(parts[5], CultureInfo.InvariantCulture);
                    float fdc2 = float.Parse(parts[6], CultureInfo.InvariantCulture);
                    float cov6_0 = float.Parse(parts[7], CultureInfo.InvariantCulture);
                    float cov6_1 = float.Parse(parts[8], CultureInfo.InvariantCulture);
                    float cov6_2 = float.Parse(parts[9], CultureInfo.InvariantCulture);
                    float cov6_3 = float.Parse(parts[10], CultureInfo.InvariantCulture);
                    float cov6_4 = float.Parse(parts[11], CultureInfo.InvariantCulture);
                    float cov6_5 = float.Parse(parts[12], CultureInfo.InvariantCulture);

                    Vector3 pos = new Vector3(x, y, z);
                
                    if (flipY) pos.y = -pos.y;
                    if (convertConventionPosition) pos.z = -pos.z;
                
                    pos -= translation;
                    pos = inverseSceneRot * pos;
                    pos *= inverseSceneScale;

                    Matrix4x4 covMatrix = new Matrix4x4();
                    covMatrix.m00 = cov6_0; covMatrix.m01 = cov6_1; covMatrix.m02 = cov6_2;
                    covMatrix.m10 = cov6_1; covMatrix.m11 = cov6_3; covMatrix.m12 = cov6_4;
                    covMatrix.m20 = cov6_2; covMatrix.m21 = cov6_4; covMatrix.m22 = cov6_5;
                    covMatrix.m33 = 1f;

                    Vector3 scale;
                    Quaternion rotation;
                    DecomposeCovarianceMatrix(covMatrix, out scale, out rotation);

                    if (convertConventionRotation)
                    {
                        Matrix4x4 Rmat = Matrix4x4.Rotate(rotation);
                        Matrix4x4 M = Matrix4x4.identity; M.m22 = -1f;
                        Matrix4x4 Rconv = M.transpose * Rmat * M;
                        rotation = QuaternionFromMatrix(Rconv);
                    }

                    rotation = Quaternion.Inverse(sceneRot) * rotation;
                    rotation = rotation.normalized;

                    scale *= inverseSceneScale;

                    float scale0 = Mathf.Log(scale.x);
                    float scale1 = Mathf.Log(scale.y);
                    float scale2 = Mathf.Log(scale.z);

                    fdc0 = (fdc0 - 0.5f) / SH_C0;
                    fdc1 = (fdc1 - 0.5f) / SH_C0;
                    fdc2 = (fdc2 - 0.5f) / SH_C0;

                    opacity = Mathf.Clamp(opacity, 0.001f, 0.999f);
                    float rawOpacity = -Mathf.Log((1.0f / opacity) - 1.0f);

                    if (!loggedFirst)
                    {
                        loggedFirst = true;
                      
                        float[] reconstructedCov6 = ReconstructCov6ForVerification(scale, rotation);
                        float originalCov6_0 = cov6_0, originalCov6_1 = cov6_1, originalCov6_2 = cov6_2;
                        float originalCov6_3 = cov6_3, originalCov6_4 = cov6_4, originalCov6_5 = cov6_5;
                    
                        float error = Mathf.Abs(reconstructedCov6[0] - originalCov6_0) + 
                                     Mathf.Abs(reconstructedCov6[1] - originalCov6_1) + 
                                     Mathf.Abs(reconstructedCov6[2] - originalCov6_2) + 
                                     Mathf.Abs(reconstructedCov6[3] - originalCov6_3) + 
                                     Mathf.Abs(reconstructedCov6[4] - originalCov6_4) + 
                                     Mathf.Abs(reconstructedCov6[5] - originalCov6_5);
                    
                    }

                    bw.Write(pos.x);
                    bw.Write(pos.y);
                    bw.Write(pos.z);
                    bw.Write(fdc0);
                    bw.Write(fdc1);
                    bw.Write(fdc2);
                    bw.Write(rawOpacity);
                    bw.Write(scale0);
                    bw.Write(scale1);
                    bw.Write(scale2);
                    bw.Write(rotation.w);
                    bw.Write(rotation.x);
                    bw.Write(rotation.y);
                    bw.Write(rotation.z);

                    if (progress != null && vi % 1000 == 0)
                        progress.Report((float)vi / vertexCount);
                }

                progress?.Report(1f);
            }

        }

        private static void WritePlyHeader(BinaryWriter bw, int vertexCount)
        {
            string header = "ply\n" +
                           "format binary_little_endian 1.0\n" +
                           $"element vertex {vertexCount}\n" +
                           "property float x\n" +
                           "property float y\n" +
                           "property float z\n" +
                           "property float f_dc_0\n" +
                           "property float f_dc_1\n" +
                           "property float f_dc_2\n" +
                           "property float opacity\n" +
                           "property float scale_0\n" +
                           "property float scale_1\n" +
                           "property float scale_2\n" +
                           "property float rot_0\n" +
                           "property float rot_1\n" +
                           "property float rot_2\n" +
                           "property float rot_3\n" +
                           "end_header\n";

            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            bw.Write(headerBytes);
        }

        private static void DecomposeCovarianceMatrix(Matrix4x4 covMatrix, out Vector3 scale, out Quaternion rotation)
        {
            float m00 = covMatrix.m00, m01 = covMatrix.m01, m02 = covMatrix.m02;
            float m10 = covMatrix.m10, m11 = covMatrix.m11, m12 = covMatrix.m12;
            float m20 = covMatrix.m20, m21 = covMatrix.m21, m22 = covMatrix.m22;
        
            float trace = m00 + m11 + m22;
            float s = (m01 * m01 + m02 * m02 + m12 * m12);
            float det = m00 * (m11 * m22 - m12 * m12) - m01 * (m10 * m22 - m12 * m20) + m02 * (m10 * m12 - m11 * m20);
        
            float[] eigenvalues = new float[3];
            Vector3[] eigenvectors = new Vector3[3];
        
            ComputeEigenvaluesAndVectors(m00, m01, m02, m11, m12, m22, out eigenvalues, out eigenvectors);
        
            scale = new Vector3(
                Mathf.Sqrt(Mathf.Max(1e-8f, eigenvalues[0])),
                Mathf.Sqrt(Mathf.Max(1e-8f, eigenvalues[1])),
                Mathf.Sqrt(Mathf.Max(1e-8f, eigenvalues[2]))
            );
        
            Matrix4x4 rotMatrix = Matrix4x4.identity;
            rotMatrix.m00 = eigenvectors[0].x; rotMatrix.m01 = eigenvectors[1].x; rotMatrix.m02 = eigenvectors[2].x;
            rotMatrix.m10 = eigenvectors[0].y; rotMatrix.m11 = eigenvectors[1].y; rotMatrix.m12 = eigenvectors[2].y;
            rotMatrix.m20 = eigenvectors[0].z; rotMatrix.m21 = eigenvectors[1].z; rotMatrix.m22 = eigenvectors[2].z;
        
            float det_rot = rotMatrix.m00 * (rotMatrix.m11 * rotMatrix.m22 - rotMatrix.m12 * rotMatrix.m21) -
                           rotMatrix.m01 * (rotMatrix.m10 * rotMatrix.m22 - rotMatrix.m12 * rotMatrix.m20) +
                           rotMatrix.m02 * (rotMatrix.m10 * rotMatrix.m21 - rotMatrix.m11 * rotMatrix.m20);
        
            if (det_rot < 0)
            {
                rotMatrix.m02 = -rotMatrix.m02;
                rotMatrix.m12 = -rotMatrix.m12;
                rotMatrix.m22 = -rotMatrix.m22;
            }
        
            rotation = QuaternionFromMatrix(rotMatrix);
        }
    
        private static void ComputeEigenvaluesAndVectors(float m00, float m01, float m02, float m11, float m12, float m22, 
                                                        out float[] eigenvalues, out Vector3[] eigenvectors)
        {
            eigenvalues = new float[3];
            eigenvectors = new Vector3[3];
        
            float[,] A = new float[3, 3] { { m00, m01, m02 }, { m01, m11, m12 }, { m02, m12, m22 } };
            float[,] V = new float[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        
            for (int iter = 0; iter < 100; iter++)
            {
                int p = 0, q = 1;
                float maxVal = Mathf.Abs(A[0, 1]);
            
                if (Mathf.Abs(A[0, 2]) > maxVal) { p = 0; q = 2; maxVal = Mathf.Abs(A[0, 2]); }
                if (Mathf.Abs(A[1, 2]) > maxVal) { p = 1; q = 2; maxVal = Mathf.Abs(A[1, 2]); }
            
                if (maxVal < 1e-12f) break;
            
                float tau = (A[q, q] - A[p, p]) / (2.0f * A[p, q]);
                float t;
                if (Mathf.Abs(tau) < 1e10f)
                {
                    t = 1.0f / (Mathf.Abs(tau) + Mathf.Sqrt(1.0f + tau * tau));
                    if (tau < 0) t = -t;
                }
                else
                {
                    t = 0.5f / tau;
                }
                float c = 1.0f / Mathf.Sqrt(1.0f + t * t);
                float s = t * c;
            
                float App = A[p, p], Aqq = A[q, q], Apq = A[p, q];
                A[p, p] = c * c * App - 2 * c * s * Apq + s * s * Aqq;
                A[q, q] = s * s * App + 2 * c * s * Apq + c * c * Aqq;
                A[p, q] = A[q, p] = 0;
            
                for (int i = 0; i < 3; i++)
                {
                    if (i != p && i != q)
                    {
                        float Aip = A[i, p], Aiq = A[i, q];
                        A[i, p] = A[p, i] = c * Aip - s * Aiq;
                        A[i, q] = A[q, i] = s * Aip + c * Aiq;
                    }
                }
            
                for (int i = 0; i < 3; i++)
                {
                    float Vip = V[i, p], Viq = V[i, q];
                    V[i, p] = c * Vip - s * Viq;
                    V[i, q] = s * Vip + c * Viq;
                }
            }
        
            eigenvalues[0] = A[0, 0];
            eigenvalues[1] = A[1, 1];
            eigenvalues[2] = A[2, 2];
        
            eigenvectors[0] = new Vector3(V[0, 0], V[1, 0], V[2, 0]).normalized;
            eigenvectors[1] = new Vector3(V[0, 1], V[1, 1], V[2, 1]).normalized;
            eigenvectors[2] = new Vector3(V[0, 2], V[1, 2], V[2, 2]).normalized;
        
            for (int i = 0; i < 2; i++)
            {
                for (int j = i + 1; j < 3; j++)
                {
                    if (eigenvalues[j] > eigenvalues[i])
                    {
                        float tempVal = eigenvalues[i];
                        eigenvalues[i] = eigenvalues[j];
                        eigenvalues[j] = tempVal;
                    
                        Vector3 tempVec = eigenvectors[i];
                        eigenvectors[i] = eigenvectors[j];
                        eigenvectors[j] = tempVec;
                    }
                }
            }
        }
    
        private static float[] ReconstructCov6ForVerification(Vector3 scale, Quaternion rotation)
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
    }
}
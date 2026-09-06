using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ShittyMaze.Vr
{
    /// <summary>
    /// Minimal runtime glTF 2.0 loader + drawable for the TT-33 pistol
    /// (new-assets/tt_33/scene.gltf + scene.bin + PNGs, shipped as raw
    /// AndroidAssets and read through <see cref="TitleContainer"/>).
    ///
    /// Scope is deliberately tailored to this asset (see VR_ADAPTATION_PLAN 3.2):
    ///  - accessors/bufferViews with byteOffset/byteStride,
    ///  - POSITION/NORMAL (VEC3 f32), TEXCOORD_0 (VEC2 f32), TANGENT ignored,
    ///  - uint32 or uint16 indices,
    ///  - node hierarchy with baked "matrix" (column-major, transposed into the
    ///    XNA row-major convention) or TRS,
    ///  - one baseColor texture per material (loaded via Texture2D.FromStream);
    ///    normal/metallicRoughness are skipped (BasicEffect has no PBR),
    ///  - all primitives are merged into a single vertex/index buffer with one
    ///    shared BasicEffect (doubleSided material -> CullMode.None set by the
    ///    caller via GraphicsDevice state).
    ///
    /// After baking the node transforms the whole model is re-centered on its
    /// bounding-box center and uniformly scaled so its longest dimension is
    /// exactly 1.0 (unit-normalized); VrWeapon then gives it real-world size.
    ///
    /// Parsing uses the JsonDocument DOM (no reflection) so it survives
    /// Release IL trimming.
    /// </summary>
    public class GltfModel : IDisposable
    {
        private readonly GraphicsDevice graphicsDevice;
        private readonly BasicEffect effect;
        private VertexBuffer vertexBuffer;
        private IndexBuffer indexBuffer;
        private int vertexCount;
        private int indexCount;

        private GltfModel(GraphicsDevice graphicsDevice)
        {
            this.graphicsDevice = graphicsDevice;
            this.effect = new BasicEffect(graphicsDevice)
            {
                TextureEnabled = true,
                LightingEnabled = false,
                VertexColorEnabled = false,
                DiffuseColor = Vector3.One,
            };
        }

        /// <summary>Graphics device the model was created on (for state setup by the caller).</summary>
        public GraphicsDevice GraphicsDevice => graphicsDevice;

        /// <summary>Unit-normalized longest axis of the loaded model (always 1.0 after normalization).</summary>
        public float NormalizedSize => 1.0f;

        /// <summary>Tint multiplier applied to the base color (used for the HP indicator).</summary>
        public Vector3 Tint
        {
            get => effect.DiffuseColor;
            set => effect.DiffuseColor = value;
        }

        /// <summary>
        /// Loads a glTF file (asset-relative path, e.g. "tt_33/scene.gltf")
        /// together with its external buffer and baseColor textures.
        /// </summary>
        public static GltfModel Load(GraphicsDevice graphicsDevice, string gltfAssetPath)
        {
            GltfModel model = new GltfModel(graphicsDevice);

            byte[] gltfBytes = ReadAssetBytes(gltfAssetPath);
            string assetDir = Path.GetDirectoryName(gltfAssetPath)?.Replace('\\', '/') ?? string.Empty;

            List<VertexPositionNormalTexture> vertices = new List<VertexPositionNormalTexture>();
            List<int> indices = new List<int>();

            using (JsonDocument doc = JsonDocument.Parse(gltfBytes))
            {
                JsonElement root = doc.RootElement;

                // --- Buffers (only external file URIs supported) ---
                JsonElement buffersEl = root.GetProperty("buffers");
                byte[][] buffers = new byte[buffersEl.GetArrayLength()][];
                for (int i = 0; i < buffers.Length; i++)
                {
                    string uri = buffersEl[i].GetProperty("uri").GetString();
                    if (uri != null && uri.StartsWith("data:", StringComparison.Ordinal))
                        throw new NotSupportedException("Embedded data-URI glTF buffers are not supported.");
                    buffers[i] = ReadAssetBytes(JoinPath(assetDir, uri));
                }

                JsonElement bufferViews = root.GetProperty("bufferViews");
                JsonElement accessors = root.GetProperty("accessors");
                JsonElement meshes = root.GetProperty("meshes");
                JsonElement nodes = root.GetProperty("nodes");

                // --- Walk the default scene node hierarchy, baking transforms ---
                int sceneIndex = root.TryGetProperty("scene", out JsonElement sceneEl) ? sceneEl.GetInt32() : 0;
                JsonElement sceneNodes = root.GetProperty("scenes")[sceneIndex].GetProperty("nodes");
                foreach (JsonElement sceneNode in sceneNodes.EnumerateArray())
                    WalkNode(nodes, sceneNode.GetInt32(), Matrix.Identity, buffers, bufferViews, accessors, meshes, vertices, indices);

                if (vertices.Count == 0 || indices.Count == 0)
                    throw new InvalidDataException("glTF contains no renderable geometry.");
            }

            model.BuildBuffers(vertices, indices);
            model.LoadTexture(gltfBytes, assetDir);
            return model;
        }

        private static void WalkNode(
            JsonElement nodes, int nodeIndex, Matrix parentWorld,
            byte[][] buffers, JsonElement bufferViews, JsonElement accessors, JsonElement meshes,
            List<VertexPositionNormalTexture> vertices, List<int> indices)
        {
            JsonElement node = nodes[nodeIndex];

            Matrix local = ReadNodeLocalMatrix(node);
            // XNA row-vector convention: child transform applied first,
            // so world = local * parentWorld.
            Matrix world = local * parentWorld;

            if (node.TryGetProperty("mesh", out JsonElement meshEl))
                AddMesh(meshes[meshEl.GetInt32()], world, buffers, bufferViews, accessors, vertices, indices);

            if (node.TryGetProperty("children", out JsonElement children))
            {
                foreach (JsonElement child in children.EnumerateArray())
                    WalkNode(nodes, child.GetInt32(), world, buffers, bufferViews, accessors, meshes, vertices, indices);
            }
        }

        /// <summary>
        /// Reads a glTF node's local transform: "matrix" (16 floats, column
        /// major -> transposed into XNA row-major) or TRS components.
        /// </summary>
        private static Matrix ReadNodeLocalMatrix(JsonElement node)
        {
            if (node.TryGetProperty("matrix", out JsonElement matrixEl))
            {
                float[] m = new float[16];
                int i = 0;
                foreach (JsonElement v in matrixEl.EnumerateArray())
                    m[i++] = v.GetSingle();

                // XNA (row-major, row-vector) matrix = transpose of the glTF column-major matrix.
                return new Matrix(
                    m[0], m[4], m[8],  m[12],
                    m[1], m[5], m[9],  m[13],
                    m[2], m[6], m[10], m[14],
                    m[3], m[7], m[11], m[15]);
            }

            Matrix result = Matrix.Identity;
            if (node.TryGetProperty("scale", out JsonElement scaleEl))
            {
                Vector3 s = ReadVector3(scaleEl);
                result *= Matrix.CreateScale(s);
            }
            if (node.TryGetProperty("rotation", out JsonElement rotEl))
            {
                int i = 0;
                float[] q = new float[4];
                foreach (JsonElement v in rotEl.EnumerateArray())
                    q[i++] = v.GetSingle();
                result *= Matrix.CreateFromQuaternion(new Quaternion(q[0], q[1], q[2], q[3]));
            }
            if (node.TryGetProperty("translation", out JsonElement transEl))
            {
                result *= Matrix.CreateTranslation(ReadVector3(transEl));
            }
            return result;
        }

        private static Vector3 ReadVector3(JsonElement el)
        {
            float[] v = new float[3];
            int i = 0;
            foreach (JsonElement e in el.EnumerateArray())
                v[i++] = e.GetSingle();
            return new Vector3(v[0], v[1], v[2]);
        }

        private static void AddMesh(
            JsonElement mesh, Matrix world,
            byte[][] buffers, JsonElement bufferViews, JsonElement accessors,
            List<VertexPositionNormalTexture> vertices, List<int> indices)
        {
            foreach (JsonElement primitive in mesh.GetProperty("primitives").EnumerateArray())
            {
                int mode = primitive.TryGetProperty("mode", out JsonElement modeEl) ? modeEl.GetInt32() : 4;
                if (mode != 4)
                    continue; // Only triangle lists.

                JsonElement attributes = primitive.GetProperty("attributes");

                // glTF note: attribute values ("POSITION", "NORMAL", ...) and
                // "indices" are integer indices INTO the "accessors" array, not
                // accessor objects. Resolve the index before reading, otherwise
                // GetProperty("bufferView") throws JsonElementHasWrongType
                // (Number has no properties) and the whole model fails to load.
                float[] positions = ReadFloatAccessor(buffers, bufferViews, accessors,
                    accessors[attributes.GetProperty("POSITION").GetInt32()], 3);
                float[] normals = attributes.TryGetProperty("NORMAL", out JsonElement normalAcc)
                    ? ReadFloatAccessor(buffers, bufferViews, accessors, accessors[normalAcc.GetInt32()], 3)
                    : null;
                float[] texcoords = attributes.TryGetProperty("TEXCOORD_0", out JsonElement uvAcc)
                    ? ReadFloatAccessor(buffers, bufferViews, accessors, accessors[uvAcc.GetInt32()], 2)
                    : null;

                int vertexBase = vertices.Count;
                int count = positions.Length / 3;
                for (int i = 0; i < count; i++)
                {
                    Vector3 position = Vector3.Transform(new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]), world);
                    Vector3 normal = Vector3.Forward;
                    if (normals != null)
                    {
                        normal = Vector3.Normalize(Vector3.TransformNormal(
                            new Vector3(normals[i * 3], normals[i * 3 + 1], normals[i * 3 + 2]), world));
                    }
                    Vector2 uv = texcoords != null
                        ? new Vector2(texcoords[i * 2], texcoords[i * 2 + 1])
                        : Vector2.Zero;

                    vertices.Add(new VertexPositionNormalTexture(position, normal, uv));
                }

                int[] primIndices = ReadIndexAccessor(buffers, bufferViews, accessors,
                    accessors[primitive.GetProperty("indices").GetInt32()]);
                foreach (int index in primIndices)
                    indices.Add(vertexBase + index);
            }
        }

        /// <summary>
        /// Extracts a float accessor honoring bufferView byteOffset/byteStride
        /// and accessor byteOffset. Interleaved views are de-interleaved here.
        /// </summary>
        private static float[] ReadFloatAccessor(
            byte[][] buffers, JsonElement bufferViews, JsonElement accessors, JsonElement accessor, int components)
        {
            const int ComponentTypeFloat = 5126;

            // glTF chain: accessor -> bufferView -> buffer. The bufferView index
            // must NOT index the buffers array (a file with several bufferViews
            // over one binary buffer would throw IndexOutOfRange here).
            JsonElement view = bufferViews[accessor.GetProperty("bufferView").GetInt32()];
            byte[] buffer = buffers[view.GetProperty("buffer").GetInt32()];
            int viewOffset = view.TryGetProperty("byteOffset", out JsonElement vo) ? vo.GetInt32() : 0;
            int accessorOffset = accessor.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0;
            int count = accessor.GetProperty("count").GetInt32();
            int componentType = accessor.GetProperty("componentType").GetInt32();
            if (componentType != ComponentTypeFloat)
                throw new NotSupportedException($"Accessor componentType {componentType} is not float32.");

            int elementBytes = components * sizeof(float);
            int stride = view.TryGetProperty("byteStride", out JsonElement bs) ? bs.GetInt32() : 0;
            if (stride < elementBytes)
                stride = elementBytes;

            float[] result = new float[count * components];
            int baseOffset = viewOffset + accessorOffset;
            for (int i = 0; i < count; i++)
                Buffer.BlockCopy(buffer, baseOffset + i * stride, result, i * elementBytes, elementBytes);

            return result;
        }

        private static int[] ReadIndexAccessor(
            byte[][] buffers, JsonElement bufferViews, JsonElement accessors, JsonElement accessor)
        {
            const int ComponentTypeUnsignedShort = 5123;
            const int ComponentTypeUnsignedInt = 5125;

            // Same accessor -> bufferView -> buffer chain as ReadFloatAccessor.
            JsonElement view = bufferViews[accessor.GetProperty("bufferView").GetInt32()];
            byte[] buffer = buffers[view.GetProperty("buffer").GetInt32()];
            int viewOffset = view.TryGetProperty("byteOffset", out JsonElement vo) ? vo.GetInt32() : 0;
            int accessorOffset = accessor.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0;
            int count = accessor.GetProperty("count").GetInt32();
            int componentType = accessor.GetProperty("componentType").GetInt32();

            int elementBytes = componentType == ComponentTypeUnsignedInt ? 4 : 2;
            int stride = view.TryGetProperty("byteStride", out JsonElement bs) ? bs.GetInt32() : 0;
            if (stride < elementBytes)
                stride = elementBytes;

            int[] result = new int[count];
            int baseOffset = viewOffset + accessorOffset;
            for (int i = 0; i < count; i++)
            {
                int offset = baseOffset + i * stride;
                if (componentType == ComponentTypeUnsignedInt)
                    result[i] = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset, 4));
                else if (componentType == ComponentTypeUnsignedShort)
                    result[i] = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset, 2));
                else
                    throw new NotSupportedException($"Index componentType {componentType} is not supported.");
            }
            return result;
        }

        /// <summary>
        /// Centers the baked geometry on its bounding-box center and scales it
        /// so the longest dimension equals 1.0, then creates the GPU buffers.
        /// </summary>
        private void BuildBuffers(List<VertexPositionNormalTexture> vertices, List<int> indices)
        {
            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);
            foreach (VertexPositionNormalTexture v in vertices)
            {
                min = Vector3.Min(min, v.Position);
                max = Vector3.Max(max, v.Position);
            }

            Vector3 center = (min + max) * 0.5f;
            Vector3 extents = max - min;
            float maxExtent = Math.Max(extents.X, Math.Max(extents.Y, extents.Z));
            if (maxExtent < 1e-8f)
                maxExtent = 1f;

            float normalizeScale = 1f / maxExtent;
            Vector3[] positions = new Vector3[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 p = (vertices[i].Position - center) * normalizeScale;
                positions[i] = p;
                vertices[i] = new VertexPositionNormalTexture(p, vertices[i].Normal, vertices[i].TextureCoordinate);
            }

            vertexCount = vertices.Count;
            vertexBuffer = new VertexBuffer(graphicsDevice, typeof(VertexPositionNormalTexture), vertexCount, BufferUsage.WriteOnly);
            vertexBuffer.SetData(vertices.ToArray());

            indexCount = indices.Count;
            bool needs32Bits = vertexCount > ushort.MaxValue;
            indexBuffer = new IndexBuffer(
                graphicsDevice,
                needs32Bits ? IndexElementSize.ThirtyTwoBits : IndexElementSize.SixteenBits,
                indexCount,
                BufferUsage.WriteOnly);
            if (needs32Bits)
                indexBuffer.SetData(indices.ToArray());
            else
            {
                short[] shortIndices = new short[indexCount];
                for (int i = 0; i < indexCount; i++)
                    shortIndices[i] = (short)indices[i];
                indexBuffer.SetData(shortIndices);
            }
        }

        /// <summary>
        /// Finds the baseColor image of the first material and loads it via
        /// Texture2D.FromStream (StbSharp handles PNG on Android GLES).
        /// </summary>
        private void LoadTexture(byte[] gltfBytes, string assetDir)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(gltfBytes))
                {
                    JsonElement root = doc.RootElement;
                    if (!root.TryGetProperty("materials", out JsonElement materials))
                        return;

                    string imageUri = null;
                    foreach (JsonElement material in materials.EnumerateArray())
                    {
                        if (material.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr) &&
                            pbr.TryGetProperty("baseColorTexture", out JsonElement baseColor) &&
                            baseColor.TryGetProperty("index", out JsonElement texIndex))
                        {
                            int imageSource = root.GetProperty("textures")[texIndex.GetInt32()].GetProperty("source").GetInt32();
                            imageUri = root.GetProperty("images")[imageSource].GetProperty("uri").GetString();
                            break; // The TT-33 uses a single shared material.
                        }
                    }

                    if (imageUri == null)
                        return;

                    using (Stream stream = TitleContainer.OpenStream(JoinPath(assetDir, imageUri)))
                        effect.Texture = Texture2D.FromStream(graphicsDevice, stream);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GltfModel] Texture load failed (model will render untextured): {ex.Message}");
            }
        }

        /// <summary>Draws the model with the given transform matrices.</summary>
        public void Draw(Matrix world, Matrix view, Matrix projection)
        {
            if (vertexBuffer == null || indexBuffer == null)
                return;

            effect.World = world;
            effect.View = view;
            effect.Projection = projection;

            graphicsDevice.SetVertexBuffer(vertexBuffer);
            graphicsDevice.Indices = indexBuffer;

            foreach (EffectPass pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    0, 0, vertexCount,
                    0, indexCount / 3);
            }
        }

        private static byte[] ReadAssetBytes(string assetPath)
        {
            using (Stream stream = TitleContainer.OpenStream(assetPath))
            using (MemoryStream memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }

        private static string JoinPath(string dir, string uri)
        {
            uri = uri.Replace('\\', '/');
            if (string.IsNullOrEmpty(dir))
                return uri;
            return dir + "/" + uri;
        }

        public void Dispose()
        {
            effect?.Texture?.Dispose();
            effect?.Dispose();
            vertexBuffer?.Dispose();
            indexBuffer?.Dispose();
        }
    }
}

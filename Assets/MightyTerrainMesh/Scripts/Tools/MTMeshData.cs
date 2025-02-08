namespace MightyTerrainMesh
{
    using System.Collections;
    using System.Collections.Generic;
    using System;
    using System.IO;
    using UnityEngine;

    public class MTMeshData
    {
        public class LOD
        {
            /// <summary>
            /// 所有顶点
            /// </summary>
            public List<Vector3> vertices;

            /// <summary>
            /// 所有法线
            /// </summary>
            public List<Vector3> normals;

            /// <summary>
            /// 所有UV
            /// </summary>
            public List<Vector2> uvs;

            /// <summary>
            /// 顶点索引信息
            /// </summary>
            public List<int> faces;

            public Vector2 uvmin;
            public Vector2 uvmax;

            public SamplerTree samplerTree;

            public void Merge(LOD lod)
            {
                // TODO 这种方式有较为严格的顺序限制，所以还是借助unity的MeshCombine处理
                /*for (int i = 0; i < lod.faces.Count; i++)
                {
                    lod.faces[i] += (vertices.Count);
                }

                faces.AddRange(lod.faces);
                
                vertices.AddRange(lod.vertices);
                normals.AddRange(lod.normals);
                uvs.AddRange(lod.uvs);
                
                // 更改指针，因为mesh已合并
                lod.vertices = vertices;
                lod.normals = normals;
                lod.uvs = uvs;
                lod.faces = faces;
                */

                foreach (var target in lod.samplerTree.mergedTileIndex)
                {
                    samplerTree.mergedTileIndex.Add(target);
                }

                lod.samplerTree.mergedTileIndex = samplerTree.mergedTileIndex;
            }
        }

        public int meshId { get; private set; }
        public LOD[] lods;

        public MTMeshData(int id)
        {
            meshId = id;
        }

        public int refTextureIndex => lods[0].samplerTree.refTextureIndex;
        public bool isSingleBlend => lods[0].samplerTree.isSingleBlend;

        public bool hasMerged
        {
            get { return lods[0].samplerTree.hasMerged; }
            set { lods[0].samplerTree.hasMerged = value; }
        }

        public HashSet<(int, int)> mergedTileIndex => lods[0].samplerTree.mergedTileIndex;
    }
}
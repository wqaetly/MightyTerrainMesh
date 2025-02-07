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
            public Vector3[] vertices;
            /// <summary>
            /// 所有法线
            /// </summary>
            public Vector3[] normals;
            /// <summary>
            /// 所有UV
            /// </summary>
            public Vector2[] uvs;
            /// <summary>
            /// 顶点索引信息
            /// </summary>
            public int[] faces;
            public Vector2 uvmin;
            public Vector2 uvmax;
        }
        public int meshId { get; private set; }
        public LOD[] lods;
        public MTMeshData(int id)
        {
            meshId = id;
        }
    }

}
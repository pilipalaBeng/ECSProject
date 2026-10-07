using UnityEngine;

namespace HanziRogue.Presentation
{
    /// <summary>
    /// 字形 quad 网格工厂：XZ 平面上 1x1 的水平四边形，法线朝 +Y，pivot 居中。
    /// 敌人（instanced）与英雄（GameObject）共用同一份几何，避免两处重复定义。
    /// </summary>
    public static class GlyphQuadMesh
    {
        private static Mesh _shared;

        /// <summary>返回共享网格实例。首次调用时构建，之后复用。</summary>
        public static Mesh Shared()
        {
            if (_shared != null)
            {
                return _shared;
            }

            _shared = new Mesh { name = "GlyphQuad" };
            _shared.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(-0.5f, 0f, 0.5f)
            };
            _shared.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            // 俯视相机从 +Y 俯看，这个环绕序配合 Cull Off 保证双面可见
            _shared.triangles = new[] { 0, 3, 2, 0, 2, 1 };
            _shared.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };

            // uv 的 V 轴与世界 +Z 方向对齐：贴图顶部朝向镜头远侧，字读起来是正的。
            // （Unity quad 默认 v 向上，这里手工指定，避免出现「倒着的字」）
            _shared.RecalculateBounds();
            return _shared;
        }
    }
}

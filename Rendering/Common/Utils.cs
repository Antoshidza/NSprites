using UnityEngine;

namespace NSprites
{
    public static class Utils
    {
        public static Vector4 GetTextureST(Sprite sprite)
        {
            var ratio = new Vector2(1f / sprite.texture.width, 1f / sprite.texture.height);
            var size = Vector2.Scale(sprite.textureRect.size, ratio);
            var offset = Vector2.Scale(sprite.textureRect.position, ratio);
            return new Vector4(size.x, size.y, offset.x, offset.y);
        }

        public static Mesh ConstructQuad()
        {
            var qaud = new Mesh();
            qaud.vertices = new Vector3[4]
            {
                new Vector3(0f, 1f, 0f),    //left up
                new Vector3(1f, 1f, 0f),    //right up
                new Vector3(0f, 0f, 0f),    //left down
                new Vector3(1f, 0f, 0f)     //right down
            };

            qaud.triangles = new int[6]
            {
                // upper left triangle
                0, 1, 2,
                // down right triangle
                3, 2, 1
            };

            qaud.normals = new Vector3[4]
            {
                -Vector3.forward,
                -Vector3.forward,
                -Vector3.forward,
                -Vector3.forward
            };

            qaud.uv = new Vector2[4]
            {
                new Vector2(0f, 1f),    //left up
                new Vector2(1f, 1f),    //right up
                new Vector2(0f, 0f),    //left down
                new Vector2(1f, 0f)     //right down
            };

            return qaud;
        }
    }
}

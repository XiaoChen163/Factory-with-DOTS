using UnityEngine;

// Only this tool-owned child is replaced on regeneration. Parameters are Unity local.
[DisallowMultipleComponent]
public sealed class FactoryBuildingCollisionAuthoring : MonoBehaviour
{
    public FactoryColliderProxy[] shapes = System.Array.Empty<FactoryColliderProxy>();

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        foreach (var s in shapes)
        {
            Gizmos.matrix = transform.localToWorldMatrix * Matrix4x4.TRS(s.position, s.rotation, Vector3.one);
            if (s.type == "Box") Gizmos.DrawWireCube(Vector3.zero, s.size);
            else if (s.type == "Sphere") Gizmos.DrawWireSphere(Vector3.zero, s.radius);
            else
            {
                // Rings describe the actual Y-axis cylinder/capsule envelope.
                float h = s.type == "Capsule" ? s.height * .5f - s.radius : s.height * .5f;
                for (int i = 0; i < 32; i++)
                {
                    float a = i * Mathf.PI / 16, b = (i + 1) * Mathf.PI / 16;
                    var p = new Vector3(Mathf.Cos(a) * s.radius, 0, Mathf.Sin(a) * s.radius);
                    var q = new Vector3(Mathf.Cos(b) * s.radius, 0, Mathf.Sin(b) * s.radius);
                    Gizmos.DrawLine(p + Vector3.up * h, q + Vector3.up * h);
                    Gizmos.DrawLine(p - Vector3.up * h, q - Vector3.up * h);
                    if (i % 8 == 0) Gizmos.DrawLine(p - Vector3.up * h, p + Vector3.up * h);
                }
                if (s.type == "Capsule") { Gizmos.DrawWireSphere(Vector3.up * h, s.radius); Gizmos.DrawWireSphere(Vector3.down * h, s.radius); }
            }
        }
        Gizmos.matrix = Matrix4x4.identity;
    }
}

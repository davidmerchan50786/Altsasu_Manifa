using UnityEngine;

public class Scroll_Track : MonoBehaviour
{
    [SerializeField]
    private float scrollSpeed = 0.05f;

    private float offset = 0.0f;
    private Material m;

    void Start()
    {
        m = GetComponent<Renderer>().material;
    }

    void Update()
    {
        offset = (offset + Time.deltaTime * scrollSpeed) % 1f;
        m.mainTextureOffset = new Vector2(offset, 0f);
    }
}

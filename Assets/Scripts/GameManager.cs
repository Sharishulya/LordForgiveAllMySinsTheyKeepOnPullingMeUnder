using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(100)] // выполняется после камеры, чтобы стрелки не дёргались
public class GameManager : MonoBehaviour
{
    [Header("Поле")]
    public Transform floor;            // сюда перетащить floor
    public int boardWidth = 6;         // клеток по X
    public int boardDepth = 6;         // клеток по Z
    public int maxHeight = 10;         // выше этой высоты — конец игры
    public float cellSize = 1f;        // размер одной клетки в юнитах

    [Header("Блок из Blender")]
    public Mesh blockMesh;
    [Tooltip("Порядок: i, j, l, o, s, t, z")]
    public Material[] shapeMaterials;

    [Header("Стрелки поворота")]
    public float arrowScreenSize = 0.06f;  // размер стрелки на экране
    public float arrowDistance = 1.2f;     // насколько далеко от фигуры (в клетках)
    public Color arrowColor = new Color(1f, 0.85f, 0.1f);

    // Фигуры в том же порядке, что и материалы: i, j, l, o, s, t, z
    // Первая клетка/центр (0,0,0) — центр вращения
    static Vector3Int V(int x, int y, int z) { return new Vector3Int(x, y, z); }
    static readonly Vector3Int[][] Shapes =
    {
        new[] { V(-1,0,0), V(0,0,0), V(1,0,0), V(2,0,0) },   // I
        new[] { V(-1,0,0), V(0,0,0), V(1,0,0), V(-1,0,1) },  // J
        new[] { V(-1,0,0), V(0,0,0), V(1,0,0), V(1,0,1) },   // L
        new[] { V(0,0,0), V(1,0,0), V(0,0,1), V(1,0,1) },    // O
        new[] { V(-1,0,0), V(0,0,0), V(0,0,1), V(1,0,1) },   // S
        new[] { V(-1,0,0), V(0,0,0), V(1,0,0), V(0,0,1) },   // T
        new[] { V(-1,0,1), V(0,0,1), V(0,0,0), V(1,0,0) },   // Z
    };

    Transform[,,] grid;                       // [x, y, z]
    Transform stackRoot, curRoot, ghostRoot;
    readonly List<Transform> curCubes = new List<Transform>();
    readonly List<Transform> ghostCubes = new List<Transform>();
    readonly Dictionary<Vector3Int, Transform> arrows = new Dictionary<Vector3Int, Transform>();

    List<Vector3Int> curCells;
    Vector3Int curPos;
    int curIndex, nextIndex;
    bool gameOver;

    Vector3 origin, boardCenter;
    float topY;
    Material ghostMat, arrowMat;
    Camera mainCam;
    OrbitCamera orbit;

    // ---------------------------------------------------------------- старт

    void Start()
    {
        if (floor == null)
        {
            GameObject f = GameObject.Find("floor");
            if (f != null) floor = f.transform;
        }

        mainCam = Camera.main;
        orbit = mainCam.GetComponent<OrbitCamera>();

        CalcBoardOrigin();
        grid = new Transform[boardWidth, maxHeight + 6, boardDepth];

        Shader flat = Shader.Find("Sprites/Default");
        ghostMat = new Material(flat) { color = new Color(1f, 1f, 1f, 0.25f) };
        arrowMat = new Material(flat) { color = arrowColor };

        stackRoot = new GameObject("Stack").transform;
        curRoot = new GameObject("CurrentPiece").transform;
        ghostRoot = new GameObject("Ghost").transform;

        for (int i = 0; i < 4; i++) ghostCubes.Add(CreateCube(ghostRoot, 0, true));

        BuildArrows();

        nextIndex = Random.Range(0, Shapes.Length);
        SpawnNext();
    }

    // Определяем, где на floor находится клетка (0,0,0), по размерам floor
    void CalcBoardOrigin()
    {
        Bounds b = new Bounds(floor != null ? floor.position : Vector3.zero, Vector3.zero);
        if (floor != null)
        {
            Renderer[] rs = floor.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                b = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            }
        }
        boardCenter = b.center;
        topY = b.max.y;
        origin = new Vector3(
            b.center.x - (boardWidth - 1) * 0.5f * cellSize,
            topY + cellSize * 0.5f,
            b.center.z - (boardDepth - 1) * 0.5f * cellSize);
    }

    Vector3 CellToWorld(Vector3 cell) { return origin + cell * cellSize; }

    // ---------------------------------------------------------------- цикл

    void Update()
    {
        if (gameOver)
        {
            if (Input.GetKeyDown(KeyCode.R))
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }

        if (Input.GetKeyDown(KeyCode.Space)) { PlacePiece(); return; }

        Vector3Int move = Vector3Int.zero;
        if (Input.GetKeyDown(KeyCode.W)) move = CamForward();
        else if (Input.GetKeyDown(KeyCode.S)) move = -CamForward();
        else if (Input.GetKeyDown(KeyCode.D)) move = CamRight();
        else if (Input.GetKeyDown(KeyCode.A)) move = -CamRight();
        if (move != Vector3Int.zero) TryMove(move);

        if (Input.GetMouseButtonDown(0)) HandleClick();
    }

    void LateUpdate()
    {
        UpdateArrows();
    }

    // ---------------------------------------------------------------- управление

    // WASD работает относительно камеры (округляется до ближайшей оси поля)
    Vector3Int SnapAxis(Vector3 v)
    {
        v.y = 0;
        if (v.sqrMagnitude < 0.0001f) return Vector3Int.forward;
        if (Mathf.Abs(v.x) > Mathf.Abs(v.z)) return new Vector3Int(v.x > 0 ? 1 : -1, 0, 0);
        return new Vector3Int(0, 0, v.z > 0 ? 1 : -1);
    }

    Vector3Int CamForward()
    {
        Vector3 f = mainCam.transform.forward;
        f.y = 0;
        if (f.sqrMagnitude < 0.05f) f = mainCam.transform.up; // камера смотрит почти строго вниз
        return SnapAxis(f);
    }

    Vector3Int CamRight() { return SnapAxis(mainCam.transform.right); }

    void TryMove(Vector3Int dir)
    {
        Vector3Int np = curPos + dir;
        if (!CanPlace(curCells, np)) return;
        curPos = np;
        Refresh();
    }

    void HandleClick()
    {
        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 500f);
        foreach (var kv in arrows)
            foreach (var hit in hits)
                if (hit.transform == kv.Value) { Rotate(kv.Key); return; }
    }

    // Фигура "заваливается" в сторону стрелки
    void Rotate(Vector3Int dir)
    {
        Vector3 axis = Vector3.Cross(Vector3.up, (Vector3)dir);
        Quaternion q = Quaternion.AngleAxis(90f, axis);

        var newCells = new List<Vector3Int>();
        foreach (var c in curCells) newCells.Add(Vector3Int.RoundToInt(q * (Vector3)c));

        // если вылезли за границы поля — сдвигаем внутрь
        Vector3Int p = curPos;
        int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
        foreach (var c in newCells)
        {
            minX = Mathf.Min(minX, p.x + c.x); maxX = Mathf.Max(maxX, p.x + c.x);
            minZ = Mathf.Min(minZ, p.z + c.z); maxZ = Mathf.Max(maxZ, p.z + c.z);
        }
        if (minX < 0) p.x -= minX;
        if (maxX > boardWidth - 1) p.x -= maxX - (boardWidth - 1);
        if (minZ < 0) p.z -= minZ;
        if (maxZ > boardDepth - 1) p.z -= maxZ - (boardDepth - 1);

        if (!CanPlace(newCells, p)) return;
        curCells = newCells;
        curPos = p;
        Refresh();
    }

    // ---------------------------------------------------------------- логика поля

    bool CanPlace(List<Vector3Int> cells, Vector3Int pos)
    {
        foreach (var c in cells)
        {
            Vector3Int p = pos + c;
            if (p.x < 0 || p.x >= boardWidth || p.z < 0 || p.z >= boardDepth || p.y < 0) return false;
            if (p.y < grid.GetLength(1) && grid[p.x, p.y, p.z] != null) return false;
        }
        return true;
    }

    int ComputeLandingY()
    {
        int y = curPos.y;
        while (CanPlace(curCells, new Vector3Int(curPos.x, y - 1, curPos.z))) y--;
        return y;
    }

    int StackTop()
    {
        for (int y = grid.GetLength(1) - 1; y >= 0; y--)
            for (int x = 0; x < boardWidth; x++)
                for (int z = 0; z < boardDepth; z++)
                    if (grid[x, y, z] != null) return y + 1;
        return 0;
    }

    void PlacePiece()
    {
        Vector3Int pos = new Vector3Int(curPos.x, ComputeLandingY(), curPos.z);

        foreach (var c in curCells)
            if (pos.y + c.y >= maxHeight) { GameOver(); return; }

        for (int i = 0; i < curCells.Count; i++)
        {
            Vector3Int cell = pos + curCells[i];
            Transform cube = curCubes[i];
            cube.SetParent(stackRoot, true);
            cube.position = CellToWorld(cell);
            cube.rotation = Quaternion.identity;
            grid[cell.x, cell.y, cell.z] = cube;
        }

        // TODO следующий этап: проверка и удаление заполненных плоскостей, очки
        SpawnNext();
    }

    void GameOver()
    {
        gameOver = true;
        ghostRoot.gameObject.SetActive(false);
        foreach (var a in arrows.Values) a.gameObject.SetActive(false);
        Debug.Log("GAME OVER (R — перезапуск)");
    }

    // ---------------------------------------------------------------- появление фигур

    void SpawnNext()
    {
        curIndex = nextIndex;
        nextIndex = Random.Range(0, Shapes.Length);   // пригодится для показа "следующей фигуры"

        curCells = new List<Vector3Int>(Shapes[curIndex]);
        curPos = new Vector3Int(boardWidth / 2 - 1, StackTop() + 4, boardDepth / 2 - 1);

        curCubes.Clear();
        foreach (Transform child in curRoot) Destroy(child.gameObject);
        for (int i = 0; i < curCells.Count; i++) curCubes.Add(CreateCube(curRoot, curIndex, false));

        Refresh();
        FocusCamera();
    }

    void Refresh()
    {
        curRoot.position = CellToWorld(curPos);
        for (int i = 0; i < curCells.Count; i++)
        {
            curCubes[i].localPosition = (Vector3)curCells[i] * cellSize;
            ghostCubes[i].localPosition = (Vector3)curCells[i] * cellSize;
        }
        ghostRoot.position = CellToWorld(new Vector3(curPos.x, ComputeLandingY(), curPos.z));
    }

    // камера смотрит на центр поля и поднимается вместе с башней
    void FocusCamera()
    {
        if (orbit == null || floor == null) return;
        float focusY = topY + Mathf.Max(1.5f, StackTop() * cellSize * 0.5f + 1.5f);
        Vector3 pivot = new Vector3(boardCenter.x, focusY, boardCenter.z);
        orbit.targetOffset = pivot - floor.position;
    }

    // ---------------------------------------------------------------- блоки

    Transform CreateCube(Transform parent, int shapeIndex, bool ghost)
    {
        var go = new GameObject("Block");
        go.transform.SetParent(parent, false);

        GameObject vis;
        if (blockMesh != null)
        {
            vis = new GameObject("Mesh");
            vis.AddComponent<MeshFilter>().sharedMesh = blockMesh;
            vis.AddComponent<MeshRenderer>();
            vis.transform.SetParent(go.transform, false);

            // авто-размер и центровка, чтобы блок ровно совпадал с клеткой
            Bounds mb = blockMesh.bounds;
            float s = cellSize / Mathf.Max(mb.size.x, 0.0001f);
            vis.transform.localScale = Vector3.one * s;
            vis.transform.localPosition = -mb.center * s;
        }
        else
        {
            vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(vis.GetComponent<Collider>());
            vis.transform.SetParent(go.transform, false);
            vis.transform.localScale = Vector3.one * cellSize;
        }

        Renderer r = vis.GetComponent<Renderer>();
        if (ghost) r.sharedMaterial = ghostMat;
        else if (shapeMaterials != null && shapeIndex < shapeMaterials.Length && shapeMaterials[shapeIndex] != null)
            r.sharedMaterial = shapeMaterials[shapeIndex];

        return go.transform;
    }

    // ---------------------------------------------------------------- стрелки (2D, лицом к камере)

    void BuildArrows()
    {
        Mesh mesh = BuildArrowMesh();
        Vector3Int[] dirs =
        {
            new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
        };
        foreach (var d in dirs)
        {
            var go = new GameObject("Arrow");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = arrowMat;
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(1.3f, 0.8f, 0.3f);
            box.center = new Vector3(0.05f, 0f, 0f);
            arrows[d] = go.transform;
        }
    }

    // плоская стрелка, смотрит вдоль +X
    Mesh BuildArrowMesh()
    {
        var m = new Mesh();
        m.vertices = new[]
        {
            new Vector3(-0.5f, -0.12f, 0f), new Vector3(0.1f, -0.12f, 0f),
            new Vector3(0.1f, 0.12f, 0f),   new Vector3(-0.5f, 0.12f, 0f),
            new Vector3(0.1f, -0.35f, 0f),  new Vector3(0.55f, 0f, 0f), new Vector3(0.1f, 0.35f, 0f)
        };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 6, 5 };
        m.RecalculateBounds();
        return m;
    }

    void UpdateArrows()
    {
        if (gameOver || curCells == null) return;

        Transform cam = mainCam.transform;

        Vector3 avg = Vector3.zero;
        foreach (var c in curCells) avg += (Vector3)c;
        avg /= curCells.Count;
        Vector3 center = CellToWorld((Vector3)curPos + avg);

        Quaternion faceCamera = Quaternion.LookRotation(cam.forward, cam.up);

        foreach (var kv in arrows)
        {
            Vector3 d = (Vector3)kv.Key;

            float ext = 0f;
            foreach (var c in curCells) ext = Mathf.Max(ext, Vector3.Dot((Vector3)c - avg, d));
            Vector3 pos = center + d * (ext + 0.5f + arrowDistance) * cellSize;

            // куда направление d выглядит на экране
            float sx = Vector3.Dot(d, cam.right);
            float sy = Vector3.Dot(d, cam.up);
            float angle = Mathf.Atan2(sy, sx) * Mathf.Rad2Deg;

            Transform a = kv.Value;
            a.position = pos;
            a.rotation = faceCamera * Quaternion.Euler(0f, 0f, angle);
            a.localScale = Vector3.one * Vector3.Distance(cam.position, pos) * arrowScreenSize;
        }
    }
}
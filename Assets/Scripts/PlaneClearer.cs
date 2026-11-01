using System;
using UnityEngine;

// Отвечает только за одно: найти полностью собранные плоскости,
// удалить их, опустить всё, что выше, и посчитать очки.
public class PlaneClearer : MonoBehaviour
{
    public int pointsPerPlane = 100;

    public int Score { get; private set; }

    // Пригодится в следующем этапе для вывода очков на экран
    public event Action<int> ScoreChanged;

    // Возвращает, сколько плоскостей было убрано за один раз
    public int ClearPlanes(Transform[,,] grid, int width, int depth, Func<int, int, int, Vector3> cellToWorld)
    {
        int height = grid.GetLength(1);
        int cleared = 0;
        int write = 0;   // на какой уровень опускаем следующий неполный слой

        for (int y = 0; y < height; y++)
        {
            if (IsPlaneFull(grid, y, width, depth))
            {
                // плоскость собрана — удаляем все блоки этого уровня
                cleared++;
                for (int x = 0; x < width; x++)
                    for (int z = 0; z < depth; z++)
                    {
                        Destroy(grid[x, y, z].gameObject);
                        grid[x, y, z] = null;
                    }
            }
            else
            {
                // обычный слой — переносим вниз на освободившееся место
                if (write != y)
                {
                    for (int x = 0; x < width; x++)
                        for (int z = 0; z < depth; z++)
                        {
                            Transform t = grid[x, y, z];
                            grid[x, write, z] = t;
                            grid[x, y, z] = null;
                            if (t != null) t.position = cellToWorld(x, write, z);
                        }
                }
                write++;
            }
        }

        if (cleared > 0)
        {
            Score += CalcScore(cleared);
            ScoreChanged?.Invoke(Score);
        }
        return cleared;
    }

    bool IsPlaneFull(Transform[,,] grid, int y, int width, int depth)
    {
        for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                if (grid[x, y, z] == null) return false;
        return true;
    }

    // 1 плоскость: 100*1; 2: (100+100)*2; 3: (100+100+100)*3; 4: (100*4)*4
    int CalcScore(int planes)
    {
        return pointsPerPlane * planes * planes;
    }
}
using UnityEngine;
using System.Collections.Generic;

public class GridCreator3D : MonoBehaviour
{
    public GameObject cellPrefab; // Hücre için kullanılacak prefab
    public int gridSize = 5;     // Grid'in boyutu
    private Dictionary<Vector3, Transform> gridCells = new Dictionary<Vector3, Transform>();
    private Dictionary<Vector3, GameObject> placedObjects = new Dictionary<Vector3, GameObject>(); // Obje takibi

    void Start()
    {
        Create3DGrid();
    }

    private void Create3DGrid()
    {
        float cellSize = 1f; // Hücre boyutu

        for (int x = 0; x < gridSize; x++)
        {
            for (int y = 0; y < gridSize; y++)
            {
                for (int z = 0; z < gridSize; z++)
                {
                    Vector3 position = transform.position + new Vector3(x * cellSize, y * cellSize, z * cellSize);
                    GameObject cell = Instantiate(cellPrefab, position, Quaternion.identity, transform);
                    cell.name = $"Cell ({x}, {y}, {z})";

                    gridCells[position] = cell.transform; // Grid'e ekleme
                }
            }
        }
        Debug.Log("3D Grid oluşturuldu!");
    }

    public bool PlaceObjectInCell(Vector3 position, GameObject obj)
    {
        if (gridCells.ContainsKey(position) && !placedObjects.ContainsKey(position)) // Eğer hücre boşsa
        {
            obj.transform.position = position;
            obj.transform.SetParent(gridCells[position]); // Obje hücreye bağlanıyor
            placedObjects[position] = obj; // Objeyi takip et
            Debug.Log($"Obje {position} konumundaki hücreye yerleştirildi.");
            return true;
        }
        Debug.LogWarning($"Hücre {position} zaten dolu!");
        return false;
    }

    public bool RemoveObjectFromCell(Vector3 position)
    {
        if (placedObjects.ContainsKey(position)) // Eğer o hücrede obje varsa
        {
            GameObject obj = placedObjects[position];
            placedObjects.Remove(position); // Takip listesinden çıkar
            obj.transform.SetParent(null); // Parent'ı kaldır
            Debug.Log($"Obje {position} konumundaki hücreden çıkarıldı.");
            return true;
        }
        Debug.LogWarning($"Hücre {position} zaten boş!");
        return false;
    }
}

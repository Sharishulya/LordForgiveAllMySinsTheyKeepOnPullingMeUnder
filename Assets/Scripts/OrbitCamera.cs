using UnityEngine;

public class OrbitCamera : MonoBehaviour
{
    [Header("Цель")]
    public Transform target;                                // сюда перетащить floor

    public Vector3 targetOffset = new Vector3(0f, 1f, 0f);  // точка, на которую смотрит камера, относительно floor

    [Header("Начальное положение")]
    public float distance = 10f;   // <<< РАССТОЯНИЕ КАМЕРЫ ОТ ЦЕНТРА FLOOR
    public float yaw = 47f;        // поворот вокруг поля (влево-вправо)
    public float pitch = 22f;      // наклон (угол сверху вниз)

    [Header("Управление")]
    private float rotateSpeed = 3f;
    private float zoomSpeed = 2f;
    private float minDistance = 5f;
    private float maxDistance = 40f;
    private float minPitch = 5f;
    private float maxPitch = 89f;

    [Header("Плавность (меньше = плавнее, больше = резче)")]
    private float rotateSmoothing = 8f;
    private float zoomSmoothing = 8f;
    private float followSmoothing = 5f;

    private float targetYaw, targetPitch, targetDistance;
    Vector3 smoothedPivot;

    void Start()
    {
        if (target == null)
        {
            GameObject f = GameObject.Find("floor");
            if (f != null) target = f.transform;
        }

        targetYaw = yaw;
        targetPitch = pitch;
        targetDistance = distance;
        smoothedPivot = GetPivot();
    }

    Vector3 GetPivot()
    {
        return (target != null ? target.position : Vector3.zero) + targetOffset;
    }

    void LateUpdate()
    {
        // зажатая ПКМ — крутим камеру (меняем только "цель", сама камера догоняет плавно)
        if (Input.GetMouseButton(1))
        {
            targetYaw += Input.GetAxis("Mouse X") * rotateSpeed;
            targetPitch -= Input.GetAxis("Mouse Y") * rotateSpeed;
            targetPitch = Mathf.Clamp(targetPitch, minPitch, maxPitch);
        }

        // колесо мыши — приближение
        targetDistance = Mathf.Clamp(targetDistance - Input.mouseScrollDelta.y * zoomSpeed, minDistance, maxDistance);

        // плавное приближение к целевым значениям (не зависит от FPS)
        float dt = Time.deltaTime;
        yaw = Mathf.Lerp(yaw, targetYaw, 1f - Mathf.Exp(-rotateSmoothing * dt));
        pitch = Mathf.Lerp(pitch, targetPitch, 1f - Mathf.Exp(-rotateSmoothing * dt));
        distance = Mathf.Lerp(distance, targetDistance, 1f - Mathf.Exp(-zoomSmoothing * dt));
        smoothedPivot = Vector3.Lerp(smoothedPivot, GetPivot(), 1f - Mathf.Exp(-followSmoothing * dt));

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        transform.position = smoothedPivot - rot * Vector3.forward * distance;
        transform.rotation = rot;
    }
}

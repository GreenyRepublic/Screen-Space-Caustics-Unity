using System.Collections;
using System.Collections.Generic;
using UnityEngine;


/* - CameraControl -
 * Provides mouse and keyboard controls for the game camera
 */

public class CameraControl : MonoBehaviour
{
    //Transform speed coefficients (percentages)
    [Range(0.0f, 1.0f)]
    public float panSpeed = 0.4f;
    [Range(0.0f, 1.0f)]
    public float rotateSpeed = 1.0f;
    [Range(0.0f, 1.0f)]
    public float zoomSpeed = 1.0f;

    //Starting values
    public float initialRotation = 0.0f; //Rotation around world-space Y-axis (up direction) in degrees.
    public float initialZoom = 15.0f;
    public float initialTilt = 45.0f; //Rotation around gimball's local X-axis in degrees.

    //Transform limits
    [Range(0.0f, 90.0f)]
    public float maxTilt = 90.0f;
    [Range(0.0f, 90.0f)]
    public float minTilt = 0.0f;
    [Range(1.0f, 500.0f)]
    public float minZoom = 1.0f;
    [Range(1.0f, 500.0f)]
    public float maxZoom = 100.0f;

    private Transform cameraTransform;
    private Camera cameraObject;

    static private float s_cameraLockCooldown = 0.0f;
    // ^ Prevents accidental selection of game entities when using the camera mouse controls
    
    static public bool WasCameraMoved()
    {
        return s_cameraLockCooldown > 0.0f;
    }
    // Start is called before the first frame update
    void Start()
    {
        cameraTransform = gameObject.transform.GetChild(0);
        cameraObject = cameraTransform.GetComponent<Camera>();
    
        cameraTransform.localPosition = new Vector3(0.0f, 0.0f, -initialZoom); //zoom

        transform.Rotate(Vector3.up, initialRotation); //rotation
        transform.Rotate(Vector3.right, initialTilt); //tilt
        transform.position = new Vector3(0.0f, 0.0f, 0.0f);
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 deltaPos = new Vector3();
        float deltaRot = 0.0f;
        float deltaZoom = 0.0f;
        float deltaTilt = 0.0f;
        
        //Mouse pan
        if (Input.GetKey(KeyCode.Mouse2))
        {
            Vector2 mouseDelta = new Vector2(Input.GetAxis("Mouse Pan x"), Input.GetAxis("Mouse Pan y")) * panSpeed;
            deltaPos += new Vector3(transform.forward.x, 0.0f, transform.forward.z) * mouseDelta.y
                + new Vector3(transform.right.x, 0.0f, transform.right.z) * mouseDelta.x;
        }       

        //Mouse Rotate and Tilt
        if (Input.GetKey(KeyCode.Mouse1))
        {
            deltaTilt += Input.GetAxis("Mouse Tilt");
            deltaRot += Input.GetAxis("Mouse Rotate") * rotateSpeed;
        }

        //Mouse Zoom
        deltaZoom = Input.mouseScrollDelta.y * zoomSpeed;
        
        // TODO: If zooming, move gimball towards point under cursor
        /*if (Mathf.Abs(deltaZoom) > 0.0f)
        {
            RaycastHit hit;
            Ray ray = cameraObject.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out hit))
            {
                Vector3 dir = -(hit.point - transform.position).normalized;
                cameraTransform.Translate(dir * deltaZoom);
            }
        }*/

        //Pan Camera
        deltaPos += new Vector3(transform.forward.x, 0.0f, transform.forward.z) * Input.GetAxis("Camera Vertical")
            + new Vector3(transform.right.x, 0.0f, transform.right.z) * Input.GetAxis("Camera Horizontal");

        //Rotate Camera
        deltaRot += Input.GetAxis("Camera Rotate") * rotateSpeed;

        //Zoom Camera
        deltaZoom += Input.GetAxis("Camera Zoom") * -zoomSpeed;
        cameraTransform.localPosition = new Vector3(
            cameraTransform.localPosition.x,
            cameraTransform.localPosition.y,
            Mathf.Clamp(cameraTransform.localPosition.z + deltaZoom, -maxZoom, -minZoom));
         

        //Tilt camera
        deltaTilt += Input.GetAxis("Camera Tilt");
        deltaTilt = (deltaTilt > 0.0)? Mathf.Min(maxTilt - transform.localEulerAngles.x, deltaTilt) :
                                        Mathf.Max(minTilt - transform.localEulerAngles.x, deltaTilt);

        transform.RotateAround(transform.position, transform.right, deltaTilt);
        transform.RotateAround(transform.position, Vector3.up, deltaRot);
        transform.position += deltaPos.normalized * panSpeed;

        if ((deltaRot != 0.0f || deltaTilt != 0.0f) || (Input.GetKey(KeyCode.Mouse1) && s_cameraLockCooldown > 0.0f))
        {
            s_cameraLockCooldown = 0.2f;
        }
        else
        {
            s_cameraLockCooldown = Mathf.Max(s_cameraLockCooldown - Time.deltaTime, 0.0f);
        }
    }

    public void SetPosition( Vector3 pos )
    {
        cameraTransform.position = pos;
    }
}

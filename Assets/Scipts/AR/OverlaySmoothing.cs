using UnityEngine;

/* Steadies a card overlay. Vuforia's pose for a card wobbles a little every frame, and an overlay
 fixed to it shakes with it. This keeps the overlay where its card target puts it, but filtered:
 small wobbles are smoothed out, while a real move (a card picked up, slid or rested) snaps
 straight there. Filtered relative to the camera, since Vuforia moves the camera as well as the
 cards. Added to each overlay by VuforiaCardOverlayManager; before CardOverlayUI's LateUpdate,
 which turns the detail panel after it. */
[DefaultExecutionOrder(-50)]
public class OverlaySmoothing : MonoBehaviour
{
    [Tooltip("How quickly the overlay catches up with its card; higher follows faster but shakes more.")]
    public float sharpness = 12f;
    [Tooltip("A move further than this (metres, about a third of a card) is the card moving, not wobble: it snaps there.")]
    public float snapDistance = 0.02f;
    [Tooltip("Likewise a turn of more than this many degrees, e.g. a card being rested.")]
    public float snapAngle = 20f;

    Transform mTarget;
    Vector3 mLocalPosition; // where on the card the overlay sits
    Quaternion mLocalRotation;
    Vector3 mPosition; // smoothed, in the camera's space
    Quaternion mRotation;
    bool mHasPose;

    void Start()
    {
        mTarget = transform.parent;
        mLocalPosition = transform.localPosition;
        mLocalRotation = transform.localRotation;
    }

    void OnEnable() => mHasPose = false; // shown again after being lost: start from where the card is

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (mTarget == null || cam == null) return;

        Transform view = cam.transform;
        Vector3 position = view.InverseTransformPoint(mTarget.TransformPoint(mLocalPosition));
        Quaternion rotation = Quaternion.Inverse(view.rotation) * (mTarget.rotation * mLocalRotation);

        if (!mHasPose || Vector3.Distance(position, mPosition) > snapDistance || Quaternion.Angle(rotation, mRotation) > snapAngle)
        {
            mPosition = position;
            mRotation = rotation;
            mHasPose = true;
        }
        else
        {
            float t = 1f - Mathf.Exp(-sharpness * Time.deltaTime);
            mPosition = Vector3.Lerp(mPosition, position, t);
            mRotation = Quaternion.Slerp(mRotation, rotation, t);
        }

        transform.SetPositionAndRotation(view.TransformPoint(mPosition), view.rotation * mRotation);
    }
}

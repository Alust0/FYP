using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

/* Settings' rotate button: the finished picture of the camera area, turned a quarter turn
 anticlockwise (online, the opponent's side on the left and yours on the right). Off, nothing here
 runs. On, the AR camera is also drawn into a texture each frame (as SplitScreenView sends it), and
 a UI camera draws that picture, then the canvases over the camera view (Follow), into a
 screen-sized texture, so everything lands where it normally would. Only that camera touches the
 texture: URP cameras always clear what they draw into, so it can't share the screen. The texture's
 view boxes are shown turned, over a black backing that hides the AR camera's usual on-screen
 picture. The sidebar and its menus stay upright. Clicks on the turned picture are turned back and
 passed on (ClickForwarder), so a card's "i" and the trigger pick circles still work; calibration
 reads the mouse itself, so calibrate with it off. Added by UISetup. */
public class RotatedView : MonoBehaviour
{
    const int UiLayer = 5; // Unity's built-in "UI" layer: all the UI camera draws

    static readonly List<Canvas> sCanvases = new List<Canvas>();
    static RotatedView sInstance;

    // Each followed canvas as it was, put back when the view turns back
    readonly Dictionary<Canvas, (int layer, int order)> mOriginal = new Dictionary<Canvas, (int, int)>();
    readonly List<Transform> mChildren = new List<Transform>();

    Camera mCamera; // the AR camera
    BoardLayout mBoard; // its zone labels turn too
    Camera mUiCamera; // draws the picture and the followed canvases into mTexture
    RenderTexture mPicture; // the AR camera's view box alone
    RenderTexture mTexture; // the whole screen's worth, picture and canvases
    Canvas mPictureCanvas; // shows mPicture to the UI camera, under the followed canvases
    RawImage mPictureImage;
    GameObject mDisplayCanvas;
    RawImage mDisplay;
    RectTransform mBacking;
    bool mTurned;

    // A canvas drawn over the camera view, e.g. the hand frames, turning with it
    public static void Follow(Canvas canvas)
    {
        if (!sCanvases.Contains(canvas)) sCanvases.Add(canvas);
        if (sInstance != null && sInstance.mTurned) sInstance.Attach(canvas);
    }

    // A label filling its box (stretched, inset by its offsets) on a followed canvas
    class Upright
    {
        public RectTransform label;
        public Vector2 offsetMin, offsetMax;
    }
    static readonly List<Upright> sUpright = new List<Upright>();

    /* Keeps a label reading upright while the view is turned: it's laid a quarter turn clockwise
     across its box instead, which the turn puts upright again */
    public static void KeepUpright(RectTransform label) =>
        sUpright.Add(new Upright { label = label, offsetMin = label.offsetMin, offsetMax = label.offsetMax });

    // The camera area is shown turned, e.g. for the card detail panels to turn the other way
    public static bool Turned => sInstance != null && sInstance.mTurned;

    void Awake() => sInstance = this;

    void Start()
    {
        mDisplayCanvas = NewCanvas("RotatedViewCanvas", -200); // under the HUD
        mBacking = NewImage<Image>("Backing", mDisplayCanvas.transform).rectTransform;
        mBacking.GetComponent<Image>().color = Color.black;
        mBacking.pivot = Vector2.zero;
        mDisplay = NewImage<RawImage>("View", mDisplayCanvas.transform);
        mDisplay.rectTransform.localEulerAngles = new Vector3(0f, 0f, 90f); // anticlockwise
        mDisplay.raycastTarget = true; // catches the camera area's clicks to pass them on
        mDisplay.gameObject.AddComponent<ClickForwarder>();
        mDisplayCanvas.AddComponent<GraphicRaycaster>(); // without one the canvas gets no clicks at all
        mDisplayCanvas.SetActive(false);

        GameObject pictureObj = NewCanvas("RotatedViewPicture", -1000); // under every followed canvas
        mPictureCanvas = pictureObj.GetComponent<Canvas>();
        mPictureImage = NewImage<RawImage>("Picture", pictureObj.transform);
        mPictureImage.rectTransform.pivot = Vector2.zero;
        SetLayer(pictureObj.transform, UiLayer);
        pictureObj.SetActive(false);
    }

    static GameObject NewCanvas(string name, int order)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        Canvas canvas = obj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order; // no scaler: one unit is one screen pixel
        return obj;
    }

    static T NewImage<T>(string name, Transform parent) where T : Graphic
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        T image = obj.AddComponent<T>();
        image.raycastTarget = false;
        image.rectTransform.anchorMin = image.rectTransform.anchorMax = Vector2.zero;
        return image;
    }

    void OnDestroy()
    {
        if (mTurned) TurnBack();
        if (sInstance == this) sInstance = null;
    }

    void LateUpdate()
    {
        if (mCamera == null) mCamera = Camera.main;
        bool wanted = GameSettings.RotateView && mCamera != null;
        if (wanted != mTurned)
        {
            if (wanted) Turn();
            else TurnBack();
        }
        if (!mTurned) return;

        sCanvases.RemoveAll(c => c == null);
        foreach (Canvas canvas in sCanvases) Attach(canvas); // e.g. one made since
        if (mBoard == null) mBoard = FindFirstObjectByType<BoardLayout>();
        if (mBoard != null) mBoard.SetLabelAngle(-90); // the view turns +90
        TurnLabels();
        UpdateTextures();
        RenderPicture();
        Place();
    }

    // Every frame, as a label's box may have moved or resized
    static void TurnLabels()
    {
        sUpright.RemoveAll(u => u.label == null);
        foreach (Upright u in sUpright)
        {
            RectTransform rt = u.label;
            Vector2 box = ((RectTransform)rt.parent).rect.size;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = (u.offsetMin + u.offsetMax) * 0.5f;
            rt.sizeDelta = new Vector2(box.y + u.offsetMax.y - u.offsetMin.y, box.x + u.offsetMax.x - u.offsetMin.x);
            rt.localEulerAngles = new Vector3(0f, 0f, -90f);
        }
    }

    static void UnturnLabels()
    {
        sUpright.RemoveAll(u => u.label == null);
        foreach (Upright u in sUpright)
        {
            RectTransform rt = u.label;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = u.offsetMin;
            rt.offsetMax = u.offsetMax;
            rt.localEulerAngles = Vector3.zero;
        }
    }

    void Turn()
    {
        mTurned = true;
        mUiCamera = new GameObject("RotatedViewCamera").AddComponent<Camera>();
        mUiCamera.clearFlags = CameraClearFlags.SolidColor;
        mUiCamera.backgroundColor = Color.black;
        mUiCamera.cullingMask = 1 << UiLayer;
        mUiCamera.nearClipPlane = 0.1f;
        mUiCamera.farClipPlane = 10f;
        UpdateTextures();

        mPictureCanvas.gameObject.SetActive(true);
        AttachCanvas(mPictureCanvas);
        mDisplayCanvas.SetActive(true);
    }

    void TurnBack()
    {
        mTurned = false;
        foreach (Canvas canvas in sCanvases) Detach(canvas);
        UnturnLabels();
        if (mBoard != null) mBoard.SetLabelAngle(0);
        if (mPictureCanvas != null)
        {
            mPictureCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            mPictureCanvas.gameObject.SetActive(false);
        }
        if (mDisplayCanvas != null) mDisplayCanvas.SetActive(false);
        if (mUiCamera != null) Destroy(mUiCamera.gameObject);
        mUiCamera = null;
        Release(ref mTexture);
        Release(ref mPicture);
    }

    static void Release(ref RenderTexture texture)
    {
        if (texture != null) texture.Release();
        texture = null;
    }

    // Screen-sized, so every canvas lays out as on the screen; the picture the AR camera's box's size
    void UpdateTextures()
    {
        if (mTexture == null || mTexture.width != Screen.width || mTexture.height != Screen.height)
        {
            Release(ref mTexture);
            mTexture = new RenderTexture(Screen.width, Screen.height, 24);
            mUiCamera.targetTexture = mTexture;
            mDisplay.texture = mTexture;
        }

        int width = Mathf.Max(1, mCamera.pixelWidth), height = Mathf.Max(1, mCamera.pixelHeight);
        if (mPicture == null || mPicture.width != width || mPicture.height != height)
        {
            Release(ref mPicture);
            mPicture = new RenderTexture(width, height, 24);
            mPictureImage.texture = mPicture;
        }
    }

    // The AR camera's view box alone, the way SplitScreenView captures it to send
    void RenderPicture()
    {
        Rect rect = mCamera.rect;
        mCamera.rect = new Rect(0f, 0f, 1f, 1f);
        var request = new RenderPipeline.StandardRequest { destination = mPicture };
        if (RenderPipeline.SupportsRenderRequest(mCamera, request))
            RenderPipeline.SubmitRenderRequest(mCamera, request);
        else
        {
            RenderTexture target = mCamera.targetTexture;
            mCamera.targetTexture = mPicture;
            mCamera.Render();
            mCamera.targetTexture = target;
        }
        mCamera.rect = rect;
    }

    void Attach(Canvas canvas)
    {
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceCamera) return;
        mOriginal[canvas] = (canvas.gameObject.layer, canvas.sortingOrder);
        AttachCanvas(canvas);
    }

    void AttachCanvas(Canvas canvas)
    {
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = mUiCamera;
        canvas.planeDistance = 1f;
    }

    void Detach(Canvas canvas)
    {
        if (canvas == null || canvas.renderMode != RenderMode.ScreenSpaceCamera) return;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = null;
        if (!mOriginal.TryGetValue(canvas, out var original)) return;
        SetLayer(canvas.transform, original.layer);
        canvas.sortingOrder = original.order; // set again so the overlays re-sort under the HUD
    }

    void SetLayer(Transform root, int layer)
    {
        root.GetComponentsInChildren(true, mChildren);
        foreach (Transform t in mChildren) t.gameObject.layer = layer;
    }

    // The view boxes, turned and as big as fits in the camera area left of the sidebar
    void Place()
    {
        // Every frame: the UI camera only draws what's on its layer, children added since included
        foreach (Canvas canvas in sCanvases) SetLayer(canvas.transform, UiLayer);

        Rect box = mCamera.pixelRect;
        mPictureImage.rectTransform.anchoredPosition = box.position;
        mPictureImage.rectTransform.sizeDelta = box.size;

        Rect source = SplitScreenView.ViewsArea;
        mDisplay.uvRect = source;

        float width = source.width * Screen.width, height = source.height * Screen.height;
        float areaWidth = (1f - SplitScreenView.SidebarWidth) * Screen.width, areaHeight = Screen.height;
        float scale = Mathf.Min(areaWidth / height, areaHeight / width); // turned, its width is upright

        mBacking.sizeDelta = new Vector2(areaWidth, areaHeight);
        RectTransform rt = mDisplay.rectTransform;
        rt.anchoredPosition = new Vector2(areaWidth * 0.5f, areaHeight * 0.5f);
        rt.sizeDelta = new Vector2(width * scale, height * scale);
    }

    /* What's under the mouse on the turned picture isn't what's drawn there, so the picture takes
     the click and passes it on to the spot it shows, as it was before the turn: e.g. a card's "i" */
    class ClickForwarder : MonoBehaviour, IPointerClickHandler
    {
        readonly List<RaycastResult> mHits = new List<RaycastResult>();

        public void OnPointerClick(PointerEventData eventData)
        {
            RectTransform rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, eventData.position, null, out Vector2 local)) return;
            Rect r = rt.rect;
            Vector2 n = new Vector2((local.x - r.x) / r.width, (local.y - r.y) / r.height); // 0-1 across the picture
            if (n.x < 0f || n.x > 1f || n.y < 0f || n.y > 1f) return;

            // The picture shows SplitScreenView.ViewsArea of the screen
            Rect source = SplitScreenView.ViewsArea;
            var unturned = new PointerEventData(EventSystem.current)
            {
                position = new Vector2((source.x + n.x * source.width) * Screen.width, (source.y + n.y * source.height) * Screen.height),
                button = eventData.button,
                clickCount = eventData.clickCount,
            };
            /* The topmost thing there that takes clicks, e.g. a card's "i": the turned canvases (hand
             frames, the opponent's picture) also lie over that spot, but don't */
            EventSystem.current.RaycastAll(unturned, mHits);
            foreach (RaycastResult hit in mHits)
            {
                if (hit.gameObject == gameObject) continue; // the picture itself, where it overlaps the spot
                GameObject target = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject);
                if (target == null) continue;
                unturned.pointerCurrentRaycast = unturned.pointerPressRaycast = hit;
                unturned.pointerPress = target;
                ExecuteEvents.Execute(target, unturned, ExecuteEvents.pointerClickHandler);
                return;
            }
        }
    }
}

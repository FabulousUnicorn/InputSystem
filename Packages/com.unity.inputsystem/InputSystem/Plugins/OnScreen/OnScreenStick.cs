#if PACKAGE_DOCS_GENERATION || UNITY_INPUT_SYSTEM_ENABLE_UI
using System;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.InputSystem.Layouts;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AnimatedValues;
#endif
////TODO: custom icon for OnScreenStick component

namespace UnityEngine.InputSystem.OnScreen
{
    /// <summary>
    /// A stick control displayed on screen and moved around by touch or other pointer
    /// input.
    /// </summary>
    /// <remarks>
    /// The <see cref="OnScreenStick"/> works by simulating events from the device specified in the <see cref="OnScreenControl.controlPath"/>
    /// property. Some parts of the Input System, such as the <see cref="PlayerInput"/> component, can be set up to
    /// auto-switch to a new device when input from them is detected. When a device is switched, any currently running
    /// inputs from the previously active device are cancelled. In the case of <see cref="OnScreenStick"/>, this can mean that the
    /// <see cref="IPointerUpHandler.OnPointerUp"/> method will be called and the stick will jump back to center, even though
    /// the pointer input has not physically been released.
    ///
    /// To avoid this situation, set the <see cref="useIsolatedInputActions"/> property to true. This will create a set of local
    /// Input Actions to drive the stick that are not cancelled when device switching occurs.
    /// </remarks>
    [AddComponentMenu("Input/On-Screen Stick")]
    [HelpURL(InputSystem.kDocUrl + "/manual/OnScreen.html#on-screen-sticks")]
    public class OnScreenStick : OnScreenControl, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public void OnPointerDown(PointerEventData eventData)
        {
            if (m_UseIsolatedInputActions)
                return;

            if (eventData == null)
                throw new System.ArgumentNullException(nameof(eventData));

            BeginInteraction(eventData.position, eventData.pressEventCamera);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (m_UseIsolatedInputActions)
                return;

            if (eventData == null)
                throw new System.ArgumentNullException(nameof(eventData));

            MoveStick(eventData.position, eventData.pressEventCamera);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (m_UseIsolatedInputActions)
                return;

            EndInteraction();
        }

        private void Start()
        {
            if (m_UseIsolatedInputActions)
            {
                // avoid allocations every time the pointer down event fires by allocating these here
                // and re-using them
                m_RaycastResults = new List<RaycastResult>();
                m_PointerEventData = new PointerEventData(EventSystem.current);

                // if the pointer actions have no bindings (the default), add some
                if (m_PointerDownAction == null || m_PointerDownAction.bindings.Count == 0)
                {
                    if (m_PointerDownAction == null)
                        m_PointerDownAction = new InputAction();

                    m_PointerDownAction.AddBinding("<Mouse>/leftButton");
                    m_PointerDownAction.AddBinding("<Pen>/tip");
                    m_PointerDownAction.AddBinding("<Touchscreen>/touch*/press");
                    m_PointerDownAction.AddBinding("<XRController>/trigger");
                }

                if (m_PointerMoveAction == null || m_PointerMoveAction.bindings.Count == 0)
                {
                    if (m_PointerMoveAction == null)
                        m_PointerMoveAction = new InputAction();

                    m_PointerMoveAction.AddBinding("<Mouse>/position");
                    m_PointerMoveAction.AddBinding("<Pen>/position");
                    m_PointerMoveAction.AddBinding("<Touchscreen>/touch*/position");
                }

                m_PointerDownAction.started += OnPointerDown;
                m_PointerDownAction.canceled += OnPointerUp;
                m_PointerDownAction.Enable();
                m_PointerMoveAction.Enable();
            }

            m_StartPos = ((RectTransform)transform).anchoredPosition;
        }

        private void BeginInteraction(Vector2 pointerPosition, Camera uiCamera)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                transform.parent.GetComponentInParent<RectTransform>(),
                pointerPosition, uiCamera, out m_PointerDownPos);
        }

        private void MoveStick(Vector2 pointerPosition, Camera uiCamera)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                transform.parent.GetComponentInParent<RectTransform>(),
                pointerPosition, uiCamera, out var position);
            var delta = position - m_PointerDownPos;

            delta = Vector2.ClampMagnitude(delta, movementRange);
            ((RectTransform)transform).anchoredPosition = m_StartPos + (Vector3)delta;

            var newPos = new Vector2(delta.x / movementRange, delta.y / movementRange);
            SendValueToControl(newPos);
        }

        private void EndInteraction()
        {
            ((RectTransform)transform).anchoredPosition = m_StartPos;
            SendValueToControl(Vector2.zero);
        }

        private void OnPointerDown(InputAction.CallbackContext ctx)
        {
            Debug.Assert(EventSystem.current != null);

            var screenPosition = Vector2.zero;
            if (ctx.control?.device is Pointer pointer)
                screenPosition = pointer.position.ReadValue();

            m_PointerEventData.position = screenPosition;
            EventSystem.current.RaycastAll(m_PointerEventData, m_RaycastResults);
            if (m_RaycastResults.Count == 0)
                return;

            var stickSelected = false;
            foreach (var result in m_RaycastResults)
            {
                if (result.gameObject != gameObject) continue;

                stickSelected = true;
                break;
            }

            if (!stickSelected)
                return;

            BeginInteraction(screenPosition, GetCameraFromCanvas());
            m_PointerMoveAction.performed += OnPointerMove;
        }

        private void OnPointerMove(InputAction.CallbackContext ctx)
        {
            // only pointer devices are allowed
            Debug.Assert(ctx.control?.device is Pointer);

            var screenPosition = ((Pointer)ctx.control.device).position.ReadValue();

            MoveStick(screenPosition, GetCameraFromCanvas());
        }

        private void OnPointerUp(InputAction.CallbackContext ctx)
        {
            EndInteraction();
            m_PointerMoveAction.performed -= OnPointerMove;
        }

        private Camera GetCameraFromCanvas()
        {
            var canvas = GetComponentInParent<Canvas>();
            var renderMode = canvas?.renderMode;
            if (renderMode == RenderMode.ScreenSpaceOverlay
                || (renderMode == RenderMode.ScreenSpaceCamera && canvas?.worldCamera == null))
                return null;

            return canvas?.worldCamera ?? Camera.main;
        }

        public float movementRange
        {
            get => m_MovementRange;
            set => m_MovementRange = value;
        }

        /// <summary>
        /// Prevents stick interactions from getting cancelled due to device switching.
        /// </summary>
        /// <remarks>
        /// This property is useful for scenarios where the active device switches automatically
        /// based on the most recently actuated device. A common situation where this happens is
        /// when using a <see cref="PlayerInput"/> component with Auto-switch set to true. Imagine
        /// a mobile game where an on-screen stick simulates the left stick of a gamepad device.
        /// When the on-screen stick is moved, the Input System will see an input event from a gamepad
        /// and switch the active device to it. This causes any active actions to be cancelled, including
        /// the pointer action driving the on screen stick, which results in the stick jumping back to
        /// the center as though it had been released.
        ///
        /// In isolated mode, the actions driving the stick are not cancelled because they are
        /// unique Input Action instances that don't share state with any others.
        /// </remarks>
        public bool useIsolatedInputActions
        {
            get => m_UseIsolatedInputActions;
            set => m_UseIsolatedInputActions = value;
        }

        [FormerlySerializedAs("movementRange")]
        [SerializeField]
        private float m_MovementRange = 50;

        [InputControl(layout = "Vector2")]
        [SerializeField]
        private string m_ControlPath;

        [SerializeField]
        [Tooltip("Set this to true to prevent cancellation of pointer events due to device switching. Cancellation " +
            "will appear as the stick jumping back and forth between the pointer position and the stick center.")]
        private bool m_UseIsolatedInputActions;

        [SerializeField]
        [Tooltip("The action that will be used to detect pointer down events on the stick control. Note that if no bindings " +
            "are set, default ones will be provided.")]
        private InputAction m_PointerDownAction;

        [SerializeField]
        [Tooltip("The action that will be used to detect pointer movement on the stick control. Note that if no bindings " +
            "are set, default ones will be provided.")]
        private InputAction m_PointerMoveAction;

        private Vector3 m_StartPos;
        private Vector2 m_PointerDownPos;

        [NonSerialized]
        private List<RaycastResult> m_RaycastResults;
        [NonSerialized]
        private PointerEventData m_PointerEventData;

        protected override string controlPathInternal
        {
            get => m_ControlPath;
            set => m_ControlPath = value;
        }

        #if UNITY_EDITOR
        [CustomEditor(typeof(OnScreenStick))]
        internal class OnScreenStickEditor : UnityEditor.Editor
        {
            private AnimBool m_ShowIsolatedInputActions;

            private SerializedProperty m_UseIsolatedInputActions;
            private SerializedProperty m_ControlPath;
            private SerializedProperty m_MovementRange;
            private SerializedProperty m_PointerDownAction;
            private SerializedProperty m_PointerMoveAction;

            public void OnEnable()
            {
                m_ShowIsolatedInputActions = new AnimBool(false);

                m_UseIsolatedInputActions = serializedObject.FindProperty(nameof(OnScreenStick.m_UseIsolatedInputActions));
                m_ControlPath = serializedObject.FindProperty(nameof(OnScreenStick.m_ControlPath));
                m_MovementRange = serializedObject.FindProperty(nameof(OnScreenStick.m_MovementRange));
                m_PointerDownAction = serializedObject.FindProperty(nameof(OnScreenStick.m_PointerDownAction));
                m_PointerMoveAction = serializedObject.FindProperty(nameof(OnScreenStick.m_PointerMoveAction));
            }

            public override void OnInspectorGUI()
            {
                EditorGUILayout.PropertyField(m_MovementRange);
                EditorGUILayout.PropertyField(m_ControlPath);

                EditorGUILayout.PropertyField(m_UseIsolatedInputActions);

                m_ShowIsolatedInputActions.target = m_UseIsolatedInputActions.boolValue;
                if (EditorGUILayout.BeginFadeGroup(m_ShowIsolatedInputActions.faded))
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(m_PointerDownAction);
                    EditorGUILayout.PropertyField(m_PointerMoveAction);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFadeGroup();

                serializedObject.ApplyModifiedProperties();
            }
        }
        #endif
    }
}
#endif
